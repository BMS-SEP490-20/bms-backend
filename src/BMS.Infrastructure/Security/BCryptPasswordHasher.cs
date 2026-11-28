using BMS.Application.Interfaces;
using BCryptNet = BCrypt.Net.BCrypt;

namespace BMS.Infrastructure.Security;

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 11;

    private static readonly string DummyHash = BCryptNet.HashPassword(Guid.NewGuid().ToString("N"), WorkFactor);

    public string Hash(string password) => BCryptNet.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCryptNet.Verify(password, hash);
        }
        catch (Exception ex) when (ex is BCrypt.Net.SaltParseException or ArgumentException)
        {
            return false;
        }
    }

    public bool NeedsRehash(string hash)
    {
        try { return BCryptNet.PasswordNeedsRehash(hash, WorkFactor); }
        catch { return false; }
    }

    public void VerifyDummy(string password) => BCryptNet.Verify(password, DummyHash);
}
