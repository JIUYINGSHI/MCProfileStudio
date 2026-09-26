using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace McProfileStudio;

public partial class MainWindow
{
    private sealed record ModConfigDefinition(string Id, string ChineseName, string EnglishName, string RelativePath, string PrimaryJson, string[] Aliases)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(ChineseName) || ChineseName.Equals(EnglishName, StringComparison.OrdinalIgnoreCase) ? EnglishName : $"{ChineseName} / {EnglishName}";
    }

    private sealed class ModConfigDraft
    {
        public required ModConfigDefinition Definition { get; init; }
        public required string SourcePath { get; init; }
        public JsonObject? Json { get; set; }
        public List<string> ConfigFiles { get; } = [];
        public bool IsReadOnlyFormat { get; set; }
        public bool IsAutoDiscovered { get; set; }
        public List<string> DiscoveredHotkeys { get; } = [];
        public JsonObject HotkeyOverrides { get; } = new();
        public List<DiscoveredBooleanOption> DiscoveredOptions { get; } = [];
        public Dictionary<string, bool> BooleanOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool IsDetected { get; set; }
        public override string ToString() => Definition.DisplayName;
    }

    private sealed record DiscoveredBooleanOption(string Category, string Key);
    private sealed record LocalizedConfigChoice(string Value, string Label)
    {
        public override string ToString() => Label;
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
    private static readonly HashSet<string> SupportedConfigExtensions = new(StringComparer.OrdinalIgnoreCase) { ".json", ".json5", ".toml", ".yml", ".yaml", ".properties", ".conf", ".cfg" };
    private static readonly Dictionary<string, string> ConfigCategoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Generic"] = "通用", ["GenericHotkeys"] = "通用快捷键", ["Fixes"] = "修复项", ["Lists"] = "列表", ["TweakToggles"] = "功能开关", ["TweakHotkeys"] = "功能快捷键", ["DisableToggles"] = "禁用项", ["DisableHotkeys"] = "禁用快捷键", ["Internal"] = "内部设置", ["Features"] = "功能", ["ModSettings"] = "Mod 设置", ["GuiSettings"] = "界面设置", ["LockedSlotsSettings"] = "锁定槽位", ["AutoRefillSettings"] = "自动补货", ["EditProfiles"] = "配置档案", ["Visuals"] = "视觉", ["Hotkeys"] = "快捷键"
    };
    private static readonly Dictionary<string, string> GenericConfigNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tooltipColors"] = "提示框颜色", ["showInGameNames"] = "显示游戏内名称", ["hideDefaultBlockLoot"] = "隐藏默认方块掉落物",
        ["showUnboundedGlobalLootModifiers"] = "显示未绑定的全局战利品修改器", ["logMoreStatistics"] = "记录更多统计信息", ["configVersion"] = "配置版本",
        ["maxGuiScale"] = "最大界面缩放", ["text"] = "文本", ["value"] = "数值", ["error"] = "错误", ["branch"] = "分支"
    };
    private static readonly Dictionary<string, string> GenericConfigTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["general"]="通用", ["client"]="客户端", ["server"]="服务端", ["common"]="公共", ["advanced"]="高级", ["internal"]="内部",
        ["show"]="显示", ["hide"]="隐藏", ["enable"]="启用", ["enabled"]="启用", ["disable"]="禁用", ["disabled"]="禁用",
        ["default"]="默认", ["global"]="全局", ["local"]="本地", ["more"]="更多", ["in"]="在", ["game"]="游戏内",
        ["name"]="名称", ["names"]="名称", ["tooltip"]="提示框", ["tooltips"]="提示框", ["color"]="颜色", ["colors"]="颜色",
        ["text"]="文本", ["value"]="数值", ["error"]="错误", ["branch"]="分支", ["version"]="版本", ["config"]="配置",
        ["block"]="方块", ["item"]="物品", ["entity"]="实体", ["loot"]="掉落物", ["modifier"]="修改器", ["modifiers"]="修改器",
        ["unbounded"]="未绑定", ["log"]="记录", ["statistics"]="统计信息", ["rendering"]="渲染", ["performance"]="性能", ["debug"]="调试",
        ["blacklist"]="黑名单", ["whitelist"]="白名单", ["range"]="范围", ["distance"]="距离", ["speed"]="速度", ["limit"]="限制",
        ["count"]="数量", ["size"]="大小", ["width"]="宽度", ["height"]="高度", ["opacity"]="透明度", ["scale"]="缩放",
        ["mode"]="模式", ["interval"]="间隔", ["delay"]="延迟", ["duration"]="持续时间", ["chance"]="概率", ["radius"]="半径",
        ["volume"]="音量", ["sound"]="声音", ["particle"]="粒子", ["particles"]="粒子", ["inventory"]="物品栏", ["search"]="搜索",
        ["display"]="显示", ["filter"]="过滤器", ["filters"]="过滤器", ["category"]="分类", ["categories"]="分类", ["gui"]="界面",
        ["max"]="最大", ["min"]="最小"
    };
    private static readonly Dictionary<string, string> MinecraftColorNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"]="黑色", ["dark_blue"]="深蓝色", ["dark_green"]="深绿色", ["dark_aqua"]="深青色", ["dark_red"]="深红色",
        ["dark_purple"]="深紫色", ["gold"]="金色", ["gray"]="灰色", ["dark_gray"]="深灰色", ["blue"]="蓝色",
        ["green"]="绿色", ["aqua"]="青色", ["red"]="红色", ["light_purple"]="亮紫色", ["yellow"]="黄色", ["white"]="白色"
    };

    private Grid? modConfigsPage;
    private ListBox? modConfigModList;
    private TabControl? modConfigTabs;
    private ComboBox? modConfigProfileCombo;
    private ComboBox? modConfigLanguageCombo;
    private TextBox? modConfigSearch;
    private TextBlock? modConfigHint;
    private readonly List<ModConfigDraft> modConfigDrafts = [];
    private readonly Dictionary<ModConfigDraft, (string Search, string Language, List<TabItem> Tabs)> modConfigViewCache = [];
    private readonly Dictionary<string, List<string>> hotkeyDiscoveryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<DiscoveredBooleanOption>> booleanDiscoveryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModInfo?> modTranslationInfoCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> modCategoryTranslationCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Label, string Tooltip)> modOptionTranslationCache = new(StringComparer.OrdinalIgnoreCase);
    private bool switchingModConfigProfile;

    private string ModConfigProfilesRoot => Path.Combine(SettingsStore.Root, "mod-config-profiles");

    private void BuildModConfigPage()
    {
        if (FindLogicalParent<Grid>(KeysPage) is not { } host || FindLogicalParent<StackPanel>(FindNavButton("3")) is not { } navigation) return;

        var nav = new RadioButton { Content = CreateMinecraftNavContent("4", "Mod 配置"), Tag = "4", Style = (Style)FindResource("Nav") };
        nav.Checked += Navigate; navigation.Children.Add(nav);

        modConfigsPage = new Grid { Visibility = Visibility.Collapsed };
        modConfigsPage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(285) });
        modConfigsPage.ColumnDefinitions.Add(new ColumnDefinition());
        modConfigsPage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(285) });

        var left = MakeModConfigCard();
        var leftStack = new Grid(); leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); leftStack.RowDefinitions.Add(new RowDefinition());
        var leftTitle = new TextBlock { Text = "检测到的独立配置", Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) }; Grid.SetRow(leftTitle, 0); leftStack.Children.Add(leftTitle);
        var leftHint = new TextBlock { Text = "只显示当前实例已安装且能够在此页修改参数的 Mod", Foreground = new SolidColorBrush(Color.FromRgb(166, 185, 203)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; Grid.SetRow(leftHint, 1); leftStack.Children.Add(leftHint);
        modConfigModList = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), DisplayMemberPath = "Definition.DisplayName" };
        modConfigModList.SelectionChanged += ModConfigModList_SelectionChanged;
        Grid.SetRow(modConfigModList, 2); leftStack.Children.Add(modConfigModList); left.Child = leftStack; Grid.SetColumn(left, 0); modConfigsPage.Children.Add(left);

        var center = MakeModConfigCard(new Thickness(10, 0, 10, 14));
        var centerGrid = new Grid(); centerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); centerGrid.RowDefinitions.Add(new RowDefinition());
        modConfigSearch = new TextBox { ToolTip = "搜索选项名称或当前值", Margin = new Thickness(0, 0, 0, 10) };
        modConfigSearch.TextChanged += (_, _) => RenderSelectedModConfig(); Grid.SetRow(modConfigSearch, 0); centerGrid.Children.Add(modConfigSearch);
        modConfigTabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        modConfigTabs.PreviewMouseWheel += ModConfigTabs_PreviewMouseWheel;
        Grid.SetRow(modConfigTabs, 1); centerGrid.Children.Add(modConfigTabs);
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
        Margin = margin ?? new Thickness(0, 0, 0, 14), Padding = new Thickness(18), CornerRadius = new CornerRadius(14),
        Background = new SolidColorBrush(Color.FromArgb(114, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)), BorderThickness = new Thickness(1)
    };

    private Button MakeActionButton(string text, RoutedEventHandler handler, Thickness margin, bool primary = true)
    {
        var button = new Button { Content = text, Margin = margin, Background = new SolidColorBrush(primary ? Color.FromArgb(210, 0, 120, 212) : Color.FromArgb(18, 255, 255, 255)) };
        button.Click += handler; return button;
    }

    private void RefreshModConfigPage()
    {
        if (modConfigModList == null) return;
        modConfigDrafts.Clear();
        modConfigViewCache.Clear();
        modTranslationInfoCache.Clear(); modCategoryTranslationCache.Clear(); modOptionTranslationCache.Clear();
        if (!string.IsNullOrWhiteSpace(instance))
        {
            foreach (var definition in SupportedModConfigs)
            {
                var path = Path.Combine(instance, definition.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var installed = mods.Keys.Any(id => definition.Aliases.Any(alias => id.Contains(alias, StringComparison.OrdinalIgnoreCase))) || File.Exists(path) || Directory.Exists(path);
                if (!installed) continue;
                var primary = Directory.Exists(path) ? Path.Combine(path, definition.PrimaryJson) : path;
                if (!File.Exists(primary)) continue;
                if (!TryReadEditableJsonObject(primary, out var json)) continue;
                var draft = new ModConfigDraft { Definition = definition, SourcePath = path, Json = json, IsDetected = true };
                draft.ConfigFiles.AddRange(Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(IsSupportedConfigFile) : [path]);
                foreach (var hotkey in DiscoverHotkeyNames(definition)) draft.DiscoveredHotkeys.Add(hotkey);
                foreach (var option in DiscoverBooleanOptions(definition, json)) draft.DiscoveredOptions.Add(option);
                modConfigDrafts.Add(draft);
            }
            var coveredIds = modConfigDrafts.SelectMany(draft => draft.Definition.Aliases.Append(draft.Definition.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var dynamicDraft in DiscoverAdditionalModConfigs(coveredIds)) modConfigDrafts.Add(dynamicDraft);
        }
        modConfigDrafts.Sort((left, right) => string.Compare(left.Definition.DisplayName, right.Definition.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        modConfigModList.ItemsSource = null; modConfigModList.ItemsSource = modConfigDrafts; if (modConfigDrafts.Count > 0) modConfigModList.SelectedIndex = 0;
        if (modConfigHint != null) modConfigHint.Text = modConfigDrafts.Count == 0 ? "请先导入包含这些 Mod 的游戏实例。" : $"已检测到 {modConfigDrafts.Count} 个可管理的独立 Mod 配置。";
    }

    private IEnumerable<ModConfigDraft> DiscoverAdditionalModConfigs(HashSet<string> coveredIds)
    {
        var configRoot = Path.Combine(instance, "config"); if (!Directory.Exists(configRoot)) yield break;
        var files = Directory.EnumerateFiles(configRoot, "*", SearchOption.AllDirectories).Where(IsSupportedConfigFile).ToList();
        foreach (var mod in mods.Values.Where(mod => mod.Id != "minecraft" && !coveredIds.Any(id => ModIdMatches(id, mod.Id))))
        {
            var matches = files.Select(file => (File: file, Score: ScoreConfigPath(configRoot, file, mod))).Where(item => item.Score > 0).OrderByDescending(item => item.Score).ThenBy(item => item.File.Length).ToList();
            if (matches.Count == 0) continue;
            var editable = matches.Where(item => Path.GetExtension(item.File).Equals(".json", StringComparison.OrdinalIgnoreCase) && TryReadEditableJsonObject(item.File, out _)).ToList();
            if (editable.Count == 0) continue;
            var primary = editable[0].File; var bestScore = editable[0].Score;
            var selected = matches.Where(item => item.Score >= Math.Max(55, bestScore - 15)).Select(item => item.File).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!selected.Contains(primary, StringComparer.OrdinalIgnoreCase)) selected.Insert(0, primary);
            TryReadJsonObject(primary, out var json);
            var commonDirectory = selected.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 && !Path.GetDirectoryName(primary)!.Equals(configRoot, StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(primary)! : primary;
            var relative = Path.GetRelativePath(instance, commonDirectory).Replace('\\', '/');
            var primaryName = Directory.Exists(commonDirectory) ? Path.GetFileName(primary) : "";
            var definition = new ModConfigDefinition(mod.Id, mod.ChineseName, mod.EnglishName, relative, primaryName, [mod.Id]);
            var draft = new ModConfigDraft { Definition = definition, SourcePath = commonDirectory, Json = json, IsDetected = true, IsAutoDiscovered = true };
            draft.ConfigFiles.AddRange(selected);
            foreach (var hotkey in DiscoverHotkeyNames(definition)) draft.DiscoveredHotkeys.Add(hotkey);
            foreach (var option in DiscoverBooleanOptions(definition, json)) draft.DiscoveredOptions.Add(option);
            yield return draft;
        }
    }

    private static bool IsSupportedConfigFile(string path) => SupportedConfigExtensions.Contains(Path.GetExtension(path));
    private static bool ModIdMatches(string candidate, string modId) => NormalizeConfigToken(candidate).Equals(NormalizeConfigToken(modId), StringComparison.OrdinalIgnoreCase);
    private static string NormalizeConfigToken(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static int ScoreConfigPath(string configRoot, string file, ModInfo mod)
    {
        var relative = Path.GetRelativePath(configRoot, file); var firstPart = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        var id = NormalizeConfigToken(mod.Id); if (id.Length < 3) return NormalizeConfigToken(Path.GetFileNameWithoutExtension(file)).Equals(id, StringComparison.OrdinalIgnoreCase) ? 100 : 0;
        var directory = NormalizeConfigToken(firstPart); var name = NormalizeConfigToken(Path.GetFileNameWithoutExtension(file)); var english = NormalizeConfigToken(mod.EnglishName);
        if (directory.Equals(id, StringComparison.OrdinalIgnoreCase)) return 120;
        if (name.Equals(id, StringComparison.OrdinalIgnoreCase)) return 110;
        if (name.StartsWith(id, StringComparison.OrdinalIgnoreCase) || directory.StartsWith(id, StringComparison.OrdinalIgnoreCase)) return 90;
        if (name.Contains(id, StringComparison.OrdinalIgnoreCase) || directory.Contains(id, StringComparison.OrdinalIgnoreCase)) return 70;
        if (english.Length >= 5 && (name.Contains(english, StringComparison.OrdinalIgnoreCase) || directory.Contains(english, StringComparison.OrdinalIgnoreCase))) return 55;
        return 0;
    }

    private static bool TryReadJsonObject(string path, out JsonObject? json)
    {
        try { json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject; return json != null; } catch { json = null; return false; }
    }

    private static bool TryReadEditableJsonObject(string path, out JsonObject? json)
    {
        if (!TryReadJsonObject(path, out json) || json == null) return false;
        return ContainsEditableConfigValue(json);
    }

    private static bool ContainsEditableConfigValue(JsonNode? node)
    {
        if (node is JsonValue) return true;
        if (node is JsonArray) return false;
        if (node is not JsonObject obj) return false;
        if (IsEditableConfigObject(obj)) return true;
        return obj.Any(item => ContainsEditableConfigValue(item.Value));
    }

    private void ModConfigModList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RenderSelectedModConfig();

    private void ModConfigTabs_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (modConfigTabs?.Template.FindName("HeaderScroll", modConfigTabs) is not ScrollViewer header || header.ScrollableWidth <= 0 || e.OriginalSource is not DependencyObject source || !IsVisualDescendantOf(source, header)) return;
        header.ScrollToHorizontalOffset(Math.Clamp(header.HorizontalOffset - e.Delta * .65, 0, header.ScrollableWidth));
        e.Handled = true;
    }

    private static bool IsVisualDescendantOf(DependencyObject child, DependencyObject ancestor)
    {
        for (DependencyObject? current = child; current != null; current = VisualTreeHelper.GetParent(current)) if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    private void RenderSelectedModConfig()
    {
        if (modConfigTabs == null) return; modConfigTabs.Items.Clear();
        if (modConfigModList?.SelectedItem is not ModConfigDraft draft) return;
        if (draft.Json == null)
        {
            var panel = new StackPanel { Margin = new Thickness(4) };
            panel.Children.Add(new TextBlock { Text = "已检测到此 Mod 的独立配置。当前格式暂不进行结构化编辑，但会随配置方案一起保存和覆盖。", Foreground = new SolidColorBrush(Color.FromRgb(166, 185, 203)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 12) });
            foreach (var file in draft.ConfigFiles)
            {
                var relative = Path.GetRelativePath(instance, file);
                panel.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)), BorderBrush = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), Child = new TextBlock { Text = relative, ToolTip = file, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap } });
            }
            modConfigTabs.Items.Add(new TabItem { Header = "配置文件", Content = MakeModConfigScroll(panel) }); modConfigTabs.SelectedIndex = 0; return;
        }
        var search = modConfigSearch?.Text?.Trim() ?? "";
        if (modConfigViewCache.TryGetValue(draft, out var cached) && cached.Search == search && cached.Language == settings.ModConfigLanguage)
        {
            foreach (var tab in cached.Tabs) modConfigTabs.Items.Add(tab);
            if (modConfigTabs.Items.Count > 0) modConfigTabs.SelectedIndex = 0;
            return;
        }
        var categoryPanels = new Dictionary<string, StackPanel>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in draft.Json)
        {
            if (category.Key == "TweakHotkeys" && draft.Json["TweakToggles"] is JsonObject) continue;
            if (category.Key == "DisableHotkeys" && draft.Json["DisableToggles"] is JsonObject) continue;
            var panel = new StackPanel { Margin = new Thickness(4) };
            if (category.Value is JsonObject group)
            {
                RenderJsonObjectRows(draft, category.Key, group, panel, search, "");
            }
            else if (category.Value is JsonValue) panel.Children.Add(BuildModOptionRow(draft, category.Key, draft.Json, category.Key, category.Value));
            if (panel.Children.Count > 0) categoryPanels[category.Key] = panel;
        }
        var existingHotkeys = CollectExistingHotkeyNames(draft.Json);
        var discovered = draft.DiscoveredHotkeys.Where(key => !existingHotkeys.Contains(key)).Where(key => search.Length == 0 || TranslateConfigOption(draft, "Hotkeys", key).Label.Contains(search, StringComparison.OrdinalIgnoreCase) || key.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        if (discovered.Count > 0)
        {
            var category = draft.Definition.Id == "tweakeroo" && draft.Json.ContainsKey("GenericHotkeys") ? "GenericHotkeys" : "Hotkeys";
            if (!categoryPanels.TryGetValue(category, out var panel)) categoryPanels[category] = panel = new StackPanel { Margin = new Thickness(4) };
            foreach (var key in discovered) panel.Children.Add(BuildDiscoveredHotkeyRow(draft, key));
        }
        var existingOptions = CollectExistingConfigNames(draft.Json);
        var discoveredOptions = draft.DiscoveredOptions
            .Where(option => !existingOptions.Contains(option.Key))
            .Where(option => search.Length == 0 || TranslateConfigOption(draft, option.Category, option.Key).Label.Contains(search, StringComparison.OrdinalIgnoreCase) || option.Key.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (discoveredOptions.Count > 0)
        {
            foreach (var option in discoveredOptions)
            {
                if (!categoryPanels.TryGetValue(option.Category, out var panel)) categoryPanels[option.Category] = panel = new StackPanel { Margin = new Thickness(4) };
                panel.Children.Add(BuildDiscoveredBooleanRow(draft, option));
            }
        }
        var tabs = categoryPanels.Select(category => new TabItem { Header = TranslateCategory(draft, category.Key), Content = MakeModConfigScroll(category.Value) }).ToList();
        modConfigViewCache[draft] = (search, settings.ModConfigLanguage, tabs);
        foreach (var tab in tabs) modConfigTabs.Items.Add(tab);
        if (modConfigTabs.Items.Count > 0) modConfigTabs.SelectedIndex = 0;
    }

    private static ScrollViewer MakeModConfigScroll(Panel panel) => new() { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 10, 2, 0) };

    private void RenderJsonObjectRows(ModConfigDraft draft, string category, JsonObject group, Panel panel, string search, string path)
    {
        foreach (var option in group)
        {
            var fullPath = string.IsNullOrWhiteSpace(path) ? option.Key : $"{path}.{option.Key}";
            if (option.Value is JsonArray || option.Value == null) continue;
            var valueText = option.Value?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "null";
            if (option.Value is JsonObject nested && !IsEditableConfigObject(nested))
            {
                var nestedPanel = new StackPanel { Margin = new Thickness(10, 3, 0, 5) };
                RenderJsonObjectRows(draft, category, nested, nestedPanel, search, fullPath);
                if (nestedPanel.Children.Count == 0) continue;
                panel.Children.Add(new TextBlock { Text = HumanizeConfigName(option.Key), ToolTip = fullPath, Foreground = new SolidColorBrush(Color.FromRgb(103, 190, 245)), FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 8, 4, 7) });
                panel.Children.Add(nestedPanel); continue;
            }
            if (search.Length > 0 && !(fullPath + valueText + TranslateConfigOption(draft, category, option.Key).Label).Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
            JsonObject? pairedHotkey = null;
            if (category == "TweakToggles" && draft.Json?["TweakHotkeys"] is JsonObject tweakHotkeys) pairedHotkey = tweakHotkeys[option.Key] as JsonObject;
            if (category == "DisableToggles" && draft.Json?["DisableHotkeys"] is JsonObject disableHotkeys) pairedHotkey = disableHotkeys[option.Key] as JsonObject;
            panel.Children.Add(BuildModOptionRow(draft, category, group, option.Key, option.Value, pairedHotkey));
        }
    }

    private static bool IsEditableConfigObject(JsonObject value)
    {
        if (value.ContainsKey("enabled") || value.ContainsKey("hotkey") || value.ContainsKey("keys") || value.ContainsKey("keybind") || value.ContainsKey("shortcut")) return true;
        return value.Count == 1 && value.First().Key == "value";
    }

    private static HashSet<string> CollectExistingHotkeyNames(JsonObject root)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(JsonObject current)
        {
            foreach (var item in current)
            {
                if (item.Value is not JsonObject child) continue;
                if (child.ContainsKey("keys") || child.ContainsKey("hotkey") || child.ContainsKey("keybind") || child.ContainsKey("shortcut")) result.Add(item.Key);
                Walk(child);
            }
        }
        Walk(root); return result;
    }

    private static HashSet<string> CollectExistingConfigNames(JsonObject root)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(JsonObject current)
        {
            foreach (var item in current)
            {
                result.Add(item.Key);
                if (item.Value is JsonObject child) Walk(child);
            }
        }
        Walk(root); return result;
    }

    private FrameworkElement BuildModOptionRow(ModConfigDraft draft, string category, JsonObject owner, string key, JsonNode? value, JsonObject? pairedHotkey = null)
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
        var hasDetectedHotkey = TryResolveHotkey(value, out var detectedHotkeyOwner, out var detectedHotkeyKey);
        if (value is JsonObject compound && compound["enabled"] is JsonValue enabledValue && enabledValue.TryGetValue<bool>(out var compoundEnabled) && hasDetectedHotkey)
        {
            editor = BuildToggleHotkeyEditor(compound, "enabled", compoundEnabled, detectedHotkeyOwner!, detectedHotkeyKey!);
        }
        else if (pairedHotkey != null && editableValue is JsonValue pairedScalar && pairedScalar.TryGetValue<bool>(out var pairedEnabled))
        {
            TryResolveHotkey(pairedHotkey, out var pairedOwner, out var pairedKey);
            editor = BuildToggleHotkeyEditor(editOwner, editKey, pairedEnabled, pairedOwner ?? pairedHotkey, pairedKey ?? "keys");
        }
        else if (hasDetectedHotkey)
        {
            editor = BuildHotkeyEditor(detectedHotkeyOwner!, detectedHotkeyKey!);
        }
        else if (editKey == "keys")
        {
            editor = BuildHotkeyEditor(editOwner, editKey);
        }
        else if (editableValue is JsonValue scalar && scalar.TryGetValue<bool>(out var boolean))
        {
            var toggle = new CheckBox { Content = boolean ? "true" : "false", IsChecked = boolean, Foreground = new SolidColorBrush(boolean ? Color.FromRgb(88, 220, 120) : Color.FromRgb(255, 105, 115)), HorizontalAlignment = HorizontalAlignment.Stretch };
            toggle.Checked += (_, _) => { editOwner[editKey] = true; toggle.Content = "true"; toggle.Foreground = new SolidColorBrush(Color.FromRgb(88, 220, 120)); MarkModConfigDraftChanged(); };
            toggle.Unchecked += (_, _) => { editOwner[editKey] = false; toggle.Content = "false"; toggle.Foreground = new SolidColorBrush(Color.FromRgb(255, 105, 115)); MarkModConfigDraftChanged(); }; editor = toggle;
        }
        else if (editableValue is JsonValue textValue && textValue.TryGetValue<string>(out var stringValue) && MinecraftColorNames.ContainsKey(stringValue))
        {
            var choices = MinecraftColorNames.Select(item => new LocalizedConfigChoice(item.Key, item.Value)).ToList();
            var combo = new ComboBox { ItemsSource = choices, SelectedItem = choices.First(item => item.Value.Equals(stringValue, StringComparison.OrdinalIgnoreCase)) };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is LocalizedConfigChoice choice) { editOwner[editKey] = choice.Value; MarkModConfigDraftChanged(); } };
            editor = combo;
        }
        else
        {
            var text = new TextBox { Text = ScalarDisplay(editableValue), ToolTip = editKey == "keys" ? "填写 MaLiLib 快捷键，例如 X,C；空白表示未绑定。" : "复杂值可直接填写 JSON。" };
            text.LostFocus += (_, _) => { editOwner[editKey] = ParseEditedValue(text.Text, editableValue); MarkModConfigDraftChanged(); }; editor = text;
        }
        Grid.SetColumn(editor, 1); grid.Children.Add(editor); border.Child = grid; return border;
    }

    private static bool TryResolveHotkey(JsonNode? value, out JsonObject? owner, out string? key)
    {
        owner = null; key = null; if (value is not JsonObject obj) return false;
        foreach (var candidate in new[] { "keys", "keybind", "shortcut", "binding" })
        {
            if (obj[candidate] is JsonValue scalar && scalar.TryGetValue<string>(out _)) { owner = obj; key = candidate; return true; }
        }
        foreach (var container in new[] { "hotkey", "keybind", "shortcut", "binding" })
        {
            if (obj[container] is not JsonObject nested) continue;
            foreach (var candidate in new[] { "keys", "key", "value", "binding" })
                if (nested[candidate] is JsonValue scalar && scalar.TryGetValue<string>(out _)) { owner = nested; key = candidate; return true; }
        }
        return false;
    }

    private FrameworkElement BuildToggleHotkeyEditor(JsonObject toggleOwner, string toggleKey, bool enabled, JsonObject hotkeyOwner, string hotkeyKey)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var toggle = new Button { Content = enabled ? "true" : "false", Foreground = new SolidColorBrush(enabled ? Color.FromRgb(88, 220, 120) : Color.FromRgb(255, 105, 115)), Background = new SolidColorBrush(Color.FromRgb(126, 126, 126)), Margin = new Thickness(0, 0, 8, 0) };
        toggle.Click += (_, _) => { var next = !(toggleOwner[toggleKey]?.GetValue<bool>() ?? false); toggleOwner[toggleKey] = next; toggle.Content = next ? "true" : "false"; toggle.Foreground = new SolidColorBrush(next ? Color.FromRgb(88, 220, 120) : Color.FromRgb(255, 105, 115)); MarkModConfigDraftChanged(); };
        grid.Children.Add(toggle);
        var hotkey = BuildHotkeyEditor(hotkeyOwner, hotkeyKey); Grid.SetColumn(hotkey, 1); grid.Children.Add(hotkey); return grid;
    }

    private FrameworkElement BuildHotkeyEditor(JsonObject owner, string key)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        var button = new Button { Content = HotkeyDisplay(owner[key]?.GetValue<string>()), Background = new SolidColorBrush(Color.FromRgb(126, 126, 126)), Foreground = Brushes.White, HorizontalContentAlignment = HorizontalAlignment.Center };
        var capturing = false;
        button.Click += (_, _) => { capturing = true; button.Content = "请按下组合键…"; button.Focus(); Keyboard.Focus(button); };
        button.PreviewKeyDown += (_, e) =>
        {
            if (!capturing) return; e.Handled = true; var pressed = e.Key == Key.System ? e.SystemKey : e.Key;
            if (pressed is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt) { button.Content = "继续按主键…"; return; }
            if (pressed == Key.Escape) { capturing = false; button.Content = HotkeyDisplay(owner[key]?.GetValue<string>()); return; }
            var parts = new List<string>();
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("LEFT_CONTROL");
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("LEFT_SHIFT");
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("LEFT_ALT");
            var main = ToMaliKey(pressed); if (!parts.Contains(main, StringComparer.OrdinalIgnoreCase)) parts.Add(main);
            var binding = string.Join(',', parts); owner[key] = binding; button.Content = HotkeyDisplay(binding); capturing = false; MarkModConfigDraftChanged();
        };
        grid.Children.Add(button);
        var clear = new Button { Content = "×", ToolTip = "清除热键", Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)) };
        clear.Click += (_, _) => { owner[key] = ""; button.Content = "NONE"; capturing = false; MarkModConfigDraftChanged(); }; Grid.SetColumn(clear, 1); grid.Children.Add(clear); return grid;
    }

    private static string HotkeyDisplay(string? value) => string.IsNullOrWhiteSpace(value) ? "NONE" : value.Replace("LEFT_CONTROL", "Ctrl").Replace("RIGHT_CONTROL", "RCtrl").Replace("LEFT_SHIFT", "Shift").Replace("RIGHT_SHIFT", "RShift").Replace("LEFT_ALT", "Alt").Replace("RIGHT_ALT", "RAlt").Replace("KP_", "Num ").Replace(',', '+');

    private static string ToMaliKey(Key key) => key switch
    {
        Key.LeftCtrl => "LEFT_CONTROL", Key.RightCtrl => "RIGHT_CONTROL", Key.LeftShift => "LEFT_SHIFT", Key.RightShift => "RIGHT_SHIFT", Key.LeftAlt => "LEFT_ALT", Key.RightAlt => "RIGHT_ALT",
        >= Key.NumPad0 and <= Key.NumPad9 => "KP_" + ((int)key - (int)Key.NumPad0), Key.Add => "KP_ADD", Key.Subtract => "KP_SUBTRACT", Key.Multiply => "KP_MULTIPLY", Key.Divide => "KP_DIVIDE", Key.Decimal => "KP_DECIMAL",
        Key.PageUp => "PAGE_UP", Key.PageDown => "PAGE_DOWN", Key.CapsLock => "CAPS_LOCK", Key.NumLock => "NUM_LOCK", Key.Scroll => "SCROLL_LOCK", Key.PrintScreen => "PRINT_SCREEN",
        Key.Oem1 => "SEMICOLON", Key.Oem2 => "SLASH", Key.Oem3 => "GRAVE_ACCENT", Key.Oem4 => "LEFT_BRACKET", Key.Oem5 => "BACKSLASH", Key.Oem6 => "RIGHT_BRACKET", Key.Oem7 => "APOSTROPHE", Key.OemComma => "COMMA", Key.OemPeriod => "PERIOD", Key.OemMinus => "MINUS", Key.OemPlus => "EQUAL",
        Key.Return => "ENTER", Key.Back => "BACKSPACE", Key.Space => "SPACE", Key.Escape => "ESCAPE", _ => key.ToString().ToUpperInvariant()
    };

    private FrameworkElement BuildDiscoveredHotkeyRow(ModConfigDraft draft, string key)
    {
        var border = new Border { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(175, 47, 47, 47)), BorderBrush = new SolidColorBrush(Color.FromRgb(116, 116, 116)), BorderThickness = new Thickness(1) };
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        var translated = TranslateConfigOption(draft, "Hotkeys", key); grid.Children.Add(new TextBlock { Text = translated.Label, ToolTip = translated.Tooltip, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) });
        var editor = BuildHotkeyEditor(draft.HotkeyOverrides, key); Grid.SetColumn(editor, 1); grid.Children.Add(editor); border.Child = grid; return border;
    }

    private FrameworkElement BuildDiscoveredBooleanRow(ModConfigDraft draft, DiscoveredBooleanOption option)
    {
        var border = new Border { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(175, 47, 47, 47)), BorderBrush = new SolidColorBrush(Color.FromRgb(116, 116, 116)), BorderThickness = new Thickness(1) };
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        var translated = TranslateConfigOption(draft, option.Category, option.Key);
        grid.Children.Add(new TextBlock { Text = translated.Label, ToolTip = translated.Tooltip, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) });
        var combo = new ComboBox { ItemsSource = new[] { "使用 Mod 默认", "强制启用", "强制禁用" }, SelectedIndex = draft.BooleanOverrides.TryGetValue(option.Key, out var enabled) ? enabled ? 1 : 2 : 0 };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex == 0) draft.BooleanOverrides.Remove(option.Key);
            else draft.BooleanOverrides[option.Key] = combo.SelectedIndex == 1;
            MarkModConfigDraftChanged();
        };
        Grid.SetColumn(combo, 1); grid.Children.Add(combo); border.Child = grid; return border;
    }

    private List<string> DiscoverHotkeyNames(ModConfigDefinition definition)
    {
        var info = FindModTranslationInfo(definition); if (info == null || string.IsNullOrWhiteSpace(info.JarPath) || !File.Exists(info.JarPath)) return [];
        var cacheKey = $"{info.JarPath}|{File.GetLastWriteTimeUtc(info.JarPath).Ticks}";
        if (hotkeyDiscoveryCache.TryGetValue(cacheKey, out var cached)) return cached;
        var prefixes = definition.Id == "inventoryprofilesnext" ? new[] { "inventoryprofiles.config.name." } : new[] { $"{definition.Id}.config.name.", $"{definition.Id}.config.hotkey.name." };
        var candidates = info.EnglishTranslations.Keys.Concat(info.ChineseTranslations.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key => prefixes.FirstOrDefault(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) is { } prefix ? key[prefix.Length..] : null)
            .Where(key => !string.IsNullOrWhiteSpace(key)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        try
        {
            using var zip = ZipFile.OpenRead(info.JarPath); var hotkeyClassText = new StringBuilder();
            foreach (var entry in zip.Entries.Where(entry => entry.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase) && (entry.FullName.Contains("hotkey", StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains("keybind", StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains("shortcut", StringComparison.OrdinalIgnoreCase))))
            {
                using var stream = entry.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory); hotkeyClassText.Append(Encoding.Latin1.GetString(memory.ToArray()));
            }
            var classText = hotkeyClassText.ToString();
            var result = candidates.Where(key => classText.Contains(key, StringComparison.OrdinalIgnoreCase) || classText.Contains(key.ToUpperInvariant(), StringComparison.Ordinal)).Order(StringComparer.OrdinalIgnoreCase).ToList();
            hotkeyDiscoveryCache[cacheKey] = result; return result;
        }
        catch { return []; }
    }

    private List<DiscoveredBooleanOption> DiscoverBooleanOptions(ModConfigDefinition definition, JsonObject? json)
    {
        var info = FindModTranslationInfo(definition); if (info == null || string.IsNullOrWhiteSpace(info.JarPath) || !File.Exists(info.JarPath)) return [];
        var categorySignature = json == null ? "" : string.Join(',', CollectExistingConfigNames(json).Order(StringComparer.OrdinalIgnoreCase));
        var cacheKey = $"{info.JarPath}|{File.GetLastWriteTimeUtc(info.JarPath).Ticks}|{categorySignature}";
        if (booleanDiscoveryCache.TryGetValue(cacheKey, out var cached)) return cached;
        var existing = json == null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : CollectExistingConfigNames(json);
        var hotkeys = DiscoverHotkeyNames(definition).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var translationKey in info.EnglishTranslations.Keys.Concat(info.ChineseTranslations.Keys))
        {
            var markers = new[] { ".config.name.", ".config.prettyName.", ".config.feature_toggle.prettyName.", ".config.disable_toggle.prettyName." };
            foreach (var marker in markers)
            {
                var index = translationKey.IndexOf(marker, StringComparison.OrdinalIgnoreCase); if (index < 0) continue;
                var candidate = translationKey[(index + marker.Length)..];
                var englishLabel = info.EnglishTranslations.TryGetValue(translationKey, out var translated) ? translated : "";
                if (!string.IsNullOrWhiteSpace(candidate) && !candidate.Contains('.') && !existing.Contains(candidate) && !hotkeys.Contains(candidate) && IsLikelyBooleanConfig(candidate, englishLabel)) candidates.Add(candidate);
            }
        }
        if (candidates.Count == 0) return [];
        try
        {
            using var zip = ZipFile.OpenRead(info.JarPath);
            var result = new Dictionary<string, DiscoveredBooleanOption>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries.Where(entry => entry.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase)
                && (entry.FullName.Contains("config", StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains("feature", StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains("setting", StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains("toggle", StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains("option", StringComparison.OrdinalIgnoreCase))
                && !entry.FullName.Contains("hotkey", StringComparison.OrdinalIgnoreCase) && !entry.FullName.Contains("keybind", StringComparison.OrdinalIgnoreCase) && !entry.FullName.Contains("shortcut", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = entry.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory); var classText = Encoding.Latin1.GetString(memory.ToArray());
                var className = Path.GetFileNameWithoutExtension(entry.FullName).Split('$')[0];
                var category = GuessDiscoveredCategory(json, entry.FullName, className);
                foreach (var candidate in candidates)
                    if (!result.ContainsKey(candidate) && (classText.Contains(candidate, StringComparison.OrdinalIgnoreCase) || classText.Contains(ToSnakeCase(candidate), StringComparison.OrdinalIgnoreCase)))
                        result[candidate] = new DiscoveredBooleanOption(category, candidate);
            }
            var discovered = result.Values.OrderBy(option => option.Category, StringComparer.OrdinalIgnoreCase).ThenBy(option => option.Key, StringComparer.OrdinalIgnoreCase).ToList();
            booleanDiscoveryCache[cacheKey] = discovered; return discovered;
        }
        catch { return []; }
    }

    private static bool IsLikelyBooleanConfig(string key, string englishLabel)
    {
        var normalized = ToSnakeCase(key);
        if (normalized.StartsWith("enable_") || normalized.StartsWith("disable_") || normalized.StartsWith("show_") || normalized.StartsWith("allow_") || normalized.StartsWith("always_") || normalized.StartsWith("use_") || normalized.StartsWith("prevent_") || normalized.StartsWith("remember_") || normalized.StartsWith("auto_") || normalized.StartsWith("highlight_") || normalized.StartsWith("continuous_")) return true;
        var label = CleanMinecraftFormatting(englishLabel);
        return label.StartsWith("Enable ", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Disable ", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Show ", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Allow ", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Always ", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Use ", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Prevent ", StringComparison.OrdinalIgnoreCase);
    }

    private static string GuessDiscoveredCategory(JsonObject? json, string classPath, string className)
    {
        if (json != null)
            foreach (var category in json.Select(item => item.Key))
                if (classPath.Contains(category, StringComparison.OrdinalIgnoreCase) || className.Contains(category, StringComparison.OrdinalIgnoreCase)) return category;
        if (className.Contains("feature", StringComparison.OrdinalIgnoreCase) || className.Contains("toggle", StringComparison.OrdinalIgnoreCase)) return "Features";
        if (className.Contains("gui", StringComparison.OrdinalIgnoreCase)) return "GuiSettings";
        return className.EndsWith("Config", StringComparison.OrdinalIgnoreCase) ? "ModSettings" : className;
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
        var cacheKey = $"{draft.Definition.Id}|{settings.ModConfigLanguage}|{category}";
        if (modCategoryTranslationCache.TryGetValue(cacheKey, out var cached)) return cached;
        var fallback = HumanizeConfigName(category);
        var info = FindModTranslationInfo(draft.Definition);
        var tokens = new[] { category, ToSnakeCase(category) };
        var chinese = info == null ? null : FindTranslation(info.ChineseTranslations, tokens, true, true);
        var english = info == null ? null : FindTranslation(info.EnglishTranslations, tokens, true, true);
        return modCategoryTranslationCache[cacheKey] = FormatLocalizedLabel(chinese ?? TranslateGenericConfigName(category), english ?? fallback, fallback);
    }

    private (string Label, string Tooltip) TranslateConfigOption(ModConfigDraft draft, string category, string key)
    {
        var cacheKey = $"{draft.Definition.Id}|{settings.ModConfigLanguage}|{category}|{key}";
        if (modOptionTranslationCache.TryGetValue(cacheKey, out var cached)) return cached;
        var fallback = HumanizeConfigName(key); var info = FindModTranslationInfo(draft.Definition);
        var candidates = BuildTranslationCandidates(draft.Definition.Id, category, key);
        var chinese = info == null ? null : FindFirst(info.ChineseTranslations, candidates) ?? FindTranslation(info.ChineseTranslations, [key, ToSnakeCase(key)], false, false);
        var english = info == null ? null : FindFirst(info.EnglishTranslations, candidates) ?? FindTranslation(info.EnglishTranslations, [key, ToSnakeCase(key)], false, false);
        var commentCandidates = candidates.Select(x => x.Replace(".name.", ".comment.").Replace(".prettyName.", ".comment.").Replace("config.name.", "config.description.")).ToArray();
        var chineseComment = info == null ? null : FindFirst(info.ChineseTranslations, commentCandidates) ?? FindTranslation(info.ChineseTranslations, [key, ToSnakeCase(key)], false, true);
        var englishComment = info == null ? null : FindFirst(info.EnglishTranslations, commentCandidates) ?? FindTranslation(info.EnglishTranslations, [key, ToSnakeCase(key)], false, true);
        var label = FormatLocalizedLabel(chinese ?? TranslateGenericConfigName(key), english ?? fallback, fallback);
        var description = FormatLocalizedDescription(chineseComment, englishComment);
        return modOptionTranslationCache[cacheKey] = (label, string.IsNullOrWhiteSpace(description) ? key : $"{description}\n\n配置键：{key}");
    }

    private ModInfo? FindModTranslationInfo(ModConfigDefinition definition)
    {
        if (modTranslationInfoCache.TryGetValue(definition.Id, out var cached)) return cached;
        foreach (var alias in definition.Aliases)
            if (mods.TryGetValue(alias, out var exact)) return modTranslationInfoCache[definition.Id] = exact;
        var result = mods.Values.FirstOrDefault(info => definition.Aliases.Any(alias => info.Id.Contains(alias, StringComparison.OrdinalIgnoreCase)) ||
            info.ChineseTranslations.Keys.Concat(info.EnglishTranslations.Keys).Any(k => k.StartsWith(definition.Id + ".", StringComparison.OrdinalIgnoreCase) || (definition.Id == "inventoryprofilesnext" && k.StartsWith("inventoryprofiles.", StringComparison.OrdinalIgnoreCase))));
        modTranslationInfoCache[definition.Id] = result; return result;
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

    private static string? TranslateGenericConfigName(string value)
    {
        if (GenericConfigNames.TryGetValue(value, out var exact)) return exact;
        var words = Regex.Matches(HumanizeConfigName(value), @"[A-Za-z0-9]+")
            .Select(match => match.Value).ToList();
        if (words.Count == 0 || !words.Any(word => GenericConfigTerms.ContainsKey(word))) return null;
        return string.Join("", words.Select(word => GenericConfigTerms.TryGetValue(word, out var translated) ? translated : word));
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
            if (draft.IsAutoDiscovered)
            {
                var configRoot = Path.Combine(instance, "config"); var filesRoot = Path.Combine(destination, "files");
                foreach (var file in draft.ConfigFiles.Where(File.Exists))
                {
                    var relative = Path.GetRelativePath(configRoot, file); var target = Path.Combine(filesRoot, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true);
                }
                if (draft.Json != null)
                {
                    var primarySource = Directory.Exists(draft.SourcePath) ? Path.Combine(draft.SourcePath, draft.Definition.PrimaryJson) : draft.SourcePath;
                    var primaryTarget = Path.Combine(filesRoot, Path.GetRelativePath(configRoot, primarySource)); Directory.CreateDirectory(Path.GetDirectoryName(primaryTarget)!);
                    WriteDraftJson(draft, primaryTarget);
                }
                continue;
            }
            if (Directory.Exists(draft.SourcePath)) CopyDirectory(draft.SourcePath, destination); else File.Copy(draft.SourcePath, Path.Combine(destination, Path.GetFileName(draft.SourcePath)), true);
            var primary = draft.Definition.PrimaryJson.Length > 0 ? Path.Combine(destination, draft.Definition.PrimaryJson) : Path.Combine(destination, Path.GetFileName(draft.SourcePath));
            if (draft.Json != null) WriteDraftJson(draft, primary);
        }
        SettingsStore.Save(settings); RefreshModConfigProfiles(); StatusText.Text = $"已保存 Mod 配置：{settings.ActiveModConfigProfile}";
    }

    private static void WriteDraftJson(ModConfigDraft draft, string target)
    {
        if (draft.Json == null) return;
        if (draft.HotkeyOverrides.Count > 0)
        {
            var hotkeys = draft.Json["Hotkeys"] as JsonObject ?? new JsonObject(); draft.Json["Hotkeys"] = hotkeys;
            foreach (var item in draft.HotkeyOverrides) hotkeys[item.Key] = new JsonObject { ["keys"] = item.Value?.DeepClone() };
        }
        foreach (var item in draft.BooleanOverrides)
        {
            var option = draft.DiscoveredOptions.FirstOrDefault(candidate => candidate.Key.Equals(item.Key, StringComparison.OrdinalIgnoreCase)); if (option == null) continue;
            var category = draft.Json[option.Category] as JsonObject ?? new JsonObject(); draft.Json[option.Category] = category;
            var usesValueWrapper = category.Any(pair => pair.Value is JsonObject wrapper && wrapper.Count == 1 && wrapper["value"] is JsonValue wrapped && wrapped.TryGetValue<bool>(out _));
            category[option.Key] = usesValueWrapper ? new JsonObject { ["value"] = item.Value } : JsonValue.Create(item.Value);
        }
        File.WriteAllText(target, draft.Json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private void LoadModConfigProfileDraft()
    {
        var root = Path.Combine(ModConfigProfilesRoot, SafeProfileName(settings.ActiveModConfigProfile));
        foreach (var draft in modConfigDrafts)
        {
            if (draft.IsAutoDiscovered)
            {
                var source = Directory.Exists(draft.SourcePath) ? Path.Combine(draft.SourcePath, draft.Definition.PrimaryJson) : draft.SourcePath;
                var profileFile = Path.Combine(root, draft.Definition.Id, "files", Path.GetRelativePath(Path.Combine(instance, "config"), source));
                if (File.Exists(profileFile)) try { draft.Json = JsonNode.Parse(File.ReadAllText(profileFile)) as JsonObject; } catch { }
                continue;
            }
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
            if (draft.IsAutoDiscovered)
            {
                var filesRoot = Path.Combine(source, "files"); if (!Directory.Exists(filesRoot)) continue;
                foreach (var profileFile in Directory.EnumerateFiles(filesRoot, "*", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(instance, "config", Path.GetRelativePath(filesRoot, profileFile)); BackupModConfig(target); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(profileFile, target, true);
                }
                applied++; continue;
            }
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
