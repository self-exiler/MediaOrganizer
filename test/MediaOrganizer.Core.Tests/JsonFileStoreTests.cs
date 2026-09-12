using System.Text.Json;
using MediaOrganizer.Core.Configuration;

namespace MediaOrganizer.Core.Tests;

/// <summary>
/// 防回归：JsonFileStore 曾用裸 File.WriteAllText（注释却声称"原子"），
/// 崩溃/断电会把含凭据的 config.json 截成半截 JSON；且 Load 吞异常静默返回 null，
/// 调用方直接回退出厂设置 → 用户网络位置与凭据全丢且无任何提示。
/// </summary>
public class JsonFileStoreTests : IDisposable
{
    private readonly TempDir _dir = new("mo-jsonfs");

    public void Dispose() => _dir.Dispose();

    private string P(string name) => Path.Combine(_dir, name);

    [Fact]
    public void 保存后可回读()
    {
        JsonFileStore.Save(P("a.json"), new JsonFileStoreTestsPayload { Name = "x", Value = 42 });
        var back = JsonFileStore.Load<JsonFileStoreTestsPayload>(P("a.json"));

        Assert.NotNull(back);
        Assert.Equal("x", back!.Name);
        Assert.Equal(42, back.Value);
    }

    [Fact]
    public void 保存后不残留tmp文件()
    {
        JsonFileStore.Save(P("b.json"), new JsonFileStoreTestsPayload { Name = "y" });

        var leftovers = Directory.GetFiles(_dir, "*.tmp");
        Assert.Empty(leftovers);
    }

    [Fact]
    public void 文件不存在返回null且不报错()
    {
        var errors = new List<string>();
        var result = JsonFileStore.Load<JsonFileStoreTestsPayload>(P("missing.json"), errors.Add);

        Assert.Null(result);
        Assert.Empty(errors);
    }

    [Fact]
    public void 损坏文件触发错误回调()
    {
        File.WriteAllText(P("bad.json"), "{ 这不是合法 JSON");

        var errors = new List<string>();
        var result = JsonFileStore.Load<JsonFileStoreTestsPayload>(P("bad.json"), errors.Add);

        Assert.Null(result);
        Assert.Single(errors);
        Assert.Contains("bad.json", errors[0]);
    }

    [Fact]
    public void 损坏文件隔离后保留副本()
    {
        var path = P("bad2.json");
        File.WriteAllText(path, "{ 这不是合法 JSON");

        var quarantined = JsonFileStore.QuarantineCorrupt(path);

        Assert.NotNull(quarantined);
        Assert.False(File.Exists(path), "原文件应已被移走");
        Assert.True(File.Exists(quarantined!), "隔离副本必须保留，供人工恢复");
        Assert.Equal("{ 这不是合法 JSON", File.ReadAllText(quarantined!));
    }

    [Fact]
    public void 隔离不存在的文件返回null()
    {
        Assert.Null(JsonFileStore.QuarantineCorrupt(P("nope.json")));
    }

    /// <summary>并发保存同一文件后，文件内容始终是完整可解析的 JSON（不出现半截写入）。</summary>
    [Fact]
    public async Task 并发保存后文件始终可解析()
    {
        var path = P("concurrent.json");
        var tasks = Enumerable.Range(0, 50)
            .Select(i => Task.Run(() =>
                JsonFileStore.Save(path, new JsonFileStoreTestsPayload { Name = $"n{i}", Value = i })))
            .ToArray();

        await Task.WhenAll(tasks);

        // 若落盘非原子，这里会读到半截 JSON 而抛异常
        var text = File.ReadAllText(path);
        var parsed = JsonSerializer.Deserialize<JsonFileStoreTestsPayload>(text, JsonFileStore.JsonOptions);

        Assert.NotNull(parsed);
        Assert.StartsWith("n", parsed!.Name);
    }

    private sealed class JsonFileStoreTestsPayload
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }
}
