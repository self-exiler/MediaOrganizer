using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Execution;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Planning;

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
            new MediaFile(Path.Combine(_src, x.Name), i + 100, Path.GetExtension(x.Name).TrimStart('.')),
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

    [Fact]
    public void Copy创建分级目录()
    {
        WriteFile(Path.Combine(_src, "a.jpg"));
        var plan = Plan(("a.jpg", new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero)));
        var r = new FileOperator(FileOperation.Copy, ExistAction.Skip, fixMtime: false).Execute(plan);

        Assert.Equal(1, r.Succeeded);
        Assert.True(File.Exists(Path.Combine(_out, "2024", "01", "15", "a.jpg")));
        Assert.True(File.Exists(Path.Combine(_src, "a.jpg")), "copy 应保留源文件");
    }

    [Fact]
    public void Move删除源文件()
    {
        WriteFile(Path.Combine(_src, "b.jpg"));
        var plan = Plan(("b.jpg", new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero)));
        var r = new FileOperator(FileOperation.Move, ExistAction.Skip, fixMtime: false).Execute(plan);

        Assert.Equal(1, r.Succeeded);
        Assert.True(File.Exists(Path.Combine(_out, "2024", "01", "15", "b.jpg")));
        Assert.False(File.Exists(Path.Combine(_src, "b.jpg")), "move 应删除源文件");
    }

    [Fact]
    public void 同名Skip跳过()
    {
        WriteFile(Path.Combine(_src, "a.jpg"), "v1");
        WriteFile(Path.Combine(_src, "dup.jpg"), "v2");
        var date = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var plan = Plan(("a.jpg", date), ("dup.jpg", date));
        WriteFile(Path.Combine(_out, "2024", "01", "15", "dup.jpg"), "existing");

        var r = new FileOperator(FileOperation.Copy, ExistAction.Skip, fixMtime: false).Execute(plan);
        Assert.Equal(1, r.Succeeded);
        Assert.Equal(1, r.Skipped);
    }

    [Fact]
    public void 同名Rename追加序号()
    {
        WriteFile(Path.Combine(_src, "a.jpg"));
        WriteFile(Path.Combine(_src, "dup.jpg"));
        var date = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var plan = Plan(("a.jpg", date), ("dup.jpg", date));
        WriteFile(Path.Combine(_out, "2024", "01", "15", "dup.jpg"));

        var r = new FileOperator(FileOperation.Copy, ExistAction.Rename, fixMtime: false).Execute(plan);
        Assert.Equal(2, r.Succeeded);
        Assert.Equal(1, r.Renamed);
        Assert.True(File.Exists(Path.Combine(_out, "2024", "01", "15", "dup_1.jpg")));
    }

    [Fact]
    public void 同名Overwrite覆盖()
    {
        WriteFile(Path.Combine(_src, "dup.jpg"), "new");
        var date = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var plan = Plan(("dup.jpg", date));
        WriteFile(Path.Combine(_out, "2024", "01", "15", "dup.jpg"), "old");

        var r = new FileOperator(FileOperation.Copy, ExistAction.Overwrite, fixMtime: false).Execute(plan);
        Assert.Equal(1, r.Succeeded);
        Assert.Equal(1, r.Overwritten);
        Assert.Equal("new", File.ReadAllText(Path.Combine(_out, "2024", "01", "15", "dup.jpg")));
    }

    [Fact]
    public void 缺失源文件计入失败()
    {
        // a.jpg 未创建
        var plan = Plan(("missing.jpg", new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero)));
        var r = new FileOperator(FileOperation.Copy, ExistAction.Skip, fixMtime: false).Execute(plan);
        Assert.Equal(0, r.Succeeded);
        Assert.Equal(1, r.Failed);
        Assert.Single(r.Errors);
    }

    [Fact]
    public void FixMtime矫正目标文件时间()
    {
        WriteFile(Path.Combine(_src, "t.jpg"));
        var date = new DateTimeOffset(2024, 3, 20, 10, 30, 0, TimeSpan.Zero);
        var plan = Plan(("t.jpg", date));
        new FileOperator(FileOperation.Copy, ExistAction.Skip, fixMtime: true).Execute(plan);

        var target = Path.Combine(_out, "2024", "03", "20", "t.jpg");
        Assert.Equal(date.UtcDateTime, File.GetLastWriteTimeUtc(target));
    }
}
