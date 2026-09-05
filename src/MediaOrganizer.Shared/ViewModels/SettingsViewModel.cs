using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Core;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Security;
using MediaOrganizer.Core.Storage;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MediaOrganizer.Shared.ViewModels;

public partial class ExtractorSettingVM : ObservableObject
{
    public required string Name { get; init; }

    /// <summary>展示名（移动端设置页用；桌面亦可用）。</summary>
    public string DisplayLabel => Name switch
    {
        "Exif" => "EXIF 提取器",
        "FileName" => "文件名提取器",
        "FileSystem" => "文件系统提取器",
        _ => Name
    };

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private double _weight;
}

/// <summary>网络位置列表项（FR-10/FR-A8.3）。</summary>
public sealed class NetworkProfileVM
{
    public required NetworkProfile Source { get; init; }
    public string Name => Source.Name;
    public string TypeLabel => Source.Type == NetworkType.Smb ? "SMB" : "WebDAV";
    public string Address => Source.Address;
    public string VerifiedLabel => Source.LastVerifiedAt is { } t ? $"✓ {t:yyyy-MM-dd HH:mm}" : "未验证";
}

/// <summary>文件名模式列表项（设置 → 文件名模式）。</summary>
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

    /// <summary>忽略的捕获组号（FR-3.1），为空时显示「无」。</summary>
    public string IgnoredGroupsText => _source.IgnoredGroups.Count == 0
        ? "无"
        : string.Join(",", _source.IgnoredGroups);

    /// <summary>时间戳位数（10/13/16），非时间戳模式显示「-」。</summary>
    public string TimestampLengthText => _source.TimestampLength?.ToString() ?? "-";

    [ObservableProperty]
    private bool _enabled;

    partial void OnEnabledChanged(bool value)
    {
        _source.Enabled = value; // 即时写回共享模式集合
    }
}

/// <summary>
/// 设置：提取器 / 文件名模式 / 网络位置 / 扫描参数（双端共享，ADR-0006 决策 4）。
/// 主题应用经注入委托（桌面 ThemeHelper；Android 侧可为空）。
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly AppConfig _config;
    private readonly List<PatternDefinition> _patterns;
    private readonly Action<string>? _applyTheme;

    // 变更通知统一走 _state.NotifyChanged()
    public event Action? NavigateToMagic;

    public ObservableCollection<ExtractorSettingVM> Extractors { get; } = [];
    public ObservableCollection<PatternSettingVM> Patterns { get; } = [];
    public ObservableCollection<NetworkProfileVM> NetworkProfiles { get; } = [];

    public string[] ThemeOptions { get; } = ["跟随系统", "浅色", "深色"];
    public string[] NetworkTypeOptions { get; } = ["SMB（\\\\服务器\\共享名）", "WebDAV（https://…）"];

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

    /// <summary>连接测试忙碌态（评审 2.13）：防重入 + 供 UI 展示“正在测试”。</summary>
    [ObservableProperty]
    private bool _isTestingNetwork;

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
    private string _windowSize = "";

    [ObservableProperty]
    private string _supportedFormatsText = "";

    [ObservableProperty]
    private bool _scanAllFiles;

    [ObservableProperty]
    private int _maxDegreeOfParallelism;

    [ObservableProperty]
    private int _executionParallelism;

    [ObservableProperty]
    private string _saveHint = "";

    [ObservableProperty]
    private string _patternSummary = "";

    public SettingsViewModel(AppState state, Action<string>? applyTheme = null)
    {
        _state = state;
        _config = state.Config;
        _patterns = state.Patterns;
        _applyTheme = applyTheme;

        LoadFromConfig();

        RefreshExtractors();
        RefreshPatterns();
        RefreshNetworkProfiles();
    }

    private void LoadFromConfig(bool notify = false)
    {
        if (notify)
        {
            // 走属性赋值：触发 PropertyChanged，使已绑定的设置页 UI 立刻刷新（P0-4「初始化默认配置」后）
            MaxYearsPast = _config.Extraction.MaxYearsPast;
            FutureDateBufferDays = _config.Extraction.FutureDateBufferDays;
            ProgressInterval = _config.Scan.ProgressInterval;
            PreviewSize = _config.General.PreviewSize;
            ThemeIndex = ThemeIndexFromName(_config.General.Theme);
            WindowSize = _config.General.WindowSize;
            SupportedFormatsText = string.Join(", ", _config.Scan.SupportedFormats);
            ScanAllFiles = _config.Scan.ScanAllFiles;
            MaxDegreeOfParallelism = _config.Scan.MaxDegreeOfParallelism;
            ExecutionParallelism = _config.Execute.MaxDegreeOfParallelism;
        }
        else
        {
            // 初始化直接写 backing field 是刻意的：走生成属性会触发 OnXxxChanged 的即时落盘/提示副作用；
            // 故在此范围内静默 MVVMTK0034（此时尚未绑定，无需通知）。
#pragma warning disable MVVMTK0034
            _maxYearsPast = _config.Extraction.MaxYearsPast;
            _futureDateBufferDays = _config.Extraction.FutureDateBufferDays;
            _progressInterval = _config.Scan.ProgressInterval;
            _previewSize = _config.General.PreviewSize;
            _themeIndex = ThemeIndexFromName(_config.General.Theme);
            _windowSize = _config.General.WindowSize;
            _supportedFormatsText = string.Join(", ", _config.Scan.SupportedFormats);
            _scanAllFiles = _config.Scan.ScanAllFiles;
            _maxDegreeOfParallelism = _config.Scan.MaxDegreeOfParallelism;
            _executionParallelism = _config.Execute.MaxDegreeOfParallelism;
#pragma warning restore MVVMTK0034
        }
    }

    /// <summary>按当前配置重建提取器列表。重建前先解绑旧 VM 的 PropertyChanged，避免重复/残留订阅（P0-5）。</summary>
    private void RefreshExtractors()
    {
        foreach (var vm in Extractors) vm.PropertyChanged -= OnExtractorChanged;
        Extractors.Clear();
        foreach (var s in _config.Extraction.Extractors)
        {
            var vm = new ExtractorSettingVM { Name = s.Name, Enabled = s.Enabled, Weight = s.Weight };
            vm.PropertyChanged += OnExtractorChanged;   // 勾选/权重即时写回配置
            Extractors.Add(vm);
        }
    }

    private void RefreshNetworkProfiles()
    {
        NetworkProfiles.Clear();
        foreach (var p in _config.NetworkProfiles)
            NetworkProfiles.Add(new NetworkProfileVM { Source = p });
    }

    // ---- 初始化默认配置 ----

    [RelayCommand]
    private void Initialize()
    {
        ResetConfigToDefaults(_config);
        RestoreDefaultPatternsCommand.Execute(null);
        LoadFromConfig(notify: true); // 刷新设置页已绑定控件的显示值（P0-4）
        RefreshExtractors();          // 提取器列表指向新的默认配置，避免界面与配置长期打架（P0-5）
        _state.SaveAll();
        SaveHint = "已恢复默认配置与内置文件名模式";
        _state.NotifyChanged();
    }

    private static void ResetConfigToDefaults(AppConfig config)
    {
        // AppConfig 各子配置的默认值与此处的重置值完全一致，
        // 直接用 new() 覆盖即可，避免逐字段手写维护两份默认值。
        // 注意：网络位置不随初始化清除，避免误删用户配置。
        var defaults = new AppConfig();
        config.Version = defaults.Version;
        config.General = defaults.General;
        config.Paths = defaults.Paths;
        config.Scan = defaults.Scan;
        config.Extraction = defaults.Extraction;
        config.Execute = defaults.Execute;
    }

    // ---- 网络位置管理（FR-10/FR-A8.3）----

    [RelayCommand]
    private void BeginAddNetwork()
    {
        _editingProfile = null;
        EditName = "";
        EditTypeIndex = 0;
        EditAddress = "";
        EditUsername = "";
        EditPassword = "";
        NetworkHint = "支持 SMB 与 WebDAV。密码加密存储（Windows DPAPI / Android Keystore）。";
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
        var (ok, message) = await RunConnectionTestAsync(profile);
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
        var (ok, message) = await RunConnectionTestAsync(vm.Source, $"「{vm.Name}」");
        NetworkHint = ok ? $"✓ 「{vm.Name}」{message}" : $"✗ 「{vm.Name}」{message}";
        if (ok)
        {
            vm.Source.LastVerifiedAt = DateTimeOffset.Now;
            _state.SaveConfig(notifyChanged: false);
            RefreshNetworkProfiles();
        }
    }

    /// <summary>
    /// 统一的连接测试入口（评审 2.13）：忙碌态防重入 + 30s 超时；
    /// 经 Task.Run 移出 UI 线程——SMB 的 TCP+NTLM 登录是同步阻塞，直接 await 会冻结界面直到 OS 超时。
    /// </summary>
    private async Task<(bool Ok, string Message)> RunConnectionTestAsync(NetworkProfile profile, string label = "")
    {
        if (IsTestingNetwork) return (false, "正在测试其他连接，请稍候");
        IsTestingNetwork = true;
        NetworkHint = string.IsNullOrEmpty(label) ? "正在测试连接…" : $"正在测试{label}…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            return await Task.Run(() => StorageFactory.TestConnectionAsync(profile, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return (false, "连接测试超时（30s）");
        }
        catch (Exception ex)
        {
            return (false, $"连接失败：{ex.Message}");
        }
        finally
        {
            IsTestingNetwork = false;
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
            // 验证状态已由 BuildEditProfile 携带；密码的保留/更新也在其中处理，此处只做替换
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
        if (string.IsNullOrWhiteSpace(EditAddress)) { NetworkHint = @"请填写地址（SMB: \\server\share；WebDAV: https://…）"; return null; }
        if (string.IsNullOrWhiteSpace(EditPassword) && _editingProfile is null) { NetworkHint = "请填写密码"; return null; }

        var isNew = _editingProfile is null;

        // WebDAV 地址清洗：手机端输入法/剪贴板常混入零宽字符、全角字符，
        // 会污染主机名导致 DNS 解析失败（“hostname nor servename provided, or not known”）。
        // 清洗后的规范形态回填输入框，用户所见即所存。
        var address = EditAddress.Trim();
        if (EditTypeIndex == 1)
        {
            try
            {
                var sanitized = WebDavAddress.Sanitize(address);
                if (sanitized != EditAddress) EditAddress = sanitized;
                address = sanitized;
            }
            catch (ArgumentException ex)
            {
                NetworkHint = ex.Message;
                return null;
            }
        }
        // 编辑态下占位符/留空 = 保留旧密码；其余情况（新建或输入新密码）一律加密存储。
        // 旧实现编辑态恒用旧密码，导致修改密码永远不生效。
        var keepOldPassword = !isNew && (string.IsNullOrWhiteSpace(EditPassword)
            || EditPassword.StartsWith("••••••", StringComparison.Ordinal));
        string storedPassword;
        try
        {
            storedPassword = keepOldPassword
                ? _editingProfile!.Password ?? ""
                : CredentialCrypto.Encrypt(EditPassword);
        }
        catch (Exception ex)
        {
            // 加密失败不得静默存空密码（评审 2.7）：给出可见提示并拒绝保存该条目
            NetworkHint = $"密码无法加密存储：{ex.Message}";
            return null;
        }

        return new NetworkProfile
        {
            Name = EditName.Trim(),
            Type = EditTypeIndex == 0 ? NetworkType.Smb : NetworkType.WebDav,
            Address = address,
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
        SaveHint = $"「{vm.Name}」启用状态已即时生效并保存";
        _state.SavePatterns();
        _state.NotifyChanged(); // 触发工作台链重建提示
    }

    [RelayCommand]
    private void DeletePattern(PatternSettingVM vm)
    {
        _patterns.RemoveAll(p => p.Name == vm.Name);
        Patterns.Remove(vm);
        RefreshPatterns();
        SaveHint = $"已删除「{vm.Name}」并保存";
        _state.SavePatterns();
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
        SaveHint = "已恢复全部内置模式并保存";
        _state.SavePatterns();
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
        SaveHint = $"「{vm.Name}」{Describe(e.PropertyName)}已即时生效并保存";
        _state.SaveConfig(notifyChanged: false);
        _state.NotifyChanged(); // 触发工作台链重建提示
    }

    private static string Describe(string? property)
        => property == nameof(ExtractorSettingVM.Weight) ? "权重" : "启用状态";

    // ---- 配置项变更实时保存 ----

    partial void OnMaxYearsPastChanged(int value)
    {
        _config.Extraction.MaxYearsPast = value;
        SaveHint = "最多追溯年限已保存";
        _state.SaveConfig();
    }

    partial void OnFutureDateBufferDaysChanged(int value)
    {
        _config.Extraction.FutureDateBufferDays = value;
        SaveHint = "未来缓冲天数已保存";
        _state.SaveConfig();
    }

    partial void OnProgressIntervalChanged(int value)
    {
        _config.Scan.ProgressInterval = value;
        SaveHint = "进度间隔已保存";
        _state.SaveConfig(notifyChanged: false);
    }

    partial void OnMaxDegreeOfParallelismChanged(int value)
    {
        _config.Scan.MaxDegreeOfParallelism = value;
        SaveHint = "扫描并发数已保存";
        _state.SaveConfig(notifyChanged: false);
    }

    partial void OnExecutionParallelismChanged(int value)
    {
        _config.Execute.MaxDegreeOfParallelism = value;
        SaveHint = "执行并发数已保存";
        _state.SaveConfig(notifyChanged: false);
    }

    partial void OnScanAllFilesChanged(bool value)
    {
        _config.Scan.ScanAllFiles = value;
        SaveHint = "扫描所有文件选项已保存";
        _state.SaveConfig();
    }

    partial void OnSupportedFormatsTextChanged(string value)
    {
        _config.Scan.SupportedFormats = value
            .Split(',', '，', ' ', ';')
            .Select(s => s.Trim().TrimStart('.'))
            .Where(s => s.Length > 0)
            .ToList();
        SaveHint = "扩展名白名单已保存";
        _state.SaveConfig();
    }

    /// <summary>主题切换即时生效（不等保存）。桌面注入 ThemeHelper.Apply；Android 侧可为空。</summary>
    partial void OnThemeIndexChanged(int value)
    {
        var theme = ThemeNameFromIndex(value);
        _config.General.Theme = theme;
        _applyTheme?.Invoke(theme);
        SaveHint = "主题已保存";
        _state.SaveConfig(notifyChanged: false);
    }

    partial void OnWindowSizeChanged(string value)
    {
        _config.General.WindowSize = string.IsNullOrWhiteSpace(value) ? "1280x760" : value.Trim();
        SaveHint = "窗口尺寸已保存";
        _state.SaveConfig(notifyChanged: false);
    }

    partial void OnPreviewSizeChanged(int value)
    {
        _config.General.PreviewSize = value;
        SaveHint = "预览图大小已保存";
        _state.SaveConfig(notifyChanged: false);
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
}
