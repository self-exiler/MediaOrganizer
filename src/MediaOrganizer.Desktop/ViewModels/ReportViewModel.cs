using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Models;

namespace MediaOrganizer.Desktop.ViewModels;

/// <summary>分析报告查看器（TXT 已由工作台分析完成时落盘，此处展示 + 另存为）。</summary>
public partial class ReportViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _reportText = "尚未生成报告。请先在工作台完成一次分析。";

    [ObservableProperty]
    private string _reportMeta = "";

    public void Set(AnalysisResult result, string outputDir)
    {
        ReportText = AnalysisReportGenerator.Generate(result, outputDir);
        ReportMeta = $"{result.SourceDir} · {result.AnalyzedAt:yyyy-MM-dd HH:mm:ss}";
    }

    /// <summary>另存为对话框保存报告（FR-4.4）。</summary>
    [RelayCommand]
    private async Task SaveReport()
    {
        var top = App.MainWindow;
        if (top is null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "另存分析报告",
            SuggestedFileName = "analysis-report.txt",
            DefaultExtension = "txt",
            FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType("文本文件") { Patterns = ["*.txt"] }]
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(ReportText);
    }
}

