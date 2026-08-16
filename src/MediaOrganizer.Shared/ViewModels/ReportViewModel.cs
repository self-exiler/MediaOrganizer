using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Shared.ViewModels;

/// <summary>
/// 分析报告查看器（双端共享）：TXT 已由工作台分析完成时落盘应用专有目录，此处展示 + 另存为/分享。
/// </summary>
public partial class ReportViewModel : ViewModelBase
{
    private readonly IFileSaver _fileSaver;

    [ObservableProperty]
    private string _reportText = "尚未生成报告。请先在工作台完成一次分析。";

    [ObservableProperty]
    private string _reportMeta = "";

    public ReportViewModel(IFileSaver fileSaver)
    {
        _fileSaver = fileSaver;
    }

    public void Set(AnalysisResult result, string outputDir)
    {
        ReportText = AnalysisReportGenerator.Generate(result, outputDir);
        ReportMeta = $"{result.SourceDir} · {result.AnalyzedAt:yyyy-MM-dd HH:mm:ss}";
    }

    /// <summary>另存为（FR-A4.3 导出；桌面 StorageProvider / Android SAF CREATE_DOCUMENT）。</summary>
    [RelayCommand]
    private async Task SaveReport()
    {
        await _fileSaver.SaveTextAsync("analysis-report.txt", ReportText);
    }

    /// <summary>系统分享（Android 分享面板；桌面降级另存为）。</summary>
    [RelayCommand]
    private Task ShareReport()
        => _fileSaver.ShareTextAsync("MediaOrganizer 分析报告", ReportText);
}
