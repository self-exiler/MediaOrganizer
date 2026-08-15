using System.Security.Cryptography;
using System.Text;

namespace MediaOrganizer.Core.Security;

/// <summary>
/// 凭据加密（FR-11）：Windows 用 DPAPI（CurrentUser 域）加密；macOS/Linux/Android 首版降级为 Base64 并标注（后续接系统密钥环）。
/// 存储格式：DPAPI:&lt;base64&gt; / B64:&lt;base64&gt;；空字符串原样返回。
/// </summary>
public static class CredentialCrypto
{
    public const string DpapiPrefix = "DPAPI:";
    public const string B64Prefix = "B64:";

    public static string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var bytes = Encoding.UTF8.GetBytes(plain);
        if (OperatingSystem.IsWindows())
        {
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return DpapiPrefix + Convert.ToBase64String(protectedBytes);
        }
        return B64Prefix + Convert.ToBase64String(bytes);
    }

    public static string Decrypt(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal) && OperatingSystem.IsWindows())
        {
            var bytes = Convert.FromBase64String(stored[DpapiPrefix.Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
        }
        if (stored.StartsWith(B64Prefix, StringComparison.Ordinal))
            return Encoding.UTF8.GetString(Convert.FromBase64String(stored[B64Prefix.Length..]));
        return stored; // 兼容旧明文
    }
}
