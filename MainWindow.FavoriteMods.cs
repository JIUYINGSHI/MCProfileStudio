using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace McProfileStudio;

public partial class MainWindow
{
    private Grid? favoriteModsPage;
    private TextBox? modSearchBox;
    private ComboBox? marketplaceVersionCombo, marketplaceLoaderCombo, marketplaceSourceCombo;
    private StackPanel? marketplaceResults, favoriteModsList, favoriteStatusList;
    private TextBlock? marketplaceStatus, favoriteHomeSummary;
    private ComboBox? favoriteProfileCombo;
    private bool switchingFavoriteProfile;
    private bool favoriteProfileDirty;
    private readonly List<FavoriteMod> favoriteModDraft = [];
    private CancellationTokenSource? marketplaceSearchCancellation;
    private static readonly string[] CommonMinecraftVersions = ["1.21.10", "1.21.8", "1.21.5", "1.21.4", "1.21.1", "1.20.6", "1.20.4", "1.20.1", "1.19.4", "1.19.2", "1.18.2", "1.16.5", "1.12.2"];
    private static readonly string[] SupportedLoaders = ["Fabric", "Forge", "NeoForge", "Quilt"];

    private void EnsureFavoriteModProfiles()
    {
        settings.FavoriteMods ??= [];
        settings.FavoriteModProfiles ??= new Dictionary<string, FavoriteModProfile>(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        if (settings.FavoriteModProfiles.Count == 0) { settings.FavoriteModProfiles["默认收藏"] = new FavoriteModProfile { Mods = CloneFavoriteMods(settings.FavoriteMods) }; changed = true; }
        if (!settings.FavoriteModProfiles.ContainsKey(settings.ActiveFavoriteModProfile)) { settings.ActiveFavoriteModProfile = settings.FavoriteModProfiles.Keys.First(); changed = true; }
        if (changed) SettingsStore.Save(settings);
        LoadFavoriteProfileDraft();
    }

    private static List<FavoriteMod> CloneFavoriteMods(IEnumerable<FavoriteMod> source) => source.Select(item => new FavoriteMod { Source = item.Source, ProjectId = item.ProjectId, Slug = item.Slug, Name = item.Name, Description = item.Description, IconUrl = item.IconUrl }).ToList();

    private void LoadFavoriteProfileDraft()
    {
        favoriteModDraft.Clear();
        if (settings.FavoriteModProfiles.TryGetValue(settings.ActiveFavoriteModProfile, out var profile)) favoriteModDraft.AddRange(CloneFavoriteMods(profile.Mods ?? []));
        favoriteProfileDirty = false;
    }

    private void RefreshFavoriteProfileSelector()
    {
        if (favoriteProfileCombo == null) return; switchingFavoriteProfile = true; favoriteProfileCombo.ItemsSource = settings.FavoriteModProfiles.Keys.Order().ToList(); favoriteProfileCombo.SelectedItem = settings.ActiveFavoriteModProfile; switchingFavoriteProfile = false;
    }

    private void FavoriteProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (switchingFavoriteProfile || favoriteProfileCombo?.SelectedItem is not string name || name == settings.ActiveFavoriteModProfile) return;
        if (favoriteProfileDirty && AppDialog.Show(this, "当前 Mod 收藏方案有尚未保存的增删。切换后这些草稿会丢失，仍要切换吗？", "未保存的收藏草稿", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            switchingFavoriteProfile = true; favoriteProfileCombo.SelectedItem = settings.ActiveFavoriteModProfile; switchingFavoriteProfile = false; return;
        }
        settings.ActiveFavoriteModProfile = name; SettingsStore.Save(settings); LoadFavoriteProfileDraft(); marketplaceResults?.Children.Clear(); RefreshFavoriteList(); RefreshFavoriteModStatus(); StatusText.Text = $"已切换 Mod 收藏方案：{name}";
    }

    private void AddFavoriteProfile_Click(object sender, RoutedEventArgs e)
    {
        var index = 1; string suggestion; do suggestion = $"收藏方案 {index++}"; while (settings.FavoriteModProfiles.ContainsKey(suggestion)); var name = PromptForProfileName("新建 Mod 收藏方案", suggestion); if (string.IsNullOrWhiteSpace(name) || settings.FavoriteModProfiles.ContainsKey(name)) return; settings.FavoriteModProfiles[name] = new FavoriteModProfile(); settings.ActiveFavoriteModProfile = name; SettingsStore.Save(settings); LoadFavoriteProfileDraft(); RefreshFavoriteProfileSelector(); RefreshFavoriteList(); RefreshFavoriteModStatus(); StatusText.Text = "新 Mod 收藏方案尚未添加收藏";
    }

    private void RenameFavoriteProfile_Click(object sender, RoutedEventArgs e)
    {
        var old = settings.ActiveFavoriteModProfile; var name = PromptForProfileName("重命名 Mod 收藏方案", old); if (string.IsNullOrWhiteSpace(name) || name == old || settings.FavoriteModProfiles.ContainsKey(name)) return; var profile = settings.FavoriteModProfiles[old]; settings.FavoriteModProfiles.Remove(old); settings.FavoriteModProfiles[name] = profile; settings.ActiveFavoriteModProfile = name; SettingsStore.Save(settings); RefreshFavoriteProfileSelector(); StatusText.Text = $"已重命名 Mod 收藏方案：{name}";
    }

    private void DeleteFavoriteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (settings.FavoriteModProfiles.Count <= 1) { AppDialog.Show(this, "至少需要保留一套 Mod 收藏方案。", "无法删除", MessageBoxButton.OK, MessageBoxImage.Information); return; } var name = settings.ActiveFavoriteModProfile; if (AppDialog.Show(this, $"确定删除 Mod 收藏方案“{name}”吗？", "删除 Mod 收藏方案", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; settings.FavoriteModProfiles.Remove(name); settings.ActiveFavoriteModProfile = settings.FavoriteModProfiles.Keys.Order().First(); SettingsStore.Save(settings); LoadFavoriteProfileDraft(); RefreshFavoriteProfileSelector(); RefreshFavoriteList(); RefreshFavoriteModStatus(); StatusText.Text = $"已删除 Mod 收藏方案：{name}";
    }

    private void SaveFavoriteProfile_Click(object sender, RoutedEventArgs e)
    {
        var saved = CloneFavoriteMods(favoriteModDraft); settings.FavoriteModProfiles[settings.ActiveFavoriteModProfile] = new FavoriteModProfile { Mods = saved }; settings.FavoriteMods = CloneFavoriteMods(saved); SettingsStore.Save(settings); favoriteProfileDirty = false; StatusText.Text = $"已保存 Mod 收藏方案：{settings.ActiveFavoriteModProfile}";
    }

    private void BuildFavoriteModsPage()
    {
        if (FindLogicalParent<Grid>(KeysPage) is not { } host || FindLogicalParent<StackPanel>(FindNavButton("4")) is not { } navigation) return;
        var nav = new RadioButton { Content = CreateMinecraftNavContent("5", "Mod 收藏"), Tag = "5", Style = (Style)FindResource("Nav") }; nav.Checked += Navigate; navigation.Children.Add(nav);
        favoriteModsPage = new Grid { Visibility = Visibility.Collapsed };
        favoriteModsPage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) }); favoriteModsPage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = MakeFavoriteCard(new Thickness(0, 0, 10, 14)); var leftGrid = new Grid(); leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); leftGrid.RowDefinitions.Add(new RowDefinition());
        var searchPanel = new StackPanel(); searchPanel.Children.Add(new TextBlock { Text = "搜索并收藏 Mod", Foreground = Brushes.White, FontSize = 19, FontWeight = FontWeights.SemiBold });
        searchPanel.Children.Add(new TextBlock { Text = "版本与加载器会随导入实例自动识别，也可以在这里手动修改。", Foreground = new SolidColorBrush(Color.FromRgb(166, 185, 203)), Margin = new Thickness(0, 5, 0, 10) });
        modSearchBox = new TextBox { Text = settings.LastModSearchQuery, ToolTip = "输入中文名或英文名，例如 投影 / Litematica；按 Enter 直接搜索" }; modSearchBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { SearchMarketplace_Click(modSearchBox, new RoutedEventArgs()); e.Handled = true; } }; searchPanel.Children.Add(modSearchBox);
        var filters = new Grid { Margin = new Thickness(0, 8, 0, 8) }; for (var i = 0; i < 3; i++) filters.ColumnDefinitions.Add(new ColumnDefinition());
        marketplaceVersionCombo = new ComboBox { ItemsSource = CommonMinecraftVersions, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Minecraft 版本" };
        marketplaceLoaderCombo = new ComboBox { ItemsSource = SupportedLoaders, Margin = new Thickness(3, 0, 3, 0), ToolTip = "Mod 加载器" };
        marketplaceSourceCombo = new ComboBox { ItemsSource = new[] { "全部", "Modrinth", "CurseForge", "GitHub" }, SelectedItem = new[] { "全部", "Modrinth", "CurseForge", "GitHub" }.Contains(settings.PreferredModSource) ? settings.PreferredModSource : "全部", Margin = new Thickness(6, 0, 0, 0), ToolTip = "下载来源" };
        marketplaceVersionCombo.SelectionChanged += (_, _) => SaveMarketplacePreferences(); marketplaceLoaderCombo.SelectionChanged += (_, _) => SaveMarketplacePreferences(); marketplaceSourceCombo.SelectionChanged += (_, _) => SaveMarketplacePreferences(); modSearchBox.LostKeyboardFocus += (_, _) => SaveMarketplacePreferences();
        Grid.SetColumn(marketplaceLoaderCombo, 1); Grid.SetColumn(marketplaceSourceCombo, 2); filters.Children.Add(marketplaceVersionCombo); filters.Children.Add(marketplaceLoaderCombo); filters.Children.Add(marketplaceSourceCombo); searchPanel.Children.Add(filters);
        var searchButtons = new WrapPanel(); var search = new Button { Content = "搜索 Mod", MinWidth = 120 }; search.Click += SearchMarketplace_Click; var detect = new Button { Content = "重新识别实例环境", Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)), Margin = new Thickness(8, 0, 0, 0) }; detect.Click += (_, _) => { DetectAndSelectInstanceEnvironment(true); RefreshFavoriteModStatus(); }; searchButtons.Children.Add(search); searchButtons.Children.Add(detect); searchPanel.Children.Add(searchButtons);
        marketplaceStatus = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(103, 190, 245)), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap }; searchPanel.Children.Add(marketplaceStatus); Grid.SetRow(searchPanel, 0); leftGrid.Children.Add(searchPanel);
        marketplaceResults = new StackPanel(); var resultScroll = new ScrollViewer { Content = marketplaceResults, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 0, 0) }; Grid.SetRow(resultScroll, 1); leftGrid.Children.Add(resultScroll); left.Child = leftGrid; favoriteModsPage.Children.Add(left);

        var right = MakeFavoriteCard(new Thickness(10, 0, 0, 14)); var rightDock = new DockPanel(); var favoriteHeader = new StackPanel(); favoriteHeader.Children.Add(new TextBlock { Text = "Mod 收藏方案", Foreground = Brushes.White, FontSize = 19, FontWeight = FontWeights.SemiBold }); favoriteHeader.Children.Add(new TextBlock { Text = "每套方案保存一组收藏 Mod；切换方案会还原对应收藏，只有点击保存才会持久化增删。", Foreground = new SolidColorBrush(Color.FromRgb(166, 185, 203)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 10) });
        favoriteProfileCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 150 }; favoriteProfileCombo.SelectionChanged += FavoriteProfile_SelectionChanged; favoriteHeader.Children.Add(favoriteProfileCombo);
        var profileButtons = new StackPanel { Margin = new Thickness(0, 8, 0, 10) };
        var addProfile = new Button { Content = "新建配置", Margin = new Thickness(0, 0, 0, 6) }; addProfile.Click += AddFavoriteProfile_Click;
        var renameProfile = new Button { Content = "重命名", Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)), Margin = new Thickness(0, 0, 0, 6) }; renameProfile.Click += RenameFavoriteProfile_Click;
        var deleteProfile = new Button { Content = "删除当前配置", Background = new SolidColorBrush(Color.FromRgb(112, 48, 56)), Margin = new Thickness(0, 0, 0, 6) }; deleteProfile.Click += DeleteFavoriteProfile_Click;
        var saveProfile = new Button { Content = "保存全部收藏 Mod 草稿" }; saveProfile.Click += SaveFavoriteProfile_Click;
        profileButtons.Children.Add(addProfile); profileButtons.Children.Add(renameProfile); profileButtons.Children.Add(deleteProfile); profileButtons.Children.Add(saveProfile); favoriteHeader.Children.Add(profileButtons);
        favoriteHeader.Children.Add(new TextBlock { Text = "当前方案收藏", Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 8) });
        DockPanel.SetDock(favoriteHeader, Dock.Top); rightDock.Children.Add(favoriteHeader);
        favoriteModsList = new StackPanel(); rightDock.Children.Add(new ScrollViewer { Content = favoriteModsList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); right.Child = rightDock; Grid.SetColumn(right, 1); favoriteModsPage.Children.Add(right);
        host.Children.Add(favoriteModsPage); BuildFavoriteHomeCard(); RefreshFavoriteProfileSelector(); RefreshFavoriteList(); DetectAndSelectInstanceEnvironment(); RefreshFavoriteModStatus();
    }

    private static Border MakeFavoriteCard(Thickness margin) => new() { Margin = margin, Padding = new Thickness(18), CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(114, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)), BorderThickness = new Thickness(1) };

    private void BuildFavoriteHomeCard()
    {
        if (HomePage.Children.OfType<StackPanel>().FirstOrDefault() is not { } home) return;
        var card = MakeFavoriteCard(new Thickness(0, 0, 0, 14)); var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = "收藏 Mod 检测", Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold });
        favoriteHomeSummary = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(186, 199, 216)), Margin = new Thickness(0, 7, 0, 8), TextWrapping = TextWrapping.Wrap }; panel.Children.Add(favoriteHomeSummary);
        favoriteStatusList = new StackPanel(); panel.Children.Add(new ScrollViewer { Content = favoriteStatusList, MaxHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) }; var download = new Button { Content = "检查并选择要下载的 Mod", Margin = new Thickness(0, 0, 8, 0) }; download.Click += OpenFavoriteDownloadDialog_Click; var apiSettings = new Button { Content = "下载源设置", Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)) }; apiSettings.Click += ConfigureMarketplace_Click; actions.Children.Add(download); actions.Children.Add(apiSettings); panel.Children.Add(actions); card.Child = panel;
        home.Children.Insert(Math.Min(2, home.Children.Count), card);
    }

    private void ConfigureMarketplace_Click(object sender, RoutedEventArgs e)
    {
        var dialog = AppDialog.CreateWindow(this, "下载源设置", 560, 500, false);
        var root = new StackPanel { Margin = new Thickness(24) }; root.Children.Add(new TextBlock { Text = "CurseForge API Key", FontSize = 18, FontWeight = FontWeights.SemiBold }); root.Children.Add(new TextBlock { Text = "用于 CurseForge 官方接口，只保存在本机。", Foreground = new SolidColorBrush(Color.FromRgb(174, 190, 207)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) });
        var keyBox = new PasswordBox { Password = settings.CurseForgeApiKey }; root.Children.Add(keyBox);
        root.Children.Add(new TextBlock { Text = "GitHub Token（可选）", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 0) }); root.Children.Add(new TextBlock { Text = "不填写也可搜索；填写个人访问令牌可提高 GitHub API 额度。令牌同样只保存在本机。", Foreground = new SolidColorBrush(Color.FromRgb(174, 190, 207)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) });
        var githubBox = new PasswordBox { Password = settings.GitHubToken }; root.Children.Add(githubBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) }; var clear = new Button { Content = "清除密钥", Background = new SolidColorBrush(Color.FromRgb(112, 48, 56)), Margin = new Thickness(0, 0, 8, 0) }; clear.Click += (_, _) => { keyBox.Password = ""; githubBox.Password = ""; }; var cancel = new Button { Content = "取消", Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)), Margin = new Thickness(0, 0, 8, 0) }; cancel.Click += (_, _) => dialog.DialogResult = false; var save = new Button { Content = "保存" }; save.Click += (_, _) => dialog.DialogResult = true; buttons.Children.Add(clear); buttons.Children.Add(cancel); buttons.Children.Add(save); root.Children.Add(buttons); AppDialog.SetBody(dialog, root);
        if (dialog.ShowDialog() == true) { settings.CurseForgeApiKey = keyBox.Password.Trim(); settings.GitHubToken = githubBox.Password.Trim(); SettingsStore.Save(settings); StatusText.Text = "已保存下载源设置"; }
    }

    private async void SearchMarketplace_Click(object sender, RoutedEventArgs e)
    {
        var version = marketplaceVersionCombo?.SelectedItem as string ?? ""; var loader = marketplaceLoaderCombo?.SelectedItem as string ?? "Fabric"; var source = marketplaceSourceCombo?.SelectedItem as string ?? "全部";
        if (string.IsNullOrWhiteSpace(version)) { marketplaceStatus!.Text = "请先选择或输入 Minecraft 版本"; return; }
        settings.PreferredMinecraftVersion = version; settings.PreferredModLoader = loader; settings.PreferredModSource = source; settings.LastModSearchQuery = modSearchBox?.Text.Trim() ?? ""; SettingsStore.Save(settings);
        marketplaceSearchCancellation?.Cancel(); marketplaceSearchCancellation = new CancellationTokenSource(); marketplaceStatus!.Text = "正在搜索…"; marketplaceResults!.Children.Clear();
        try
        {
            var rawQuery = modSearchBox?.Text.Trim() ?? ""; var queries = ModNameLocalization.ContainsChinese(rawQuery) ? ModNameLocalization.FindSlugsByChinese(rawQuery).ToList() : [rawQuery]; if (queries.Count == 0) queries.Add(rawQuery);
            var batches = await Task.WhenAll(queries.Select(query => ModMarketplaceService.SearchAsync(query, version, loader, source, settings.CurseForgeApiKey, settings.GitHubToken, marketplaceSearchCancellation.Token)));
            var results = batches.SelectMany(batch => batch).DistinctBy(item => item.Source + ":" + item.ProjectId).OrderByDescending(item => item.Downloads).ToList();
            foreach (var result in results) marketplaceResults.Children.Add(BuildMarketplaceResult(result));
            marketplaceStatus.Text = $"找到 {results.Count} 个候选项目（下载时将再次核对 {version} / {loader}）" + (source == "全部" && string.IsNullOrWhiteSpace(settings.CurseForgeApiKey) ? "；未设置 CurseForge Key，本次搜索 Modrinth 与 GitHub" : "");
        }
        catch (Exception ex) { marketplaceStatus.Text = "搜索失败：" + ex.Message; }
    }

    private void SaveMarketplacePreferences()
    {
        if (marketplaceVersionCombo?.SelectedItem is string version) settings.PreferredMinecraftVersion = version;
        if (marketplaceLoaderCombo?.SelectedItem is string loader) settings.PreferredModLoader = loader;
        if (marketplaceSourceCombo?.SelectedItem is string source) settings.PreferredModSource = source;
        if (modSearchBox != null) settings.LastModSearchQuery = modSearchBox.Text.Trim();
        SettingsStore.Save(settings);
    }

    private Border BuildMarketplaceResult(ModSearchResult result)
    {
        var card = new Border { Background = new SolidColorBrush(Color.FromArgb(70, 29, 42, 55)), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8) };
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromRgb(42, 58, 72)), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top };
        if (TryCreateRemoteImage(result.IconUrl) is { } image) icon.Child = image; else icon.Child = new TextBlock { Text = "MOD", Foreground = new SolidColorBrush(Color.FromRgb(103, 190, 245)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 10 };
        grid.Children.Add(icon); var text = new StackPanel(); text.Children.Add(new TextBlock { Text = GetLocalizedMarketplaceName(result.Slug, result.Name), Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold }); text.Children.Add(new TextBlock { Text = result.Description, Foreground = new SolidColorBrush(Color.FromRgb(174, 190, 207)), TextWrapping = TextWrapping.Wrap, MaxHeight = 42 }); text.Children.Add(new TextBlock { Text = result.Source == "GitHub" ? $"GitHub  ·  {result.Downloads:N0} Stars" : $"{result.Source}  ·  {result.Downloads:N0} 次下载", Foreground = new SolidColorBrush(Color.FromRgb(103, 190, 245)), Margin = new Thickness(0, 5, 0, 0) }); Grid.SetColumn(text, 1); grid.Children.Add(text);
        var exists = favoriteModDraft.Any(item => item.Source == result.Source && item.ProjectId == result.ProjectId); var button = new Button { Content = exists ? "已收藏" : "收藏", IsEnabled = !exists, Margin = new Thickness(10, 0, 0, 0), MinWidth = 78, Tag = result }; button.Click += FavoriteSearchResult_Click; Grid.SetColumn(button, 2); grid.Children.Add(button); card.Child = grid; return card;
    }

    private static Image? TryCreateRemoteImage(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.UriSource = uri; bitmap.CacheOption = BitmapCacheOption.OnDemand; bitmap.DecodePixelWidth = 96; bitmap.EndInit(); return new Image { Source = bitmap, Stretch = Stretch.UniformToFill }; } catch { return null; }
    }

    private void FavoriteSearchResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModSearchResult result }) return;
        favoriteModDraft.Add(new FavoriteMod { Source = result.Source, ProjectId = result.ProjectId, Slug = result.Slug, Name = result.Name, Description = result.Description, IconUrl = result.IconUrl }); favoriteProfileDirty = true; RefreshFavoriteList(); RefreshFavoriteModStatus(); StatusText.Text = "Mod 收藏草稿已修改（尚未保存方案）"; ((Button)sender).Content = "已收藏"; ((Button)sender).IsEnabled = false;
    }

    private void RefreshFavoriteList()
    {
        if (favoriteModsList == null) return; favoriteModsList.Children.Clear();
        foreach (var favorite in favoriteModDraft.OrderBy(item => item.Name))
        {
            var row = new Border { Background = new SolidColorBrush(Color.FromArgb(60, 29, 42, 55)), CornerRadius = new CornerRadius(9), Padding = new Thickness(11), Margin = new Thickness(0, 0, 0, 7) }; var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); var text = new StackPanel(); text.Children.Add(new TextBlock { Text = GetLocalizedMarketplaceName(favorite.Slug, favorite.Name), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold }); text.Children.Add(new TextBlock { Text = $"{favorite.Source}  ·  {favorite.Slug}", Foreground = new SolidColorBrush(Color.FromRgb(103, 190, 245)) }); grid.Children.Add(text); var remove = new Button { Content = "移除", Tag = favorite, Background = new SolidColorBrush(Color.FromRgb(112, 48, 56)), Margin = new Thickness(8, 0, 0, 0) }; remove.Click += (_, _) => { favoriteModDraft.Remove(favorite); favoriteProfileDirty = true; RefreshFavoriteList(); RefreshFavoriteModStatus(); StatusText.Text = "Mod 收藏草稿已修改（尚未保存方案）"; }; Grid.SetColumn(remove, 1); grid.Children.Add(remove); row.Child = grid; favoriteModsList.Children.Add(row);
        }
        if (favoriteModDraft.Count == 0) favoriteModsList.Children.Add(new TextBlock { Text = "还没有收藏 Mod，请从左侧搜索。", Foreground = new SolidColorBrush(Color.FromRgb(166, 185, 203)) });
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
        if (favoriteStatusList == null) return; favoriteStatusList.Children.Clear(); var missing = favoriteModDraft.Where(item => !IsFavoriteInstalled(item)).ToList(); var installed = favoriteModDraft.Count - missing.Count;
        var selectedVersion = marketplaceVersionCombo?.SelectedItem as string ?? "未识别"; var selectedLoader = marketplaceLoaderCombo?.SelectedItem as string ?? "未识别";
        favoriteHomeSummary!.Text = string.IsNullOrWhiteSpace(instance) ? $"已收藏 {favoriteModDraft.Count} 个 Mod；导入实例后检查缺失情况。" : $"当前实例：Minecraft {selectedVersion} / {selectedLoader}；已安装 {installed} 个，缺少 {missing.Count} 个收藏 Mod。";
        foreach (var favorite in favoriteModDraft) favoriteStatusList.Children.Add(new TextBlock { Text = $"{(IsFavoriteInstalled(favorite) ? "✓" : "○")}  {GetLocalizedMarketplaceName(favorite.Slug, favorite.Name)}  ·  {(IsFavoriteInstalled(favorite) ? "已安装" : "缺失")}", Foreground = IsFavoriteInstalled(favorite) ? new SolidColorBrush(Color.FromRgb(113, 211, 151)) : new SolidColorBrush(Color.FromRgb(255, 190, 105)), Margin = new Thickness(0, 2, 0, 2) });
    }

    private void DetectAndSelectInstanceEnvironment(bool forceDetected = false)
    {
        var detected = DetectInstanceEnvironment(); var version = !forceDetected && !string.IsNullOrWhiteSpace(settings.PreferredMinecraftVersion) ? settings.PreferredMinecraftVersion : detected.Version; var loader = !forceDetected && !string.IsNullOrWhiteSpace(settings.PreferredModLoader) ? settings.PreferredModLoader : detected.Loader;
        if (marketplaceVersionCombo != null) { var selectedVersion = string.IsNullOrWhiteSpace(version) ? CommonMinecraftVersions[0] : version; marketplaceVersionCombo.ItemsSource = CommonMinecraftVersions.Prepend(selectedVersion).Distinct().ToList(); marketplaceVersionCombo.SelectedItem = selectedVersion; }
        if (marketplaceLoaderCombo != null) marketplaceLoaderCombo.SelectedItem = SupportedLoaders.Contains(loader, StringComparer.OrdinalIgnoreCase) ? SupportedLoaders.First(item => item.Equals(loader, StringComparison.OrdinalIgnoreCase)) : "Fabric";
        if (marketplaceStatus != null && !string.IsNullOrWhiteSpace(instance)) marketplaceStatus.Text = $"实例识别：Minecraft {detected.Version} / {detected.Loader}；可手动修改";
    }

    private (string Version, string Loader) DetectInstanceEnvironment()
    {
        var version = ""; var loader = "";
        if (!string.IsNullOrWhiteSpace(instance) && Directory.Exists(instance))
        {
            var versionJson = Path.Combine(instance, Path.GetFileName(instance) + ".json");
            var candidates = File.Exists(versionJson) ? new[] { versionJson } : Directory.EnumerateFiles(instance, "*.json", SearchOption.TopDirectoryOnly).Where(file => Path.GetFileNameWithoutExtension(file).Equals(Path.GetFileName(instance), StringComparison.OrdinalIgnoreCase)).Take(1);
            foreach (var file in candidates)
            {
                try
                {
                    var text = File.ReadAllText(file); using var document = JsonDocument.Parse(text); var root = document.RootElement;
                    foreach (var property in new[] { "clientVersion", "minecraftVersion", "inheritsFrom" }) if (string.IsNullOrWhiteSpace(version) && root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String) { var match = Regex.Match(value.GetString() ?? "", @"(?<!\d)(1\.\d+(?:\.\d+)?)(?!\d)"); if (match.Success) version = match.Groups[1].Value; }
                    if (string.IsNullOrWhiteSpace(version) && root.TryGetProperty("arguments", out var arguments) && arguments.TryGetProperty("game", out var game) && game.ValueKind == JsonValueKind.Array)
                    {
                        var values = game.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString() ?? "").ToList(); var index = values.FindIndex(item => item == "--fml.mcVersion"); if (index >= 0 && index + 1 < values.Count) version = values[index + 1];
                    }
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
        if (string.IsNullOrWhiteSpace(instance) || !Directory.Exists(instance)) { AppDialog.Show(this, "请先导入要安装 Mod 的 Minecraft 实例。", "尚未导入实例", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var missing = favoriteModDraft.Where(item => !IsFavoriteInstalled(item)).ToList(); if (missing.Count == 0) { AppDialog.Show(this, "当前实例没有缺失的收藏 Mod。", "检测完成", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        await ShowFavoriteDownloadDialogAsync(missing);
    }

    private async Task ShowFavoriteDownloadDialogAsync(List<FavoriteMod> missing)
    {
        var dialog = AppDialog.CreateWindow(this, "选择要安装的收藏 Mod", 920, 728); dialog.MinWidth = 760; dialog.MinHeight = 568;
        var root = new Grid { Margin = new Thickness(20) }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var currentVersion = marketplaceVersionCombo?.SelectedItem as string ?? CommonMinecraftVersions[0]; var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition()); var versionBox = new ComboBox { ItemsSource = CommonMinecraftVersions.Prepend(currentVersion).Distinct().ToList(), SelectedItem = currentVersion, Margin = new Thickness(0, 0, 6, 0) }; var loaderBox = new ComboBox { ItemsSource = SupportedLoaders, SelectedItem = marketplaceLoaderCombo?.SelectedItem ?? "Fabric", Margin = new Thickness(6, 0, 0, 0) }; Grid.SetColumn(loaderBox, 1); top.Children.Add(versionBox); top.Children.Add(loaderBox); root.Children.Add(top);
        var rows = new StackPanel(); var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 14, 0, 14) }; Grid.SetRow(scroll, 1); root.Children.Add(scroll); var selections = new List<(FavoriteMod Mod, CheckBox Check, ComboBox Versions)>();
        async Task LoadRowsAsync()
        {
            rows.Children.Clear(); selections.Clear(); var version = versionBox.SelectedItem as string ?? currentVersion; var loader = loaderBox.SelectedItem as string ?? "Fabric";
            foreach (var mod in missing)
            {
                var card = new Border { Background = new SolidColorBrush(Color.FromArgb(75, 29, 42, 55)), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8) }; var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) }); var check = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }; grid.Children.Add(check); var label = new StackPanel(); label.Children.Add(new TextBlock { Text = GetLocalizedMarketplaceName(mod.Slug, mod.Name), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold }); label.Children.Add(new TextBlock { Text = mod.Source, Foreground = new SolidColorBrush(Color.FromRgb(103, 190, 245)) }); Grid.SetColumn(label, 1); grid.Children.Add(label); var versions = new ComboBox { Margin = new Thickness(12, 0, 0, 0), ToolTip = "手动选择此 Mod 在当前 Minecraft 版本与加载器下的具体版本" }; Grid.SetColumn(versions, 2); grid.Children.Add(versions); card.Child = grid; rows.Children.Add(card); selections.Add((mod, check, versions));
                try { var available = await ModMarketplaceService.GetVersionsAsync(mod, version, loader, settings.CurseForgeApiKey, settings.GitHubToken, CancellationToken.None); versions.ItemsSource = available; versions.SelectedIndex = available.Count > 0 ? 0 : -1; if (available.Count == 0) { check.IsChecked = false; check.IsEnabled = false; versions.ToolTip = "没有找到能明确匹配当前版本和加载器的文件"; } }
                catch (Exception ex) { check.IsChecked = false; check.IsEnabled = false; versions.ToolTip = ex.Message; }
            }
        }
        var bottom = new DockPanel(); var status = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(166, 185, 203)), VerticalAlignment = VerticalAlignment.Center }; bottom.Children.Add(status); var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; var refresh = new Button { Content = "按新环境刷新版本", Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)), Margin = new Thickness(0, 0, 8, 0) }; refresh.Click += async (_, _) => await LoadRowsAsync(); var cancel = new Button { Content = "取消", Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)), Margin = new Thickness(0, 0, 8, 0) }; cancel.Click += (_, _) => dialog.Close(); var install = new Button { Content = "下载并安装勾选项", MinWidth = 150 }; install.Click += async (_, _) =>
        {
            var selected = selections.Where(item => item.Check.IsChecked == true && item.Versions.SelectedItem is ModDownloadVersion).ToList(); if (selected.Count == 0) { status.Text = "请至少勾选一个有可用版本的 Mod"; return; }
            install.IsEnabled = false; var installed = 0;
            try { foreach (var item in selected) { var chosen = (ModDownloadVersion)item.Versions.SelectedItem; var safeFileName = Path.GetFileName(chosen.FileName); if (!safeFileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"{item.Mod.Name} 返回的文件不是 Mod JAR：{safeFileName}"); status.Text = $"正在下载 {item.Mod.Name}：{chosen.VersionNumber}"; await ModMarketplaceService.DownloadAsync(chosen, Path.Combine(instance, "mods", safeFileName), CancellationToken.None); installed++; } status.Text = $"已安装 {installed} 个 Mod"; LoadInstance(); RefreshFavoriteModStatus(); AppDialog.Show(dialog, $"已安装 {installed} 个 Mod。请重启 Minecraft。", "安装完成", MessageBoxButton.OK, MessageBoxImage.Information); dialog.Close(); } catch (Exception ex) { status.Text = "安装失败：" + ex.Message; install.IsEnabled = true; }
        }; buttons.Children.Add(refresh); buttons.Children.Add(cancel); buttons.Children.Add(install); DockPanel.SetDock(buttons, Dock.Right); bottom.Children.Add(buttons); Grid.SetRow(bottom, 2); root.Children.Add(bottom); AppDialog.SetBody(dialog, root);
        dialog.Loaded += async (_, _) => await LoadRowsAsync(); dialog.ShowDialog();
    }
}
