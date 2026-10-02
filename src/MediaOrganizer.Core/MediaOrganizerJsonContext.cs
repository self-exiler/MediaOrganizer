using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Configuration;

namespace MediaOrganizer.Core;

/// <summary>
/// 配置/模式/分析结果的 System.Text.Json 源生成上下文（Native AOT 要求：反射式序列化在裁剪后不可用）。
/// 落盘选项集中在此，与旧 JsonSerializerOptions 保持一致，确保既有 config.json / patterns.json 仍能读回。
/// internal：ResultFile 是内部类型，公开上下文会撞上可访问性不一致（CS0053）。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(PatternsFile))]
[JsonSerializable(typeof(AnalysisResultStore.ResultFile))]
internal sealed partial class MediaOrganizerJsonContext : JsonSerializerContext;

/// <summary>持久化落盘用的 <see cref="JsonTypeInfo{T}"/> 入口，调用方以此把类型信息交给 JsonFileStore。</summary>
public static class AppJson
{
    /// <summary>源生成选项不提供 Encoder（.NET 10 无此属性），只能在上下文默认 options 之上派生一份覆盖编码器：
    /// 不放宽则非 ASCII 全被转义成 \uXXXX，中文配置不可读。文件始终以 UTF-8 落盘，放宽转义是安全的
    /// （该输出不进入 HTML 上下文）。</summary>
    private static readonly JsonSerializerOptions Options = new(MediaOrganizerJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static JsonTypeInfo<AppConfig> AppConfig { get; } = (JsonTypeInfo<AppConfig>)Options.GetTypeInfo(typeof(AppConfig));
    public static JsonTypeInfo<PatternsFile> PatternsFile { get; } = (JsonTypeInfo<PatternsFile>)Options.GetTypeInfo(typeof(PatternsFile));
    internal static JsonTypeInfo<AnalysisResultStore.ResultFile> ResultFile { get; } =
        (JsonTypeInfo<AnalysisResultStore.ResultFile>)Options.GetTypeInfo(typeof(AnalysisResultStore.ResultFile));
}
