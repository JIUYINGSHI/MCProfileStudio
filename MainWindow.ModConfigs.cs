using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace McProfileStudio;

public partial class MainWindow
{
    private sealed record ModConfigDefinition(string Id, string ChineseName, string EnglishName, string RelativePath, string PrimaryJson, string[] Aliases)
    {
        public string DisplayName => $"{ChineseName} / {EnglishName}";
    }

    private sealed class ModConfigDraft
    {
        public required ModConfigDefinition Definition { get; init; }
        public required string SourcePath { get; init; }
        public JsonObject? Json { get; set; }
        public bool IsDetected { get; set; }
        public override string ToString() => Definition.DisplayName;
    }

    private static readonly ModConfigDefinition[] SupportedModConfigs =
    [
        new("inventoryprofilesnext", "一键背包整理 Next", "Inventory Profiles Next", "config/inventoryprofilesnext", "inventoryprofiles.json", ["inventoryprofilesnext", "inventory-profiles-next", "libipn"]),
        new("tweakeroo", "推客工坊", "Tweakeroo", "config/tweakeroo.json", "", ["tweakeroo"]),
        new("litematica", "投影", "Litematica", "config/litematica.json", "", ["litematica"]),
        new("tweakermore", "更多功能", "TweakerMore", "config/tweakermore.json", "", ["tweakermore"]),
        new("itemscroller", "物品滚轮", "Item Scroller", "config/itemscroller.json", "", ["itemscroller"]),
        new("minihud", "迷你信息显示", "MiniHUD", "config/minihud.json", "", ["minihud"]),
        new("malilib", "MaLiLib 前置", "MaLiLib", "config/malilib.json", "", ["malilib"])
    ];
    private static readonly Dictionary<string, string> ConfigCategoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Generic"] = "通用", ["GenericHotkeys"] = "通用快捷键", ["Fixes"] = "修复项", ["Lists"] = "列表", ["TweakToggles"] = "功能开关", ["TweakHotkeys"] = "功能快捷键", ["DisableToggles"] = "禁用项", ["DisableHotkeys"] = "禁用快捷键", ["Internal"] = "内部设置", ["Features"] = "功能", ["ModSettings"] = "Mod 设置", ["GuiSettings"] = "界面设置", ["LockedSlotsSettings"] = "锁定槽位", ["AutoRefillSettings"] = "自动补货", ["EditProfiles"] = "配置档案", ["Visuals"] = "视觉", ["Hotkeys"] = "快捷键"
    };

    private Grid? modConfigsPage;
    private ListBox? modConfigModList;
    private TabControl? modConfigTabs;
    private ComboBox? modConfigProfileCombo;
    private ComboBox? modConfigLanguageCombo;
    private TextBox? modConfigSearch;
    private TextBlock? modConfigHint;
    private readonly List<ModConfigDraft> modConfigDrafts = [];
    private bool switchingModConfigProfile;

    private string ModConfigProfilesRoot => Path.Combine(SettingsStore.Root, "mod-config-profiles");

    private void BuildModConfigPage()
    {
        if (FindLogicalParent<Grid>(KeysPage) is not { } host || FindLogicalParent<StackPanel>(FindNavButton("3")) is not { } navigation) return;

        var nav = new RadioButton { Content = "▤  Mod 配置", Tag = "4", Style = (Style)FindResource("Nav") };
        nav.Checked += Navigate; navigation.Children.Add(nav);

        modConfigsPage = new Grid { Visibility = Visibility.Collapsed };
        modConfigsPage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(285) });
        modConfigsPage.ColumnDefinitions.Add(new ColumnDefinition());
        modConfigsPage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(285) });

        var left = MakeModConfigCard();
        var leftStack = new Grid(); leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); leftStack.RowDefinitions.Add(new RowDefinition());
        var leftTitle = new TextBlock { Text = "检测到的独立配置", Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) }; Grid.SetRow(leftTitle, 0); leftStack.Children.Add(leftTitle);
        var leftHint = new TextBlock { Text = "只显示当前实例已安装且有配置文件的 Mod", Foreground = new SolidColorBrush(Color.FromRgb(166, 185, 203)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; Grid.SetRow(leftHint, 1); leftStack.Children.Add(leftHint);
        modConfigModList = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), DisplayMemberPath = "Definition.DisplayName" };
        modConfigModList.SelectionChanged += ModConfigModList_SelectionChanged;
        Grid.SetRow(modConfigModList, 2); leftStack.Children.Add(modConfigModList); left.Child = leftStack; Grid.SetColumn(left, 0); modConfigsPage.Children.Add(left);

        var center = MakeModConfigCard(new Thickness(10, 0, 10, 14));
        var centerGrid = new Grid(); centerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); centerGrid.RowDefinitions.Add(new RowDefinition());
        modConfigSearch = new TextBox { ToolTip = "搜索选项名称或当前值", Margin = new Thickness(0, 0, 0, 10) };
        modConfigSearch.TextChanged += (_, _) => RenderSelectedModConfig(); Grid.SetRow(modConfigSearch, 0); centerGrid.Children.Add(modConfigSearch);
        modConfigTabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0) }; Grid.SetRow(modConfigTabs, 1); centerGrid.Children.Add(modConfigTabs);
        center.Child = centerGrid; Grid.SetColumn(center, 1); modConfigsPage.Children.Add(center);

        var right = MakeModConfigCard();
        var actions = new StackPanel();
        actions.Children.Add(new TextBlock { Text = "Mod 配置方案", Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold });
        actions.Children.Add(new TextBlock { Text = "保存后才会成为可复用模板；应用时只覆盖当前实例存在的 Mod。", Foreground = new SolidColorBrush(Color.FromRgb(166, 185, 203)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 12) });
        modConfigProfileCombo = new ComboBox(); modConfigProfileCombo.SelectionChanged += ModConfigProfile_SelectionChanged; actions.Children.Add(modConfigProfileCombo);
        actions.Children.Add(MakeActionButton("新建配置", AddModConfigProfile_Click, new Thickness(0, 10, 0, 0)));
        actions.Children.Add(MakeActionButton("重命名", RenameModConfigProfile_Click, new Thickness(0, 7, 0, 0), false));
        actions.Children.Add(MakeActionButton("保存当前草稿", SaveModConfigProfile_Click, new Thickness(0, 7, 0, 0)));
        actions.Children.Add(new TextBlock { Text = "选项显示语言", Foreground = new SolidColorBrush(Color.FromRgb(166, 185, 203)), Margin = new Thickness(0, 14, 0, 6) });
        modConfigLanguageCombo = new ComboBox { ItemsSource = new[] { "中文优先", "中英双语", "English" }, SelectedItem = settings.ModConfigLanguage };
        modConfigLanguageCombo.SelectionChanged += (_, _) => { if (modConfigLanguageCombo.SelectedItem is string language) { settings.ModConfigLanguage = language; SettingsStore.Save(settings); RenderSelectedModConfig(); } }; actions.Children.Add(modConfigLanguageCombo);
        actions.Children.Add(new Separator { Opacity = .2, Margin = new Thickness(0, 14, 0, 12) });
        actions.Children.Add(MakeActionButton("应用当前 Mod", ApplySelectedModConfig_Click, new Thickness(0), false));
        actions.Children.Add(MakeActionButton("一键覆盖全部已安装 Mod", ApplyAllModConfigs_Click, new Thickness(0, 7, 0, 0)));
        modConfigHint = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(103, 190, 245)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) }; actions.Children.Add(modConfigHint);
        right.Child = actions; Grid.SetColumn(right, 2); modConfigsPage.Children.Add(right);

        host.Children.Add(modConfigsPage); RefreshModConfigProfiles(); RefreshModConfigPage();
    }

    private RadioButton? FindNavButton(string tag)
    {
        return FindDescendant<RadioButton>(this, x => Equals(x.Tag, tag));
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match && predicate(match)) return match;
            if (FindDescendant<T>(child, predicate) is { } nested) return nested;
        }
        return null;
    }

    private static Border MakeModConfigCard(Thickness? margin = null) => new()
    {
        Margin = margin ?? new Thickness(0, 0, 0, 14), Padding = new Thickness(18), CornerRadius = new CornerRadius(18),
        Background = new SolidColorBrush(Color.FromArgb(205, 12, 24, 34)), BorderBrush = new SolidColorBrush(Color.FromArgb(80, 116, 154, 184)), BorderThickness = new Thickness(1)
    };

    private Button MakeActionButton(string text, RoutedEventHandler handler, Thickness margin, bool primary = true)
    {
        var button = new Button { Content = text, Margin = margin, Background = new SolidColorBrush(primary ? Color.FromRgb(0, 120, 212) : Color.FromRgb(56, 71, 86)) };
        button.Click += handler; return button;
    }

    private void RefreshModConfigPage()
    {
        if (modConfigModList == null) return;
        modConfigDrafts.Clear();
        if (!string.IsNullOrWhiteSpace(instance))
        {
            foreach (var definition in SupportedModConfigs)
            {
                var path = Path.Combine(instance, definition.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var installed = mods.Keys.Any(id => definition.Aliases.Any(alias => id.Contains(alias, StringComparison.OrdinalIgnoreCase))) || File.Exists(path) || Directory.Exists(path);
                if (!installed) continue;
                var primary = Directory.Exists(path) ? Path.Combine(path, definition.PrimaryJson) : path;
                if (!File.Exists(primary)) continue;
                JsonObject? json = null; try { json = JsonNode.Parse(File.ReadAllText(primary)) as JsonObject; } catch { }
                modConfigDrafts.Add(new ModConfigDraft { Definition = definition, SourcePath = path, Json = json, IsDetected = true });
            }
        }
        modConfigModList.ItemsSource = null; modConfigModList.ItemsSource = modConfigDrafts; if (modConfigDrafts.Count > 0) modConfigModList.SelectedIndex = 0;
        if (modConfigHint != null) modConfigHint.Text = modConfigDrafts.Count == 0 ? "请先导入包含这些 Mod 的游戏实例。" : $"已检测到 {modConfigDrafts.Count} 个可管理的独立 Mod 配置。";
    }

    private void ModConfigModList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RenderSelectedModConfig();

    private void RenderSelectedModConfig()
    {
        if (modConfigTabs == null) return; modConfigTabs.Items.Clear();
        if (modConfigModList?.SelectedItem is not ModConfigDraft draft || draft.Json == null) return;
        var search = modConfigSearch?.Text?.Trim() ?? "";
        foreach (var category in draft.Json)
        {
            var panel = new StackPanel { Margin = new Thickness(4) };
            if (category.Value is JsonObject group)
            {
                foreach (var option in group)
                {
                    var valueText = option.Value?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "null";
                    if (search.Length > 0 && !(option.Key + valueText).Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                    panel.Children.Add(BuildModOptionRow(draft, category.Key, group, option.Key, option.Value));
                }
            }
            else panel.Children.Add(BuildModOptionRow(draft, category.Key, draft.Json, category.Key, category.Value));
            if (panel.Children.Count == 0) continue;
            var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            modConfigTabs.Items.Add(new TabItem { Header = TranslateCategory(draft, category.Key), Content = scroll });
        }
        if (modConfigTabs.Items.Count > 0) modConfigTabs.SelectedIndex = 0;
    }

    private FrameworkElement BuildModOptionRow(ModConfigDraft draft, string category, JsonObject owner, string key, JsonNode? value)
    {
        var editOwner = owner; var editKey = key; var editableValue = value;
        if (value is JsonObject wrapper && wrapper.Count == 1)
        {
            var wrapped = wrapper.First();
            if (wrapped.Key is "keys" or "value") { editOwner = wrapper; editKey = wrapped.Key; editableValue = wrapped.Value; }
        }
        var border = new Border { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(175, 47, 47, 47)), BorderBrush = new SolidColorBrush(Color.FromRgb(116, 116, 116)), BorderThickness = new Thickness(1) };
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        var translated = TranslateConfigOption(draft, category, key);
        var label = new TextBlock { Text = translated.Label, ToolTip = translated.Tooltip, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) }; grid.Children.Add(label);
        FrameworkElement editor;
        if (editableValue is JsonValue scalar && scalar.TryGetValue<bool>(out var boolean))
        {
            var toggle = new CheckBox { Content = boolean ? "true" : "false", IsChecked = boolean, Foreground = new SolidColorBrush(boolean ? Color.FromRgb(88, 220, 120) : Color.FromRgb(255, 105, 115)), HorizontalAlignment = HorizontalAlignment.Stretch };
            toggle.Checked += (_, _) => { editOwner[editKey] = true; toggle.Content = "true"; toggle.Foreground = new SolidColorBrush(Color.FromRgb(88, 220, 120)); MarkModConfigDraftChanged(); };
            toggle.Unchecked += (_, _) => { editOwner[editKey] = false; toggle.Content = "false"; toggle.Foreground = new SolidColorBrush(Color.FromRgb(255, 105, 115)); MarkModConfigDraftChanged(); }; editor = toggle;
        }
        else
        {
            var text = new TextBox { Text = ScalarDisplay(editableValue), ToolTip = editKey == "keys" ? "填写 MaLiLib 快捷键，例如 X,C；空白表示未绑定。" : "复杂值可直接填写 JSON。" };
            text.LostFocus += (_, _) => { editOwner[editKey] = ParseEditedValue(text.Text, editableValue); MarkModConfigDraftChanged(); }; editor = text;
        }
        Grid.SetColumn(editor, 1); grid.Children.Add(editor); border.Child = grid; return border;
    }

    private static string ScalarDisplay(JsonNode? value)
    {
        if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text)) return text;
        return value?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "null";
    }

    private static JsonNode? ParseEditedValue(string text, JsonNode? original)
    {
        try
        {
            if (original is JsonValue value)
            {
                if (value.TryGetValue<int>(out _) && int.TryParse(text, out var integer)) return JsonValue.Create(integer);
                if (value.TryGetValue<double>(out _) && double.TryParse(text, out var number)) return JsonValue.Create(number);
                if (value.TryGetValue<string>(out _)) return JsonValue.Create(text);
            }
            return JsonNode.Parse(text);
        }
        catch { return JsonValue.Create(text); }
    }

    private string TranslateCategory(ModConfigDraft draft, string category)
    {
        var fallback = HumanizeConfigName(category);
        var info = FindModTranslationInfo(draft.Definition); if (info == null) return fallback;
        var tokens = new[] { category, ToSnakeCase(category) };
        var chinese = FindTranslation(info.ChineseTranslations, tokens, true, true);
        var english = FindTranslation(info.EnglishTranslations, tokens, true, true);
        return FormatLocalizedLabel(chinese, english, fallback);
    }

    private (string Label, string Tooltip) TranslateConfigOption(ModConfigDraft draft, string category, string key)
    {
        var fallback = HumanizeConfigName(key); var info = FindModTranslationInfo(draft.Definition);
        if (info == null) return (fallback, key);
        var candidates = BuildTranslationCandidates(draft.Definition.Id, category, key);
        var chinese = FindFirst(info.ChineseTranslations, candidates) ?? FindTranslation(info.ChineseTranslations, [key, ToSnakeCase(key)], false, false);
        var english = FindFirst(info.EnglishTranslations, candidates) ?? FindTranslation(info.EnglishTranslations, [key, ToSnakeCase(key)], false, false);
        var commentCandidates = candidates.Select(x => x.Replace(".name.", ".comment.").Replace(".prettyName.", ".comment.").Replace("config.name.", "config.description.")).ToArray();
        var chineseComment = FindFirst(info.ChineseTranslations, commentCandidates) ?? FindTranslation(info.ChineseTranslations, [key, ToSnakeCase(key)], false, true);
        var englishComment = FindFirst(info.EnglishTranslations, commentCandidates) ?? FindTranslation(info.EnglishTranslations, [key, ToSnakeCase(key)], false, true);
        var label = FormatLocalizedLabel(chinese, english, fallback);
        var description = FormatLocalizedDescription(chineseComment, englishComment);
        return (label, string.IsNullOrWhiteSpace(description) ? key : $"{description}\n\n配置键：{key}");
    }

    private ModInfo? FindModTranslationInfo(ModConfigDefinition definition)
    {
        foreach (var alias in definition.Aliases)
            if (mods.TryGetValue(alias, out var exact)) return exact;
        return mods.Values.FirstOrDefault(info => definition.Aliases.Any(alias => info.Id.Contains(alias, StringComparison.OrdinalIgnoreCase)) ||
            info.ChineseTranslations.Keys.Concat(info.EnglishTranslations.Keys).Any(k => k.StartsWith(definition.Id + ".", StringComparison.OrdinalIgnoreCase) || (definition.Id == "inventoryprofilesnext" && k.StartsWith("inventoryprofiles.", StringComparison.OrdinalIgnoreCase))));
    }

    private static string[] BuildTranslationCandidates(string modId, string category, string key)
    {
        var snake = ToSnakeCase(key); var lowerCategory = ToSnakeCase(category);
        if (modId == "inventoryprofilesnext") return [$"inventoryprofiles.config.name.{key}", $"inventoryprofiles.config.name.{snake}"];
        var prefix = modId switch { "tweakeroo" => "tweakeroo", "litematica" => "litematica", "minihud" => "minihud", "itemscroller" => "itemscroller", _ => modId };
        var section = category switch
        {
            "TweakToggles" or "TweakHotkeys" => "feature_toggle",
            "DisableToggles" or "DisableHotkeys" => "disable_toggle",
            "GenericHotkeys" or "Hotkeys" => "hotkey",
            "Fixes" => "fix",
            "Lists" => "list",
            _ => lowerCategory.Replace("_hotkeys", "").Replace("_settings", "")
        };
        return [$"{prefix}.config.{section}.prettyName.{key}", $"{prefix}.config.{section}.name.{key}", $"{prefix}.config.{section}.name.{snake}", $"{prefix}.config.name.{key}", $"{prefix}.config.name.{snake}"];
    }

    private static string? FindFirst(Dictionary<string, string> translations, IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates) if (translations.TryGetValue(candidate, out var value) && !string.IsNullOrWhiteSpace(value)) return CleanMinecraftFormatting(value);
        return null;
    }

    private static string? FindTranslation(Dictionary<string, string> translations, IEnumerable<string> tokens, bool category, bool comment)
    {
        foreach (var token in tokens)
        {
            var suffix = "." + token;
            var matches = translations.Where(pair => pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (comment) matches = matches.Where(pair => pair.Key.Contains(".comment.", StringComparison.OrdinalIgnoreCase) || pair.Key.Contains(".description.", StringComparison.OrdinalIgnoreCase));
            else if (category) matches = matches.Where(pair => pair.Key.Contains("category", StringComparison.OrdinalIgnoreCase) || pair.Key.Contains("gui.config", StringComparison.OrdinalIgnoreCase));
            else matches = matches.Where(pair => pair.Key.Contains(".name.", StringComparison.OrdinalIgnoreCase) || pair.Key.Contains(".prettyName.", StringComparison.OrdinalIgnoreCase));
            var match = matches.FirstOrDefault(); if (!string.IsNullOrWhiteSpace(match.Value)) return CleanMinecraftFormatting(match.Value);
        }
        return null;
    }

    private string FormatLocalizedLabel(string? chinese, string? english, string fallback)
    {
        return settings.ModConfigLanguage switch
        {
            "English" => english ?? chinese ?? fallback,
            "中英双语" when !string.IsNullOrWhiteSpace(chinese) && !string.IsNullOrWhiteSpace(english) && !chinese.Equals(english, StringComparison.OrdinalIgnoreCase) => $"{chinese}  /  {english}",
            "中英双语" => chinese ?? english ?? fallback,
            _ => chinese ?? english ?? fallback
        };
    }

    private string FormatLocalizedDescription(string? chinese, string? english)
    {
        return settings.ModConfigLanguage switch
        {
            "English" => english ?? chinese ?? "",
            "中英双语" when !string.IsNullOrWhiteSpace(chinese) && !string.IsNullOrWhiteSpace(english) && !chinese.Equals(english, StringComparison.OrdinalIgnoreCase) => $"{chinese}\n\n{english}",
            "中英双语" => chinese ?? english ?? "",
            _ => chinese ?? english ?? ""
        };
    }

    private static string CleanMinecraftFormatting(string value)
    {
        return System.Text.RegularExpressions.Regex.Replace(value, "§[0-9a-fk-or]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value; var result = new System.Text.StringBuilder();
        for (var i = 0; i < value.Length; i++) { if (i > 0 && char.IsUpper(value[i]) && (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1]))) result.Append('_'); result.Append(char.ToLowerInvariant(value[i])); }
        return result.ToString().Replace('-', '_');
    }

    private static string HumanizeConfigName(string value)
    {
        if (ConfigCategoryNames.TryGetValue(value, out var translated)) return translated;
        if (string.IsNullOrWhiteSpace(value)) return value;
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < value.Length; i++) { if (i > 0 && char.IsUpper(value[i]) && char.IsLower(value[i - 1])) result.Append(' '); result.Append(value[i]); }
        return result.ToString().Replace('_', ' ');
    }

    private void MarkModConfigDraftChanged() => StatusText.Text = "Mod 配置已修改（尚未保存配置）";

    private void RefreshModConfigProfiles()
    {
        if (modConfigProfileCombo == null) return; Directory.CreateDirectory(ModConfigProfilesRoot);
        var names = Directory.EnumerateDirectories(ModConfigProfilesRoot).Select(Path.GetFileName).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().Order().ToList();
        if (!names.Contains(settings.ActiveModConfigProfile)) names.Insert(0, settings.ActiveModConfigProfile);
        switchingModConfigProfile = true; modConfigProfileCombo.ItemsSource = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList(); modConfigProfileCombo.SelectedItem = settings.ActiveModConfigProfile; switchingModConfigProfile = false;
    }

    private void ModConfigProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (switchingModConfigProfile || modConfigProfileCombo?.SelectedItem is not string name) return; settings.ActiveModConfigProfile = name; SettingsStore.Save(settings); LoadModConfigProfileDraft();
    }

    private void AddModConfigProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptForProfileName("新建 Mod 配置", "新的 Mod 配置"); if (string.IsNullOrWhiteSpace(name)) return; settings.ActiveModConfigProfile = name; RefreshModConfigProfiles(); StatusText.Text = "新 Mod 配置尚未保存";
    }

    private void RenameModConfigProfile_Click(object sender, RoutedEventArgs e)
    {
        var old = settings.ActiveModConfigProfile; var name = PromptForProfileName("重命名 Mod 配置", old); if (string.IsNullOrWhiteSpace(name) || name == old) return;
        var oldPath = Path.Combine(ModConfigProfilesRoot, SafeProfileName(old)); var newPath = Path.Combine(ModConfigProfilesRoot, SafeProfileName(name)); if (Directory.Exists(oldPath) && !Directory.Exists(newPath)) Directory.Move(oldPath, newPath); settings.ActiveModConfigProfile = name; SettingsStore.Save(settings); RefreshModConfigProfiles();
    }

    private void SaveModConfigProfile_Click(object sender, RoutedEventArgs e)
    {
        if (modConfigDrafts.Count == 0) return; var root = Path.Combine(ModConfigProfilesRoot, SafeProfileName(settings.ActiveModConfigProfile)); Directory.CreateDirectory(root);
        foreach (var draft in modConfigDrafts)
        {
            var destination = Path.Combine(root, draft.Definition.Id); if (Directory.Exists(destination)) Directory.Delete(destination, true); Directory.CreateDirectory(destination);
            if (Directory.Exists(draft.SourcePath)) CopyDirectory(draft.SourcePath, destination); else File.Copy(draft.SourcePath, Path.Combine(destination, Path.GetFileName(draft.SourcePath)), true);
            var primary = draft.Definition.PrimaryJson.Length > 0 ? Path.Combine(destination, draft.Definition.PrimaryJson) : Path.Combine(destination, Path.GetFileName(draft.SourcePath));
            if (draft.Json != null) File.WriteAllText(primary, draft.Json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        SettingsStore.Save(settings); RefreshModConfigProfiles(); StatusText.Text = $"已保存 Mod 配置：{settings.ActiveModConfigProfile}";
    }

    private void LoadModConfigProfileDraft()
    {
        var root = Path.Combine(ModConfigProfilesRoot, SafeProfileName(settings.ActiveModConfigProfile));
        foreach (var draft in modConfigDrafts)
        {
            var folder = Path.Combine(root, draft.Definition.Id); var primary = draft.Definition.PrimaryJson.Length > 0 ? Path.Combine(folder, draft.Definition.PrimaryJson) : Path.Combine(folder, Path.GetFileName(draft.SourcePath));
            if (!File.Exists(primary)) continue; try { draft.Json = JsonNode.Parse(File.ReadAllText(primary)) as JsonObject; } catch { }
        }
        RenderSelectedModConfig(); StatusText.Text = $"已载入 Mod 配置草稿：{settings.ActiveModConfigProfile}";
    }

    private void ApplySelectedModConfig_Click(object sender, RoutedEventArgs e)
    {
        if (modConfigModList?.SelectedItem is ModConfigDraft draft) ApplyModConfigProfile([draft]);
    }

    private void ApplyAllModConfigs_Click(object sender, RoutedEventArgs e) => ApplyModConfigProfile(modConfigDrafts);

    private void ApplyModConfigProfile(IEnumerable<ModConfigDraft> drafts)
    {
        if (string.IsNullOrWhiteSpace(instance)) return; var profileRoot = Path.Combine(ModConfigProfilesRoot, SafeProfileName(settings.ActiveModConfigProfile)); var applied = 0;
        foreach (var draft in drafts)
        {
            var source = Path.Combine(profileRoot, draft.Definition.Id); if (!Directory.Exists(source) || !draft.IsDetected) continue;
            if (Directory.Exists(draft.SourcePath)) { BackupModConfig(draft.SourcePath); CopyDirectory(source, draft.SourcePath); }
            else { var profileFile = Path.Combine(source, Path.GetFileName(draft.SourcePath)); if (!File.Exists(profileFile)) continue; BackupModConfig(draft.SourcePath); Directory.CreateDirectory(Path.GetDirectoryName(draft.SourcePath)!); File.Copy(profileFile, draft.SourcePath, true); }
            applied++;
        }
        StatusText.Text = $"已覆盖 {applied} 个已安装 Mod 的独立配置"; MessageBox.Show(this, $"已应用 {applied} 个 Mod 配置。写入前已生成 .mcprofilestudio.bak 备份。\n请在 Minecraft 关闭时执行覆盖，部分 Mod 只会在下次启动时读取配置。", "Mod 配置已应用", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void BackupModConfig(string path)
    {
        if (File.Exists(path)) File.Copy(path, path + ".mcprofilestudio.bak", true);
        else if (Directory.Exists(path)) { var primaryFiles = Directory.EnumerateFiles(path, "*.json", SearchOption.TopDirectoryOnly); foreach (var file in primaryFiles) File.Copy(file, file + ".mcprofilestudio.bak", true); }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
    }

    private static string SafeProfileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_'); return value.Trim();
    }
}
