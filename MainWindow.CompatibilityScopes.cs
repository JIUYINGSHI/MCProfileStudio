using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace McProfileStudio;

public partial class MainWindow
{
    private ComboBox? packRangeCombo;
    private bool switchingPackRange;
    private ComboBox? modConfigScopeCombo;
    private bool switchingModConfigScope;

    private void EnsurePackVersionRanges(PackProfile profile)
    {
        profile.VersionRanges ??= [];
        if (profile.VersionRanges.Count == 0)
            profile.VersionRanges.Add(new PackVersionRange { Id = "default", Name = "全部版本（默认）", PackOrder = profile.PackOrder.ToList(), EnabledPacks = profile.EnabledPacks.ToList() });
        if (string.IsNullOrWhiteSpace(profile.ActiveRangeId) || profile.VersionRanges.All(item => item.Id != profile.ActiveRangeId)) profile.ActiveRangeId = profile.VersionRanges[0].Id;
        foreach (var range in profile.VersionRanges) CompatibilityScopes.UpgradeLegacyInclusiveRange(range);
    }

    private PackVersionRange EditingPackRange(PackProfile profile)
    {
        EnsurePackVersionRanges(profile); return profile.VersionRanges.First(item => item.Id == profile.ActiveRangeId);
    }

    private PackVersionRange ResolvePackRange(PackProfile profile)
    {
        EnsurePackVersionRanges(profile); var environment = DetectInstanceEnvironment();
        return CompatibilityScopes.Resolve(profile.VersionRanges, environment.Version);
    }

    private void RefreshPackRangeSelector(bool selectForInstance = false)
    {
        if (packRangeCombo == null || !settings.PackProfiles.TryGetValue(settings.ActivePackProfile, out var profile)) return;
        EnsurePackVersionRanges(profile);
        if (selectForInstance && !string.IsNullOrWhiteSpace(instance)) profile.ActiveRangeId = ResolvePackRange(profile).Id;
        var items = profile.VersionRanges.ToList();
        switchingPackRange = true; packRangeCombo.ItemsSource = null; packRangeCombo.ItemsSource = items; packRangeCombo.SelectedItem = items.First(item => item.Id == profile.ActiveRangeId); switchingPackRange = false;
        packRangeCombo.ToolTip = CompatibilityScopes.Describe(EditingPackRange(profile));
    }

    private void PackRange_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (switchingPackRange || packRangeCombo?.SelectedItem is not PackVersionRange range || !settings.PackProfiles.TryGetValue(settings.ActivePackProfile, out var profile) || range.Id == profile.ActiveRangeId) return;
        if (IsPackProfileDirty() && AppDialog.Show(this, "当前版本范围的资源包排序尚未保存。切换后草稿会丢失，仍要切换吗？", "未保存的范围草稿", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { RefreshPackRangeSelector(); return; }
        profile.ActiveRangeId = range.Id; SettingsStore.Save(settings); ApplyPackRangeToLoadedLibrary(); RefreshPackRangeSelector(); StatusText.Text = $"已切换资源包范围：{range.Name}";
    }

    private void AddPackRange_Click(object sender, RoutedEventArgs e)
    {
        if (!settings.PackProfiles.TryGetValue(settings.ActivePackProfile, out var profile)) return;
        var scope = ShowScopeEditor("新增资源包版本范围", false); if (scope == null) return;
        if (profile.VersionRanges.Where(item => !IsFallback(item)).Any(item => CompatibilityScopes.Overlaps(item, scope))) { AppDialog.Show(this, "该范围与已有的非默认范围重叠。请调整起止版本，避免同一版本匹配两套列表。", "范围重叠", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var range = new PackVersionRange { Id = scope.Id, Name = scope.Name, MinVersion = scope.MinVersion, MaxVersion = scope.MaxVersion, PackOrder = packs.Select(item => item.Name).ToList(), EnabledPacks = packs.Where(item => item.Enabled).Select(item => item.Name).ToList() };
        profile.VersionRanges.Add(range); profile.ActiveRangeId = range.Id; SettingsStore.Save(settings); RefreshPackRangeSelector(); ApplyPackRangeToLoadedLibrary(); StatusText.Text = $"已新增范围：{range.Name}";
    }

    private void DeletePackRange_Click(object sender, RoutedEventArgs e)
    {
        if (!settings.PackProfiles.TryGetValue(settings.ActivePackProfile, out var profile) || profile.VersionRanges.Count <= 1 || packRangeCombo?.SelectedItem is not PackVersionRange range) { AppDialog.Show(this, "至少需要保留一个版本范围。", "无法删除"); return; }
        if (AppDialog.Show(this, $"删除“{range.Name}”及其中独立保存的资源包列表？", "删除版本范围", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        profile.VersionRanges.Remove(range); profile.ActiveRangeId = profile.VersionRanges[0].Id; SettingsStore.Save(settings); RefreshPackRangeSelector(); ApplyPackRangeToLoadedLibrary();
    }

    private List<CompatibilityScope> EnsureModConfigScopes(string profileName)
    {
        settings.ModConfigScopes ??= new(StringComparer.OrdinalIgnoreCase); settings.ActiveModConfigScopeIds ??= new(StringComparer.OrdinalIgnoreCase);
        if (!settings.ModConfigScopes.TryGetValue(profileName, out var scopes) || scopes.Count == 0) settings.ModConfigScopes[profileName] = scopes = [new CompatibilityScope { Id = "default", Name = "全部版本 / 加载器（默认）" }];
        if (!settings.ActiveModConfigScopeIds.TryGetValue(profileName, out var active) || scopes.All(item => item.Id != active)) settings.ActiveModConfigScopeIds[profileName] = scopes[0].Id;
        foreach (var scope in scopes) CompatibilityScopes.UpgradeLegacyInclusiveRange(scope);
        return scopes;
    }

    private CompatibilityScope EditingModConfigScope()
    {
        var scopes = EnsureModConfigScopes(settings.ActiveModConfigProfile); var id = settings.ActiveModConfigScopeIds[settings.ActiveModConfigProfile]; return scopes.First(item => item.Id == id);
    }

    private CompatibilityScope ResolveModConfigScope()
    {
        var environment = DetectInstanceEnvironment(); return CompatibilityScopes.Resolve(EnsureModConfigScopes(settings.ActiveModConfigProfile), environment.Version, environment.Loader);
    }

    private string ModConfigProfileBaseRoot => Path.Combine(ModConfigProfilesRoot, SafeProfileName(settings.ActiveModConfigProfile));
    private string ModConfigScopeRoot(CompatibilityScope scope) => scope.Id == "default" ? ModConfigProfileBaseRoot : Path.Combine(ModConfigProfileBaseRoot, "_ranges", scope.Id);
    private string EditingModConfigProfileRoot => ModConfigScopeRoot(EditingModConfigScope());
    private string ResolvedModConfigProfileRoot => ModConfigScopeRoot(ResolveModConfigScope());

    private void RefreshModConfigScopeSelector(bool selectForInstance = false)
    {
        if (modConfigScopeCombo == null) return; var scopes = EnsureModConfigScopes(settings.ActiveModConfigProfile);
        if (selectForInstance && !string.IsNullOrWhiteSpace(instance)) settings.ActiveModConfigScopeIds[settings.ActiveModConfigProfile] = ResolveModConfigScope().Id;
        var items = scopes.ToList();
        switchingModConfigScope = true; modConfigScopeCombo.ItemsSource = null; modConfigScopeCombo.ItemsSource = items; modConfigScopeCombo.SelectedItem = items.First(item => item.Id == settings.ActiveModConfigScopeIds[settings.ActiveModConfigProfile]); switchingModConfigScope = false; modConfigScopeCombo.ToolTip = CompatibilityScopes.Describe(EditingModConfigScope());
    }

    private void ModConfigScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (switchingModConfigScope || modConfigScopeCombo?.SelectedItem is not CompatibilityScope scope || scope.Id == EditingModConfigScope().Id) return;
        if (modConfigProfileDirty && AppDialog.Show(this, "当前环境范围的 Mod 配置尚未保存。切换后草稿会丢失，仍要切换吗？", "未保存的范围草稿", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { RefreshModConfigScopeSelector(); return; }
        settings.ActiveModConfigScopeIds[settings.ActiveModConfigProfile] = scope.Id; SettingsStore.Save(settings); modConfigProfileDirty = false; LoadModConfigProfileDraft(); RefreshModConfigScopeSelector();
    }

    private void AddModConfigScope_Click(object sender, RoutedEventArgs e)
    {
        var scope = ShowScopeEditor("新增 Mod 配置适用环境", true); if (scope == null) return; var scopes = EnsureModConfigScopes(settings.ActiveModConfigProfile);
        if (scopes.Where(item => !IsFallback(item)).Any(item => CompatibilityScopes.Overlaps(item, scope))) { AppDialog.Show(this, "该版本和加载器组合与已有范围重叠。请缩小版本范围或调整加载器。", "范围重叠", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        scopes.Add(scope); settings.ActiveModConfigScopeIds[settings.ActiveModConfigProfile] = scope.Id; SettingsStore.Save(settings); RefreshModConfigScopeSelector(); RefreshModConfigPage(); StatusText.Text = $"已新增 Mod 配置范围：{scope.Name}";
    }

    private void DeleteModConfigScope_Click(object sender, RoutedEventArgs e)
    {
        var scopes = EnsureModConfigScopes(settings.ActiveModConfigProfile); var scope = EditingModConfigScope(); if (scopes.Count <= 1) { AppDialog.Show(this, "至少需要保留一个 Mod 配置范围。", "无法删除"); return; }
        if (AppDialog.Show(this, $"删除“{scope.Name}”及其独立保存的全部 Mod 配置？", "删除适用范围", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var root = ModConfigScopeRoot(scope); if (scope.Id != "default" && Directory.Exists(root)) Directory.Delete(root, true); scopes.Remove(scope); settings.ActiveModConfigScopeIds[settings.ActiveModConfigProfile] = scopes[0].Id; SettingsStore.Save(settings); RefreshModConfigScopeSelector(); RefreshModConfigPage();
    }

    private CompatibilityScope? ShowScopeEditor(string title, bool includeLoaders)
    {
        var dialog = AppDialog.CreateWindow(this, title, 520, includeLoaders ? 570 : 500, false);
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var form = new StackPanel();
        form.Children.Add(new TextBlock { Text = title, FontSize = 21, FontWeight = FontWeights.SemiBold });
        form.Children.Add(new TextBlock { Text = "范围规则：最低版本包含，最高版本不包含。例如 1.20 ～ 1.21 表示全部 1.20.x。", Foreground = new SolidColorBrush(Color.FromRgb(90, 190, 255)), Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap });
        TextBox TextField(string label, string hint) { form.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(184, 199, 214)), Margin = new Thickness(0, 13, 0, 5) }); var box = new TextBox { ToolTip = hint }; form.Children.Add(box); return box; }
        ComboBox VersionField(string label) { form.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(184, 199, 214)), Margin = new Thickness(0, 13, 0, 5) }); var box = new ComboBox { ItemsSource = new[] { "不限" }.Concat(CompatibilityScopes.OfficialMajorVersions), SelectedIndex = 0 }; form.Children.Add(box); return box; }
        var name = TextField("范围名称", "例如：1.18～1.20 科技包");
        var min = VersionField("最低版本（包含）");
        var max = VersionField("最高版本（不包含）");
        var loaderChecks = new List<CheckBox>();
        if (includeLoaders) { form.Children.Add(new TextBlock { Text = "适用加载器（不勾选表示全部）", Foreground = new SolidColorBrush(Color.FromRgb(184, 199, 214)), Margin = new Thickness(0, 13, 0, 5) }); var row = new WrapPanel(); foreach (var loader in new[] { "Fabric", "Forge", "NeoForge", "Quilt" }) { var check = new CheckBox { Content = loader, Margin = new Thickness(0, 0, 14, 0) }; loaderChecks.Add(check); row.Children.Add(check); } form.Children.Add(row); }
        var hintText = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(255, 183, 83)), Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap }; form.Children.Add(hintText);
        var formScroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(formScroll, 0); root.Children.Add(formScroll);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; var cancel = new Button { Content = "取消", Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)), Margin = new Thickness(0, 0, 8, 0) }; cancel.Click += (_, _) => dialog.DialogResult = false; var save = new Button { Content = "添加范围" }; buttons.Children.Add(cancel); buttons.Children.Add(save); root.Children.Add(buttons);
        Grid.SetRow(buttons, 1);
        CompatibilityScope ReadScope() => new() { Name = name.Text.Trim(), MinVersion = min.SelectedIndex <= 0 ? "" : min.SelectedItem?.ToString() ?? "", MaxVersion = max.SelectedIndex <= 0 ? "" : max.SelectedItem?.ToString() ?? "", Loaders = loaderChecks.Where(item => item.IsChecked == true).Select(item => item.Content?.ToString() ?? "").Where(item => item.Length > 0).ToList() };
        save.Click += (_, _) => { var scope = ReadScope(); if (string.IsNullOrWhiteSpace(scope.Name)) { hintText.Text = "请填写范围名称。"; return; } if (!CompatibilityScopes.IsOrdered(scope)) { hintText.Text = "最高版本必须高于最低版本（最高版本不包含在范围内）。"; return; } dialog.DialogResult = true; };
        AppDialog.SetBody(dialog, root);
        if (dialog.ShowDialog() != true) return null; return ReadScope();
    }

    private static bool IsFallback(CompatibilityScope scope) => string.IsNullOrWhiteSpace(scope.MinVersion) && string.IsNullOrWhiteSpace(scope.MaxVersion) && scope.Loaders.Count == 0;
}
