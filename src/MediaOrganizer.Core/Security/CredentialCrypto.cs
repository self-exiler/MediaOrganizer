using System.Security.Cryptography;
using System.Text;
using MediaOrganizer.Core.Platforms;

namespace MediaOrganizer.Core.Security;

/// <summary>
/// 凭据加密门面（FR-A10，ADR-0005 §7）：静态 API 不变（StorageFactory 等调用点无感），
/// 实现经 <see cref="Current"/> 注入——桌面默认 DPAPI/B64，Android 启动时设为 Keystore 实现（KS: 前缀）。
/// 存储格式：DPAPI:&lt;base64&gt; / B64:&lt;base64&gt; / KS:&lt;base64&gt;（Android Keystore）；空字符串原样返回。
/// </summary>
public static class CredentialCrypto
{
    public const string DpapiPrefix = "DPAPI:";
    public const string B64Prefix = "B64:";
    public const string KeystorePrefix = "KS:";

    /// <summary>当前平台实现。Android 组合根启动时设置为 AndroidCredentialCrypto。</summary>
    public static ICredentialCrypto Current { get; set; } = new DefaultCredentialCrypto();

    public static string Encrypt(string plain) => Current.Encrypt(plain);

    public static string Decrypt(string stored) => Current.Decrypt(stored);

    /// <summary>桌面/共享默认实现：Windows DPAPI（CurrentUser 域），其他平台降级 Base64 并标注。</summary>
    private sealed class DefaultCredentialCrypto : ICredentialCrypto
    {
        public string Encrypt(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            var bytes = Encoding.UTF8.GetBytes(plain);
#if !ANDROID
            if (OperatingSystem.IsWindows())
            {
                var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return DpapiPrefix + Convert.ToBase64String(protectedBytes);
            }
#endif
            return B64Prefix + Convert.ToBase64String(bytes);
        }

        public string Decrypt(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
#if !ANDROID
            if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal) && OperatingSystem.IsWindows())
            {
                var bytes = Convert.FromBase64String(stored[DpapiPrefix.Length..]);
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
            }
#endif
            if (stored.StartsWith(B64Prefix, StringComparison.Ordinal))
                return Encoding.UTF8.GetString(Convert.FromBase64String(stored[B64Prefix.Length..]));
            return stored; // 兼容旧明文
        }
    }
}
