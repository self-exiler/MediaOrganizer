namespace MediaOrganizer.Core.Sources;

/// <summary>内存字节源：连接测试探针等无实体文件的场景。</summary>
public sealed class MemoryMediaSource(string name, byte[] bytes) : IMediaSource
{
    private readonly string _name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("名称不能为空", nameof(name)) : name;
    private readonly byte[] _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));

    public string Identifier => _name;
    public string DisplayName => _name;
    public long Length => _bytes.Length;
    public DateTimeOffset? ModifiedTime { get; } = DateTimeOffset.UtcNow;

    public Stream OpenRead() => new MemoryStream(_bytes, writable: false);

    public void Delete() { /* 内存源为测试探针，删除为空操作 */ }
}
