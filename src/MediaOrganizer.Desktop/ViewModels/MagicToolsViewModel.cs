using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Core;
using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Patterns;
using System.Collections.ObjectModel;

namespace MediaOrganizer.Desktop.ViewModels;

public sealed record RegexTestItem(string Sample, bool Matched, string Result);

/// <summary>魔术工具：样本加载（失败文件/源目录/JSON）→ 指纹与字符着色 → 圈选标记生成正则 / 多变体生成 → 实时测试 → 保存（FR-7）。</summary>
public partial class MagicToolsViewModel : ViewModelBase
{
    private readonly List<PatternDefinition> _patterns;
    private readonly AppState _state;

    public ObservableCollection<string> Samples { get; } = [];
    public ObservableCollection<RegexTestItem> TestResults { get; } = [];

    /// <summary>当前选中样本的字符着色单元（FR-7.2）。</summary>
    public ObservableCollection<MarkableCharVM> CharCells { get; } = [];

    /// <summary>时间戳长度选项（与参考实现 magic_tools.py 一致：10/13/16）。</summary>
    public int[] TimestampLengthOptions { get; } = [10, 13, 16];

    [ObservableProperty]
    private string _sampleInfo = "尚未载入样本";

    [ObservableProperty]
    private string _selectedSample = "";

    [ObservableProperty]
    private string _fingerprint = "";

    [ObservableProperty]
    private string _regexText = "";

    [ObservableProperty]
    private string _patternName = "";

    [ObservableProperty]
    private string _saveHint = "";

    [ObservableProperty]
    private MarkableCharVM? _selectedChar;

    /// <summary>时间戳长度：标记时间戳时自动选中该长度的连续数字。</summary>
    [ObservableProperty]
    private int _timestampLength = 13;

    /// <summary>当前标记模式（对应 Python selection_mode）：null=未进入标记模式，点击角色按钮后进入，点击字符时按此角色标记并扩展范围。</summary>
    [ObservableProperty]
    private MarkRole? _currentRole;

    /// <summary>「生成的正则」展示：模式 / 映射 / 解析三行（对应原型步骤 ②）。</summary>
    [ObservableProperty]
    private string _regexInfo = "";

    private AnalysisResult? _lastResult;

    public MagicToolsViewModel(AppState state)
    {
        _state = state;
        _patterns = state.Patterns;
    }

    // ---- 样本加载（FR-7.1）----

    public void LoadSamples(IEnumerable<string> names)
    {
        Samples.Clear();
        foreach (var n in names) Samples.Add(n);
        SampleInfo = $"{Samples.Count} 个样本";
        SelectedSample = Samples.FirstOrDefault() ?? "";
        if (!string.IsNullOrEmpty(SelectedSample)) TestRegex();
    }

    /// <summary>从源目录加载前 N 个文件名字样（FR-7.1）。</summary>
    [RelayCommand]
    private void LoadFromSourceDir(int max = 100)
    {
        var dir = _state.Config.Paths.SourceDir;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            SaveHint = "请先在设置/工作台指定源目录";
            return;
        }
        var names = Directory.EnumerateFiles(dir)
            .Take(max)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToArray();
        LoadSamples(names);
        SaveHint = $"已从源目录载入 {names.Length} 个文件名样本";
    }

    /// <summary>从分析结果（成功或失败）载入文件名字样（FR-7.1）。</summary>
    public void LoadFromAnalysisResult(AnalysisResult? result, int max = 100)
    {
        _lastResult = result;
        if (result is null)
        {
            SaveHint = "尚无分析结果，请先在工作台完成一次分析";
            return;
        }
        var names = result.Parsed.Select(p => p.File.FileName)
            .Concat(result.Unparsed.Select(u => u.File.FileName))
            .Take(max)
            .ToArray();
        LoadSamples(names);
        SaveHint = $"已从分析结果载入 {names.Length} 个文件名样本";
    }

    /// <summary>从源目录的 analysis-result.json 载入失败文件（原型「从失败文件载入」）。</summary>
    [RelayCommand]
    private void LoadFromFailedFiles()
    {
        var dir = _state.Config.Paths.SourceDir;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            SaveHint = "请先在设置/工作台指定源目录";
            return;
        }

        var path = Path.Combine(dir, "analysis-result.json");
        if (!File.Exists(path))
        {
            SaveHint = "源目录下未找到 analysis-result.json，请先完成一次分析";
            return;
        }

        var result = AnalysisResultStore.Load(path);
        if (result is null)
        {
            SaveHint = "analysis-result.json 解析失败";
            return;
        }

        var names = result.Unparsed.Select(u => u.File.FileName).Take(100).ToArray();
        if (names.Length == 0)
        {
            SaveHint = "分析结果中没有失败文件";
            return;
        }

        LoadSamples(names);
        SaveHint = $"已从失败文件载入 {names.Length} 个文件名样本";
    }

    /// <summary>按钮入口：用最近一次分析结果作样本。</summary>
    [RelayCommand]
    private void LoadFromAnalysisResult()
        => LoadFromAnalysisResult(_lastResult);

    /// <summary>手动选择 JSON 文件加载样本（参考实现 magic_tools.py）。</summary>
    [RelayCommand]
    private async Task BrowseJsonFile()
    {
        var top = App.MainWindow;
        if (top is null) return;

        var options = new FilePickerOpenOptions
        {
            Title = "选择 JSON 文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("JSON 文件") { Patterns = ["*.json"] },
                new FilePickerFileType("所有文件") { Patterns = ["*"] }
            ]
        };

        var files = await top.StorageProvider.OpenFilePickerAsync(options);
        var file = files.FirstOrDefault();
        if (file is null) return;

        try
        {
            var names = await LoadFileNamesFromJsonAsync(file.Path.LocalPath);
            if (names.Length == 0)
            {
                SaveHint = "JSON 文件中未找到有效的媒体文件名";
                return;
            }
            LoadSamples(names);
            SaveHint = $"已从 JSON 载入 {names.Length} 个文件名样本";
        }
        catch (Exception ex)
        {
            SaveHint = $"加载 JSON 失败：{ex.Message}";
        }
    }

    private static async Task<string[]> LoadFileNamesFromJsonAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        var mediaExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".tiff", ".bmp", ".gif", ".webp",
            ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".flv", ".webm",
            ".heic", ".heif"
        };

        bool IsMediaFile(string? name) => name is not null && mediaExts.Contains(Path.GetExtension(name));

        var result = new HashSet<string>();
        var doc = System.Text.Json.JsonDocument.Parse(json);

        // 1. analysis-result.json / unparsed_files.json 结构：Unparsed[].Path
        if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (doc.RootElement.TryGetProperty("Unparsed", out var unparsed))
            {
                foreach (var item in unparsed.EnumerateArray())
                {
                    if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        if (IsMediaFile(item.GetString())) result.Add(Path.GetFileName(item.GetString()!)!);
                    }
                    else if (item.TryGetProperty("Path", out var p) || item.TryGetProperty("path", out p))
                    {
                        if (IsMediaFile(p.GetString())) result.Add(Path.GetFileName(p.GetString()!)!);
                    }
                }
            }

            // 2. files 结构：按日期分组的文件
            if (doc.RootElement.TryGetProperty("files", out var files))
            {
                foreach (var dateGroup in files.EnumerateObject())
                {
                    foreach (var fileProp in dateGroup.Value.EnumerateObject())
                    {
                        if (IsMediaFile(fileProp.Name)) result.Add(fileProp.Name);
                        if (fileProp.Value.TryGetProperty("path", out var p) && IsMediaFile(p.GetString()))
                            result.Add(Path.GetFileName(p.GetString()!)!);
                    }
                }
            }

            // 3. 简单字符串列表
            if (doc.RootElement.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                    if (IsMediaFile(item.GetString())) result.Add(Path.GetFileName(item.GetString()!)!);
            }
        }

        // 4. 顶层字符串数组
        if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
                if (IsMediaFile(item.GetString())) result.Add(Path.GetFileName(item.GetString()!)!);
        }

        return result.OrderBy(n => n).ToArray();
    }

    // ---- 字符着色与圈选（FR-7.2/7.3）----

    partial void OnSelectedSampleChanged(string value)
    {
        // 与参考实现 magic_tools.py 一致：指纹/字符分解均基于不含扩展名的主文件名
        var nameWithoutExt = Path.GetFileNameWithoutExtension(value);
        Fingerprint = string.IsNullOrEmpty(value) ? "" : StructureFingerprint.Compute(nameWithoutExt);
        RebuildCharCells(value);
        TestRegex();
    }

    partial void OnSelectedCharChanged(MarkableCharVM? value)
    {
        foreach (var c in CharCells) c.IsSelected = ReferenceEquals(c, value);
    }

    /// <summary>chips 快速标记：设置当前标记角色（对应 Python selection_mode）。设好后点击字符即可标记并扩展范围。</summary>
    [RelayCommand]
    private void SetRole(string roleName)
    {
        var role = roleName switch
        {
            "年" => MarkRole.Year,
            "月" => MarkRole.Month,
            "日" => MarkRole.Day,
            "时" => MarkRole.Hour,
            "分" => MarkRole.Minute,
            "秒" => MarkRole.Second,
            "时间戳" => MarkRole.Timestamp,
            "忽略" => MarkRole.Ignore,
            "必现" => MarkRole.Required,
            _ => MarkRole.None
        };

        // 时间戳：若已选中字符，立即按 TimestampLength 自动选中连续数字（与参考实现一致）。
        if (role == MarkRole.Timestamp && SelectedChar is not null)
        {
            AutoRangeTimestamp(SelectedChar);
            return;
        }

        // 再次点击同一角色 → 退出标记模式
        if (CurrentRole == role)
        {
            CurrentRole = null;
            SaveHint = $"已退出「{roleName}」标记模式";
            return;
        }

        CurrentRole = role;
        SaveHint = role == MarkRole.Timestamp
            ? $"已进入「时间戳」标记模式，点击数字起始位置将自动选中 {TimestampLength} 位"
            : $"已进入「{roleName}」标记模式，点击字符标记（可连续点击多个字符扩展范围），再次点击「{roleName}」退出";
    }

    /// <summary>时间戳自动选段：从指定字符起向后取 TimestampLength 位连续数字。</summary>
    private void AutoRangeTimestamp(MarkableCharVM cell)
    {
        var start = CharCells.IndexOf(cell);
        if (start < 0) return;

        if (!char.IsDigit(cell.Char))
        {
            SaveHint = "时间戳必须从数字开始";
            return;
        }

        var end = start;
        while (end < CharCells.Count && char.IsDigit(CharCells[end].Char) && end - start < TimestampLength)
            end++;

        if (end - start != TimestampLength)
        {
            SaveHint = $"从位置 {start} 开始找不到 {TimestampLength} 位连续数字";
            return;
        }

        for (var i = start; i < end; i++) CharCells[i].Role = MarkRole.Timestamp;
        CurrentRole = null;
        SaveHint = $"已标记 {TimestampLength} 位时间戳（位置 {start}-{end - 1}）；点「从标记生成正则」生成规则";
    }

    private void RebuildCharCells(string fileName)
    {
        CharCells.Clear();
        // 与参考实现 magic_tools.py 一致：字符分解不含扩展名
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        foreach (var cell in PatternInferrer.ToCharCells(nameWithoutExt))
            CharCells.Add(new MarkableCharVM(cell.Char, cell.Role));
        SelectedChar = null;
        CurrentRole = null;
    }

    /// <summary>点击字符：若处于标记模式（CurrentRole 已设置），则按角色标记并扩展范围；否则仅选中该字符。</summary>
    [RelayCommand]
    private void SelectChar(MarkableCharVM cell)
    {
        var index = CharCells.IndexOf(cell);
        if (index < 0) return;

        SelectedChar = cell;

        // 未进入标记模式：仅选中供显示
        if (CurrentRole is null) return;

        // 时间戳：自动选段
        if (CurrentRole == MarkRole.Timestamp)
        {
            AutoRangeTimestamp(cell);
            return;
        }

        var role = CurrentRole.Value;

        // 冲突检查：该字符已被标记为其他角色
        if (cell.Role != MarkRole.None && cell.Role != role)
        {
            SaveHint = $"位置 {index} 已标记为「{RoleLabel(cell.Role)}」，请先清除标记";
            return;
        }

        // 标记当前字符
        cell.Role = role;

        // 扩展范围：找到所有同角色字符，填充区间内未标记的间隙（对应 Python _handle_date_part_selection 的范围合并）
        var min = CharCells.Select((c, i) => (c, i)).Where(x => x.c.Role == role).Min(x => x.i);
        var max = CharCells.Select((c, i) => (c, i)).Where(x => x.c.Role == role).Max(x => x.i);
        for (var i = min; i <= max; i++)
        {
            if (CharCells[i].Role == MarkRole.None)
                CharCells[i].Role = role;
        }

        SaveHint = $"已标记「{RoleLabel(role)}」（位置 {min}-{max}）；继续点击扩展，或选下一个角色";
    }

    [RelayCommand]
    private void CycleMark(MarkableCharVM cell)
    {
        cell.AdvanceRole();
        if (cell.Role != MarkRole.None)
            SaveHint = $"已标记「{cell.Char}」为 {RoleLabel(cell.Role)}；点「从标记生成正则」";
    }

    private static string RoleLabel(MarkRole r) => r switch
    {
        MarkRole.Year => "年",
        MarkRole.Month => "月",
        MarkRole.Day => "日",
        MarkRole.Hour => "时",
        MarkRole.Minute => "分",
        MarkRole.Second => "秒",
        MarkRole.Timestamp => "时间戳",
        MarkRole.Ignore => "忽略",
        MarkRole.Required => "必现",
        _ => "未标记"
    };

    [RelayCommand]
    private void ClearMarks()
    {
        foreach (var cell in CharCells) cell.ResetRole();
        CurrentRole = null;
        SaveHint = "已清除全部标记";
    }

    [RelayCommand]
    private void GenerateFromMarks()
    {
        if (string.IsNullOrEmpty(SelectedSample)) { SaveHint = "请先选择样本"; return; }
        var pattern = PatternInferrer.GenerateFromMarks(SelectedSample, CharCells.Select(c => c.AsCell()).ToArray());
        if (pattern is null) { SaveHint = "请先圈选日期片段并标记"; return; }

        RegexText = pattern.Pattern;
        SaveHint = $"已生成正则：{pattern.Pattern}（映射 {string.Join(", ", pattern.GroupMapping.Select(kv => $"{kv.Key}→组{kv.Value}"))}）";
        TestRegex();
    }

    [RelayCommand]
    private void GenerateVariants()
    {
        if (Samples.Count == 0) { SaveHint = "请先载入样本"; return; }
        RegexText = PatternInferrer.GenerateVariantRegex(Samples.ToArray());
        SaveHint = $"已按 {Samples.Count} 个样本生成多变体正则；点「测试」验证覆盖";
        TestRegex();
    }

    // ---- 实时测试（FR-7.5）----

    partial void OnRegexTextChanged(string value) => TestRegex();

    [RelayCommand]
    private void TestRegex()
    {
        TestResults.Clear();
        SaveHint = "";
        if (string.IsNullOrWhiteSpace(RegexText) || Samples.Count == 0)
        {
            RegexInfo = string.IsNullOrWhiteSpace(RegexText) ? "" : $"模式   {RegexText}\n映射   -\n解析   无样本";
            return;
        }

        // 与参考实现 magic_tools.py 一致：测试/推断均基于不含扩展名的主文件名
        var nameOnlySamples = Samples.Select(s => Path.GetFileNameWithoutExtension(s) ?? s).ToArray();

        var pattern = PatternInferrer.Infer(RegexText, nameOnlySamples);
        if (pattern is null)
        {
            SaveHint = "正则无效：请先通过测试";
            RegexInfo = $"模式   {RegexText}\n映射   -\n解析   正则语法错误";
            return;
        }

        foreach (var sample in Samples)
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(sample);
            var date = PatternEngine.TryExtract(nameWithoutExt, pattern);
            TestResults.Add(new RegexTestItem(sample, date is not null,
                date is { } d ? d.ToString("yyyy-MM-dd HH:mm:ss") : "未命中"));
        }
        SaveHint = TestResults.Any(t => t.Matched) ? "测试通过，可保存" : "";

        // 「生成的正则」面板：模式 / 映射 / 首个命中样本的解析
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"模式   {pattern.Pattern}");
        if (pattern.TimestampLength is { } len)
            sb.AppendLine($"映射   timestamp → 组 {pattern.GroupMapping["timestamp"]} · {len} 位");
        else
            sb.AppendLine($"映射   {string.Join(" / ", pattern.GroupMapping.Select(kv => $"{kv.Key}→组{kv.Value}"))}");
        var hit = TestResults.FirstOrDefault(t => t.Matched);
        sb.Append(hit is null ? "解析   无命中样本" : $"解析   {hit.Sample} → {hit.Result}");
        RegexInfo = sb.ToString();
    }

    // ---- 保存（FR-7.6）----

    [RelayCommand]
    private void SavePattern()
    {
        if (string.IsNullOrWhiteSpace(PatternName))
        {
            SaveHint = "请先填写规则名称";
            return;
        }
        if (string.IsNullOrWhiteSpace(RegexText))
        {
            SaveHint = "请先生成正则表达式";
            return;
        }
        if (TestResults.All(t => !t.Matched))
        {
            SaveHint = "没有样本命中，请调整正则后再保存";
            return;
        }

        var nameOnlySamples = Samples.Select(s => Path.GetFileNameWithoutExtension(s) ?? s).ToArray();
        var test = PatternInferrer.Infer(RegexText, nameOnlySamples)
                   ?? new PatternDefinition { Pattern = RegexText };
        var existing = _patterns.FirstOrDefault(p => p.Name == PatternName);
        if (existing is not null) _patterns.Remove(existing);

        _patterns.Add(new PatternDefinition
        {
            Name = PatternName,
            Pattern = RegexText,
            GroupMapping = test.GroupMapping,
            IgnoredGroups = test.IgnoredGroups,
            TimestampLength = test.TimestampLength,
            Enabled = true,
            Weight = 1.0,
            Builtin = false
        });
        _state.SavePatterns();
        SaveHint = $"已保存「{PatternName}」到 patterns.json";
        _state.NotifyChanged();
    }
}

/// <summary>可圈选的字符单元：点击循环切换标记角色（FR-7.2 着色 / FR-7.3 圈选）。</summary>
public partial class MarkableCharVM : ObservableObject
{
    public MarkableCharVM(char ch, MarkRole role)
    {
        Char = ch;
        _role = role;
    }

    public char Char { get; }

    [ObservableProperty]
    private MarkRole _role;

    [ObservableProperty]
    private bool _isSelected;

    public string Brush => Role switch
    {
        MarkRole.Year => "#C42B1C",
        MarkRole.Month => "#C42B1C",
        MarkRole.Day => "#C42B1C",
        MarkRole.Hour => "#CA5010",
        MarkRole.Minute => "#CA5010",
        MarkRole.Second => "#CA5010",
        MarkRole.Timestamp => "#17A2B8",
        MarkRole.Ignore => "#808080",
        MarkRole.Required => "#FFD700",
        _ => char.IsDigit(Char) ? "#E91E8C" : char.IsLetter(Char) ? "#107C10" : "#0067C0"
    };

    public string Tooltip => Role == MarkRole.None ? Char.ToString() : $"{Char} → {RoleLabel(Role)}";

    partial void OnRoleChanged(MarkRole value) => OnPropertyChanged(nameof(Brush));

    public void AdvanceRole() => Role = Role switch
    {
        MarkRole.None => MarkRole.Year,
        MarkRole.Year => MarkRole.Month,
        MarkRole.Month => MarkRole.Day,
        MarkRole.Day => MarkRole.Hour,
        MarkRole.Hour => MarkRole.Minute,
        MarkRole.Minute => MarkRole.Second,
        MarkRole.Second => MarkRole.Timestamp,
        MarkRole.Timestamp => MarkRole.Ignore,
        MarkRole.Ignore => MarkRole.Required,
        _ => MarkRole.None
    };

    public void ResetRole() => Role = MarkRole.None;

    public CharCell AsCell() => new(Char, Role);

    private static string RoleLabel(MarkRole r) => r switch
    {
        MarkRole.Year => "年",
        MarkRole.Month => "月",
        MarkRole.Day => "日",
        MarkRole.Hour => "时",
        MarkRole.Minute => "分",
        MarkRole.Second => "秒",
        MarkRole.Timestamp => "时间戳",
        MarkRole.Ignore => "忽略",
        MarkRole.Required => "必现",
        _ => "未标记"
    };
}
