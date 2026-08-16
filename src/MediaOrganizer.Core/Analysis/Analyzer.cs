using System.Collections.Concurrent;
using MediaOrganizer.Core.Extraction;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Scanning;

namespace MediaOrganizer.Core.Analysis;

public sealed record AnalysisProgress(int Processed, int Total, int Succeeded, int Failed);

/// <summary>分析编排：扫描 → 并行提取 → 汇总（SRS FR-4 / FR-A4）。扫描器面向 IFileScanner 接口，Android 可注入 SAF 实现。</summary>
public sealed class Analyzer
{
    private readonly IFileScanner _scanner;
    private readonly ExtractorChain _chain;
    private readonly int _maxDegreeOfParallelism;
    private readonly int _progressInterval;

    public Analyzer(IFileScanner scanner, ExtractorChain chain, int maxDegreeOfParallelism = 0, int progressInterval = 10)
    {
        _scanner = scanner;
        _chain = chain;
        _maxDegreeOfParallelism = maxDegreeOfParallelism <= 0 ? Environment.ProcessorCount : maxDegreeOfParallelism;
        _progressInterval = Math.Max(1, progressInterval);
    }

    public async Task<AnalysisResult> AnalyzeAsync(string sourceDir, IProgress<AnalysisProgress>? progress = null, CancellationToken ct = default)
    {
        var files = _scanner.Scan(sourceDir);
        var parsed = new ConcurrentQueue<ParsedFile>();
        var unparsed = new ConcurrentQueue<UnparsedFile>();
        long processed = 0;
        var total = files.Count;

        var options = new ParallelOptions { MaxDegreeOfParallelism = _maxDegreeOfParallelism, CancellationToken = ct };
        await Parallel.ForEachAsync(files, options, (file, token) =>
        {
            token.ThrowIfCancellationRequested();
            var result = _chain.TryExtract(file);
            if (result is not null)
                parsed.Enqueue(new ParsedFile(file, result.Date, result.Source));
            else
                unparsed.Enqueue(new UnparsedFile(file, "NoValidDate"));

            var done = Interlocked.Increment(ref processed);
            if (progress is not null && (done % _progressInterval == 0 || done == total))
                progress.Report(new AnalysisProgress((int)done, total, parsed.Count, unparsed.Count));
            return ValueTask.CompletedTask;
        });

        return new AnalysisResult(sourceDir, DateTimeOffset.Now, parsed.ToArray(), unparsed.ToArray());
    }
}
