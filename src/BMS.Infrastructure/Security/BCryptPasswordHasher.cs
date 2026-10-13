using BMS.Application.Interfaces;
using BCryptNet = BCrypt.Net.BCrypt;

namespace BMS.Infrastructure.Security;

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    // Work factor 11 khớp với dữ liệu hiện có ($2a$11$...). Tăng dần theo phần cứng; hash cũ tự được nâng cấp khi login.
    private const int WorkFactor = 11;

    // Hash hợp lệ của một chuỗi ngẫu nhiên, tạo 1 lần để verify giả.
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
            // Hash trong DB sai định dạng -> coi như sai mật khẩu, không để rò lỗi ra ngoài
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
