using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Patterns;

namespace MediaOrganizer.Desktop.ViewModels;

public sealed record RegexTestItem(string Sample, bool Matched, string Result);

/// <summary>魔术工具：样本加载 → 指纹查看 → 正则测试 → 保存模式（交互式圈选为后续迭代）。</summary>
public partial class MagicToolsViewModel : ViewModelBase
{
    private readonly List<PatternDefinition> _patterns;
    private readonly string _patternsPath;

    public event Action? PatternsSaved;

    public ObservableCollection<string> Samples { get; } = [];
    public ObservableCollection<RegexTestItem> TestResults { get; } = [];

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

    public MagicToolsViewModel(List<PatternDefinition> patterns, string patternsPath)
    {
        _patterns = patterns;
        _patternsPath = patternsPath;
    }

    public void LoadSamples(IEnumerable<string> names)
    {
        Samples.Clear();
        foreach (var n in names) Samples.Add(n);
        SampleInfo = $"{Samples.Count} 个样本";
        SelectedSample = Samples.FirstOrDefault() ?? "";
        if (!string.IsNullOrEmpty(SelectedSample)) TestRegex();
    }

    partial void OnSelectedSampleChanged(string value)
    {
        Fingerprint = string.IsNullOrEmpty(value) ? "" : StructureFingerprint.Compute(value);
        TestRegex();
    }

    partial void OnRegexTextChanged(string value) => TestRegex();

    [RelayCommand]
    private void TestRegex()
    {
        TestResults.Clear();
        SaveHint = "";
        if (string.IsNullOrWhiteSpace(RegexText) || Samples.Count == 0) return;

        var pattern = BuildTestPattern(RegexText);
        if (pattern is null)
        {
            SaveHint = "正则无效：请先通过测试";
            return;
        }

        foreach (var sample in Samples)
        {
            var date = PatternEngine.TryExtract(sample, pattern);
            TestResults.Add(new RegexTestItem(sample, date is not null,
                date is { } d ? d.ToString("yyyy-MM-dd HH:mm:ss") : "未命中"));
        }
        SaveHint = TestResults.Any(t => t.Matched) ? "测试通过，可保存" : "";
    }

    private static PatternDefinition? BuildTestPattern(string regex)
    {
        try
        {
            var probe = new PatternDefinition
            {
                Name = "__test__",
                Pattern = regex,
                Enabled = true,
                GroupMapping = new Dictionary<string, int>()
            };

            // 用第一个样本探测组结构
            var m = Regex.Match("probe12345678901234567890", regex);
            if (!m.Success) return probe; // 允许零命中（测试显示未命中）

            var map = new Dictionary<string, int>();
            for (var i = 1; i < m.Groups.Count; i++)
            {
                if (m.Groups[i].Name != i.ToString() && !string.IsNullOrEmpty(m.Groups[i].Name))
                    map[m.Groups[i].Name] = i; // 命名组：year/month/day/.../timestamp
            }

            if (map.Count == 0)
            {
                // 位置推断：1 组纯数字 → 时间戳；3 组 → y/m/d；6 组 → y/m/d/h/m/s
                var n = m.Groups.Count - 1;
                if (n == 1 && long.TryParse(m.Groups[1].Value, out var ts))
                {
                    var len = m.Groups[1].Value.Length;
                    if (len is 10 or 13 or 16)
                    {
                        map["timestamp"] = 1;
                        probe.TimestampLength = len;
                    }
                }
                else if (n >= 3)
                {
                    map["year"] = 1; map["month"] = 2; map["day"] = 3;
                    if (n >= 6) { map["hour"] = 4; map["minute"] = 5; map["second"] = 6; }
                }
            }

            probe.GroupMapping = map;
            return probe;
        }
        catch
        {
            return null; // 正则非法
        }
    }

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
            SaveHint = "请先填写正则表达式";
            return;
        }
        if (TestResults.All(t => !t.Matched))
        {
            SaveHint = "没有样本命中，确认要保存吗？";
        }

        var test = BuildTestPattern(RegexText) ?? new PatternDefinition { Pattern = RegexText };
        var existing = _patterns.FirstOrDefault(p => p.Name == PatternName);
        if (existing is not null) _patterns.Remove(existing);

        _patterns.Add(new PatternDefinition
        {
            Name = PatternName,
            Pattern = RegexText,
            GroupMapping = test.GroupMapping,
            TimestampLength = test.TimestampLength,
            Enabled = true,
            Weight = 1.0,
            Builtin = false
        });
        PatternsStore.Save(_patternsPath, _patterns);
        SaveHint = $"已保存「{PatternName}」到 patterns.json";
        PatternsSaved?.Invoke();
    }
}
