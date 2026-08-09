using MediaOrganizer.Core;
using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Configuration;

namespace MediaOrganizer.Core.Tests;

public class AnalyzerTests : IDisposable
{
    private readonly string _dir;

    public AnalyzerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mo-analyze-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        foreach (var name in new[] { "IMG_20240115_123456.jpg", "mm_export1718012345678.jpg", "scan001.tif" })
            File.WriteAllText(Path.Combine(_dir, name), "x");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static AppConfig Config() => new()
    {
        Extraction = new ExtractionConfig
        {
            Extractors =
            [
                new ExtractorSetting { Name = "Exif", Enabled = false, Weight = 1.2 },
                new ExtractorSetting { Name = "FileName", Enabled = true, Weight = 1.1 },
                new ExtractorSetting { Name = "FileSystem", Enabled = false, Weight = 0.8 }
            ]
        }
    };

    [Fact]
    public async Task 分析成功与失败文件()
    {
        var analyzer = CoreFactory.CreateAnalyzer(Config(), PatternsStore.GetBuiltinPatterns());
        var result = await analyzer.AnalyzeAsync(_dir);

        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.Parsed.Count);
        Assert.Equal(1, result.Unparsed.Count);
        Assert.Equal("scan001.tif", result.Unparsed[0].File.FileName);
        Assert.All(result.Parsed, p => Assert.Equal("FileName", p.Source));
    }

    [Fact]
    public async Task 进度上报()
    {
        var analyzer = CoreFactory.CreateAnalyzer(Config(), PatternsStore.GetBuiltinPatterns());
        var last = new AnalysisProgress(0, 0, 0, 0);
        var tcs = new TaskCompletionSource<AnalysisProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        await analyzer.AnalyzeAsync(_dir, new Progress<AnalysisProgress>(p => { last = p; tcs.TrySetResult(p); }));
        var final = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, final.Total);
        Assert.Equal(3, final.Processed);
    }

    [Fact]
    public async Task 取消分析()
    {
        var analyzer = CoreFactory.CreateAnalyzer(Config(), PatternsStore.GetBuiltinPatterns());
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analyzer.AnalyzeAsync(_dir, null, cts.Token));
    }

    [Fact]
    public void 报告包含统计与失败清单()
    {
        var analyzer = CoreFactory.CreateAnalyzer(Config(), PatternsStore.GetBuiltinPatterns());
        var result = analyzer.AnalyzeAsync(_dir).GetAwaiter().GetResult();
        var report = AnalysisReportGenerator.Generate(result, @"D:\out");

        Assert.Contains("媒体文件分析报告", report);
        Assert.Contains("成功: 2", report);
        Assert.Contains("按来源", report);
        Assert.Contains("scan001.tif", report);
        Assert.Contains("按年份", report);
    }
}
