using BMS.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Middleware;

/// <summary>Chuẩn hoá mọi lỗi thành RFC 7807 ProblemDetails, không lộ stack trace.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        ProblemDetails problem;

        switch (ex)
        {
            case AuthException auth:
                problem = new ProblemDetails
                {
                    Status = auth.StatusCode,
                    Title = auth.Message,
                    Extensions = { ["code"] = auth.Code }
                };
                if (auth is AccountLockedException locked)
                {
                    var seconds = (int)Math.Ceiling(locked.RetryAfter.TotalSeconds);
                    ctx.Response.Headers.RetryAfter = seconds.ToString();
                    problem.Extensions["retryAfterSeconds"] = seconds;
                }
                break;

            case UnauthorizedAccessException:
                problem = new ProblemDetails { Status = 401, Title = "Chưa xác thực.", Extensions = { ["code"] = "UNAUTHORIZED" } };
                break;

            case BadHttpRequestException bad:
                problem = new ProblemDetails { Status = 400, Title = "Yêu cầu không hợp lệ.", Detail = bad.Message, Extensions = { ["code"] = "BAD_REQUEST" } };
                break;

            default:
                logger.LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
                problem = new ProblemDetails { Status = 500, Title = "Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau.", Extensions = { ["code"] = "INTERNAL_ERROR" } };
                break;
        }

        problem.Extensions["traceId"] = ctx.TraceIdentifier;
        ctx.Response.StatusCode = problem.Status!.Value;
        await ctx.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }
}
