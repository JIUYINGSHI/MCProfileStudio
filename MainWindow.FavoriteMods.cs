using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace McProfileStudio;

public partial class MainWindow
{
    private Grid? favoriteModsPage;
    private TextBox? modSearchBox;
    private ComboBox? marketplaceVersionCombo, marketplaceLoaderCombo, marketplaceSourceCombo;
    private PasswordBox? curseForgeKeyBox;
    private StackPanel? marketplaceResults, favoriteModsList, favoriteStatusList;
    private TextBlock? marketplaceStatus, favoriteHomeSummary;
    private CancellationTokenSource? marketplaceSearchCancellation;
    private static readonly string[] CommonMinecraftVersions = ["1.21.10", "1.21.8", "1.21.5", "1.21.4", "1.21.1", "1.20.6", "1.20.4", "1.20.1", "1.19.4", "1.19.2", "1.18.2", "1.16.5", "1.12.2"];
    private static readonly string[] SupportedLoaders = ["Fabric", "Forge", "NeoForge", "Quilt"];

    private void BuildFavoriteModsPage()
    {
        if (FindLogicalParent<Grid>(KeysPage) is not { } host || FindLogicalParent<StackPanel>(FindNavButton("4")) is not { } navigation) return;
        var nav = new RadioButton { Content = CreateMinecraftNavContent("5", "Mod 收藏"), Tag = "5", Style = (Style)FindResource("Nav") }; nav.Checked += Navigate; navigation.Children.Add(nav);
        favoriteModsPage = new Grid { Visibility = Visibility.Collapsed };
        favoriteModsPage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) }); favoriteModsPage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = MakeFavoriteCard(new Thickness(0, 0, 10, 14)); var leftGrid = new Grid(); leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); leftGrid.RowDefinitions.Add(new RowDefinition());
        var searchPanel = new StackPanel(); searchPanel.Children.Add(new TextBlock { Text = "搜索并收藏 Mod", Foreground = Brushes.White, FontSize = 19, FontWeight = FontWeights.SemiBold });
        searchPanel.Children.Add(new TextBlock { Text = "版本与加载器会随导入实例自动识别，也可以在这里手动修改。", Foreground = new SolidColorBrush(Color.FromRgb(166, 185, 203)), Margin = new Thickness(0, 5, 0, 10) });
        modSearchBox = new TextBox { ToolTip = "输入 Mod 名称，例如 Tweakeroo" }; searchPanel.Children.Add(modSearchBox);
        var filters = new Grid { Margin = new Thickness(0, 8, 0, 8) }; for (var i = 0; i < 3; i++) filters.ColumnDefinitions.Add(new ColumnDefinition());
        marketplaceVersionCombo = new ComboBox { IsEditable = true, ItemsSource = CommonMinecraftVersions, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Minecraft 版本" };
        marketplaceLoaderCombo = new ComboBox { ItemsSource = SupportedLoaders, Margin = new Thickness(3, 0, 3, 0), ToolTip = "Mod 加载器" };
        marketplaceSourceCombo = new ComboBox { ItemsSource = new[] { "全部", "Modrinth", "CurseForge" }, SelectedIndex = 0, Margin = new Thickness(6, 0, 0, 0), ToolTip = "下载来源" };
        Grid.SetColumn(marketplaceLoaderCombo, 1); Grid.SetColumn(marketplaceSourceCombo, 2); filters.Children.Add(marketplaceVersionCombo); filters.Children.Add(marketplaceLoaderCombo); filters.Children.Add(marketplaceSourceCombo); searchPanel.Children.Add(filters);
        var searchButtons = new WrapPanel(); var search = new Button { Content = "搜索 Mod", MinWidth = 120 }; search.Click += SearchMarketplace_Click; var detect = new Button { Content = "重新识别实例环境", Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)), Margin = new Thickness(8, 0, 0, 0) }; detect.Click += (_, _) => { DetectAndSelectInstanceEnvironment(true); RefreshFavoriteModStatus(); }; searchButtons.Children.Add(search); searchButtons.Children.Add(detect); searchPanel.Children.Add(searchButtons);
        marketplaceStatus = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(103, 190, 245)), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap }; searchPanel.Children.Add(marketplaceStatus); Grid.SetRow(searchPanel, 0); leftGrid.Children.Add(searchPanel);
        marketplaceResults = new StackPanel(); var resultScroll = new ScrollViewer { Content = marketplaceResults, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 0, 0) }; Grid.SetRow(resultScroll, 1); leftGrid.Children.Add(resultScroll); left.Child = leftGrid; favoriteModsPage.Children.Add(left);

        var right = MakeFavoriteCard(new Thickness(10, 0, 0, 14)); var rightDock = new DockPanel(); var favoriteHeader = new StackPanel(); favoriteHeader.Children.Add(new TextBlock { Text = "已收藏的 Mod", Foreground = Brushes.White, FontSize = 19, FontWeight = FontWeights.SemiBold }); favoriteHeader.Children.Add(new TextBlock { Text = "收藏针对项目本身，不绑定单一 Minecraft 版本。", Foreground = new SolidColorBrush(Color.FromRgb(166, 185, 203)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 10) });
        curseForgeKeyBox = new PasswordBox { Password = settings.CurseForgeApiKey, ToolTip = "CurseForge 官方 API Key（仅保存在本机设置）" }; favoriteHeader.Children.Add(curseForgeKeyBox); var saveKey = new Button { Content = "保存 CurseForge API Key", Margin = new Thickness(0, 7, 0, 10), Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)) }; saveKey.Click += (_, _) => { settings.CurseForgeApiKey = curseForgeKeyBox.Password.Trim(); SettingsStore.Save(settings); marketplaceStatus!.Text = "已保存 CurseForge API Key"; }; favoriteHeader.Children.Add(saveKey); DockPanel.SetDock(favoriteHeader, Dock.Top); rightDock.Children.Add(favoriteHeader);
        favoriteModsList = new StackPanel(); rightDock.Children.Add(new ScrollViewer { Content = favoriteModsList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); right.Child = rightDock; Grid.SetColumn(right, 1); favoriteModsPage.Children.Add(right);
        host.Children.Add(favoriteModsPage); BuildFavoriteHomeCard(); RefreshFavoriteList(); DetectAndSelectInstanceEnvironment(); RefreshFavoriteModStatus();
    }

    private static Border MakeFavoriteCard(Thickness margin) => new() { Margin = margin, Padding = new Thickness(18), CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(114, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)), BorderThickness = new Thickness(1) };

    private void BuildFavoriteHomeCard()
    {
        if (HomePage.Children.OfType<StackPanel>().FirstOrDefault() is not { } home) return;
        var card = MakeFavoriteCard(new Thickness(0, 0, 0, 14)); var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = "收藏 Mod 检测", Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold });
        favoriteHomeSummary = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(186, 199, 216)), Margin = new Thickness(0, 7, 0, 8), TextWrapping = TextWrapping.Wrap }; panel.Children.Add(favoriteHomeSummary);
        favoriteStatusList = new StackPanel(); panel.Children.Add(new ScrollViewer { Content = favoriteStatusList, MaxHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); var download = new Button { Content = "检查并选择要下载的 Mod", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) }; download.Click += OpenFavoriteDownloadDialog_Click; panel.Children.Add(download); card.Child = panel;
        home.Children.Insert(Math.Min(2, home.Children.Count), card);
    }

    private async void SearchMarketplace_Click(object sender, RoutedEventArgs e)
    {
        var version = marketplaceVersionCombo?.Text.Trim() ?? ""; var loader = marketplaceLoaderCombo?.SelectedItem as string ?? "Fabric"; var source = marketplaceSourceCombo?.SelectedItem as string ?? "全部";
        if (string.IsNullOrWhiteSpace(version)) { marketplaceStatus!.Text = "请先选择或输入 Minecraft 版本"; return; }
        settings.PreferredMinecraftVersion = version; settings.PreferredModLoader = loader; settings.CurseForgeApiKey = curseForgeKeyBox?.Password.Trim() ?? settings.CurseForgeApiKey; SettingsStore.Save(settings);
        marketplaceSearchCancellation?.Cancel(); marketplaceSearchCancellation = new CancellationTokenSource(); marketplaceStatus!.Text = "正在搜索…"; marketplaceResults!.Children.Clear();
        try
        {
            var results = await ModMarketplaceService.SearchAsync(modSearchBox?.Text.Trim() ?? "", version, loader, source, settings.CurseForgeApiKey, marketplaceSearchCancellation.Token);
            foreach (var result in results) marketplaceResults.Children.Add(BuildMarketplaceResult(result));
            marketplaceStatus.Text = $"找到 {results.Count} 个兼容 {version} / {loader} 的 Mod" + (source == "全部" && string.IsNullOrWhiteSpace(settings.CurseForgeApiKey) ? "；未设置 CurseForge Key，本次仅搜索 Modrinth" : "");
        }
        catch (Exception ex) { marketplaceStatus.Text = "搜索失败：" + ex.Message; }
    }

    private Border BuildMarketplaceResult(ModSearchResult result)
    {
        var card = new Border { Background = new SolidColorBrush(Color.FromArgb(70, 29, 42, 55)), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8) };
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel(); text.Children.Add(new TextBlock { Text = GetLocalizedMarketplaceName(result.Slug, result.Name), Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold }); text.Children.Add(new TextBlock { Text = result.Description, Foreground = new SolidColorBrush(Color.FromRgb(174, 190, 207)), TextWrapping = TextWrapping.Wrap, MaxHeight = 42 }); text.Children.Add(new TextBlock { Text = $"{result.Source}  ·  {result.Downloads:N0} 次下载", Foreground = new SolidColorBrush(Color.FromRgb(103, 190, 245)), Margin = new Thickness(0, 5, 0, 0) }); grid.Children.Add(text);
        var exists = settings.FavoriteMods.Any(item => item.Source == result.Source && item.ProjectId == result.ProjectId); var button = new Button { Content = exists ? "已收藏" : "收藏", IsEnabled = !exists, Margin = new Thickness(10, 0, 0, 0), MinWidth = 78, Tag = result }; button.Click += FavoriteSearchResult_Click; Grid.SetColumn(button, 1); grid.Children.Add(button); card.Child = grid; return card;
    }

    private void FavoriteSearchResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModSearchResult result }) return;
        settings.FavoriteMods.Add(new FavoriteMod { Source = result.Source, ProjectId = result.ProjectId, Slug = result.Slug, Name = result.Name, Description = result.Description, IconUrl = result.IconUrl }); SettingsStore.Save(settings); RefreshFavoriteList(); RefreshFavoriteModStatus(); ((Button)sender).Content = "已收藏"; ((Button)sender).IsEnabled = false;
    }

    private void RefreshFavoriteList()
    {
        if (favoriteModsList == null) return; favoriteModsList.Children.Clear();
        foreach (var favorite in settings.FavoriteMods.OrderBy(item => item.Name))
        {
            var row = new Border { Background = new SolidColorBrush(Color.FromArgb(60, 29, 42, 55)), CornerRadius = new CornerRadius(9), Padding = new Thickness(11), Margin = new Thickness(0, 0, 0, 7) }; var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); var text = new StackPanel(); text.Children.Add(new TextBlock { Text = GetLocalizedMarketplaceName(favorite.Slug, favorite.Name), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold }); text.Children.Add(new TextBlock { Text = $"{favorite.Source}  ·  {favorite.Slug}", Foreground = new SolidColorBrush(Color.FromRgb(103, 190, 245)) }); grid.Children.Add(text); var remove = new Button { Content = "移除", Tag = favorite, Background = new SolidColorBrush(Color.FromRgb(112, 48, 56)), Margin = new Thickness(8, 0, 0, 0) }; remove.Click += (_, _) => { settings.FavoriteMods.Remove(favorite); SettingsStore.Save(settings); RefreshFavoriteList(); RefreshFavoriteModStatus(); }; Grid.SetColumn(remove, 1); grid.Children.Add(remove); row.Child = grid; favoriteModsList.Children.Add(row);
        }
        if (settings.FavoriteMods.Count == 0) favoriteModsList.Children.Add(new TextBlock { Text = "还没有收藏 Mod，请从左侧搜索。", Foreground = new SolidColorBrush(Color.FromRgb(166, 185, 203)) });
    }

    private bool IsFavoriteInstalled(FavoriteMod favorite)
    {
        var tokens = new[] { favorite.Slug, favorite.Name, favorite.ProjectId }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(NormalizeFavoriteToken).Where(value => value.Length >= 3).ToList();
        return mods.Values.Any(mod => tokens.Any(token => NormalizeFavoriteToken(mod.Id).Contains(token) || NormalizeFavoriteToken(mod.EnglishName).Contains(token) || token.Contains(NormalizeFavoriteToken(mod.Id))));
    }
    private static string NormalizeFavoriteToken(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string GetLocalizedMarketplaceName(string slug, string englishName)
    {
        var chinese = ModNameLocalization.Find(slug, englishName, "");
        return string.IsNullOrWhiteSpace(chinese) || chinese.Equals(englishName, StringComparison.OrdinalIgnoreCase) ? englishName : $"{chinese} / {englishName}";
    }

    private void RefreshFavoriteModStatus()
    {
        if (favoriteStatusList == null) return; favoriteStatusList.Children.Clear(); var missing = settings.FavoriteMods.Where(item => !IsFavoriteInstalled(item)).ToList(); var installed = settings.FavoriteMods.Count - missing.Count;
        favoriteHomeSummary!.Text = string.IsNullOrWhiteSpace(instance) ? $"已收藏 {settings.FavoriteMods.Count} 个 Mod；导入实例后检查缺失情况。" : $"当前实例：已安装 {installed} 个，缺少 {missing.Count} 个收藏 Mod。";
        foreach (var favorite in settings.FavoriteMods) favoriteStatusList.Children.Add(new TextBlock { Text = $"{(IsFavoriteInstalled(favorite) ? "✓" : "○")}  {GetLocalizedMarketplaceName(favorite.Slug, favorite.Name)}  ·  {(IsFavoriteInstalled(favorite) ? "已安装" : "缺失")}", Foreground = IsFavoriteInstalled(favorite) ? new SolidColorBrush(Color.FromRgb(113, 211, 151)) : new SolidColorBrush(Color.FromRgb(255, 190, 105)), Margin = new Thickness(0, 2, 0, 2) });
    }

    private void DetectAndSelectInstanceEnvironment(bool forceDetected = false)
    {
        var detected = DetectInstanceEnvironment(); var version = !forceDetected && !string.IsNullOrWhiteSpace(settings.PreferredMinecraftVersion) ? settings.PreferredMinecraftVersion : detected.Version; var loader = !forceDetected && !string.IsNullOrWhiteSpace(settings.PreferredModLoader) ? settings.PreferredModLoader : detected.Loader;
        if (marketplaceVersionCombo != null) marketplaceVersionCombo.Text = string.IsNullOrWhiteSpace(version) ? CommonMinecraftVersions[0] : version;
        if (marketplaceLoaderCombo != null) marketplaceLoaderCombo.SelectedItem = SupportedLoaders.Contains(loader, StringComparer.OrdinalIgnoreCase) ? SupportedLoaders.First(item => item.Equals(loader, StringComparison.OrdinalIgnoreCase)) : "Fabric";
        if (marketplaceStatus != null && !string.IsNullOrWhiteSpace(instance)) marketplaceStatus.Text = $"实例识别：Minecraft {detected.Version} / {detected.Loader}；可手动修改";
    }

    private (string Version, string Loader) DetectInstanceEnvironment()
    {
        var version = ""; var loader = "";
        if (!string.IsNullOrWhiteSpace(instance) && Directory.Exists(instance))
        {
            foreach (var file in Directory.EnumerateFiles(instance, "*.json", SearchOption.TopDirectoryOnly).Take(20))
            {
                try
                {
                    var text = File.ReadAllText(file); using var document = JsonDocument.Parse(text); var root = document.RootElement;
                    foreach (var property in new[] { "inheritsFrom", "id" }) if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String) { var match = Regex.Match(value.GetString() ?? "", @"(?<!\d)(1\.\d+(?:\.\d+)?)(?!\d)"); if (match.Success) version = match.Groups[1].Value; }
                    var lower = text.ToLowerInvariant(); if (lower.Contains("neoforge")) loader = "NeoForge"; else if (lower.Contains("fabric-loader") || lower.Contains("fabricloader")) loader = "Fabric"; else if (lower.Contains("quilt_loader") || lower.Contains("quilt-loader")) loader = "Quilt"; else if (lower.Contains("net.minecraftforge") || lower.Contains("forge:")) loader = "Forge";
                }
                catch { }
            }
            if (string.IsNullOrWhiteSpace(version)) { var match = Regex.Match(Path.GetFileName(instance), @"(?<!\d)(1\.\d+(?:\.\d+)?)(?!\d)"); if (match.Success) version = match.Groups[1].Value; }
            if (string.IsNullOrWhiteSpace(loader)) { if (mods.ContainsKey("fabric-api")) loader = "Fabric"; else if (mods.Keys.Any(id => id.Contains("neoforge", StringComparison.OrdinalIgnoreCase))) loader = "NeoForge"; }
        }
        return (string.IsNullOrWhiteSpace(version) ? CommonMinecraftVersions[0] : version, string.IsNullOrWhiteSpace(loader) ? "Fabric" : loader);
    }

    private async void OpenFavoriteDownloadDialog_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(instance) || !Directory.Exists(instance)) { MessageBox.Show(this, "请先导入要安装 Mod 的 Minecraft 实例。", "尚未导入实例", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var missing = settings.FavoriteMods.Where(item => !IsFavoriteInstalled(item)).ToList(); if (missing.Count == 0) { MessageBox.Show(this, "当前实例没有缺失的收藏 Mod。", "检测完成", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        await ShowFavoriteDownloadDialogAsync(missing);
    }

    private async Task ShowFavoriteDownloadDialogAsync(List<FavoriteMod> missing)
    {
        var dialog = new Window { Owner = this, Title = "选择要安装的收藏 Mod", Width = 920, Height = 680, MinWidth = 760, MinHeight = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(14, 22, 31)), Foreground = Brushes.White, FontFamily = (FontFamily)Application.Current.Resources["AppFont"] };
        var root = new Grid { Margin = new Thickness(20) }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition()); var versionBox = new ComboBox { IsEditable = true, ItemsSource = CommonMinecraftVersions, Text = marketplaceVersionCombo?.Text ?? "", Margin = new Thickness(0, 0, 6, 0) }; var loaderBox = new ComboBox { ItemsSource = SupportedLoaders, SelectedItem = marketplaceLoaderCombo?.SelectedItem ?? "Fabric", Margin = new Thickness(6, 0, 0, 0) }; Grid.SetColumn(loaderBox, 1); top.Children.Add(versionBox); top.Children.Add(loaderBox); root.Children.Add(top);
        var rows = new StackPanel(); var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 14, 0, 14) }; Grid.SetRow(scroll, 1); root.Children.Add(scroll); var selections = new List<(FavoriteMod Mod, CheckBox Check, ComboBox Versions)>();
        async Task LoadRowsAsync()
        {
            rows.Children.Clear(); selections.Clear(); var version = versionBox.Text.Trim(); var loader = loaderBox.SelectedItem as string ?? "Fabric";
            foreach (var mod in missing)
            {
                var card = new Border { Background = new SolidColorBrush(Color.FromArgb(75, 29, 42, 55)), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8) }; var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) }); var check = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }; grid.Children.Add(check); var label = new StackPanel(); label.Children.Add(new TextBlock { Text = GetLocalizedMarketplaceName(mod.Slug, mod.Name), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold }); label.Children.Add(new TextBlock { Text = mod.Source, Foreground = new SolidColorBrush(Color.FromRgb(103, 190, 245)) }); Grid.SetColumn(label, 1); grid.Children.Add(label); var versions = new ComboBox { Margin = new Thickness(12, 0, 0, 0), ToolTip = "手动选择此 Mod 在当前 Minecraft 版本与加载器下的具体版本" }; Grid.SetColumn(versions, 2); grid.Children.Add(versions); card.Child = grid; rows.Children.Add(card); selections.Add((mod, check, versions));
                try { var available = await ModMarketplaceService.GetVersionsAsync(mod, version, loader, settings.CurseForgeApiKey, CancellationToken.None); versions.ItemsSource = available; versions.SelectedIndex = available.Count > 0 ? 0 : -1; if (available.Count == 0) { check.IsChecked = false; check.IsEnabled = false; versions.ToolTip = "没有找到兼容版本"; } }
                catch (Exception ex) { check.IsChecked = false; check.IsEnabled = false; versions.ToolTip = ex.Message; }
            }
        }
        var bottom = new DockPanel(); var status = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(166, 185, 203)), VerticalAlignment = VerticalAlignment.Center }; bottom.Children.Add(status); var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; var refresh = new Button { Content = "按新环境刷新版本", Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)), Margin = new Thickness(0, 0, 8, 0) }; refresh.Click += async (_, _) => await LoadRowsAsync(); var cancel = new Button { Content = "取消", Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)), Margin = new Thickness(0, 0, 8, 0) }; cancel.Click += (_, _) => dialog.Close(); var install = new Button { Content = "下载并安装勾选项", MinWidth = 150 }; install.Click += async (_, _) =>
        {
            var selected = selections.Where(item => item.Check.IsChecked == true && item.Versions.SelectedItem is ModDownloadVersion).ToList(); if (selected.Count == 0) { status.Text = "请至少勾选一个有可用版本的 Mod"; return; }
            install.IsEnabled = false; var installed = 0;
            try { foreach (var item in selected) { var chosen = (ModDownloadVersion)item.Versions.SelectedItem; var safeFileName = Path.GetFileName(chosen.FileName); if (!safeFileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"{item.Mod.Name} 返回的文件不是 Mod JAR：{safeFileName}"); status.Text = $"正在下载 {item.Mod.Name}：{chosen.VersionNumber}"; await ModMarketplaceService.DownloadAsync(chosen, Path.Combine(instance, "mods", safeFileName), CancellationToken.None); installed++; } status.Text = $"已安装 {installed} 个 Mod"; LoadInstance(); RefreshFavoriteModStatus(); MessageBox.Show(dialog, $"已安装 {installed} 个 Mod。请重启 Minecraft。", "安装完成", MessageBoxButton.OK, MessageBoxImage.Information); dialog.Close(); } catch (Exception ex) { status.Text = "安装失败：" + ex.Message; install.IsEnabled = true; }
        }; buttons.Children.Add(refresh); buttons.Children.Add(cancel); buttons.Children.Add(install); DockPanel.SetDock(buttons, Dock.Right); bottom.Children.Add(buttons); Grid.SetRow(bottom, 2); root.Children.Add(bottom); dialog.Content = root;
        dialog.Loaded += async (_, _) => await LoadRowsAsync(); dialog.ShowDialog();
    }
}
