using MediaOrganizer.Core.Configuration;

namespace MediaOrganizer.Core.Tests;

public class ConfigStoreTests
{
    [Fact]
    public void 配置往返保持默认值()
    {
        var cfg = new AppConfig
        {
            Paths = new PathsConfig { SourceDir = @"D:\photos", OutputDir = @"D:\out" },
            Extraction = new ExtractionConfig { MaxYearsPast = 25, FutureDateBufferDays = 3 }
        };
        var path = Path.Combine(Path.GetTempPath(), "mo-config-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            JsonFileStore.Save(path, cfg);
            var loaded = JsonFileStore.Load<AppConfig>(path) ?? new AppConfig();
            Assert.Equal(1, loaded.Version);
            Assert.Equal(@"D:\photos", loaded.Paths.SourceDir);
            Assert.Equal(25, loaded.Extraction.MaxYearsPast);
            Assert.Equal(3, loaded.Extraction.FutureDateBufferDays);
            Assert.Equal(FileOperation.Copy, loaded.Execute.Operation);
            Assert.Equal(ClassificationLevel.Day, loaded.Execute.ClassificationLevel);
            Assert.Equal(3, loaded.Extraction.Extractors.Count);
            Assert.Equal(1.2, loaded.Extraction.Extractors[0].Weight);
            Assert.False(loaded.Extraction.Extractors[2].Enabled);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void 损坏配置回退默认值()
    {
        var path = Path.Combine(Path.GetTempPath(), "mo-config-bad-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ not valid json !!");
        try
        {
            // JsonFileStore.Load 在解析失败时返回 null，调用方兜底为 new AppConfig()
            var loaded = JsonFileStore.Load<AppConfig>(path) ?? new AppConfig();
            Assert.Equal(30, loaded.Extraction.MaxYearsPast);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void 模式存储读写往返()
    {
        var path = Path.Combine(Path.GetTempPath(), "mo-patterns-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var patterns = new List<PatternDefinition>
            {
                new() { Name = "自定义", Pattern = @"AAA(\d{4})(\d{2})(\d{2})",
                    GroupMapping = new() { ["year"] = 1, ["month"] = 2, ["day"] = 3 } }
            };
            PatternsStore.Save(path, patterns);
            var loaded = PatternsStore.Load(path);
            Assert.Single(loaded);
            Assert.Equal("自定义", loaded[0].Name);
            Assert.Equal(3, loaded[0].GroupMapping["day"]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void 无模式文件时回退内置()
    {
        var path = Path.Combine(Path.GetTempPath(), "mo-patterns-none-" + Guid.NewGuid().ToString("N") + ".json");
        var loaded = PatternsStore.Load(path);
        Assert.NotEmpty(loaded);
        Assert.All(loaded, p => Assert.True(p.Builtin));
    }
}
