using System.Text;
using System.Threading.RateLimiting;
using BMS.Api.Authorization;
using BMS.Api.Middleware;
using BMS.Api.Startup;
using BMS.Application.Interfaces;
using BMS.Application.Options;
using BMS.Application.Services;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Email;
using BMS.Infrastructure.Mongo;
using BMS.Infrastructure.Repositories;
using BMS.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;

namespace BMS.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMongo(this IServiceCollection services, IConfiguration config)
    {
        var conn = config["MongoDB:ConnectionString"] ?? throw new InvalidOperationException("Missing MongoDB:ConnectionString");
        var dbName = config["MongoDB:DatabaseName"] ?? throw new InvalidOperationException("Missing MongoDB:DatabaseName");

        MongoMappings.Register();

        var client = new MongoClient(conn);
        services.AddSingleton<IMongoClient>(client);
        services.AddSingleton(client.GetDatabase(dbName));
        services.AddSingleton(new MongoCollections(
            Users: config["MongoDB:UsersCollectionName"] ?? "accounts",
            Roles: config["MongoDB:RolesCollectionName"] ?? "roles",
            RefreshTokens: config["MongoDB:RefreshTokensCollectionName"] ?? "refresh_tokens",
            ServicePackages: config["MongoDB:ServicePackagesCollectionName"] ?? "service_packages",
            TestCriteria: config["MongoDB:TestCriteriaCollectionName"] ?? "test_criteria",
            PasswordResetTokens: config["MongoDB:PasswordResetTokensCollectionName"] ?? "password_reset_tokens"));

        services.AddSingleton<IUserRoleMigration, BMS.Infrastructure.Migrations.UserRoleMigration>();
        services.AddSingleton<IFlowIndexInitializer, BMS.Infrastructure.Repositories.FlowIndexInitializer>();
        services.AddHostedService<MongoInitializer>();
        return services;
    }

    public static IServiceCollection AddAuthModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.SectionName))
            .Validate(o => Encoding.UTF8.GetByteCount(o.SecretKey ?? "") >= 32
                           && !o.SecretKey!.StartsWith("REPLACE_WITH_", StringComparison.Ordinal),
                "Jwt:SecretKey phải >= 32 byte và không phải giá trị mẫu (đặt qua biến môi trường Jwt__SecretKey / user-secrets).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Issuer/Audience bắt buộc.")
            .ValidateOnStart();
        services.AddOptions<AuthOptions>().Bind(config.GetSection(AuthOptions.SectionName));

        var jwt = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                  ?? throw new InvalidOperationException("Missing Jwt section");
        if (string.IsNullOrEmpty(jwt.SecretKey))
            throw new InvalidOperationException("Missing Jwt:SecretKey");

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IRoleRepository, RoleRepository>();
        services.AddSingleton<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuthService, AuthService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build());
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
        return services;
    }

    /// <summary>Quên mật khẩu / đặt lại mật khẩu qua email.</summary>
    public static IServiceCollection AddPasswordRecovery(
        this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        services.AddOptions<PasswordResetOptions>().Bind(config.GetSection(PasswordResetOptions.SectionName))
            .Validate(o => Uri.TryCreate(o.ResetPasswordUrl, UriKind.Absolute, out _),
                "PasswordReset:ResetPasswordUrl phải là URL tuyệt đối.")
            .Validate(o => o.TokenMinutes is >= 5 and <= 1440, "PasswordReset:TokenMinutes phải từ 5 đến 1440.")
            .ValidateOnStart();
        services.AddOptions<EmailOptions>().Bind(config.GetSection(EmailOptions.SectionName));

        if (string.IsNullOrWhiteSpace(config[$"{EmailOptions.SectionName}:Host"]) && env.IsDevelopment())
            services.AddSingleton<IEmailSender, DevLoggingEmailSender>();
        else
            services.AddSingleton<IEmailSender, SmtpEmailSender>();

        services.AddSingleton<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        return services;
    }

    /// <summary>CRUD + tìm kiếm gói dịch vụ.</summary>
    public static IServiceCollection AddServicePackages(this IServiceCollection services)
    {
        services.AddSingleton<IServicePackageRepository, ServicePackageRepository>();
        services.AddScoped<IServicePackageService, ServicePackageService>();
        return services;
    }

    public static IServiceCollection AddTestCriteria(this IServiceCollection services)
    {
        services.AddSingleton<BMS.Core.Interfaces.ITestCriterionRepository, TestCriterionRepository>();
        services.AddSingleton<BMS.Application.Interfaces.ITestCriterionRepository, TestCriterionRepository>();
        return services;
    }

    public static IServiceCollection AddApiHardening(this IServiceCollection services, IConfiguration config)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.Configure<ApiBehaviorOptions>(o =>
            o.InvalidModelStateResponseFactory = ctx =>
            {
                var pd = new ValidationProblemDetails(ctx.ModelState)
                {
                    Status = 400,
                    Title = "Dữ liệu không hợp lệ.",
                    Extensions = { ["code"] = "VALIDATION_ERROR", ["traceId"] = ctx.HttpContext.TraceIdentifier }
                };
                return new BadRequestObjectResult(pd);
            });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = config.GetValue("RateLimiting:LoginPermitPerMinute", 10),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

            o.AddPolicy("forgot-password", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = config.GetValue("RateLimiting:ForgotPasswordPermitPerMinute", 5),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
            o.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.StatusCode = 429;
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
                await ctx.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = 429,
                    Title = "Quá nhiều yêu cầu. Vui lòng thử lại sau.",
                    Extensions = { ["code"] = "TOO_MANY_REQUESTS" }
                }, ct);
            };
        });

        var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(o => o.AddDefaultPolicy(p =>
        {
            if (origins.Length > 0)
                p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }));

        return services;
    }
}
