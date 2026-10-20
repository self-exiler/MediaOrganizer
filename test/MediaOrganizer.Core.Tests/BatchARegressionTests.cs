using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Execution;
using MediaOrganizer.Core.Extraction;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Patterns;
using MediaOrganizer.Core.Planning;
using MediaOrganizer.Core.Sources;
using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core.Tests;

/// <summary>
/// 2026-10-20 外部审计批次 A（数据正确性）回归用例：
/// 审计 1（UTC/本地口径）、2（Overwrite 删除时机）、3（大小写互斥闸）、
/// 5（紧凑日期抢匹配）、6（文件名口径统一）。审计 4 见 WebDavFileStorageTests。
/// </summary>
public class BatchARegressionTests : IDisposable
{
    private readonly TempDir _src = new("mo-a-src");
    private readonly TempDir _out = new("mo-a-out");

    public void Dispose()
    {
        _src.Dispose();
        _out.Dispose();
    }

    private static void WriteFile(string path, string content = "hello")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private ArchivePlan Plan(params (string Name, DateTimeOffset Date)[] items)
    {
        var parsed = items.Select(x => new ParsedFile(
            new MediaFile(Path.Combine(_src, x.Name), 100, Path.GetExtension(x.Name).TrimStart('.'))
            {
                Source = new LocalMediaSource(Path.Combine(_src, x.Name))
            },
            x.Date, "FileName")).ToArray();
        return new ArchivePlanner(ClassificationLevel.Day)
            .Plan(new AnalysisResult(_src, DateTimeOffset.Now, parsed, []), _out);
    }

    private Task<FileOperationResult> Run(ArchivePlan plan, ExistAction exist, IFileStorage? storage = null)
        => new FileOperator(storage ?? new LocalFileStorage(_out), FileOperation.Copy, exist, fixMtime: false)
            .ExecuteAsync(plan);

    // ---- 审计 1：统一按本地时间归档 ----

    [Fact]
    public void UTC口径的日期按本地日历日归档()
    {
        // 2023-12-31T18:00Z = 东八区 2024-01-01T02:00；修复前 BuildRelative 直接取 UTC 字段 → 2023/12/31
        var utc = new DateTimeOffset(2023, 12, 31, 18, 0, 0, TimeSpan.Zero);
        var local = utc.ToLocalTime(); // 开发机（UTC+8）上 = 2024-01-01 02:00
        var plan = new ArchivePlanner(ClassificationLevel.Day,
                now: new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero))
            .Plan(new AnalysisResult("C:\\src", DateTimeOffset.Now,
                [new ParsedFile(new MediaFile(@"C:\src\a.jpg", 10, "jpg"), utc, "FileName")], []), "D:\\out");
        Assert.Equal($"{local.Year:0000}/{local.Month:00}/{local.Day:00}/a.jpg", plan.Files[0].RelativeTarget);
    }

    [Fact]
    public async Task 时间戳文件名归入本地日期目录()
    {
        // mm_export + UTC 18:00 的毫秒时间戳 → 提取结果为 UTC 口径 → 规划层必须转本地
        long ts = new DateTimeOffset(2023, 12, 31, 18, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var extractor = new FileNameExtractor(PatternsStore.GetBuiltinPatterns(), true, 1.1);
        var file = new MediaFile(Path.Combine(_src, $"mm_export{ts}.jpg"), 10, "jpg");
        var date = extractor.Extract(file);
        Assert.NotNull(date);
        var local = date!.Value.ToLocalTime();
        var plan = new ArchivePlanner(ClassificationLevel.Day,
                now: new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero))
            .Plan(new AnalysisResult(_src, DateTimeOffset.Now,
                [new ParsedFile(file, date.Value, "FileName")], []), _out);
        Assert.Equal($"{local.Year:0000}/{local.Month:00}/{local.Day:00}/mm_export{ts}.jpg",
            plan.Files[0].RelativeTarget);
    }

    // ---- 审计 2：Overwrite 先传后删 ----

    /// <summary>CopyFromAsync 必失败的存储：模拟传输通道全程不可用。</summary>
    private sealed class FailingCopyStorage(IFileStorage inner) : IFileStorage
    {
        public Task<bool> ExistsAsync(string r, CancellationToken ct = default) => inner.ExistsAsync(r, ct);
        public Task CreateDirectoryAsync(string r, CancellationToken ct = default) => inner.CreateDirectoryAsync(r, ct);
        public Task<long> GetLengthAsync(string r, CancellationToken ct = default) => inner.GetLengthAsync(r, ct);
        public Task<long> CopyFromAsync(IMediaSource s, string r, IProgress<long>? p = null, CancellationToken ct = default)
            => Task.FromException<long>(new IOException("模拟网络中断"));
        public Task DeleteAsync(string r, CancellationToken ct = default) => inner.DeleteAsync(r, ct);
        public Task MoveAsync(string from, string to, CancellationToken ct = default) => inner.MoveAsync(from, to, ct);
        public Task SetModifiedUtcAsync(string r, DateTime utc, CancellationToken ct = default) => inner.SetModifiedUtcAsync(r, utc, ct);
        public void Dispose() => inner.Dispose();
    }

    [Fact]
    public async Task Overwrite传输全程失败_旧文件仍在()
    {
        WriteFile(Path.Combine(_src, "dup.jpg"), "new");
        var date = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var plan = Plan(("dup.jpg", date));
        var target = Path.Combine(_out, "2024", "01", "15", "dup.jpg");
        WriteFile(target, "old");

        var r = await Run(plan, ExistAction.Overwrite, new FailingCopyStorage(new LocalFileStorage(_out)));

        Assert.Equal(1, r.Failed);
        Assert.True(File.Exists(target), "重试耗尽后目标端旧文件不得被删");
        Assert.Equal("old", File.ReadAllText(target));
    }

    /// <summary>按调用顺序记录 Delete/Move 的存储。</summary>
    private sealed class OrderingStorage(IFileStorage inner) : IFileStorage
    {
        public List<string> Log { get; } = [];
        public Task<bool> ExistsAsync(string r, CancellationToken ct = default) => inner.ExistsAsync(r, ct);
        public Task CreateDirectoryAsync(string r, CancellationToken ct = default) => inner.CreateDirectoryAsync(r, ct);
        public Task<long> GetLengthAsync(string r, CancellationToken ct = default) => inner.GetLengthAsync(r, ct);
        public Task<long> CopyFromAsync(IMediaSource s, string r, IProgress<long>? p = null, CancellationToken ct = default)
        {
            Log.Add($"copy:{r}");
            return inner.CopyFromAsync(s, r, p, ct);
        }
        public Task DeleteAsync(string r, CancellationToken ct = default)
        {
            Log.Add($"delete:{r}");
            return inner.DeleteAsync(r, ct);
        }
        public Task MoveAsync(string from, string to, CancellationToken ct = default)
        {
            Log.Add($"move:{from}->{to}");
            return inner.MoveAsync(from, to, ct);
        }
        public Task SetModifiedUtcAsync(string r, DateTime utc, CancellationToken ct = default) => inner.SetModifiedUtcAsync(r, utc, ct);
        public void Dispose() => inner.Dispose();
    }

    [Fact]
    public async Task Overwrite成功路径_删除发生在拷贝之后改名之前()
    {
        WriteFile(Path.Combine(_src, "dup.jpg"), "new");
        var date = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var plan = Plan(("dup.jpg", date));
        WriteFile(Path.Combine(_out, "2024", "01", "15", "dup.jpg"), "old");
        var storage = new OrderingStorage(new LocalFileStorage(_out));

        var r = await Run(plan, ExistAction.Overwrite, storage);

        Assert.Equal(1, r.Succeeded);
        Assert.Equal(1, r.Overwritten);
        Assert.Equal("new", File.ReadAllText(Path.Combine(_out, "2024", "01", "15", "dup.jpg")));
        var iCopy = storage.Log.IndexOf("copy:2024/01/15/dup.jpg.mo-tmp");
        var iDelete = storage.Log.IndexOf("delete:2024/01/15/dup.jpg");
        var iMove = storage.Log.IndexOf("move:2024/01/15/dup.jpg.mo-tmp->2024/01/15/dup.jpg");
        Assert.True(iCopy >= 0 && iDelete > iCopy && iMove > iDelete,
            $"删除必须发生在临时文件写完之后、改名之前：{string.Join(" | ", storage.Log)}");
    }

    // ---- 审计 3：大小写互斥闸（Windows/SMB 不区分大小写） ----

    private ArchivePlan PlanTwoNamesSameDate(string name1, string content1, string name2, string content2)
    {
        var date = new DateTimeOffset(2024, 2, 20, 0, 0, 0, TimeSpan.Zero);
        var parsed = new List<ParsedFile>();
        foreach (var (name, content) in new[] { (name1, content1), (name2, content2) })
        {
            var sub = Path.Combine(_src, "dir-" + name.Length + content);
            Directory.CreateDirectory(sub);
            var path = Path.Combine(sub, name);
            WriteFile(path, content);
            parsed.Add(new ParsedFile(
                new MediaFile(path, 100, "jpg") { Source = new LocalMediaSource(path) }, date, "FileName"));
        }
        return new ArchivePlanner(ClassificationLevel.Day)
            .Plan(new AnalysisResult(_src, DateTimeOffset.Now, parsed, []), _out);
    }

    [Fact]
    public async Task 大小写同名Skip不覆盖不丢文件()
    {
        var plan = PlanTwoNamesSameDate("IMG.JPG", "big", "img.jpg", "small");
        var r = await Run(plan, ExistAction.Skip);

        Assert.Equal(0, r.Failed);
        Assert.Equal(2, r.Succeeded + r.Skipped);
        var dir = Path.Combine(_out, "2024", "02", "20");
        // Windows：1 个文件（1 成功 1 跳过）；大小写敏感 FS：2 个文件。两种都合法，但内容不得互相覆盖
        foreach (var f in Directory.GetFiles(dir))
            Assert.Contains(File.ReadAllText(f), new[] { "big", "small" });
    }

    [Fact]
    public async Task 大小写同名Rename不丢文件()
    {
        var plan = PlanTwoNamesSameDate("IMG.JPG", "big", "img.jpg", "small");
        var r = await Run(plan, ExistAction.Rename);

        Assert.Equal(0, r.Failed);
        Assert.Equal(2, r.Succeeded);
        var dir = Path.Combine(_out, "2024", "02", "20");
        var contents = Directory.GetFiles(dir).Select(File.ReadAllText).OrderBy(x => x).ToArray();
        Assert.Equal(["big", "small"], contents);
        Assert.Empty(Directory.GetFiles(dir, "*.mo-tmp"));
    }

    // ---- 审计 5：紧凑日期 vs 时间戳（1000 例抽样） ----

    [Fact]
    public void 千例毫秒时间戳文件名全部正确解析()
    {
        var extractor = new FileNameExtractor(PatternsStore.GetBuiltinPatterns(), true, 1.1);
        var rand = new Random(20261020);
        long lo = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        long hi = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var formats = new[] { "IMG_{0}.jpg", "VID_{0}.mp4", "{0}.jpg", "mm_export{0}.jpg" };

        for (var i = 0; i < 1000; i++)
        {
            long ts = lo + (long)(rand.NextDouble() * (hi - lo));
            var name = string.Format(formats[i % formats.Length], ts);
            var file = new MediaFile(Path.Combine(_src, name), 10, "jpg");
            var d = extractor.Extract(file);
            Assert.NotNull(d);
            Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(ts), d!.Value);
        }
    }

    [Fact]
    public void 紧凑日期与OPPO等常规文件名解析不变()
    {
        var extractor = new FileNameExtractor(PatternsStore.GetBuiltinPatterns(), true, 1.1);

        var d1 = extractor.Extract(new MediaFile(Path.Combine(_src, "20240115.jpg"), 10, "jpg"));
        Assert.Equal(new DateTime(2024, 1, 15), d1!.Value.DateTime);

        var d2 = extractor.Extract(new MediaFile(Path.Combine(_src, "IMG_20240115_143022.jpg"), 10, "jpg"));
        Assert.Equal(new DateTime(2024, 1, 15, 14, 30, 22), d2!.Value.DateTime);

        var d3 = extractor.Extract(new MediaFile(Path.Combine(_src, "20240115-143022.jpg"), 10, "jpg"));
        Assert.Equal(new DateTime(2024, 1, 15, 14, 30, 22), d3!.Value.DateTime);
    }

    [Fact]
    public void 旧配置紧凑日期抠出非法年份时继续尝试下一模式()
    {
        // 用户 patterns.json 不会自动获得新边界：模拟旧「紧凑日期」正则 + 时间戳模式并存，
        // 靠 PatternEngine 的年份合理性校验（校验下放）跳过 1704 年结果
        var legacy = PatternsStore.GetBuiltinPatterns();
        legacy.First(p => p.Name == "紧凑日期").Pattern = @"(\d{4})(\d{2})(\d{2})"; // 无边界
        var extractor = new FileNameExtractor(legacy, true, 1.1);

        // 17040672 前 8 位是旧紧凑日期的合法月日组合（06/72 中 72 非法，换用真实抢匹配样本）
        long ts = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(); // 1704096000000 → 前8位 17040960：月04日60 非法
        ts = new DateTimeOffset(2024, 1, 15, 8, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();      // 1705283200000 → 前8位 17052832：月05日28 合法 → 修复前被抠成 1705 年
        var d = extractor.Extract(new MediaFile(Path.Combine(_src, $"IMG_{ts}.jpg"), 10, "jpg"));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(ts), d);
    }

    // ---- 审计 6：魔术工具与运行时口径统一（含扩展名文件端到端命中） ----

    [Fact]
    public void 日期在末尾的生成正则对带扩展名文件端到端命中()
    {
        var sample = "Photos20240312.jpg";
        var nameOnly = Path.GetFileNameWithoutExtension(sample); // "Photos20240312"，日期位于末尾

        // 圈选生成：末 8 位标为年(4)月(2)日(2)，前缀不标 → 间隙用 (?:.*) 通配
        var cells = nameOnly.Select((ch, i) => new CharCell(ch, MarkRole.None)).ToList();
        for (var i = 6; i < 10; i++) cells[i] = new CharCell(cells[i].Char, MarkRole.Year);
        for (var i = 10; i < 12; i++) cells[i] = new CharCell(cells[i].Char, MarkRole.Month);
        for (var i = 12; i < 14; i++) cells[i] = new CharCell(cells[i].Char, MarkRole.Day);

        var generated = PatternInferrer.GenerateFromMarks(nameOnly, cells);
        Assert.NotNull(generated);

        // 工具内测试（主文件名口径）与运行时（FileNameExtractor，源带扩展名）必须同口径命中
        var testDate = PatternEngine.TryExtract(nameOnly, generated!, cache: false);
        Assert.Equal(new DateTime(2024, 3, 12), testDate!.Value.DateTime);

        var extractor = new FileNameExtractor([generated!], true, 1.1);
        var runtime = extractor.Extract(new MediaFile(Path.Combine(_src, sample), 10, "jpg"));
        Assert.Equal(new DateTime(2024, 3, 12), runtime!.Value.DateTime);
    }

    [Fact]
    public void 多变体生成的正则不含扩展名且可被自身测试命中()
    {
        var samples = new[] { "IMG_20240115_143022.jpg", "IMG_20240116_090011.jpg", "IMG_20240220_181500.jpg" };
        var nameOnly = samples.Select(Path.GetFileNameWithoutExtension).ToArray();

        var regex = PatternInferrer.GenerateVariantRegex(nameOnly);
        Assert.DoesNotContain(@"\.", regex);

        var pattern = PatternInferrer.Infer(regex, nameOnly);
        Assert.NotNull(pattern);
        foreach (var name in nameOnly)
            Assert.NotNull(PatternEngine.TryExtract(name, pattern!, cache: false));

        // 运行时带扩展名同样命中（口径统一的另一侧）
        var extractor = new FileNameExtractor([pattern!], true, 1.1);
        foreach (var s in samples)
            Assert.NotNull(extractor.Extract(new MediaFile(Path.Combine(_src, s), 10, "jpg")));
    }

    // ---- 审计 4 的 FileOperator 侧：Exists 瞬时故障走退避重试 ----

    /// <summary>第一次 Exists 抛异常、之后放行的存储。</summary>
    private sealed class FlakyExistsStorage(IFileStorage inner) : IFileStorage
    {
        private int _calls;
        public Task<bool> ExistsAsync(string r, CancellationToken ct = default)
            => Interlocked.Increment(ref _calls) == 1
                ? Task.FromException<bool>(new IOException("模拟瞬时网络故障"))
                : inner.ExistsAsync(r, ct);
        public Task CreateDirectoryAsync(string r, CancellationToken ct = default) => inner.CreateDirectoryAsync(r, ct);
        public Task<long> GetLengthAsync(string r, CancellationToken ct = default) => inner.GetLengthAsync(r, ct);
        public Task<long> CopyFromAsync(IMediaSource s, string r, IProgress<long>? p = null, CancellationToken ct = default)
            => inner.CopyFromAsync(s, r, p, ct);
        public Task DeleteAsync(string r, CancellationToken ct = default) => inner.DeleteAsync(r, ct);
        public Task MoveAsync(string from, string to, CancellationToken ct = default) => inner.MoveAsync(from, to, ct);
        public Task SetModifiedUtcAsync(string r, DateTime utc, CancellationToken ct = default) => inner.SetModifiedUtcAsync(r, utc, ct);
        public void Dispose() => inner.Dispose();
    }

    [Fact]
    public async Task Exists瞬时故障_重试后成功而非误判不存在()
    {
        WriteFile(Path.Combine(_src, "a.jpg"), "new");
        var date = new DateTimeOffset(2024, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var plan = Plan(("a.jpg", date));
        WriteFile(Path.Combine(_out, "2024", "04", "01", "a.jpg"), "old");

        var r = await Run(plan, ExistAction.Skip, new FlakyExistsStorage(new LocalFileStorage(_out)));

        // 首次 Exists 抛错 → 重试成功 → 判定已存在 → Skip（修复前：吞错返回 false → 覆盖旧文件）
        Assert.Equal(1, r.Skipped);
        Assert.Equal(0, r.Succeeded);
        Assert.Equal("old", File.ReadAllText(Path.Combine(_out, "2024", "04", "01", "a.jpg")));
    }
}
