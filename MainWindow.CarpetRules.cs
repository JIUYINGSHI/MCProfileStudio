using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace McProfileStudio;

public partial class MainWindow
{
    private sealed class CarpetRuleCatalog
    {
        public string ConfigPath { get; init; } = "";
        public List<CarpetRuleEntry> Rules { get; } = [];
    }

    private sealed class CarpetRuleEntry
    {
        public string Name { get; init; } = "";
        public string SourceId { get; init; } = "";
        public string SourceName { get; init; } = "";
        public string ChineseName { get; init; } = "";
        public string EnglishName { get; init; } = "";
        public string ChineseDescription { get; init; } = "";
        public string EnglishDescription { get; init; } = "";
        public string Value { get; set; } = "";
        public CarpetRuleValueKind Kind { get; init; } = CarpetRuleValueKind.Text;
        public IReadOnlyList<string> Suggestions { get; init; } = [];
    }

    private ModConfigDraft? DiscoverCarpetRuleConfig()
    {
        if (!mods.ContainsKey("carpet") || !mods.ContainsKey("carpetgui")) return null;
        var configPath = Path.Combine(instance, "config", "carpet", "default_carpet.conf");
        var values = ReadCarpetConf(configPath);
        var catalog = new CarpetRuleCatalog { ConfigPath = configPath };
        var metadata = CarpetRuleMetadataScanner.Scan(mods.Values.Where(IsCarpetRuleProvider).Select(mod => mod.JarPath));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods.Values.Where(IsCarpetRuleProvider).OrderBy(mod => mod.Id.Equals("carpet", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(mod => mod.EnglishName))
        {
            var ruleNames = mod.EnglishTranslations.Keys.Concat(mod.ChineseTranslations.Keys)
                .Select(key => TryParseCarpetTranslationKey(key, out var ruleName, out _) ? ruleName : null)
                .Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var name in ruleNames)
            {
                if (name.Length == 0 || !seen.Add(name)) continue;
                var chineseName = FindCarpetTranslation(mod.ChineseTranslations, name, "name");
                var chineseDescription = FindCarpetTranslation(mod.ChineseTranslations, name, "desc");
                var englishName = FindCarpetTranslation(mod.EnglishTranslations, name, "name");
                var englishDescription = FindCarpetTranslation(mod.EnglishTranslations, name, "desc");
                values.TryGetValue(name, out var value);
                metadata.TryGetValue(name, out var ruleMetadata);
                catalog.Rules.Add(new CarpetRuleEntry
                {
                    Name = name, SourceId = mod.Id, SourceName = string.IsNullOrWhiteSpace(mod.ChineseName) ? mod.EnglishName : mod.ChineseName,
                    ChineseName = chineseName ?? "", EnglishName = englishName ?? HumanizeConfigName(name),
                    ChineseDescription = chineseDescription ?? "", EnglishDescription = englishDescription ?? "", Value = value ?? "",
                    Kind = ruleMetadata?.Kind ?? InferCarpetRuleKind(value, chineseDescription, englishDescription), Suggestions = ruleMetadata?.Suggestions ?? []
                });
            }
        }
        if (catalog.Rules.Count == 0) return null;
        var definition = new ModConfigDefinition("carpet-rules", "地毯系列规则", "Carpet Rules", "config/carpet/default_carpet.conf", "", ["carpet", "carpetgui", "carpet-extra", "carpet-tis-addition", "gca", "carpet-org-addition"]);
        var draft = new ModConfigDraft { Definition = definition, SourcePath = configPath, IsDetected = true, CarpetRules = catalog };
        draft.ConfigFiles.Add(configPath);
        return draft;
    }

    private static bool IsCarpetRuleProvider(ModInfo mod) => mod.Id.Equals("carpet", StringComparison.OrdinalIgnoreCase) ||
        mod.EnglishTranslations.Keys.Concat(mod.ChineseTranslations.Keys).Any(key => TryParseCarpetTranslationKey(key, out _, out _));

    private static bool TryParseCarpetTranslationKey(string key, out string ruleName, out string part)
    {
        ruleName = ""; part = "";
        var marker = key.StartsWith("carpet.rule.", StringComparison.OrdinalIgnoreCase) ? "carpet.rule." : ".carpet_translations.rule.";
        var start = marker == "carpet.rule." ? 0 : key.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return false;
        var body = key[(start + marker.Length)..];
        if (body.EndsWith(".name", StringComparison.OrdinalIgnoreCase)) part = "name";
        else if (body.EndsWith(".desc", StringComparison.OrdinalIgnoreCase)) part = "desc";
        else return false;
        ruleName = body[..^(part.Length + 1)];
        return ruleName.Length > 0;
    }

    private static string? FindCarpetTranslation(Dictionary<string, string> translations, string ruleName, string part)
    {
        foreach (var item in translations) if (TryParseCarpetTranslationKey(item.Key, out var candidate, out var candidatePart) && candidate.Equals(ruleName, StringComparison.OrdinalIgnoreCase) && candidatePart.Equals(part, StringComparison.OrdinalIgnoreCase)) return item.Value;
        return null;
    }

    private static CarpetRuleValueKind InferCarpetRuleKind(string? value, string? chineseDescription, string? englishDescription)
    {
        if (bool.TryParse(value, out _)) return CarpetRuleValueKind.Boolean;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return CarpetRuleValueKind.Integer;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return CarpetRuleValueKind.Decimal;
        var description = $"{chineseDescription} {englishDescription}";
        if (Regex.IsMatch(description, "整数|数量|距离|半径|方块|区块|刻|秒|上限|下限|integer|count|distance|radius|chunks?|blocks?|ticks?|limit", RegexOptions.IgnoreCase)) return CarpetRuleValueKind.Integer;
        if (Regex.IsMatch(description, "数值|范围|系数|倍率|概率|速度|number|range|multiplier|chance|probability|speed|factor", RegexOptions.IgnoreCase)) return CarpetRuleValueKind.Decimal;
        return CarpetRuleValueKind.Boolean;
    }

    private void RenderCarpetRuleConfig(ModConfigDraft draft)
    {
        if (modConfigTabs == null || draft.CarpetRules == null) return;
        var search = modConfigSearch?.Text?.Trim() ?? "";
        foreach (var source in draft.CarpetRules.Rules.GroupBy(rule => (rule.SourceId, rule.SourceName)).OrderBy(group => group.Key.SourceId.Equals("carpet", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(group => group.Key.SourceName))
        {
            var panel = new StackPanel { Margin = new Thickness(4) };
            foreach (var rule in source.OrderBy(rule => LocalizedCarpetRuleName(rule), StringComparer.CurrentCultureIgnoreCase))
            {
                var label = LocalizedCarpetRuleName(rule);
                var description = LocalizedCarpetRuleDescription(rule);
                if (search.Length > 0 && !label.Contains(search, StringComparison.OrdinalIgnoreCase) && !rule.Name.Contains(search, StringComparison.OrdinalIgnoreCase) && !description.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                panel.Children.Add(BuildCarpetRuleRow(rule));
            }
            if (panel.Children.Count > 0) modConfigTabs.Items.Add(new TabItem { Header = source.Key.SourceName, Content = MakeModConfigScroll(panel) });
        }
        if (modConfigTabs.Items.Count == 0)
        {
            var empty = new TextBlock { Text = "没有匹配的地毯规则。", Foreground = new SolidColorBrush(Color.FromRgb(166, 185, 203)), Margin = new Thickness(10) };
            modConfigTabs.Items.Add(new TabItem { Header = "搜索结果", Content = empty });
        }
        modConfigTabs.SelectedIndex = 0;
        if (modConfigHint != null) modConfigHint.Text = $"已从当前实例的地毯系列 JAR 解析 {draft.CarpetRules.Rules.Count} 条规则；留空表示使用 Mod 默认值。";
    }

    private FrameworkElement BuildCarpetRuleRow(CarpetRuleEntry rule)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = LocalizedCarpetRuleName(rule), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = rule.Name, Foreground = new SolidColorBrush(Color.FromRgb(103, 190, 245)), FontSize = 11, Margin = new Thickness(0, 3, 0, 0) });
        var description = LocalizedCarpetRuleDescription(rule);
        if (!string.IsNullOrWhiteSpace(description)) text.Children.Add(new TextBlock { Text = description, Foreground = new SolidColorBrush(Color.FromRgb(166, 185, 203)), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 12, 0), MaxHeight = 76 });
        Grid.SetColumn(text, 0); grid.Children.Add(text);
        var editor = BuildCarpetRuleEditor(rule);
        Grid.SetColumn(editor, 1); grid.Children.Add(editor);
        return new Border { Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)), BorderBrush = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), Child = grid };
    }

    private FrameworkElement BuildCarpetRuleEditor(CarpetRuleEntry rule)
    {
        const string inheritedHint = "留空表示使用 Mod 默认值";
        if (rule.Kind == CarpetRuleValueKind.Boolean)
        {
            var check = new CheckBox { IsThreeState = true, IsChecked = rule.Value.Equals("true", StringComparison.OrdinalIgnoreCase) ? true : rule.Value.Equals("false", StringComparison.OrdinalIgnoreCase) ? false : null, VerticalAlignment = VerticalAlignment.Center, ToolTip = "点击循环：默认 / 开启 / 关闭" };
            void UpdateLabel() => check.Content = check.IsChecked switch { true => "开启", false => "关闭", _ => "默认" };
            check.Click += (_, _) => { rule.Value = check.IsChecked switch { true => "true", false => "false", _ => "" }; UpdateLabel(); MarkModConfigDraftChanged(); };
            UpdateLabel(); return check;
        }
        if (rule.Kind is CarpetRuleValueKind.Integer or CarpetRuleValueKind.Decimal)
        {
            var box = new TextBox { Text = rule.Value, Height = 36, VerticalAlignment = VerticalAlignment.Center, ToolTip = inheritedHint + "；这里只接受" + (rule.Kind == CarpetRuleValueKind.Integer ? "整数" : "数值"), VerticalContentAlignment = VerticalAlignment.Center };
            box.LostKeyboardFocus += (_, _) =>
            {
                var value = box.Text.Trim();
                var valid = value.Length == 0 || (rule.Kind == CarpetRuleValueKind.Integer ? long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) : double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
                if (!valid) { box.Text = rule.Value; ShowToast("数值格式不正确", $"{LocalizedCarpetRuleName(rule)} 需要有效的{(rule.Kind == CarpetRuleValueKind.Integer ? "整数" : "数值")}。", false, 3200); return; }
                rule.Value = value; MarkModConfigDraftChanged();
            };
            return box;
        }
        if (rule.Kind == CarpetRuleValueKind.Choice && rule.Suggestions.Count > 0)
        {
            var choices = new[] { "" }.Concat(rule.Suggestions).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var combo = new ComboBox { IsEditable = false, ItemsSource = choices, SelectedItem = choices.FirstOrDefault(value => value.Equals(rule.Value, StringComparison.OrdinalIgnoreCase)) ?? "", ToolTip = inheritedHint };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string value) { rule.Value = value; MarkModConfigDraftChanged(); } };
            return combo;
        }
        var text = new TextBox { Text = rule.Value, Height = 36, VerticalAlignment = VerticalAlignment.Center, ToolTip = inheritedHint, VerticalContentAlignment = VerticalAlignment.Center };
        text.LostKeyboardFocus += (_, _) => { rule.Value = text.Text.Trim(); MarkModConfigDraftChanged(); };
        return text;
    }

    private string LocalizedCarpetRuleName(CarpetRuleEntry rule) => FormatLocalizedLabel(rule.ChineseName, rule.EnglishName, rule.Name);
    private string LocalizedCarpetRuleDescription(CarpetRuleEntry rule) => FormatLocalizedDescription(rule.ChineseDescription, rule.EnglishDescription);

    private static Dictionary<string, string> ReadCarpetConf(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return result;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim(); if (line.Length == 0 || line.StartsWith('#') || line.Equals("locked", StringComparison.OrdinalIgnoreCase)) continue;
            var split = Regex.Match(line, "^(\\S+)\\s+(.+)$"); if (split.Success) result[split.Groups[1].Value] = split.Groups[2].Value.Trim();
        }
        return result;
    }

    private static void SaveCarpetRuleProfile(CarpetRuleCatalog catalog, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = new JsonObject();
        foreach (var rule in catalog.Rules.OrderBy(rule => rule.Name, StringComparer.OrdinalIgnoreCase)) json[rule.Name] = rule.Value;
        File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void LoadCarpetRuleProfile(CarpetRuleCatalog catalog, string path)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject json) return;
            foreach (var rule in catalog.Rules) if (json[rule.Name] is JsonValue value && value.TryGetValue<string>(out var text)) rule.Value = text;
        }
        catch { }
    }

    private void ApplyCarpetRuleProfile(CarpetRuleCatalog catalog)
    {
        var values = catalog.Rules.Where(rule => !string.IsNullOrWhiteSpace(rule.Value)).ToDictionary(rule => rule.Name, rule => rule.Value.Trim(), StringComparer.OrdinalIgnoreCase);
        var knownRules = catalog.Rules.Select(rule => rule.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        WriteMergedCarpetConf(catalog.ConfigPath, knownRules, values, "# This is Carpet Mod's default configuration file\n# Managed by MC Profile Studio. Blank rules keep their Mod defaults.");
        var saves = Path.Combine(instance, "saves");
        if (!Directory.Exists(saves)) return;
        foreach (var world in Directory.EnumerateDirectories(saves))
        {
            var worldConf = Path.Combine(world, "carpet.conf");
            if (!File.Exists(worldConf)) continue;
            WriteMergedCarpetConf(worldConf, knownRules, values, "# Carpet rules managed by MC Profile Studio");
        }
    }

    private static void WriteMergedCarpetConf(string path, HashSet<string> knownRules, Dictionary<string, string> values, string header)
    {
        BackupModConfig(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var oldLines = File.Exists(path) ? File.ReadAllLines(path) : [];
        var locked = oldLines.Any(line => line.Trim().Equals("locked", StringComparison.OrdinalIgnoreCase));
        var lines = new List<string> { header };
        if (locked) lines.Add("locked");
        foreach (var raw in oldLines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.Equals("locked", StringComparison.OrdinalIgnoreCase)) continue;
            var match = Regex.Match(line, "^(\\S+)\\s+(.+)$");
            if (!match.Success || knownRules.Contains(match.Groups[1].Value)) continue;
            lines.Add(raw);
        }
        lines.AddRange(values.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(item => $"{item.Key} {item.Value}"));
        MakeFileWritable(path);
        File.WriteAllLines(path, lines);
    }
}
