using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Models;

namespace MediaOrganizer.Desktop.ViewModels;

/// <summary>分析报告查看器。</summary>
public partial class ReportViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _reportText = "尚未生成报告。请先在工作台完成一次分析。";

    [ObservableProperty]
    private string _reportMeta = "";

    private string _sourceDir = "";

    public void Set(AnalysisResult result, string outputDir)
    {
        _sourceDir = result.SourceDir;
        ReportText = AnalysisReportGenerator.Generate(result, outputDir);
        ReportMeta = $"{result.SourceDir} · {result.AnalyzedAt:yyyy-MM-dd HH:mm:ss}";
    }

    [RelayCommand]
    private void SaveReport()
    {
        if (string.IsNullOrWhiteSpace(_sourceDir) || !Directory.Exists(_sourceDir)) return;
        var path = Path.Combine(_sourceDir, "analysis-report.txt");
        File.WriteAllText(path, ReportText);
    }
}
