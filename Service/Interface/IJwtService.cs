using iam_service.Modals;
using System.Security.Claims;



namespace IamService.Services.Interface
{
    public interface IJwtService
    {
        string GenerateToken(Account user, List<string> roles);

        ClaimsPrincipal? ValidateToken(string token);

    }
}