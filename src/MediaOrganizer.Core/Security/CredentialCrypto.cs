using MediaOrganizer.Core.Platforms;

namespace MediaOrganizer.Core.Security;

/// <summary>
/// 凭据加密门面（FR-A10）：静态 API 不变（调用点无感），
/// 实现住在 <see cref="PlatformServices"/>（组合根注入，桌面默认 DPAPI/B64，Android 为 Keystore 实现）。
/// 存储格式：DPAPI:&lt;base64&gt; / B64:&lt;base64&gt; / KS:&lt;base64&gt;（Android Keystore）；空字符串原样返回。
/// </summary>
public static class CredentialCrypto
{
    public const string DpapiPrefix = "DPAPI:";
    public const string B64Prefix = "B64:";
    public const string KeystorePrefix = "KS:";

    public static string Encrypt(string plain) => PlatformServices.Current.EncryptCredential(plain);

    public static string Decrypt(string stored) => PlatformServices.Current.DecryptCredential(stored);
}
