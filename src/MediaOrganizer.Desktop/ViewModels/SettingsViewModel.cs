using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Core;
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

/// <summary>网络位置列表项（FR-10）。</summary>
public sealed class NetworkProfileVM
{
    public required NetworkProfile Source { get; init; }
    public string Name => Source.Name;
    public string TypeLabel => Source.Type == NetworkType.Smb ? "SMB" : "WebDAV";
    public string Address => Source.Address;
    public string VerifiedLabel => Source.LastVerifiedAt is { } t ? $"✓ {t:yyyy-MM-dd HH:mm}" : "未验证";
}

/// <summary>文件名模式列表项（对应原型「设置 → 文件名模式」Tab）。</summary>
public partial class PatternSettingVM : ObservableObject
{
    private readonly PatternDefinition _source;

    public PatternSettingVM(PatternDefinition source)
    {
        _source = source;
        _enabled = source.Enabled;
    }

    public string Name => _source.Name;
    public string Regex => _source.Pattern;
    public double Weight => _source.Weight;
    public string Source => _source.Builtin ? "内置" : "自定义";

    [ObservableProperty]
    private bool _enabled;

    partial void OnEnabledChanged(bool value)
    {
        _source.Enabled = value; // 即时写回共享模式集合
    }
}

/// <summary>设置：提取器 / 文件名模式 / 系统 / 界面 四个 Tab（对应原型「设置」页）。</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly AppConfig _config;
    private readonly List<PatternDefinition> _patterns;

    // 变更通知统一走 _state.NotifyChanged()
    public event Action? NavigateToMagic;

    public ObservableCollection<ExtractorSettingVM> Extractors { get; } = [];
    public ObservableCollection<PatternSettingVM> Patterns { get; } = [];
    public ObservableCollection<NetworkProfileVM> NetworkProfiles { get; } = [];

    public string[] ThemeOptions { get; } = ["跟随系统", "浅色", "深色"];
    public string[] NetworkTypeOptions { get; } = ["SMB（Windows UNC 路径）", "WebDAV（全平台）"];

    // ---- 网络位置内联编辑表单状态 ----
    private NetworkProfile? _editingProfile; // null = 新建

    [ObservableProperty]
    private bool _editingNetwork;

    [ObservableProperty]
    private string _editName = "";

    [ObservableProperty]
    private int _editTypeIndex;

    [ObservableProperty]
    private string _editAddress = "";

    [ObservableProperty]
    private string _editUsername = "";

    [ObservableProperty]
    private string _editPassword = "";

    [ObservableProperty]
    private string _networkHint = "";

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
    private string _windowSize;

    [ObservableProperty]
    private string _supportedFormatsText;

    [ObservableProperty]
    private bool _scanAllFiles;

    [ObservableProperty]
    private int _classificationLevelIndex;

    [ObservableProperty]
    private int _existActionIndex;

    [ObservableProperty]
    private int _maxDegreeOfParallelism;

    [ObservableProperty]
    private bool _fixMtime;

    /// <summary>文件执行并行度：0 = 自动（本地 2 / 网络 4）。</summary>
    [ObservableProperty]
    private int _executionParallelism;

    [ObservableProperty]
    private string _saveHint = "";

    [ObservableProperty]
    private string _patternSummary = "";

    public string[] ClassificationLevelOptions { get; } = ["按日 2024/01/15", "按月 2024/01", "按年 2024"];
    public string[] ExistActionOptions { get; } = ["跳过 skip", "覆盖 overwrite", "重命名 rename（_1、_2…）"];

    public SettingsViewModel(AppState state)
    {
        _state = state;
        _config = state.Config;
        _patterns = state.Patterns;

        _sourceDir = _config.Paths.SourceDir;
        _outputDir = _config.Paths.OutputDir;
        _pendingDir = _config.Paths.PendingDir;
        _maxYearsPast = _config.Extraction.MaxYearsPast;
        _futureDateBufferDays = _config.Extraction.FutureDateBufferDays;
        _progressInterval = _config.Scan.ProgressInterval;
        _previewSize = _config.General.PreviewSize;
        _themeIndex = ThemeIndexFromName(_config.General.Theme);
        _windowSize = _config.General.WindowSize;
        _supportedFormatsText = string.Join(", ", _config.Scan.SupportedFormats);
        _scanAllFiles = _config.Scan.ScanAllFiles;
        _classificationLevelIndex = _config.Execute.ClassificationLevel switch
        {
            ClassificationLevel.Month => 1,
            ClassificationLevel.Year => 2,
            _ => 0
        };
        _existActionIndex = _config.Execute.ExistAction switch
        {
            ExistAction.Overwrite => 1,
            ExistAction.Rename => 2,
            _ => 0
        };
        _maxDegreeOfParallelism = _config.Scan.MaxDegreeOfParallelism;
        _fixMtime = _config.Execute.FixMtime;
        _executionParallelism = _config.Execute.MaxDegreeOfParallelism;

        foreach (var s in _config.Extraction.Extractors)
        {
            var vm = new ExtractorSettingVM { Name = s.Name, Enabled = s.Enabled, Weight = s.Weight };
            vm.PropertyChanged += OnExtractorChanged;   // 勾选/权重即时写回配置
            Extractors.Add(vm);
        }

        RefreshPatterns();
        RefreshNetworkProfiles();
    }

    private void RefreshNetworkProfiles()
    {
        NetworkProfiles.Clear();
        foreach (var p in _config.NetworkProfiles)
            NetworkProfiles.Add(new NetworkProfileVM { Source = p });
    }

    // ---- 网络位置管理（FR-10）----

    [RelayCommand]
    private void BeginAddNetwork()
    {
        _editingProfile = null;
        EditName = "";
        EditTypeIndex = 0;
        EditAddress = "";
        EditUsername = "";
        EditPassword = "";
        NetworkHint = "支持 SMB（UNC 路径）与 WebDAV；FTP 不支持。密码 Windows 以 DPAPI 加密存储。";
        EditingNetwork = true;
    }

    [RelayCommand]
    private void BeginEditNetwork(NetworkProfileVM vm)
    {
        _editingProfile = vm.Source;
        EditName = vm.Source.Name;
        EditTypeIndex = vm.Source.Type == NetworkType.Smb ? 0 : 1;
        EditAddress = vm.Source.Address;
        EditUsername = vm.Source.Username;
        EditPassword = string.IsNullOrEmpty(vm.Source.Password) ? "" : "••••••（留空则不修改）";
        NetworkHint = "";
        EditingNetwork = true;
    }

    [RelayCommand]
    private void DeleteNetwork(NetworkProfileVM vm)
    {
        _config.NetworkProfiles.Remove(vm.Source);
        if (_config.Paths.OutputNetworkProfile == vm.Source.Name)
            _config.Paths.OutputNetworkProfile = "";
        RefreshNetworkProfiles();
        NetworkHint = $"已删除「{vm.Source.Name}」";
        _state.SaveConfig(notifyChanged: false);
        _state.NotifyChanged();
    }

    [RelayCommand]
    private async Task TestNetworkAsync()
    {
        var profile = BuildEditProfile();
        if (profile is null) return;
        NetworkHint = "正在测试连接…";
        var (ok, message) = await StorageFactory.TestConnectionAsync(profile);
        NetworkHint = ok ? $"✓ {message}" : $"✗ {message}";
        if (ok && _editingProfile is not null)
        {
            _editingProfile.LastVerifiedAt = DateTimeOffset.Now;
            RefreshNetworkProfiles();
        }
    }

    /// <summary>列表行「测试」：直接测试该条已保存的配置（不经过编辑表单）。</summary>
    [RelayCommand]
    private async Task TestProfileAsync(NetworkProfileVM vm)
    {
        NetworkHint = $"正在测试「{vm.Name}」…";
        var (ok, message) = await StorageFactory.TestConnectionAsync(vm.Source);
        NetworkHint = ok ? $"✓ 「{vm.Name}」{message}" : $"✗ 「{vm.Name}」{message}";
        if (ok)
        {
            vm.Source.LastVerifiedAt = DateTimeOffset.Now;
            _state.SaveConfig(notifyChanged: false);
            RefreshNetworkProfiles();
        }
    }

    [RelayCommand]
    private void SaveNetwork()
    {
        var profile = BuildEditProfile();
        if (profile is null) return;

        var existing = _config.NetworkProfiles.FirstOrDefault(p => p.Name == profile.Name);
        if (_editingProfile is null)
        {
            if (existing is not null) { NetworkHint = $"名称「{profile.Name}」已存在"; return; }
            _config.NetworkProfiles.Add(profile);
        }
        else
        {
            // 保留验证状态与未修改的密码
            profile.LastVerifiedAt = _editingProfile.LastVerifiedAt;
            if (EditPassword.StartsWith("••••••") && !string.IsNullOrEmpty(_editingProfile.Password))
                profile.Password = _editingProfile.Password;
            var idx = _config.NetworkProfiles.IndexOf(_editingProfile);
            if (idx >= 0) _config.NetworkProfiles[idx] = profile;
        }

        RefreshNetworkProfiles();
        EditingNetwork = false;
        NetworkHint = $"已保存「{profile.Name}」；可在工作台选择该网络目标";
        _state.SaveConfig(notifyChanged: false);
        _state.NotifyChanged();
    }

    [RelayCommand]
    private void CancelNetwork()
    {
        EditingNetwork = false;
        NetworkHint = "";
    }

    private NetworkProfile? BuildEditProfile()
    {
        if (string.IsNullOrWhiteSpace(EditName)) { NetworkHint = "请填写名称"; return null; }
        if (string.IsNullOrWhiteSpace(EditAddress)) { NetworkHint = "请填写地址（SMB: \\\\server\\share；WebDAV: https://…）"; return null; }
        if (string.IsNullOrWhiteSpace(EditPassword) && _editingProfile is null) { NetworkHint = "请填写密码"; return null; }

        var isNew = _editingProfile is null;
        var storedPassword = isNew
            ? Core.Security.CredentialCrypto.Encrypt(EditPassword)
            : (_editingProfile!.Password ?? "");

        return new NetworkProfile
        {
            Name = EditName.Trim(),
            Type = EditTypeIndex == 0 ? NetworkType.Smb : NetworkType.WebDav,
            Address = EditAddress.Trim(),
            Username = EditUsername.Trim(),
            Password = storedPassword,
            LastVerifiedAt = _editingProfile?.LastVerifiedAt
        };
    }

    private void RefreshPatterns()
    {
        Patterns.Clear();
        foreach (var p in _patterns)
        {
            var vm = new PatternSettingVM(p);
            vm.PropertyChanged += OnPatternChanged;    // 启停即时生效
            Patterns.Add(vm);
        }
        PatternSummary = $"{_patterns.Count} 条 · {_patterns.Count(p => p.Enabled)} 启用";
    }

    private void OnPatternChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PatternSettingVM vm) return;
        PatternSummary = $"{_patterns.Count} 条 · {_patterns.Count(p => p.Enabled)} 启用";
        SaveHint = $"「{vm.Name}」启用状态已即时生效；如需保留请点「保存配置」";
        _state.NotifyChanged(); // 触发工作台链重建提示
    }

    [RelayCommand]
    private void DeletePattern(PatternSettingVM vm)
    {
        _patterns.RemoveAll(p => p.Name == vm.Name);
        Patterns.Remove(vm);
        RefreshPatterns();
        SaveHint = $"已删除「{vm.Name}」；点「保存配置」写入 patterns.json";
        _state.NotifyChanged();
    }

    [RelayCommand]
    private void RestoreDefaultPatterns()
    {
        // 内置模式回到出厂清单（保留自定义模式）
        foreach (var builtin in PatternsStore.GetBuiltinPatterns())
        {
            if (_patterns.All(p => p.Name != builtin.Name))
                _patterns.Add(builtin);
        }
        RefreshPatterns();
        SaveHint = "已恢复全部内置模式；点「保存配置」写入 patterns.json";
        _state.NotifyChanged();
    }

    [RelayCommand]
    private void OpenMagicTools() => NavigateToMagic?.Invoke();

    /// <summary>提取器开关/权重即时生效：写回共享配置并通知工作台（无需点保存配置）。</summary>
    private void OnExtractorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ExtractorSettingVM vm) return;
        var setting = _config.Extraction.Extractors.FirstOrDefault(x => x.Name == vm.Name);
        if (setting is null) return;

        setting.Enabled = vm.Enabled;
        setting.Weight = vm.Weight;
        SaveHint = $"「{vm.Name}」{Describe(e.PropertyName)}已即时生效；如需保留请点「保存配置」";
        _state.NotifyChanged(); // 触发工作台链重建提示
    }

    private static string Describe(string? property)
        => property == nameof(ExtractorSettingVM.Weight) ? "权重" : "启用状态";

    /// <summary>主题切换即时生效（不等保存）。</summary>
    partial void OnThemeIndexChanged(int value)
    {
        var theme = ThemeNameFromIndex(value);
        _config.General.Theme = theme;
        ThemeHelper.Apply(theme);
    }

    private static string ThemeNameFromIndex(int index) => index switch
    {
        1 => "Light",
        2 => "Dark",
        _ => "Default"
    };

    private static int ThemeIndexFromName(string name) => name switch
    {
        "Dark" => 2,
        "Light" => 1,
        _ => 0
    };

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        if (await App.PickFolderAsync() is { } dir) SourceDir = dir;
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        if (await App.PickFolderAsync() is { } dir) OutputDir = dir;
    }

    [RelayCommand]
    private async Task BrowsePendingAsync()
    {
        if (await App.PickFolderAsync() is { } dir) PendingDir = dir;
    }

    [RelayCommand]
    private void SaveConfig()
    {
        ApplyToConfig();

        _config.Extraction.Extractors = Extractors
            .Select(e => new ExtractorSetting { Name = e.Name, Enabled = e.Enabled, Weight = e.Weight })
            .ToList();

        _state.SaveAll();
        SaveHint = "已保存到 config.json 与 patterns.json";
        _state.NotifyChanged();
    }

    /// <summary>保存前的收集：把 VM 状态写回共享 config（SaveConfig 调用）。</summary>
    private void ApplyToConfig()
    {
        _config.Paths.SourceDir = SourceDir;
        _config.Paths.OutputDir = OutputDir;
        _config.Paths.PendingDir = PendingDir;
        _config.Extraction.MaxYearsPast = MaxYearsPast;
        _config.Extraction.FutureDateBufferDays = FutureDateBufferDays;
        _config.Scan.ProgressInterval = ProgressInterval;
        _config.Scan.MaxDegreeOfParallelism = MaxDegreeOfParallelism;
        _config.Scan.ScanAllFiles = ScanAllFiles;
        _config.Scan.SupportedFormats = SupportedFormatsText
            .Split(',', '，', ' ', ';')
            .Select(s => s.Trim().TrimStart('.'))
            .Where(s => s.Length > 0)
            .ToList();
        _config.Execute.ClassificationLevel = ClassificationLevelIndex switch
        {
            1 => ClassificationLevel.Month,
            2 => ClassificationLevel.Year,
            _ => ClassificationLevel.Day
        };
        _config.Execute.ExistAction = ExistActionIndex switch
        {
            1 => ExistAction.Overwrite,
            2 => ExistAction.Rename,
            _ => ExistAction.Skip
        };
        _config.Execute.FixMtime = FixMtime;
        _config.Execute.MaxDegreeOfParallelism = ExecutionParallelism;
        _config.General.PreviewSize = PreviewSize;
        _config.General.WindowSize = string.IsNullOrWhiteSpace(WindowSize) ? "1280x760" : WindowSize.Trim();
        _config.General.Theme = ThemeNameFromIndex(ThemeIndex);
    }
}
