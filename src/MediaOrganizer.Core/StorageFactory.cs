using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Security;
using MediaOrganizer.Core.Sources;
using MediaOrganizer.Core.Storage;
using System.Net.Sockets;
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
            // SMB 存储持有 TCP 连接 + 会话，必须释放，否则每次「测试连接」泄漏一条连接
            using var storage = CreateProfileStorage(profile);
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
            // 405/501 = 链路上有中间层（代理/网关）或目标服务不支持 WebDAV 扩展方法（PROPFIND 等）：
            // 服务器本身正常时对未认证 PROPFIND 返回 401，走到这里说明请求被中间环节拒收。
            if (code is 405 or 501)
            {
                return (false, "连接失败：WebDAV 协议方法（PROPFIND/MKCOL）被拒绝 [HTTP " + code + "]。" +
                    "若地址无误，通常是链路上的代理/网关不支持 WebDAV——请换网络（WiFi↔移动数据）或关闭手机的系统代理/代理软件的 HTTP 代理模式后重试。");
            }
            var codeStr = code > 0 ? $" [HTTP {code}]" : "";
            return (false, $"连接失败{codeStr}：{ex.Message}");
        }
        catch (Exception ex)
        {
            // 主机名解析失败（常见于地址混入不可见字符/拼写错误/断网）：
            // 原始报文如 "hostname nor servename provided, or not known" 对用户无指导性，给出可操作提示并附实际解析出的主机名供排查。
            if (FindSocketError(ex) is SocketError.HostNotFound or SocketError.TryAgain)
            {
                return (false, $"无法解析服务器主机名「{DisplayHost(profile)}」：请检查地址有无拼写错误或不可见字符（建议删除重输），并确认当前网络可访问该服务器。");
            }
            return (false, $"连接失败：{ex.Message}");
        }
    }

    /// <summary>沿异常链查找 SocketException（传输层错误常被 HttpRequestException 包裹）。</summary>
    private static SocketError? FindSocketError(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is SocketException sock) return sock.SocketErrorCode;
        }
        return null;
    }

    /// <summary>报错中展示的主机名：WebDAV 经清洗后解析（与存储层一致），解析失败退回原始地址。</summary>
    private static string DisplayHost(NetworkProfile profile)
    {
        if (profile.Type == NetworkType.WebDav)
        {
            try
            {
                return new Uri(WebDavAddress.Sanitize(profile.Address)).Host;
            }
            catch
            {
                // 地址非法时退回原文，报错重点在提示重输
            }
        }
        return profile.Address;
    }

    public static IFileStorage CreateProfileStorage(NetworkProfile profile)
        => profile.Type switch
        {
            // ADR-0007：SMBLibrary 客户端 + 显式 NTLM 认证（凭据真正生效，替代旧 UNC 方式）
            NetworkType.Smb => new SmbFileStorage(profile.Address, profile.Username, CredentialCrypto.Decrypt(profile.Password)),
            _ => new WebDavFileStorage(profile.Address, profile.Username, CredentialCrypto.Decrypt(profile.Password))
        };
}
