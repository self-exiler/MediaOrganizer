using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Security;
using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core;

/// <summary>按配置/输出目标创建目标存储（ADR-0004）。</summary>
public static class StorageFactory
{
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

        return new LocalFileStorage(config.Paths.OutputDir);
    }

    /// <summary>测试连接：创建目录 + 写探针文件 + 校验 + 清理。</summary>
    public static async Task<(bool Ok, string Message)> TestConnectionAsync(NetworkProfile profile, CancellationToken ct = default)
    {
        try
        {
            var storage = CreateProfileStorage(profile);
            const string probeDir = ".mo-test";
            const string probeFile = ".mo-test/probe.txt";
            var probe = Path.Combine(Path.GetTempPath(), "mo-probe-" + Guid.NewGuid().ToString("N") + ".txt");
            await File.WriteAllTextAsync(probe, "ok", ct);
            try
            {
                await storage.CreateDirectoryAsync(probeDir, ct);
                await storage.CopyFromAsync(probe, probeFile, ct: ct);
                var len = await storage.GetLengthAsync(probeFile, ct);
                if (len != 2) return (false, "校验失败：目标文件大小异常");
                await storage.DeleteAsync(probeFile, ct);
                await storage.DeleteAsync(probeDir, ct);
                return (true, "连接测试通过：认证成功，拥有写入权限");
            }
            finally
            {
                if (File.Exists(probe)) File.Delete(probe);
            }
        }
        catch (Exception ex)
        {
            return (false, $"连接失败：{ex.Message}");
        }
    }

    public static IFileStorage CreateProfileStorage(NetworkProfile profile)
        => profile.Type switch
        {
            NetworkType.Smb => new SmbFileStorage(profile.Address),
            _ => new WebDavFileStorage(profile.Address, profile.Username, CredentialCrypto.Decrypt(profile.Password))
        };
}
