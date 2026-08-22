using Android.Content;
using AndroidX.DocumentFile.Provider;
using MediaOrganizer.Core.Sources;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// SAF 源端抽象（ADR-0006 决策 1）：包装扫描到的 DocumentFile。
/// Identifier 为文档 URI（content://…/document/…），落盘到 analysis-result.json 后可重新解析；
/// 名称/大小/mtime 为扫描时预取值，OpenRead/Delete 经 ContentResolver。
/// </summary>
public sealed class AndroidSafMediaSource : IMediaSource
{
    private readonly ContentResolver _resolver;
    private readonly global::Android.Net.Uri _uri;

    public AndroidSafMediaSource(DocumentFile doc, ContentResolver resolver)
    {
        _uri = doc.Uri ?? throw new ArgumentException("DocumentFile 缺少 URI");
        _resolver = resolver;
        Identifier = _uri.ToString()!;
        DisplayName = doc.Name ?? "unknown";
        Length = doc.Length();
        var millis = doc.LastModified();
        ModifiedTime = millis > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(millis) : null;
    }

    public string Identifier { get; }
    public string DisplayName { get; }
    public long Length { get; }
    public DateTimeOffset? ModifiedTime { get; }

    public Stream OpenRead()
        => _resolver.OpenInputStream(_uri) ?? throw new IOException($"无法读取源文件：{DisplayName}");

    public void Delete()
    {
        try
        {
            _resolver.Delete(_uri, null);
        }
        catch
        {
            // 删除失败由上层大小校验/结果统计兜底（NFR-A5）
        }
    }
}
