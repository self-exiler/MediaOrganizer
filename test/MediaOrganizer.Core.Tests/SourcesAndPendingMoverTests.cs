using MediaOrganizer.Core.Execution;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Sources;
using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core.Tests;

public class LocalMediaSourceTests : IDisposable
{
    private readonly string _dir;

    public LocalMediaSourceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mo-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void 属性与读流()
    {
        var path = Path.Combine(_dir, "a.jpg");
        File.WriteAllText(path, "hello");
        var source = new LocalMediaSource(path);

        Assert.Equal(path, source.Identifier);
        Assert.Equal("a.jpg", source.DisplayName);
        Assert.Equal(5, source.Length);
        Assert.NotNull(source.ModifiedTime);
        using var stream = source.OpenRead();
        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek, "提取链路（TagLib/EXIF）依赖可 seek 流");
    }

    [Fact]
    public void Delete删除源()
    {
        var path = Path.Combine(_dir, "b.jpg");
        File.WriteAllText(path, "x");
        new LocalMediaSource(path).Delete();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void 缺失文件OpenRead抛异常()
    {
        var source = new LocalMediaSource(Path.Combine(_dir, "none.jpg"));
        Assert.Throws<FileNotFoundException>(() => source.OpenRead());
    }
}

public class MemoryMediaSourceTests
{
    [Fact]
    public void 内存源读写()
    {
        var source = new MemoryMediaSource("probe.txt", [1, 2, 3]);
        Assert.Equal(3, source.Length);
        using var stream = source.OpenRead();
        Assert.Equal(3, stream.Length);
        Assert.True(stream.CanSeek);
    }
}

public class TagLibStreamFileAbstractionTests
{
    [Fact]
    public void 可Seek流原样返回()
    {
        using var ms = new MemoryStream([1, 2, 3]);
        var wrapped = MediaOrganizer.Core.Extraction.TagLibStreamFileAbstraction.EnsureSeekable(ms);
        Assert.Same(ms, wrapped);
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private int _pos;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(count, data.Length - _pos);
            Array.Copy(data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void 不可Seek流降级拷贝()
    {
        using var wrapped = MediaOrganizer.Core.Extraction.TagLibStreamFileAbstraction.EnsureSeekable(new NonSeekableStream([4, 5, 6, 7]));
        Assert.True(wrapped.CanSeek);
        Assert.Equal(4, wrapped.Length);
        Assert.Equal(4, wrapped.ReadByte());
        wrapped.Position = 0;
        Assert.Equal(4, wrapped.ReadByte());
    }
}

public class PendingFileMoverTests : IDisposable
{
    private readonly string _src;
    private readonly string _pending;

    public PendingFileMoverTests()
    {
        _src = Path.Combine(Path.GetTempPath(), "mo-pend-src-" + Guid.NewGuid().ToString("N"));
        _pending = Path.Combine(Path.GetTempPath(), "mo-pend-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_src);
    }

    public void Dispose()
    {
        foreach (var d in new[] { _src, _pending })
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    private MediaFile MakeFile(string name, string content = "x")
    {
        var path = Path.Combine(_src, name);
        File.WriteAllText(path, content);
        return new MediaFile(path, content.Length, Path.GetExtension(name).TrimStart('.'))
        {
            Source = new LocalMediaSource(path)
        };
    }

    [Fact]
    public async Task 移动复制内容并删除源()
    {
        var f = MakeFile("scan001.tif", "abc");
        var r = await new PendingFileMover().MoveAsync([f], new LocalFileStorage(_pending));

        Assert.Equal(1, r.Moved);
        Assert.Equal(0, r.Failed);
        Assert.Equal("abc", File.ReadAllText(Path.Combine(_pending, "scan001.tif")));
        Assert.False(File.Exists(f.Path), "移动语义应删除源");
    }

    [Fact]
    public async Task 同名追加序号()
    {
        Directory.CreateDirectory(_pending);
        File.WriteAllText(Path.Combine(_pending, "dup.jpg"), "existing");
        var f = MakeFile("dup.jpg", "new");
        var r = await new PendingFileMover().MoveAsync([f], new LocalFileStorage(_pending));

        Assert.Equal(1, r.Moved);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(_pending, "dup.jpg")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(_pending, "dup_1.jpg")));
    }

    [Fact]
    public async Task 单文件失败不中断()
    {
        var missing = new MediaFile(Path.Combine(_src, "none.tif"), 1, "tif")
        {
            Source = new LocalMediaSource(Path.Combine(_src, "none.tif"))
        };
        var ok = MakeFile("ok.tif", "y");
        var r = await new PendingFileMover().MoveAsync([missing, ok], new LocalFileStorage(_pending));

        Assert.Equal(1, r.Moved);
        Assert.Equal(1, r.Failed);
        Assert.Single(r.Errors);
        Assert.True(File.Exists(Path.Combine(_pending, "ok.tif")));
    }
}
