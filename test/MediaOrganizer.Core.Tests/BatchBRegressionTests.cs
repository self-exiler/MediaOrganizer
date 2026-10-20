using MediaOrganizer.Core;
using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Execution;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Planning;
using MediaOrganizer.Core.Sources;
using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core.Tests;

/// <summary>
/// 批次 B 回归用例：审计 8（取消后清理）、11（配置事件拆分）、12（按 Identifier 判定）、
/// 18（分析结果定序）、19（mtime 失败降级）。
/// </summary>
public class BatchBRegressionTests : IDisposable
{
    private readonly TempDir _src = new("mo-b-src");
    private readonly TempDir _out = new("mo-b-out");

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

    private ArchivePlan Plan(params string[] names)
    {
        var date = new DateTimeOffset(2024, 5, 10, 0, 0, 0, TimeSpan.Zero);
        var parsed = names.Select(n => new ParsedFile(
            new MediaFile(Path.Combine(_src, n), 100, "jpg")
            {
                Source = new LocalMediaSource(Path.Combine(_src, n))
            },
            date, "FileName")).ToArray();
        return new ArchivePlanner(ClassificationLevel.Day)
            .Plan(new AnalysisResult(_src, DateTimeOffset.Now, parsed, []), _out);
    }

    /// <summary>CopyFrom 抛「传输中被取消」，Delete 记录令牌是否可取消（模拟 SMB 闸在取消令牌下拒service）。</summary>
    private sealed class CleanupTokenProbingStorage(IFileStorage inner) : IFileStorage
    {
        public List<bool> DeleteUsedCancellableToken { get; } = [];

        public Task<bool> ExistsAsync(string r, CancellationToken ct = default) => inner.ExistsAsync(r, ct);
        public Task CreateDirectoryAsync(string r, CancellationToken ct = default) => inner.CreateDirectoryAsync(r, ct);
        public Task<long> GetLengthAsync(string r, CancellationToken ct = default) => inner.GetLengthAsync(r, ct);
        public Task<long> CopyFromAsync(IMediaSource s, string r, IProgress<long>? p = null, CancellationToken ct = default)
            => Task.FromException<long>(new OperationCanceledException("模拟传输中途取消"));
        public Task DeleteAsync(string r, CancellationToken ct = default)
        {
            DeleteUsedCancellableToken.Add(ct.CanBeCanceled);
            return inner.DeleteAsync(r, ct);
        }
        public Task MoveAsync(string from, string to, CancellationToken ct = default) => inner.MoveAsync(from, to, ct);
        public Task SetModifiedUtcAsync(string r, DateTime utc, CancellationToken ct = default) => inner.SetModifiedUtcAsync(r, utc, ct);
        public void Dispose() => inner.Dispose();
    }

    [Fact]
    public async Task 传输中取消_清理临时文件不得复用已取消令牌()
    {
        // 审计 8：清理若沿用已取消的 ct，SMB 的 _gate.WaitAsync(ct) 立即抛出被吞 → .mo-tmp 残留
        WriteFile(Path.Combine(_src, "a.jpg"));
        var storage = new CleanupTokenProbingStorage(new LocalFileStorage(_out));
        var plan = Plan("a.jpg");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new FileOperator(storage, FileOperation.Copy, ExistAction.Skip, fixMtime: false).ExecuteAsync(plan));

        Assert.Contains(false, storage.DeleteUsedCancellableToken); // 至少一次清理用了 CancellationToken.None
    }

    /// <summary>SetModifiedUtcAsync 恒抛异常的包装。</summary>
    private sealed class MtimeFailingStorage(IFileStorage inner) : IFileStorage
    {
        public int CopyCount;
        public Task<bool> ExistsAsync(string r, CancellationToken ct = default) => inner.ExistsAsync(r, ct);
        public Task CreateDirectoryAsync(string r, CancellationToken ct = default) => inner.CreateDirectoryAsync(r, ct);
        public Task<long> GetLengthAsync(string r, CancellationToken ct = default) => inner.GetLengthAsync(r, ct);
        public Task<long> CopyFromAsync(IMediaSource s, string r, IProgress<long>? p = null, CancellationToken ct = default)
        {
            Interlocked.Increment(ref CopyCount);
            return inner.CopyFromAsync(s, r, p, ct);
        }
        public Task DeleteAsync(string r, CancellationToken ct = default) => inner.DeleteAsync(r, ct);
        public Task MoveAsync(string from, string to, CancellationToken ct = default) => inner.MoveAsync(from, to, ct);
        public Task SetModifiedUtcAsync(string r, DateTime utc, CancellationToken ct = default)
            => Task.FromException(new IOException("模拟 mtime 设置失败（只读挂载等）"));
        public void Dispose() => inner.Dispose();
    }

    [Fact]
    public async Task mtime失败降级为警告_文件仍算成功且不重传()
    {
        // 审计 19
        WriteFile(Path.Combine(_src, "a.jpg"));
        var storage = new MtimeFailingStorage(new LocalFileStorage(_out));
        var plan = Plan("a.jpg");

        var r = await new FileOperator(storage, FileOperation.Copy, ExistAction.Skip, fixMtime: true).ExecuteAsync(plan);

        Assert.Equal(1, r.Succeeded);
        Assert.Equal(0, r.Failed);
        Assert.Empty(r.Errors);
        Assert.Equal(1, storage.CopyCount); // 不得因 mtime 失败重传
        Assert.True(File.Exists(Path.Combine(_out, "2024", "05", "10", "a.jpg")));
    }

    [Fact]
    public void 网络位置保存事件不触发提取链重建()
    {
        // 审计 11：SaveConfig(notifyChanged:false)+NotifyChanged（网络增删的现行写法）不得动 ChainChanged
        var dir = new TempDir("mo-b-state");
        try
        {
            var state = new AppState(new AppConfig(), PatternsStore.GetBuiltinPatterns(),
                Path.Combine(dir, "config.json"), Path.Combine(dir, "patterns.json"));
            int changed = 0, chain = 0;
            state.Changed += () => changed++;
            state.ChainChanged += () => chain++;

            state.SaveConfig(notifyChanged: false);
            state.NotifyChanged();
            Assert.Equal(1, changed);
            Assert.Equal(0, chain);

            state.NotifyChainChanged();
            Assert.Equal(2, changed);
            Assert.Equal(1, chain);

            state.SavePatterns();
            Assert.Equal(3, changed);
            Assert.Equal(2, chain); // SavePatterns 必带链重建
        }
        finally
        {
            dir.Dispose();
        }
    }

    [Fact]
    public async Task 待处理移动按完整路径记账_同名不同目录不误移除()
    {
        // 审计 12：MovedIdentifiers 必须是源完整路径而非文件名
        var subA = Path.Combine(_src, "a");
        var subB = Path.Combine(_src, "b");
        WriteFile(Path.Combine(subA, "IMG_0001.jpg"), "aaa");
        var okPath = Path.Combine(subA, "IMG_0001.jpg");
        var failPath = Path.Combine(subB, "IMG_0001.jpg"); // 同名不同目录，源端不可用 → 必失败

        var files = new[]
        {
            new MediaFile(okPath, 3, "jpg") { Source = new LocalMediaSource(okPath) },
            new MediaFile(failPath, 3, "jpg") { Source = null },
        };

        var r = await new PendingFileMover().MoveAsync(files, new LocalFileStorage(_out));

        Assert.Equal(1, r.Moved);
        Assert.Equal(1, r.Failed);
        Assert.Equal([okPath], r.MovedIdentifiers); // 按文件名匹配会让失败的那个也被「移除」
    }

    [Fact]
    public async Task 分析结果按路径定序_重复分析顺序一致()
    {
        // 审计 18：ConcurrentQueue 完成时序不定 → Rename 后缀跨次不稳定
        foreach (var i in Enumerable.Range(0, 40))
            WriteFile(Path.Combine(_src, $"f{i:D2}_20240115.jpg"), "x");

        var analyzer = CoreFactory.CreateAnalyzer(
            new AppConfig { Extraction = new ExtractionConfig { Extractors = [
                new ExtractorSetting { Name = "Exif", Enabled = false, Weight = 1.2 },
                new ExtractorSetting { Name = "FileName", Enabled = true, Weight = 1.1 },
                new ExtractorSetting { Name = "FileSystem", Enabled = false, Weight = 0.8 }] } },
            PatternsStore.GetBuiltinPatterns());

        var r1 = await analyzer.AnalyzeAsync(_src);
        var r2 = await analyzer.AnalyzeAsync(_src);

        Assert.Equal(r1.Parsed.Select(p => p.File.Path), r2.Parsed.Select(p => p.File.Path));
        Assert.Equal(r1.Parsed.Select(p => p.File.Path).OrderBy(x => x, StringComparer.Ordinal), r1.Parsed.Select(p => p.File.Path));
    }
}
