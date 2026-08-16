using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Execution;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Planning;
using MediaOrganizer.Core.Sources;
using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core.Tests;

public class FileOperatorTests : IDisposable
{
    private readonly string _src;
    private readonly string _out;

    public FileOperatorTests()
    {
        _src = Path.Combine(Path.GetTempPath(), "mo-test-src-" + Guid.NewGuid().ToString("N"));
        _out = Path.Combine(Path.GetTempPath(), "mo-test-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_src);
    }

    public void Dispose()
    {
        foreach (var d in new[] { _src, _out })
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    private ArchivePlan Plan(params (string Name, DateTimeOffset Date)[] items)
    {
        var parsed = items.Select((x, i) => new ParsedFile(
            new MediaFile(Path.Combine(_src, x.Name), i + 100, Path.GetExtension(x.Name).TrimStart('.'))
            {
                Source = new LocalMediaSource(Path.Combine(_src, x.Name))
            },
            x.Date, "FileName")).ToArray();
        var result = new AnalysisResult(_src, DateTimeOffset.Now, parsed, []);
        return new ArchivePlanner(ClassificationLevel.Day).Plan(result, _out);
    }

    private static string WriteFile(string path, string content = "hello")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private Task<FileOperationResult> Run(ArchivePlan plan, FileOperation op, ExistAction exist, bool fixMtime)
        => new FileOperator(new LocalFileStorage(_out), op, exist, fixMtime).ExecuteAsync(plan);

    [Fact]
    public async Task Copy创建分级目录()
    {
        WriteFile(Path.Combine(_src, "a.jpg"));
        var plan = Plan(("a.jpg", new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero)));
        var r = await Run(plan, FileOperation.Copy, ExistAction.Skip, fixMtime: false);

        Assert.Equal(1, r.Succeeded);
        Assert.True(File.Exists(Path.Combine(_out, "2024", "01", "15", "a.jpg")));
        Assert.True(File.Exists(Path.Combine(_src, "a.jpg")), "copy 应保留源文件");
        Assert.False(File.Exists(Path.Combine(_out, "2024", "01", "15", "a.jpg.mo-tmp")), "临时文件应已改名");
    }

    [Fact]
    public async Task Move删除源文件()
    {
        WriteFile(Path.Combine(_src, "b.jpg"));
        var plan = Plan(("b.jpg", new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero)));
        var r = await Run(plan, FileOperation.Move, ExistAction.Skip, fixMtime: false);

        Assert.Equal(1, r.Succeeded);
        Assert.True(File.Exists(Path.Combine(_out, "2024", "01", "15", "b.jpg")));
        Assert.False(File.Exists(Path.Combine(_src, "b.jpg")), "move 应删除源文件");
    }

    [Fact]
    public async Task 同名Skip跳过()
    {
        WriteFile(Path.Combine(_src, "a.jpg"), "v1");
        WriteFile(Path.Combine(_src, "dup.jpg"), "v2");
        var date = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var plan = Plan(("a.jpg", date), ("dup.jpg", date));
        WriteFile(Path.Combine(_out, "2024", "01", "15", "dup.jpg"), "existing");

        var r = await Run(plan, FileOperation.Copy, ExistAction.Skip, fixMtime: false);
        Assert.Equal(1, r.Succeeded);
        Assert.Equal(1, r.Skipped);
    }

    [Fact]
    public async Task 同名Rename追加序号()
    {
        WriteFile(Path.Combine(_src, "a.jpg"));
        WriteFile(Path.Combine(_src, "dup.jpg"));
        var date = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var plan = Plan(("a.jpg", date), ("dup.jpg", date));
        WriteFile(Path.Combine(_out, "2024", "01", "15", "dup.jpg"));

        var r = await Run(plan, FileOperation.Copy, ExistAction.Rename, fixMtime: false);
        Assert.Equal(2, r.Succeeded);
        Assert.Equal(1, r.Renamed);
        Assert.True(File.Exists(Path.Combine(_out, "2024", "01", "15", "dup_1.jpg")));
    }

    [Fact]
    public async Task 同名Overwrite覆盖()
    {
        WriteFile(Path.Combine(_src, "dup.jpg"), "new");
        var date = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var plan = Plan(("dup.jpg", date));
        WriteFile(Path.Combine(_out, "2024", "01", "15", "dup.jpg"), "old");

        var r = await Run(plan, FileOperation.Copy, ExistAction.Overwrite, fixMtime: false);
        Assert.Equal(1, r.Succeeded);
        Assert.Equal(1, r.Overwritten);
        Assert.Equal("new", File.ReadAllText(Path.Combine(_out, "2024", "01", "15", "dup.jpg")));
    }

    [Fact]
    public async Task 缺失源文件计入失败()
    {
        // a.jpg 未创建
        var plan = Plan(("missing.jpg", new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero)));
        var r = await Run(plan, FileOperation.Copy, ExistAction.Skip, fixMtime: false);
        Assert.Equal(0, r.Succeeded);
        Assert.Equal(1, r.Failed);
        Assert.Single(r.Errors);
    }

    [Fact]
    public async Task FixMtime矫正目标文件时间()
    {
        WriteFile(Path.Combine(_src, "t.jpg"));
        var date = new DateTimeOffset(2024, 3, 20, 10, 30, 0, TimeSpan.Zero);
        var plan = Plan(("t.jpg", date));
        await Run(plan, FileOperation.Copy, ExistAction.Skip, fixMtime: true);

        var target = Path.Combine(_out, "2024", "03", "20", "t.jpg");
        Assert.Equal(date.UtcDateTime, File.GetLastWriteTimeUtc(target));
    }

    [Fact]
    public async Task 顺序执行全部成功()
    {
        // 30 个不同日期文件 → 全部成功且不丢文件
        for (var i = 0; i < 30; i++)
            WriteFile(Path.Combine(_src, $"f{i:00}.jpg"), $"content-{i}");
        var items = Enumerable.Range(0, 30)
            .Select(i => ($"f{i:00}.jpg", new DateTimeOffset(2024, 1, (i % 28) + 1, 0, 0, 0, TimeSpan.Zero)))
            .ToArray();
        var plan = Plan(items);
        var r = await Run(plan, FileOperation.Copy, ExistAction.Skip, fixMtime: false);

        Assert.Equal(30, r.Succeeded);
        Assert.Equal(0, r.Failed);
        for (var i = 0; i < 30; i++)
        {
            var target = Path.Combine(_out, "2024", "01", $"{i % 28 + 1:00}", $"f{i:00}.jpg");
            Assert.True(File.Exists(target), $"缺失 {target}");
            Assert.Equal($"content-{i}", File.ReadAllText(target));
        }
    }

    [Fact]
    public async Task Rename不丢文件()
    {
        // 4 个不同源目录下的同名 base.jpg → 同一目标路径 → Rename 策略 _1/_2/_3 各就位
        var date = new DateTimeOffset(2024, 5, 6, 0, 0, 0, TimeSpan.Zero);
        var parsed = new List<ParsedFile>();
        for (var i = 0; i < 4; i++)
        {
            var sub = Path.Combine(_src, $"sub{i}");
            Directory.CreateDirectory(sub);
            WriteFile(Path.Combine(sub, "base.jpg"), $"v{i}");
            parsed.Add(new ParsedFile(
                new MediaFile(Path.Combine(sub, "base.jpg"), i + 100, "jpg") { Source = new LocalMediaSource(Path.Combine(sub, "base.jpg")) },
                date, "FileName"));
        }
        var result = new AnalysisResult(_src, DateTimeOffset.Now, parsed, []);
        var plan = new ArchivePlanner(ClassificationLevel.Day).Plan(result, _out);

        var r = await Run(plan, FileOperation.Copy, ExistAction.Rename, fixMtime: false);
        Assert.Equal(4, r.Succeeded);
        Assert.Equal(3, r.Renamed);
        Assert.Equal(0, r.Failed);

        var dir = Path.Combine(_out, "2024", "05", "06");
        var files = Directory.GetFiles(dir).Select(Path.GetFileName).ToHashSet();
        Assert.Contains("base.jpg", files);
        Assert.Contains("base_1.jpg", files);
        Assert.Contains("base_2.jpg", files);
        Assert.Contains("base_3.jpg", files);
        Assert.Equal(4, files.Count(f => f!.StartsWith("base")));
    }

    [Fact]
    public async Task Skip只写首个()
    {
        // 4 个不同源目录下的同名 base.jpg → 同一目标路径 → Skip 策略下仅 1 个成功，3 个跳过
        var date = new DateTimeOffset(2024, 7, 8, 0, 0, 0, TimeSpan.Zero);
        var parsed = new List<ParsedFile>();
        for (var i = 0; i < 4; i++)
        {
            var sub = Path.Combine(_src, $"skip{i}");
            Directory.CreateDirectory(sub);
            WriteFile(Path.Combine(sub, "base.jpg"), $"v{i}");
            parsed.Add(new ParsedFile(
                new MediaFile(Path.Combine(sub, "base.jpg"), i + 100, "jpg") { Source = new LocalMediaSource(Path.Combine(sub, "base.jpg")) },
                date, "FileName"));
        }
        var result = new AnalysisResult(_src, DateTimeOffset.Now, parsed, []);
        var plan = new ArchivePlanner(ClassificationLevel.Day).Plan(result, _out);

        var r = await Run(plan, FileOperation.Copy, ExistAction.Skip, fixMtime: false);
        Assert.Equal(1, r.Succeeded);
        Assert.Equal(3, r.Skipped);
        Assert.Equal(0, r.Failed);
        var dir = Path.Combine(_out, "2024", "07", "08");
        Assert.Single(Directory.GetFiles(dir));
    }
}
