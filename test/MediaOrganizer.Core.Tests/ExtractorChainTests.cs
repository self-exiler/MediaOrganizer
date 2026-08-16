using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Extraction;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Sources;

namespace MediaOrganizer.Core.Tests;

public class ExtractorChainTests
{
    private sealed class FakeExtractor(string name, double weight, bool enabled, DateTimeOffset? result) : IDateExtractor
    {
        public string Name => name;
        public bool Enabled => enabled;
        public double Weight => weight;
        public DateTimeOffset? Extract(MediaFile file) => result;
    }

    private static readonly DateTimeOffset SomeDate = new(2024, 5, 6, 7, 8, 9, TimeSpan.Zero);

    [Fact]
    public void 权重高者优先()
    {
        var chain = new ExtractorChain(
        [
            new FakeExtractor("低权重", 0.5, true, SomeDate),
            new FakeExtractor("高权重", 2.0, true, SomeDate.AddDays(1))
        ], new DateRangeValidator());
        var r = chain.TryExtract(new MediaFile("x.jpg", 1, "jpg"));
        Assert.Equal("高权重", r!.Source);
    }

    [Fact]
    public void 第一个有效结果胜出()
    {
        var chain = new ExtractorChain(
        [
            new FakeExtractor("先", 2.0, true, null),
            new FakeExtractor("后", 1.0, true, SomeDate)
        ], new DateRangeValidator());
        Assert.Equal("后", chain.TryExtract(new MediaFile("x.jpg", 1, "jpg"))!.Source);
    }

    [Fact]
    public void 无效日期继续尝试()
    {
        var now = new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);
        var chain = new ExtractorChain(
        [
            new FakeExtractor("未来", 2.0, true, now.AddDays(10)),  // 超出缓冲
            new FakeExtractor("正常", 1.0, true, SomeDate)
        ], new DateRangeValidator(maxYearsPast: 30, futureDateBufferDays: 0, now: now));
        var r = chain.TryExtract(new MediaFile("x.jpg", 1, "jpg"));
        Assert.Equal("正常", r!.Source);
        Assert.Equal(SomeDate, r.Date);
    }

    [Fact]
    public void 全部无效返回null()
    {
        var chain = new ExtractorChain(
        [
            new FakeExtractor("A", 1.0, true, null),
            new FakeExtractor("B", 1.0, true, null)
        ], new DateRangeValidator());
        Assert.Null(chain.TryExtract(new MediaFile("x.jpg", 1, "jpg")));
    }

    [Fact]
    public void 从配置构建默认三条链()
    {
        var cfg = new ExtractionConfig();
        var chain = ExtractorChain.FromConfig(cfg, PatternsStore.GetBuiltinPatterns());
        var r = chain.TryExtract(new MediaFile(@"C:\tmp\mm_export1718012345678.jpg", 1, "jpg"));
        Assert.NotNull(r);
        Assert.Equal("FileName", r!.Source);
    }

    [Fact]
    public void FileSystem禁用时不得使用mtime()
    {
        // 文件系统提取器默认禁用：仅 mtime 可用的文件必须返回 null（回归保护）
        var cfg = new ExtractionConfig(); // 默认 FileSystem.Enabled = false
        var chain = ExtractorChain.FromConfig(cfg, PatternsStore.GetBuiltinPatterns());

        var dir = Path.Combine(Path.GetTempPath(), "mo-chain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "scan_no_date.tif");
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc));
            // 文件系统提取器读 IMediaSource.ModifiedTime（扫描时预取），需携带源端抽象
            var media = new MediaFile(path, 1, "tif") { Source = new LocalMediaSource(path) };

            var result = chain.TryExtract(media);
            Assert.Null(result);

            // 启用后应命中 mtime
            cfg.Extractors.First(e => e.Name == "FileSystem").Enabled = true;
            var chain2 = ExtractorChain.FromConfig(cfg, PatternsStore.GetBuiltinPatterns());
            var result2 = chain2.TryExtract(media);
            Assert.NotNull(result2);
            Assert.Equal("FileSystem", result2!.Source);
            Assert.Equal(2024, result2.Date.Year);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
