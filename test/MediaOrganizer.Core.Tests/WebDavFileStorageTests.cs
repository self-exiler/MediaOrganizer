using MediaOrganizer.Core.Sources;
using MediaOrganizer.Core.Storage;
using WebDAVClient;
using WebDAVClient.Helpers;
using WebDAVClient.Model;

namespace MediaOrganizer.Core.Tests;

/// <summary>
/// 审计 4 回归：ExistsAsync 仅 404 视为不存在、其余上抛；CopyFromAsync 上传后回查实际长度自证。
/// 用注入 IClient 假实现绕开真实 HTTP。
/// </summary>
public class WebDavFileStorageTests
{
    private sealed class FakeClient : IClient
    {
        public Dictionary<string, Item> Files { get; } = [];
        public List<string> CreateDirCalls { get; } = [];
        public Func<string, Item>? GetFileOverride { get; set; }
        public long? UploadReportedLength { get; set; } // null = 如实上报流长度
        public bool UploadStoresFile { get; set; } = true;
        public int FailGetFileFirstN { get; set; }      // 前 N 次 GetFile 抛 HttpRequestException
        private int _getFileCalls;

        public string Server { get; set; } = "http://localhost";
        public string BasePath { get; set; } = "";
        public int? Port { get; set; }
        public string UserAgent { get; set; } = "fake";
        public string UserAgentVersion { get; set; } = "0";
        public ICollection<KeyValuePair<string, string>> CustomHeaders { get; set; } = [];

        public Task<Item> GetFile(string path, CancellationToken ct = default)
        {
            _getFileCalls++;
            if (_getFileCalls <= FailGetFileFirstN)
                throw new HttpRequestException("模拟瞬时网络故障");
            if (GetFileOverride is { } ov)
            {
                var item = ov(path);
                if (item is null) throw new WebDAVException(404, "not found");
                return Task.FromResult(item);
            }
            return Files.TryGetValue(path, out var found)
                ? Task.FromResult(found)
                : Task.FromException<Item>(new WebDAVException(404, "not found"));
        }

        public Task<Item> GetFile(string path, IEnumerable<PropertyName> properties, CancellationToken ct = default)
            => GetFile(path, ct);

        public Task<bool> Upload(string remoteFilePath, Stream content, string name, string? lockToken = null, CancellationToken ct = default)
        {
            long len = content.Length - content.Position;
            if (UploadStoresFile)
                Files[remoteFilePath + "/" + name] = new Item { ContentLength = UploadReportedLength ?? len };
            return Task.FromResult(true);
        }

        public Task<bool> MoveFile(string src, string dst, bool overwrite = true, string? sourceLockToken = null, string? destinationLockToken = null, CancellationToken ct = default)
        {
            if (!Files.TryGetValue(src, out var item))
                throw new WebDAVException(404, "source missing");
            Files.Remove(src);
            Files[dst] = item;
            return Task.FromResult(true);
        }

        public Task DeleteFile(string path = "/", string? lockToken = null, CancellationToken ct = default)
        {
            if (!Files.Remove(path))
                throw new WebDAVException(404, "not found");
            return Task.CompletedTask;
        }

        public Task<bool> CreateDir(string remotePath, string name, CancellationToken ct = default)
        {
            CreateDirCalls.Add(remotePath + "/" + name);
            return Task.FromResult(true);
        }

        public void Dispose() { }

        // ---- 以下成员与测试无关 ----
        public Task<ServerOptions> GetServerOptions(string path = "/", CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IEnumerable<Item>> List(string path = "/", int? depth = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IEnumerable<Item>> List(string path, int? depth, IEnumerable<PropertyName> properties, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IEnumerable<Item>> ListPropertyNames(string path = "/", int? depth = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Item> GetFolder(string path = "/", CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Item> GetFolder(string path, IEnumerable<PropertyName> properties, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Item> GetFolderPropertyNames(string path = "/", CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Item> GetFilePropertyNames(string path = "/", CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Stream> Download(string remoteFilePath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Stream> DownloadPartial(string remoteFilePath, long startBytes, long endBytes, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> UploadPartial(string remoteFilePath, Stream content, string name, long startBytes, long endBytes, string? lockToken = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteFolder(string path = "/", string? lockToken = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> MoveFolder(string src, string dst, bool overwrite = true, string? sourceLockToken = null, string? destinationLockToken = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> CopyFolder(string src, string dst, bool overwrite = true, string? destinationLockToken = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> CopyFile(string src, string dst, bool overwrite = true, string? destinationLockToken = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LockInfo> LockFile(string filePath, int timeoutSeconds = 600, string? owner = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LockInfo> LockFolder(string folderPath, int timeoutSeconds = 600, string? owner = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UnlockFile(string filePath, string lockToken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UnlockFolder(string folderPath, string lockToken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LockInfo> RefreshLock(string path, string lockToken, int timeoutSeconds = 600, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SetProperty(string path, string propertyName, string propertyNamespace, string value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RemoveProperty(string path, string propertyName, string propertyNamespace, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StreamSource(byte[] data) : IMediaSource
    {
        public string Identifier => "fake://a.jpg";
        public string DisplayName => "a.jpg";
        public long Length => data.Length;
        public DateTimeOffset? ModifiedTime => null;
        public Stream OpenRead() => new MemoryStream(data, writable: false);
        public void Delete() { }
    }

    private static WebDavFileStorage Storage(FakeClient client) => new(client, "");

    [Fact]
    public async Task Exists_仅404返回false()
    {
        var client = new FakeClient();
        Assert.False(await Storage(client).ExistsAsync("2024/01/15/a.jpg"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public async Task Exists_非404状态码上抛(int code)
    {
        var client = new FakeClient
        {
            GetFileOverride = _ => throw new WebDAVException(code, "transient")
        };
        await Assert.ThrowsAnyAsync<Exception>(() => Storage(client).ExistsAsync("2024/01/15/a.jpg"));
    }

    [Fact]
    public async Task Exists_HTTP异常上抛而非返回false()
    {
        var client = new FakeClient
        {
            GetFileOverride = _ => throw new HttpRequestException("net down")
        };
        await Assert.ThrowsAsync<HttpRequestException>(() => Storage(client).ExistsAsync("x"));
    }

    [Fact]
    public async Task CreateDirectory_探测故障时宽容继续创建()
    {
        var client = new FakeClient { FailGetFileFirstN = 99 };
        // 目录探测全程抛 HttpRequestException → 按不存在处理并尝试创建，不得向外抛
        await Storage(client).CreateDirectoryAsync("2024/01/15");
        Assert.NotEmpty(client.CreateDirCalls);
    }

    [Fact]
    public async Task CopyFrom_回查长度一致返回实际值()
    {
        var client = new FakeClient();
        var r = await Storage(client).CopyFromAsync(new StreamSource(new byte[7]), "2024/01/15/a.jpg.mo-tmp");
        Assert.Equal(7, r);
    }

    [Fact]
    public async Task CopyFrom_回查长度不符抛异常()
    {
        var client = new FakeClient { UploadReportedLength = 5 }; // 服务端只存下 5 字节
        await Assert.ThrowsAsync<IOException>(
            () => Storage(client).CopyFromAsync(new StreamSource(new byte[7]), "2024/01/15/a.jpg.mo-tmp"));
    }

    [Fact]
    public async Task CopyFrom_回查不到文件抛异常()
    {
        var client = new FakeClient { UploadStoresFile = false }; // 上传「成功」但服务端无此文件
        await Assert.ThrowsAsync<IOException>(
            () => Storage(client).CopyFromAsync(new StreamSource(new byte[7]), "2024/01/15/a.jpg.mo-tmp"));
    }
}
