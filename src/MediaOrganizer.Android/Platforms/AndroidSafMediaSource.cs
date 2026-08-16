using Android.Content;
using Android.Content.Res;
using Android.OS;
using Android.Provider;
using AndroidX.DocumentFile.Provider;
using MediaOrganizer.Core.Sources;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// Android SAF 源端抽象（ADR-0006 决策 1）：MediaFile.Path 为 content:// URI，此处解析为 DocumentFile。
/// ModifiedTime 由扫描时自 DocumentFile.LastModified() 预取（FileSystemExtractor 兜底零额外查询）；
/// OpenRead 走 ContentResolver.OpenAssetFileDescriptor 取可 seek 流（提取链 TagLib/EXIF 需要）；
/// Delete 走 DocumentsContract.DeleteDocument（move 语义 = 复制 + 删除源）。
/// </summary>
public sealed class AndroidSafMediaSource : IMediaSource
{
    private readonly ContentResolver _resolver;
    private readonly DocumentFile _doc;

    public AndroidSafMediaSource(DocumentFile doc, ContentResolver? resolver = null)
    {
        _doc = doc;
        _resolver = resolver ?? global::Android.App.Application.Context.ContentResolver;
    }

    public string Identifier => _doc.Uri?.ToString() ?? "";
    public string DisplayName => _doc.Name ?? "";

    public long Length
    {
        get
        {
            try { return _doc.Length(); } catch { return 0; }
        }
    }

    public DateTimeOffset? ModifiedTime
    {
        get
        {
            try
            {
                var ms = _doc.LastModified();
                return ms <= 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(ms);
            }
            catch { return null; }
        }
    }

    public Stream OpenRead()
    {
        // OpenAssetFileDescriptor 提供可 seek 的流（TagLib 随机访问需要）
        var afd = _resolver.OpenAssetFileDescriptor(_doc.Uri!, "r");
        if (afd is null)
            throw new FileNotFoundException($"无法打开 {Identifier}");
        return new AssetFileStream(afd);
    }

    public void Delete()
    {
        var context = global::Android.App.Application.Context;
        if (_doc.Uri is { } uri && DocumentsContract.IsDocumentUri(context, uri))
            DocumentsContract.DeleteDocument(_resolver, uri);
    }
}

/// <summary>包装 AssetFileDescriptor 的可 seek 流（底层 FileInputStream 共享 fd，seek 走 Os.Lseek）。</summary>
internal sealed class AssetFileStream : Stream
{
    private readonly AssetFileDescriptor _afd;
    private readonly Stream _inner;
    private long _position;

    public AssetFileStream(AssetFileDescriptor afd)
    {
        _afd = afd;
        // .NET Android 绑定的 CreateInputStream 直接返回 System.IO.Stream（定位到资产起始偏移）
        _inner = afd.CreateInputStream()!;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _afd.Length;
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = _inner.Read(buffer, offset, count);
        if (n > 0) _position += n;
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var pos = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => Length + offset
        };
        // FileInputStream 与 ParcelFileDescriptor 共享 fd：lseek 直接移动读取位置（SAF 文档 StartOffset 通常为 0）
        _position = global::Android.Systems.Os.Lseek(_afd.FileDescriptor!, _afd.StartOffset + pos, global::Android.Systems.OsConstants.SeekSet) - _afd.StartOffset;
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            _afd.Close();
        }
        base.Dispose(disposing);
    }
}