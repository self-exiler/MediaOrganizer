using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaOrganizer.Core.Configuration;

/// <summary>JSON 文件读写帮助（建目录 + 序列化 + **原子**落盘），供配置/模式/分析结果共用。</summary>
public static class JsonFileStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // 默认编码器会把所有非 ASCII 字符转义成 \uXXXX，中文配置全部不可读；
        // 文件以 UTF-8 落盘，放宽转义是安全的（仅不在 HTML 上下文中使用该输出）。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // 同一目标文件的保存必须串行：Windows 上并发 File.Move(overwrite:true) 替换同一目标会抛 UnauthorizedAccessException。
    // 路径集合是固定的少数几个（config/patterns/analysis-result），锁表不会无界增长。
    private static readonly ConcurrentDictionary<string, object> SaveLocks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 原子写：先落同目录临时文件，再原子替换目标。
    /// 直接 File.WriteAllText 时，崩溃/断电/磁盘满会把文件截成半截 JSON——
    /// config.json 含网络位置与凭据，写坏即用户配置全丢。
    /// </summary>
    public static void Save<T>(string path, T payload)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        lock (SaveLocks.GetOrAdd(full, _ => new object()))
        {
            // 每次保存用唯一临时名，避免并发写同一 .tmp 互相覆盖；
            // 最后的 File.Move 原子替换保证目标要么是旧值要么是新值，绝不出现半截 JSON。
            var temp = full + ".tmp-" + Guid.NewGuid().ToString("N");
            // perf-3：直写流（原先先序列化成整串再 WriteAllText，10 万级文件的 analysis-result.json
            // 会产生 MB 级大字符串 + 再编码一次）
            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, payload, JsonOptions);
            }
            try
            {
                File.Move(temp, full, overwrite: true);
            }
            catch
            {
                TryDeleteQuietly(temp);
                throw;
            }
        }
    }

    /// <summary>读取；文件不存在返回 null。解析失败时经 <paramref name="onError"/> 上报错误后返回 null。</summary>
    public static T? Load<T>(string path, Action<string>? onError = null) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            var message = $"JSON 读取失败 {path}：{ex.Message}";
            System.Diagnostics.Debug.WriteLine(message);
            onError?.Invoke(message);
            return null;
        }
    }

    /// <summary>
    /// 把损坏/无法解析的文件改名隔离（保留 `.corrupt-时间戳` 副本）：
    /// 既便于人工恢复，也避免下一次保存把原始损坏文件彻底覆盖。
    /// 返回隔离后的路径；无需隔离或隔离失败则返回 null。
    /// </summary>
    public static string? QuarantineCorrupt(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var target = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Move(path, target, overwrite: false);
            return target;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"隔离损坏文件失败 {path}：{ex.Message}");
            return null;
        }
    }

    private static void TryDeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"删除临时文件失败 {path}：{ex.Message}");
        }
    }
}
