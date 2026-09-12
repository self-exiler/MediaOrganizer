namespace MediaOrganizer.Core.Tests;

/// <summary>测试临时目录：构造即创建，Dispose 递归删除；隐式转 string 可直接参与 Path.Combine。</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir(string? prefix = null)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            (prefix ?? "mo-test") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public static implicit operator string(TempDir dir) => dir.Path;

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
