using System.Text.Json.Serialization;

namespace MediaOrganizer.Core.Configuration;

/// <summary>文件操作类型。</summary>
public enum FileOperation { Copy, Move }

/// <summary>同名文件处理策略。</summary>
public enum ExistAction { Skip, Overwrite, Rename }

/// <summary>目标目录分级。</summary>
public enum ClassificationLevel { Year, Month, Day }

/// <summary>网络位置协议（ADR-0004：明确不支持 FTP）。</summary>
public enum NetworkType { Smb, WebDav }

/// <summary>网络位置连接配置（FR-10/FR-11）。</summary>
public sealed class NetworkProfile
{
    public string Name { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NetworkType Type { get; set; } = NetworkType.Smb;

    /// <summary>SMB：UNC 路径 \\server\share；WebDAV：完整地址 https://dav.example.com/photos。</summary>
    public string Address { get; set; } = "";

    public string Username { get; set; } = "";

    /// <summary>加密存储（DPAPI:/B64:，见 CredentialCrypto）。</summary>
    public string Password { get; set; } = "";

    public DateTimeOffset? LastVerifiedAt { get; set; }
}

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
    public List<NetworkProfile> NetworkProfiles { get; set; } = [];
}

public sealed class GeneralConfig
{
    public string Theme { get; set; } = "Default";
    public string WindowSize { get; set; } = "1280x760";
    public int PreviewSize { get; set; } = 320;
}

public sealed class PathsConfig
{
    public string SourceDir { get; set; } = "";
    public string OutputDir { get; set; } = "";

    /// <summary>输出目标为网络位置时，此处存 NetworkProfile.Name（空 = 本地）。</summary>
    public string OutputNetworkProfile { get; set; } = "";

    public string PendingDir { get; set; } = "";
}

public sealed class ScanConfig
{
    public List<string> SupportedFormats { get; set; } =
    [
        "jpg", "jpeg", "png", "tiff", "tif", "bmp", "webp", "heic", "heif",
        "mp4", "mov", "avi", "mkv", "wmv", "flv", "webm", "m4v", "mpg", "mpeg", "3gp", "3g2"
    ];

    /// <summary>扫描所有文件（忽略扩展名白名单）。FR-1.3/FR-8.3。</summary>
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

    /// <summary>文件执行并行度：0 = 自动（本地 2 / 网络 4）。</summary>
    public int MaxDegreeOfParallelism { get; set; }
}
