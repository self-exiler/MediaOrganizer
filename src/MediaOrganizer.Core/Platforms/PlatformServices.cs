using System.Security.Cryptography;
using System.Text;
using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core.Platforms;

/// <summary>
/// 平台服务组合（2026-10 精简：取代 CredentialCrypto.Current / StorageFactory.CustomLocalStorageFactory /
/// CoreLog.Sink 三个全局静态钩子）：凭据加密、本地目录存储工厂、Core 诊断日志出口统一为一个对象，
/// 组合根启动时设置 <see cref="Current"/> 一次；不设置即为桌面默认（DPAPI/Base64 + LocalFileStorage + 日志静默）。
/// </summary>
public sealed class PlatformServices
{
    /// <summary>进程级单例；组合根（App 启动）赋值一次。</summary>
    public static PlatformServices Current { get; set; } = new();

    /// <summary>凭据加密实现；未注入时为桌面默认（DPAPI/Base64）。</summary>
    public ICredentialCrypto Credentials { get; init; } = DefaultCredentialCryptoHolder.Instance;

    /// <summary>本地目录存储工厂：Android 注入「content: 树 URI 换算真实路径」逻辑；null = 直接 LocalFileStorage。</summary>
    public Func<string, IFileStorage>? LocalStorageFactory { get; init; }

    /// <summary>Core 内部诊断出口（SMB 协商参数、mtime 降级警告等）；null = 丢弃。</summary>
    public Action<string>? LogSink { get; init; }

    public IFileStorage CreateLocalStorage(string path)
        => LocalStorageFactory is { } factory ? factory(path) : new Storage.LocalFileStorage(path);

    public string EncryptCredential(string plain) => Credentials.Encrypt(plain);

    public string DecryptCredential(string stored) => Credentials.Decrypt(stored);

    /// <summary>诊断日志。Core 内调用点自行拼好带来源前缀的完整消息。</summary>
    public void Log(string message) => LogSink?.Invoke(message);
}

/// <summary>桌面/共享默认凭据实现：Windows DPAPI（CurrentUser 域），其他平台降级 Base64 并标注。</summary>
internal sealed class DefaultCredentialCryptoHolder : ICredentialCrypto
{
    public static readonly DefaultCredentialCryptoHolder Instance = new();

    public string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var bytes = Encoding.UTF8.GetBytes(plain);
#if !ANDROID
        if (OperatingSystem.IsWindows())
        {
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Security.CredentialCrypto.DpapiPrefix + Convert.ToBase64String(protectedBytes);
        }
#endif
        return Security.CredentialCrypto.B64Prefix + Convert.ToBase64String(bytes);
    }

    public string Decrypt(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
#if !ANDROID
        if (stored.StartsWith(Security.CredentialCrypto.DpapiPrefix, StringComparison.Ordinal) && OperatingSystem.IsWindows())
        {
            var bytes = Convert.FromBase64String(stored[Security.CredentialCrypto.DpapiPrefix.Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
        }
#endif
        if (stored.StartsWith(Security.CredentialCrypto.B64Prefix, StringComparison.Ordinal))
            return Encoding.UTF8.GetString(Convert.FromBase64String(stored[Security.CredentialCrypto.B64Prefix.Length..]));
        return stored; // 兼容旧明文
    }
}
