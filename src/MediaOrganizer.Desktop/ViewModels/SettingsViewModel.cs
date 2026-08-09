using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Desktop.Services;

namespace MediaOrganizer.Desktop.ViewModels;

public partial class ExtractorSettingVM : ObservableObject
{
    public required string Name { get; init; }

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private double _weight;
}

/// <summary>设置：提取器 / 文件名模式 / 系统 / 界面 四个 Tab（对应原型「设置」页）。</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private readonly string _configPath;

    public event Action? ConfigSaved;

    public ObservableCollection<ExtractorSettingVM> Extractors { get; } = [];

    public string[] ThemeOptions { get; } = ["跟随系统", "浅色", "深色"];
    public string[] LanguageOptions { get; } = ["简体中文"];

    [ObservableProperty]
    private string _sourceDir;

    [ObservableProperty]
    private string _outputDir;

    [ObservableProperty]
    private string _pendingDir;

    [ObservableProperty]
    private int _maxYearsPast;

    [ObservableProperty]
    private int _futureDateBufferDays;

    [ObservableProperty]
    private int _progressInterval;

    [ObservableProperty]
    private int _previewSize;

    [ObservableProperty]
    private int _themeIndex;

    [ObservableProperty]
    private string _saveHint = "";

    public SettingsViewModel(AppConfig config, string configPath)
    {
        _config = config;
        _configPath = configPath;

        _sourceDir = config.Paths.SourceDir;
        _outputDir = config.Paths.OutputDir;
        _pendingDir = config.Paths.PendingDir;
        _maxYearsPast = config.Extraction.MaxYearsPast;
        _futureDateBufferDays = config.Extraction.FutureDateBufferDays;
        _progressInterval = config.Scan.ProgressInterval;
        _previewSize = config.General.PreviewSize;
        _themeIndex = config.General.Theme switch { "Dark" => 2, "Light" => 1, _ => 0 };

        foreach (var s in config.Extraction.Extractors)
        {
            var vm = new ExtractorSettingVM { Name = s.Name, Enabled = s.Enabled, Weight = s.Weight };
            vm.PropertyChanged += OnExtractorChanged;   // 勾选/权重即时写回配置
            Extractors.Add(vm);
        }
    }

    /// <summary>提取器开关/权重即时生效：写回共享配置并通知工作台（无需点保存配置）。</summary>
    private void OnExtractorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ExtractorSettingVM vm) return;
        var setting = _config.Extraction.Extractors.FirstOrDefault(x => x.Name == vm.Name);
        if (setting is null) return;

        setting.Enabled = vm.Enabled;
        setting.Weight = vm.Weight;
        SaveHint = $"「{vm.Name}」{Describe(e.PropertyName)}已即时生效；如需保留请点「保存配置」";
        ConfigSaved?.Invoke(); // 触发工作台链重建提示
    }

    private static string Describe(string? property)
        => property == nameof(ExtractorSettingVM.Weight) ? "权重" : "启用状态";

    /// <summary>主题切换即时生效（不等保存）。</summary>
    partial void OnThemeIndexChanged(int value)
    {
        var theme = value switch { 1 => "Light", 2 => "Dark", _ => "Default" };
        _config.General.Theme = theme;
        ThemeHelper.Apply(theme);
    }

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        if (await PickFolderAsync() is { } dir) SourceDir = dir;
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        if (await PickFolderAsync() is { } dir) OutputDir = dir;
    }

    [RelayCommand]
    private async Task BrowsePendingAsync()
    {
        if (await PickFolderAsync() is { } dir) PendingDir = dir;
    }

    private static async Task<string?> PickFolderAsync()
    {
        var top = App.MainWindow;
        if (top is null) return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "选择目录",
            AllowMultiple = false
        });
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    [RelayCommand]
    private void SaveConfig()
    {
        _config.Paths.SourceDir = SourceDir;
        _config.Paths.OutputDir = OutputDir;
        _config.Paths.PendingDir = PendingDir;
        _config.Extraction.MaxYearsPast = MaxYearsPast;
        _config.Extraction.FutureDateBufferDays = FutureDateBufferDays;
        _config.Scan.ProgressInterval = ProgressInterval;
        _config.General.PreviewSize = PreviewSize;
        _config.General.Theme = ThemeIndex switch { 1 => "Light", 2 => "Dark", _ => "Default" };

        _config.Extraction.Extractors = Extractors
            .Select(e => new ExtractorSetting { Name = e.Name, Enabled = e.Enabled, Weight = e.Weight })
            .ToList();

        ConfigManager.Save(_configPath, _config);
        SaveHint = $"已保存到 {_configPath}";
        ConfigSaved?.Invoke();
    }
}
