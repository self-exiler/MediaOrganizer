using System.Text.Json.Serialization;

namespace MediaOrganizer.Core.Configuration;

/// <summary>文件操作类型。</summary>
public enum FileOperation { Copy, Move }

/// <summary>同名文件处理策略。</summary>
public enum ExistAction { Skip, Overwrite, Rename }

/// <summary>目标目录分级。</summary>
public enum ClassificationLevel { Year, Month, Day }

/// <summary>单个提取器的启用与权重配置。</summary>
public sealed class ExtractorSetting
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public double Weight { get; set; } = 1.0;
}

/// <summary>应用配置（config.json）。结构见 SRS §5.1。</summary>
public sealed class AppConfig
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public GeneralConfig General { get; set; } = new();
    public PathsConfig Paths { get; set; } = new();
    public ScanConfig Scan { get; set; } = new();
    public ExtractionConfig Extraction { get; set; } = new();
    public ExecuteConfig Execute { get; set; } = new();
}

public sealed class GeneralConfig
{
    public string Language { get; set; } = "zh";
    public string Theme { get; set; } = "Default";
    public string WindowSize { get; set; } = "1280x760";
    public int PreviewSize { get; set; } = 320;
}

public sealed class PathsConfig
{
    public string SourceDir { get; set; } = "";
    public string OutputDir { get; set; } = "";
    public string PendingDir { get; set; } = "";
}

public sealed class ScanConfig
{
    public List<string> SupportedFormats { get; set; } =
    [
        "jpg", "jpeg", "png", "tiff", "tif", "bmp", "webp", "heic", "heif",
        "mp4", "mov", "avi", "mkv", "wmv", "flv", "webm", "m4v", "mpg", "mpeg", "3gp", "3g2"
    ];

    public bool ScanAllFiles { get; set; }
    public int ProgressInterval { get; set; } = 10;

    /// <summary>0 = 自动（CPU 核心数）。</summary>
    public int MaxDegreeOfParallelism { get; set; }
}

public sealed class ExtractionConfig
{
    public int MaxYearsPast { get; set; } = 30;
    public int FutureDateBufferDays { get; set; }

    public List<ExtractorSetting> Extractors { get; set; } =
    [
        new() { Name = "Exif", Weight = 1.2 },
        new() { Name = "FileName", Weight = 1.1 },
        new() { Name = "FileSystem", Enabled = false, Weight = 0.8 }
    ];

    [JsonIgnore]
    public double? MaxYearsPastExplicit => null;
}

public sealed class ExecuteConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FileOperation Operation { get; set; } = FileOperation.Copy;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ExistAction ExistAction { get; set; } = ExistAction.Skip;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClassificationLevel ClassificationLevel { get; set; } = ClassificationLevel.Day;

    public bool FixMtime { get; set; }
}
