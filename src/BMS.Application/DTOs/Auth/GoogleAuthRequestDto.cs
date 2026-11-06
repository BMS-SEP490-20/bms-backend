using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Auth;

/// <summary>
/// Payload submitted by web/mobile client containing the Google ID Token (credential) for OAuth2 authentication.
/// </summary>
public class GoogleAuthRequestDto
{
    /// <summary>
    /// Cryptographically signed Google ID Token (JWT) issued by Google Identity Services.
    /// </summary>
    [Required(ErrorMessage = "Google ID Token (credential) is required.")]
    public string IdToken { get; set; } = string.Empty;
}
