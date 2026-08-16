using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Security;
using MediaOrganizer.Core.Sources;
using MediaOrganizer.Core.Storage;
using WebDAVClient.Helpers;

namespace MediaOrganizer.Core;

/// <summary>按配置/输出目标创建目标存储（ADR-0004）。</summary>
public static class StorageFactory
{
    /// <summary>
    /// 本地目录存储工厂钩子：Android 组合根启动时设置为 SAF 实现（content:// URI），
    /// 桌面保持默认 LocalFileStorage。待处理目录（PendingFileMover）同样经此创建。
    /// </summary>
    public static Func<string, IFileStorage>? CustomLocalStorageFactory { get; set; }

    /// <summary>创建本地目录存储（桌面文件路径 / Android SAF 树 URI）。</summary>
    public static IFileStorage CreateLocalStorage(string path)
        => CustomLocalStorageFactory is { } factory ? factory(path) : new LocalFileStorage(path);

    /// <summary>
    /// 依据 config 与当前选定的输出目标创建存储。
    /// profileName 为空 → 本地目录（config.Paths.OutputDir）。
    /// </summary>
    public static IFileStorage CreateTarget(AppConfig config, string? profileName = null)
    {
        var name = profileName ?? config.Paths.OutputNetworkProfile;
        var profile = config.NetworkProfiles.FirstOrDefault(p => p.Name == name);
        if (profile is not null)
            return CreateProfileStorage(profile);

        return CreateLocalStorage(config.Paths.OutputDir);
    }

    /// <summary>测试连接：创建目录 + 写探针源 + 校验 + 清理（探针为内存源，不落临时文件）。</summary>
    public static async Task<(bool Ok, string Message)> TestConnectionAsync(NetworkProfile profile, CancellationToken ct = default)
    {
        try
        {
            var storage = CreateProfileStorage(profile);
            const string probeDir = ".mo-test";
            const string probeFile = ".mo-test/probe.txt";
            var probe = new MemoryMediaSource("probe.txt", "ok"u8.ToArray());
            await storage.CreateDirectoryAsync(probeDir, ct);
            await storage.CopyFromAsync(probe, probeFile, ct: ct);
            var len = await storage.GetLengthAsync(probeFile, ct);
            if (len != 2) return (false, "校验失败：目标文件大小异常");
            await storage.DeleteAsync(probeFile, ct);
            await storage.DeleteAsync(probeDir, ct);
            return (true, "连接测试通过：认证成功，拥有写入权限");
        }
        catch (WebDAVException ex)
        {
            var code = ex.GetHttpCode();
            var codeStr = code > 0 ? $" [HTTP {code}]" : "";
            return (false, $"连接失败{codeStr}：{ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"连接失败：{ex.Message}");
        }
    }

    public static IFileStorage CreateProfileStorage(NetworkProfile profile)
        => profile.Type switch
        {
            // ADR-0007：SMBLibrary 客户端 + 显式 NTLM 认证（凭据真正生效，替代旧 UNC 方式）
            NetworkType.Smb => new SmbFileStorage(profile.Address, profile.Username, CredentialCrypto.Decrypt(profile.Password)),
            _ => new WebDavFileStorage(profile.Address, profile.Username, CredentialCrypto.Decrypt(profile.Password))
        };
}
