namespace MediaOrganizer.Core.Extraction;

/// <summary>
/// TagLib# 流抽象（ADR-0006 决策 2）：让容器元数据解析从 IMediaSource 流读取，
/// 桌面传 FileStream、Android 传 ContentResolver 流——同一份解析代码双端共享。
/// TagLib 需要可 seek 的流；不可 seek 的提供者降级拷贝到 MemoryStream（ADR-0006 风险表）。
/// </summary>
public sealed class TagLibStreamFileAbstraction(string name, Func<Stream> openRead) : TagLib.File.IFileAbstraction
{
    public string Name { get; } = name;

    public Stream ReadStream => EnsureSeekable(openRead());

    public Stream WriteStream => throw new NotSupportedException("只读场景不支持写入流");

    public void CloseStream(Stream stream) => stream.Close();

    /// <summary>TagLib 解析需要随机访问：SAF 等提供者给出的流不可 seek 时整段拷入内存。</summary>
    public static Stream EnsureSeekable(Stream stream)
    {
        if (stream.CanSeek) return stream;
        try
        {
            var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            buffer.Position = 0;
            return buffer;
        }
        finally
        {
            stream.Close();
        }
    }
}
