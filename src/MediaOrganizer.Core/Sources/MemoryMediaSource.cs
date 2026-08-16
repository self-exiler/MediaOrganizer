namespace MediaOrganizer.Core.Sources;

/// <summary>内存字节源：连接测试探针等无实体文件的场景。</summary>
public sealed class MemoryMediaSource(string name, byte[] bytes) : IMediaSource
{
    public string Identifier { get; } = name;
    public string DisplayName { get; } = name;
    public long Length { get; } = bytes.Length;
    public DateTimeOffset? ModifiedTime { get; } = DateTimeOffset.UtcNow;

    public Stream OpenRead() => new MemoryStream(bytes, writable: false);

    public void Delete() => throw new NotSupportedException("内存源不支持删除");
}
