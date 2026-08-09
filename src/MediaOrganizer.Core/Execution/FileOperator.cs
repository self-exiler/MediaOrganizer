using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Planning;

namespace MediaOrganizer.Core.Execution;

public sealed record FileOperationResult(
    int Succeeded, int Skipped, int Overwritten, int Renamed, int Failed,
    IReadOnlyList<string> Errors);

/// <summary>执行 copy/move，处理同名策略与 mtime 矫正（SRS FR-5.2 / FR-5.3 / FR-5.5）。</summary>
public sealed class FileOperator
{
    private readonly FileOperation _operation;
    private readonly ExistAction _existAction;
    private readonly bool _fixMtime;

    public FileOperator(FileOperation operation, ExistAction existAction, bool fixMtime)
    {
        _operation = operation;
        _existAction = existAction;
        _fixMtime = fixMtime;
    }

    public FileOperationResult Execute(ArchivePlan plan, IProgress<double>? progress = null)
    {
        int ok = 0, skip = 0, overwrite = 0, rename = 0, fail = 0;
        var errors = new List<string>();
        var files = plan.Files;

        for (var i = 0; i < files.Count; i++)
        {
            var f = files[i];
            var target = Path.Combine(plan.OutputRoot, f.RelativeTarget.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                var finalTarget = ResolveCollision(target, ref skip, ref overwrite, ref rename);
                if (finalTarget is null) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(finalTarget)!);
                Transfer(f.Source.Path, finalTarget);

                if (_fixMtime)
                    File.SetLastWriteTimeUtc(finalTarget, f.Date.UtcDateTime);
                ok++;
            }
            catch (Exception ex)
            {
                fail++;
                errors.Add($"{f.Source.FileName} -> {target}: {ex.Message}");
            }
            progress?.Report((double)(i + 1) / files.Count);
        }
        return new FileOperationResult(ok, skip, overwrite, rename, fail, errors);
    }

    private string? ResolveCollision(string target, ref int skip, ref int overwrite, ref int rename)
    {
        if (!File.Exists(target))
            return target;

        switch (_existAction)
        {
            case ExistAction.Skip:
                skip++;
                return null;
            case ExistAction.Overwrite:
                overwrite++;
                return target;
            default:
                rename++;
                return FindFreeName(target);
        }
    }

    private void Transfer(string source, string target)
    {
        if (_operation == FileOperation.Copy)
        {
            File.Copy(source, target, overwrite: true);
            return;
        }

        // Move：优先 File.Move；跨卷/占用时回退 copy+delete
        try
        {
            File.Move(source, target, overwrite: true);
        }
        catch (IOException)
        {
            File.Copy(source, target, overwrite: true);
            File.Delete(source);
        }
    }

    private static string FindFreeName(string target)
    {
        var dir = Path.GetDirectoryName(target)!;
        var name = Path.GetFileNameWithoutExtension(target);
        var ext = Path.GetExtension(target);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
