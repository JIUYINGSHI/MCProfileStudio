using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace McProfileStudio;

public partial class MainWindow : Window
{
    private readonly AppSettings settings = SettingsStore.Load();
    private readonly ObservableCollection<PackItem> packs = [];
    private readonly ObservableCollection<PackItem> shaders = [];
    private readonly ObservableCollection<KeyBindingItem> allKeys = [];
    private string instance = "";
    private Dictionary<string, ModInfo> mods = new(StringComparer.OrdinalIgnoreCase);
    private Point dragStart; private bool capturing;
    private ListBox? disabledPackList, enabledPackList;
    private ComboBox? packProfileCombo;
    private bool switchingProfile;
    private CancellationTokenSource? shaderPreviewRefresh;

    private static readonly Dictionary<string, string[][]> KeyboardLayouts = CreateKeyboardLayouts();

    public MainWindow()
    {
        EnsurePackProfiles(); InitializeComponent(); PackList.ItemsSource = packs; ShaderList.ItemsSource = shaders; BuildPackManager();
        LayoutCombo.ItemsSource = KeyboardLayouts.Keys; LayoutCombo.SelectedItem = KeyboardLayouts.ContainsKey(settings.KeyboardLayout) ? settings.KeyboardLayout : "108 键全尺寸";
        SourceInitialized += (_, _) => EnableMica(); Loaded += (_, _) => { ReloadLibraries(); RefreshSummary(); FitKeyboard(); }; SizeChanged += (_, _) => FitKeyboard();
    }

    private void Navigate(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return; var index = int.Parse((string)((RadioButton)sender).Tag);
        HomePage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed; PacksPage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed; ShadersPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed; KeysPage.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = new[] { "概览", "资源包排序", "光影包覆盖", "可视化键位" }[index];
        PageSubtitle.Text = new[] { "选择整合包实例，然后统一应用资源与键位。", "拖动调整优先级；不会阻止版本不兼容的资源包。", "固定库存，一键覆盖到任意新整合包。", "从键盘占用定位冲突，再按 Mod 保存专属键位。" }[index];
    }

    private string? PickFolder(string title) { var d = new OpenFolderDialog { Title = title, Multiselect = false }; return d.ShowDialog(this) == true ? d.FolderName : null; }
    private void PickInstance_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFolder("选择包含 options.txt、mods 的 MC 游戏文件夹"); if (path == null) return;
        if (!File.Exists(Path.Combine(path, "options.txt"))) { MessageBox.Show(this, "该目录没有 options.txt。请选实例的游戏目录，并确保游戏至少启动过一次。", "无法导入", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        instance = path; InstanceLabel.Text = path; LoadInstance();
    }
    private void LoadInstance()
    {
        var options = MinecraftConfig.ReadOptions(instance); mods = MinecraftConfig.ScanMods(instance); allKeys.Clear();
        foreach (var pair in options.Where(p => p.Key.StartsWith("key_", StringComparison.Ordinal)))
        {
            var mod = MinecraftConfig.MatchKeyToMod(pair.Key, mods); var translationKey = pair.Key[4..];
            mod.EnglishTranslations.TryGetValue(translationKey, out var english); mod.ChineseTranslations.TryGetValue(translationKey, out var chinese);
            english = string.IsNullOrWhiteSpace(english) ? Humanize(pair.Key) : english; chinese ??= "";
            var item = new KeyBindingItem { OptionKey = pair.Key, DisplayName = string.IsNullOrWhiteSpace(chinese) ? english : chinese, FunctionEnglish = english, FunctionChinese = chinese, ModId = mod.Id, ModDisplayName = mod.DisplayName, IsLibrary = mod.IsLibrary, OriginalValue = pair.Value, Value = pair.Value };
            if (settings.ModKeyProfiles.TryGetValue(mod.Id, out var profile) && profile.TryGetValue(pair.Key, out var saved)) { item.Value = saved; item.Remember = true; } allKeys.Add(item);
        }
        ModFilter.ItemsSource = new[] { "全部有键位的 Mod" }.Concat(allKeys.GroupBy(k => k.ModId).Select(g => g.First().ModDisplayName).Order()).ToList(); ModFilter.SelectedIndex = 0; RefreshKeyList(); BuildKeyboard(); RefreshSummary(); StatusText.Text = $"已导入 {Path.GetFileName(instance)}：{allKeys.Select(k => k.ModId).Distinct().Count()} 个有键位 Mod，{allKeys.Count} 个键位";
    }
    private static string Humanize(string key) => key.Replace("key_key.", "").Replace("key_", "").Replace('.', ' ').Replace('_', ' ');
    private void PickPackLibrary_Click(object sender, RoutedEventArgs e) { var p = PickFolder("选择固定资源包存放目录"); if (p != null) { settings.PackLibrary = p; SaveAndReload(); } }
    private void PickShaderLibrary_Click(object sender, RoutedEventArgs e) { var p = PickFolder("选择固定光影包存放目录"); if (p != null) { settings.ShaderLibrary = p; SaveAndReload(); } }
    private void SaveAndReload() { SettingsStore.Save(settings); ReloadLibraries(); RefreshSummary(); }
    private void ReloadLibraries()
    {
        var profile = settings.PackProfiles[settings.ActivePackProfile]; LoadPacks(settings.PackLibrary, packs, false); var order = profile.PackOrder.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i, StringComparer.OrdinalIgnoreCase); var sorted = packs.OrderBy(p => order.TryGetValue(p.Name, out var i) ? i : int.MaxValue).ThenBy(p => p.Name).ToList(); packs.Clear(); foreach (var p in sorted) { p.Enabled = profile.EnabledPacks.Contains(p.Name, StringComparer.OrdinalIgnoreCase); packs.Add(p); }
        LoadPacks(settings.ShaderLibrary, shaders, true); ShaderList.SelectedItem = shaders.FirstOrDefault(s => s.Name.Equals(settings.SelectedShader, StringComparison.OrdinalIgnoreCase)); BeginOnlineShaderPreviewRefresh(); PackPathText.Text = string.IsNullOrWhiteSpace(settings.PackLibrary) ? "尚未设置" : settings.PackLibrary;
        RefreshPackColumns();
    }

    private async void BeginOnlineShaderPreviewRefresh()
    {
        shaderPreviewRefresh?.Cancel(); shaderPreviewRefresh?.Dispose(); shaderPreviewRefresh = new CancellationTokenSource(); var token = shaderPreviewRefresh.Token;
        var missing = shaders.Where(s => string.IsNullOrWhiteSpace(s.PreviewPath)).ToList(); if (missing.Count == 0) return;
        var onlineLog = Path.Combine(SettingsStore.Root, "shader-preview.log");
        try { File.WriteAllText(onlineLog, $"{DateTime.Now:O} 开始在线匹配：{missing.Count} 个光影包{Environment.NewLine}"); } catch { }
        var found = new System.Collections.Concurrent.ConcurrentBag<(PackItem Item, string Path)>();
        try
        {
            await Parallel.ForEachAsync(missing, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = 3 }, async (item, ct) =>
            {
                try { var path = await ShaderPreviewService.FindAndCacheAsync(item.Name, ct); if (!string.IsNullOrWhiteSpace(path)) found.Add((item, path)); }
                catch (Exception ex) when (!ct.IsCancellationRequested) { try { await File.AppendAllTextAsync(onlineLog, $"{item.Name}: {ex}{Environment.NewLine}", ct); } catch { } }
            });
        }
        catch (OperationCanceledException) { return; }
        foreach (var result in found)
        {
            if (!string.IsNullOrWhiteSpace(result.Item.PreviewPath)) continue;
            result.Item.PreviewPath = result.Path; result.Item.PreviewImage = LoadThumbnail(result.Path, 360); settings.ShaderPreviews[result.Item.Name] = result.Path;
        }
        if (!found.IsEmpty) { SettingsStore.Save(settings); if (ShaderList.SelectedItem is PackItem selected && !string.IsNullOrWhiteSpace(selected.PreviewPath)) { SetImage(ShaderPreview, selected.PreviewPath); ShaderPreviewEmpty.Visibility = Visibility.Collapsed; } }
    }
    private void LoadPacks(string folder, ObservableCollection<PackItem> target, bool shader)
    {
        target.Clear(); if (!Directory.Exists(folder)) return;
        foreach (var path in Directory.EnumerateFileSystemEntries(folder).Where(p => Directory.Exists(p) || Path.GetExtension(p).Equals(".zip", StringComparison.OrdinalIgnoreCase)))
        { var name = Path.GetFileName(path); var preview = shader && settings.ShaderPreviews.TryGetValue(name, out var saved) ? saved : shader ? FindSidecarPreview(path) : MinecraftConfig.ExtractPackPreview(path); var banner = shader ? "" : PackBannerGenerator.GetOrCreate(path); target.Add(new() { Name = name, FullPath = path, PreviewPath = preview, PreviewImage = shader ? LoadThumbnail(preview, 360) : LoadCroppedPackIcon(preview, 128), BannerImage = LoadThumbnail(banner, 720), IsFontBanner = !string.IsNullOrWhiteSpace(banner), Description = shader ? "光影效果预览" : MinecraftConfig.ReadPackDescription(path) }); }
    }
    private static string FindSidecarPreview(string shaderPath) { var dir = Path.GetDirectoryName(shaderPath) ?? ""; var stem = Path.GetFileNameWithoutExtension(shaderPath); foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" }) { var image = Path.Combine(dir, stem + ext); if (File.Exists(image)) return image; } return ""; }
    private void ShaderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShaderList.SelectedItem is not PackItem p) return; settings.SelectedShader = p.Name; SettingsStore.Save(settings); SelectedShaderName.Text = p.Name; SetImage(ShaderPreview, p.PreviewPath); ShaderPreviewEmpty.Visibility = string.IsNullOrWhiteSpace(p.PreviewPath) ? Visibility.Visible : Visibility.Collapsed;
    }
    private void PickShaderPreview_Click(object sender, RoutedEventArgs e)
    {
        if (ShaderList.SelectedItem is not PackItem p) { MessageBox.Show(this, "请先选择一个光影包。"); return; }
        var d = new OpenFileDialog { Title = "选择该光影包的效果图", Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp" }; if (d.ShowDialog(this) != true) return;
        var dir = Path.Combine(SettingsStore.Root, "shader-previews"); Directory.CreateDirectory(dir); var target = Path.Combine(dir, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(p.Name)))[..16] + Path.GetExtension(d.FileName)); File.Copy(d.FileName, target, true); settings.ShaderPreviews[p.Name] = target; p.PreviewPath = target; SettingsStore.Save(settings); ReloadLibraries();
    }
    private static void SetImage(Image image, string path) { image.Source = string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : new BitmapImage(new Uri(path)); }
    private static BitmapSource? LoadThumbnail(string path, int width)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat; image.DecodePixelWidth = width;
            image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
        }
        catch { return null; }
    }

    private static BitmapSource? LoadCroppedPackIcon(string path, int width)
    {
        var source = LoadThumbnail(path, width); if (source == null) return null;
        try
        {
            var bitmap = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var stride = bitmap.PixelWidth * 4; var pixels = new byte[stride * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, stride, 0);
            var left = bitmap.PixelWidth; var top = bitmap.PixelHeight; var right = -1; var bottom = -1;
            for (var y = 0; y < bitmap.PixelHeight; y++) for (var x = 0; x < bitmap.PixelWidth; x++)
            {
                if (pixels[y * stride + x * 4 + 3] < 12) continue; left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
            if (right < left || bottom < top || left + top + (bitmap.PixelWidth - right - 1) + (bitmap.PixelHeight - bottom - 1) < 4) return source;
            var cropped = new CroppedBitmap(bitmap, new Int32Rect(left, top, right - left + 1, bottom - top + 1)); cropped.Freeze(); return cropped;
        }
        catch { return source; }
    }

    private void PackList_MouseDown(object sender, MouseButtonEventArgs e) => dragStart = e.GetPosition(null);
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); var list = e.OriginalSource is DependencyObject d ? FindParent<ListBox>(d) : null; if (e.LeftButton != MouseButtonState.Pressed || list?.SelectedItem is not PackItem item) return; var p = e.GetPosition(null); if (Math.Abs(p.X - dragStart.X) + Math.Abs(p.Y - dragStart.Y) > 8) DragDrop.DoDragDrop(list, item, DragDropEffects.Move); }
    private void PackList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PackItem)) is not PackItem source || sender is not ListBox destination) return; source.Enabled = destination == enabledPackList; var element = destination.InputHitTest(e.GetPosition(destination)) as DependencyObject; while (element != null && element is not ListBoxItem) element = VisualTreeHelper.GetParent(element); var target = (element as ListBoxItem)?.DataContext as PackItem; if (target != null && source != target) { var index = packs.IndexOf(target); packs.Remove(source); packs.Insert(Math.Max(0, index), source); } PersistPackOrder(); RefreshPackColumns();
    }
    private void PersistPackOrder() { var profile = settings.PackProfiles[settings.ActivePackProfile]; profile.PackOrder = packs.Select(p => p.Name).ToList(); profile.EnabledPacks = packs.Where(p => p.Enabled).Select(p => p.Name).ToList(); settings.PackOrder = profile.PackOrder.ToList(); settings.EnabledPacks = profile.EnabledPacks.ToList(); SettingsStore.Save(settings); }

    private void BuildPackManager()
    {
        var template = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(PackCard)) }; PacksPage.Children.Clear(); PacksPage.ColumnDefinitions.Clear(); PacksPage.RowDefinitions.Clear(); PacksPage.RowDefinitions.Add(new() { Height = GridLength.Auto }); PacksPage.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        PacksPage.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); PacksPage.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var profileBar = new DockPanel { Margin = new Thickness(0, 0, 0, 14), LastChildFill = false };
        var profileTools = new StackPanel { Orientation = Orientation.Horizontal }; profileTools.Children.Add(new TextBlock { Text = "资源包配置", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), FontSize = 15 }); packProfileCombo = new ComboBox { Width = 230, ItemsSource = settings.PackProfiles.Keys.Order().ToList(), SelectedItem = settings.ActivePackProfile }; packProfileCombo.SelectionChanged += PackProfile_SelectionChanged; var add = new Button { Content = "新建配置", Margin = new Thickness(10, 0, 0, 0) }; add.Click += AddPackProfile_Click; var rename = new Button { Content = "重命名", Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)), Margin = new Thickness(8, 0, 0, 0) }; rename.Click += RenamePackProfile_Click; profileTools.Children.Add(packProfileCombo); profileTools.Children.Add(add); profileTools.Children.Add(rename);
        var batchTools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(batchTools, Dock.Right); var enable = new Button { Content = "全部启用 →", Padding = new Thickness(13, 7, 13, 7) }; enable.Click += (_, _) => { foreach (var p in packs) p.Enabled = true; PersistPackOrder(); RefreshPackColumns(); }; var disable = new Button { Content = "全部关闭", Padding = new Thickness(13, 7, 13, 7), Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)), Margin = new Thickness(8, 0, 0, 0) }; disable.Click += (_, _) => { foreach (var p in packs) p.Enabled = false; PersistPackOrder(); RefreshPackColumns(); }; batchTools.Children.Add(enable); batchTools.Children.Add(disable); profileBar.Children.Add(batchTools); profileBar.Children.Add(profileTools); Grid.SetColumnSpan(profileBar, 2); PacksPage.Children.Add(profileBar);
        disabledPackList = CreatePackColumn("未应用的资源包", template, false, 0); enabledPackList = CreatePackColumn("已应用的资源包（上方优先）", template, true, 1);
    }
    private ListBox CreatePackColumn(string title, DataTemplate template, bool enabled, int column)
    {
        var list = new ListBox { ItemTemplate = template, AllowDrop = true }; ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled); list.PreviewMouseLeftButtonDown += PackList_MouseDown; list.Drop += PackList_Drop; list.PreviewMouseLeftButtonUp += (_, _) => Dispatcher.BeginInvoke(() => { PersistPackOrder(); RefreshPackColumns(); });
        var panel = new DockPanel(); var header = new TextBlock { Text = title, FontSize = 18, Foreground = Brushes.White, Margin = new Thickness(4, 0, 0, 12) }; DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header); panel.Children.Add(list);
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is PackItem item) { item.Enabled = !enabled; PersistPackOrder(); RefreshPackColumns(); } };
        var border = new Border { Background = new SolidColorBrush(Color.FromArgb(190, 14, 23, 32)), BorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(14), Margin = column == 0 ? new Thickness(0, 0, 7, 14) : new Thickness(7, 0, 0, 14), Child = panel }; Grid.SetColumn(border, column); Grid.SetRow(border, 1); PacksPage.Children.Add(border); return list;
    }
    private void RefreshPackColumns() { if (disabledPackList == null || enabledPackList == null) return; disabledPackList.ItemsSource = packs.Where(p => !p.Enabled).ToList(); enabledPackList.ItemsSource = packs.Where(p => p.Enabled).ToList(); }
    private static T? FindParent<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T found) return found;
            current = current switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement content => content.Parent ?? ContentOperations.GetParent(content),
                ContentElement content => ContentOperations.GetParent(content),
                _ => LogicalTreeHelper.GetParent(current)
            };
        }
        return null;
    }

    private void EnsurePackProfiles()
    {
        if (settings.PackProfiles.Count == 0) settings.PackProfiles["默认配置"] = new PackProfile { PackOrder = settings.PackOrder.ToList(), EnabledPacks = settings.EnabledPacks.ToList() };
        if (!settings.PackProfiles.ContainsKey(settings.ActivePackProfile)) settings.ActivePackProfile = settings.PackProfiles.Keys.First();
    }
    private void PackProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (switchingProfile || packProfileCombo?.SelectedItem is not string name || name == settings.ActivePackProfile) return; PersistPackOrder(); settings.ActivePackProfile = name; SettingsStore.Save(settings); ReloadLibraries();
    }
    private void AddPackProfile_Click(object sender, RoutedEventArgs e)
    {
        PersistPackOrder(); var n = 1; string suggestion; do suggestion = $"配置 {n++}"; while (settings.PackProfiles.ContainsKey(suggestion)); var name = PromptForProfileName("新建资源包配置", suggestion); if (name == null) return; if (settings.PackProfiles.ContainsKey(name)) { MessageBox.Show(this, "已经存在同名配置。", "无法新建"); return; } settings.PackProfiles[name] = new PackProfile(); settings.ActivePackProfile = name; RefreshProfileSelector(); ReloadLibraries();
    }
    private void RenamePackProfile_Click(object sender, RoutedEventArgs e)
    {
        var old = settings.ActivePackProfile; var name = PromptForProfileName("重命名资源包配置", old); if (name == null || name == old) return; if (settings.PackProfiles.ContainsKey(name)) { MessageBox.Show(this, "已经存在同名配置。", "无法重命名"); return; } var profile = settings.PackProfiles[old]; settings.PackProfiles.Remove(old); settings.PackProfiles[name] = profile; settings.ActivePackProfile = name; RefreshProfileSelector();
    }
    private void RefreshProfileSelector() { SettingsStore.Save(settings); if (packProfileCombo == null) return; switchingProfile = true; packProfileCombo.ItemsSource = settings.PackProfiles.Keys.Order().ToList(); packProfileCombo.SelectedItem = settings.ActivePackProfile; switchingProfile = false; }

    private string? PromptForProfileName(string title, string initial)
    {
        var dialog = new Window { Owner = this, Title = title, Width = 460, Height = 235, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, FontFamily = (FontFamily)Application.Current.Resources["AppFont"] };
        var root = new Border { Background = new SolidColorBrush(Color.FromArgb(248, 14, 22, 31)), BorderBrush = new SolidColorBrush(Color.FromArgb(100, 49, 183, 255)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(22), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 35, ShadowDepth = 8, Opacity = .45, Color = Colors.Black } };
        var rows = new Grid(); rows.RowDefinitions.Add(new() { Height = GridLength.Auto }); rows.RowDefinitions.Add(new() { Height = GridLength.Auto }); rows.RowDefinitions.Add(new() { Height = GridLength.Auto }); rows.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new DockPanel(); var close = new Button { Content = "×", Width = 34, Height = 30, Padding = new Thickness(0), Background = Brushes.Transparent, FontSize = 20 }; close.Click += (_, _) => dialog.DialogResult = false; DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close); heading.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }); Grid.SetRow(heading, 0); rows.Children.Add(heading);
        var hint = new TextBlock { Text = "请输入配置名称", Foreground = new SolidColorBrush(Color.FromRgb(174, 190, 207)), Margin = new Thickness(0, 14, 0, 7) }; Grid.SetRow(hint, 1); rows.Children.Add(hint);
        var input = new TextBox { Text = initial, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(160, 22, 32, 43)), BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), BorderThickness = new Thickness(1), FontSize = 15 }; Grid.SetRow(input, 2); rows.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; var cancel = new Button { Content = "取消", Width = 90, Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)), Margin = new Thickness(0, 0, 9, 0) }; cancel.Click += (_, _) => dialog.DialogResult = false; var ok = new Button { Content = "确定", Width = 100 }; ok.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = true; }; buttons.Children.Add(cancel); buttons.Children.Add(ok); Grid.SetRow(buttons, 3); rows.Children.Add(buttons); root.Child = rows; dialog.Content = root; dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); }; return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private void KeyFilterChanged(object sender, EventArgs e) { if (IsLoaded) RefreshKeyList(); }
    private void RefreshKeyList()
    {
        if (KeyList == null) return; IEnumerable<KeyBindingItem> q = allKeys; if (!string.IsNullOrWhiteSpace(KeySearch.Text)) q = q.Where(k => (k.FunctionDisplay + k.OptionKey + k.ModDisplayName + k.ModId + k.KeyLabel).Contains(KeySearch.Text, StringComparison.OrdinalIgnoreCase)); if (ModFilter.SelectedItem is string mod && mod != "全部有键位的 Mod") q = q.Where(k => k.ModDisplayName == mod); if (ConflictOnly.IsChecked == true) { var c = allKeys.Where(k => !k.Value.EndsWith("unknown")).GroupBy(k => k.Value).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(); q = q.Where(k => c.Contains(k.Value)); } KeyList.ItemsSource = q.ToList();
    }
    private void BuildKeyboard()
    {
        KeyboardPanel.Children.Clear(); var active = allKeys.Where(k => !k.Value.Contains("unknown", StringComparison.OrdinalIgnoreCase)).ToList(); var counts = active.GroupBy(k => NormalizeKeyLabel(k.KeyLabel.Split(':')[0])).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase); var conflictKeys = active.GroupBy(k => k.Value, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => NormalizeKeyLabel(g.First().KeyLabel.Split(':')[0])).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedLayout = LayoutCombo.SelectedItem as string ?? "108 键全尺寸"; if (!KeyboardLayouts.TryGetValue(selectedLayout, out var rows)) rows = KeyboardLayouts["108 键全尺寸"];
        foreach (var row in rows) { var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center }; foreach (var key in row) { counts.TryGetValue(key, out var used); var width = key switch { "SPACE" => 245, "BACKSPACE" or "RSHIFT" or "LSHIFT" or "ENTER" => 92, "CAPS" or "TAB" => 72, _ => 48 }; var b = new Button { Content = used == 0 ? key : $"{key}\n{used}", ToolTip = key, Margin = new(3), Width = width, Height = 42, FontSize = key.Length > 3 ? 8.5 : 12, Background = new SolidColorBrush(conflictKeys.Contains(key) ? Color.FromRgb(190, 64, 74) : used > 0 ? Color.FromRgb(0, 120, 212) : Color.FromRgb(48, 61, 75)), Tag = key }; b.Click += KeyboardKey_Click; panel.Children.Add(b); } KeyboardPanel.Children.Add(panel); } Dispatcher.BeginInvoke(FitKeyboard, System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void FitKeyboard() { if (!IsLoaded || KeyboardPanel.Children.Count == 0) return; KeyboardPanel.LayoutTransform = Transform.Identity; KeyboardPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); var available = Math.Max(100, ActualWidth - 330); var scale = Math.Min(1, available / Math.Max(1, KeyboardPanel.DesiredSize.Width)); KeyboardPanel.LayoutTransform = new ScaleTransform(scale, scale); }
    private static string NormalizeKeyLabel(string key) => key.Replace("LEFT ", "L").Replace("RIGHT ", "R").Replace("LEFT", "L").Replace("RIGHT", "R").Replace("RETURN", "ENTER").Replace("OEM", "").Trim();
    private void KeyboardKey_Click(object sender, RoutedEventArgs e) { var key = (string)((Button)sender).Tag; var matches = allKeys.Where(k => NormalizeKeyLabel(k.KeyLabel.Split(':')[0]).Equals(key, StringComparison.OrdinalIgnoreCase)).ToList(); KeyOccupancyList.ItemsSource = matches; SelectedKeyName.Text = matches.Count == 0 ? $"{key}：未占用" : $"{key}：{matches.Count} 个功能占用"; SelectedKeyMod.Text = matches.Count > 1 ? "这里包含相同主键的全部组合；只有组合键完全相同才算冲突。" : ""; }
    private void LayoutCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!IsLoaded || LayoutCombo.SelectedItem is not string layout) return; settings.KeyboardLayout = layout; SettingsStore.Save(settings); BuildKeyboard(); }
    private KeyBindingItem? SelectedKey => KeyList.SelectedItem as KeyBindingItem;
    private void KeyList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (SelectedKey is not { } k) return; SelectedKeyName.Text = k.FunctionDisplay; SelectedKeyMod.Text = k.ModDisplayName + (k.IsLibrary ? "  ·  前置/依赖库" : ""); KeyOccupancyList.ItemsSource = allKeys.Where(x => x.Value == k.Value).ToList(); RememberKey.IsChecked = k.Remember; CaptureButton.Content = $"当前：{k.KeyLabel}（点击重新绑定）"; }
    private void CaptureButton_Click(object sender, RoutedEventArgs e) { if (SelectedKey == null) return; capturing = true; CaptureButton.Content = "请按主键，支持 Ctrl / Shift / Alt + 主键"; CaptureButton.Focus(); Keyboard.Focus(CaptureButton); }
    private void CaptureButton_KeyDown(object sender, KeyEventArgs e) { if (!capturing || SelectedKey == null) return; e.Handled = true; var key = e.Key == Key.System ? e.SystemKey : e.Key; if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt) { CaptureButton.Content = $"已按下 {key}，请继续按主键"; return; } var minecraftKey = ToMinecraftKey(key); var modifier = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? ":CONTROL" : (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? ":SHIFT" : (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? ":ALT" : ""; SelectedKey.Value = "key.keyboard." + minecraftKey + modifier; capturing = false; CaptureButton.Content = $"当前：{SelectedKey.KeyLabel}（点击重新绑定）"; BuildKeyboard(); RefreshKeyList(); }
    private static string ToMinecraftKey(Key key) => key switch { Key.Oem1 => "semicolon", Key.Oem2 => "slash", Key.Oem3 => "grave.accent", Key.Oem4 => "left.bracket", Key.Oem5 => "backslash", Key.Oem6 => "right.bracket", Key.Oem7 => "apostrophe", Key.OemComma => "comma", Key.OemPeriod => "period", Key.OemMinus => "minus", Key.OemPlus => "equal", Key.Return => "enter", Key.Back => "backspace", Key.Space => "space", Key.LeftShift => "left.shift", Key.RightShift => "right.shift", Key.LeftCtrl => "left.control", Key.RightCtrl => "right.control", Key.LeftAlt => "left.alt", Key.RightAlt => "right.alt", >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(), >= Key.NumPad0 and <= Key.NumPad9 => "keypad." + ((int)key - (int)Key.NumPad0), _ => key.ToString().ToLowerInvariant() };
    private void ClearKey_Click(object sender, RoutedEventArgs e) { if (SelectedKey == null) return; SelectedKey.Value = "key.keyboard.unknown"; BuildKeyboard(); RefreshKeyList(); }
    private void RememberKey_Changed(object sender, RoutedEventArgs e) { if (SelectedKey == null) return; SelectedKey.Remember = RememberKey.IsChecked == true; if (SelectedKey.Remember) { if (!settings.ModKeyProfiles.TryGetValue(SelectedKey.ModId, out var map)) settings.ModKeyProfiles[SelectedKey.ModId] = map = []; map[SelectedKey.OptionKey] = SelectedKey.Value; } else if (settings.ModKeyProfiles.TryGetValue(SelectedKey.ModId, out var map)) map.Remove(SelectedKey.OptionKey); SettingsStore.Save(settings); }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(instance)) { MessageBox.Show(this, "请先导入一个 MC 游戏文件夹。", "尚未选择实例"); return; }
        try { PersistPackOrder(); MinecraftConfig.MirrorLibrary(settings.PackLibrary, Path.Combine(instance, "resourcepacks")); MinecraftConfig.MirrorLibrary(settings.ShaderLibrary, Path.Combine(instance, "shaderpacks")); var changes = allKeys.ToDictionary(k => k.OptionKey, k => k.Value, StringComparer.Ordinal); foreach (var mod in mods.Keys) if (settings.ModKeyProfiles.TryGetValue(mod, out var profile)) foreach (var p in profile) changes[p.Key] = p.Value; changes["resourcePacks"] = MinecraftConfig.ResourcePackValue(packs); MinecraftConfig.PatchOptions(instance, changes); ApplyShaderSelection(); StatusText.Text = $"应用完成：{DateTime.Now:HH:mm:ss}（已备份 options.txt）"; MessageBox.Show(this, "资源包、光影包与适用键位已写入。请在 Minecraft 完全退出时应用。", "应用完成", MessageBoxButton.OK, MessageBoxImage.Information); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "应用失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void ApplyShaderSelection() { if (string.IsNullOrWhiteSpace(settings.SelectedShader)) return; PatchProperty(Path.Combine(instance, "config", "iris.properties"), "shaderPack", settings.SelectedShader); PatchProperty(Path.Combine(instance, "optionsof.txt"), "ofShaderPack", settings.SelectedShader); }
    private static void PatchProperty(string file, string key, string value) { if (!File.Exists(file)) return; File.Copy(file, file + ".mcprofilestudio.bak", true); var lines = File.ReadAllLines(file).ToList(); var i = lines.FindIndex(x => x.StartsWith(key + "=", StringComparison.Ordinal)); if (i >= 0) lines[i] = key + "=" + value; else lines.Add(key + "=" + value); File.WriteAllLines(file, lines); }
    private void RefreshSummary() { PackCount.Text = packs.Count.ToString(); ModCount.Text = allKeys.Select(k => k.ModId).Distinct().Count().ToString(); var conflicts = allKeys.Where(k => !k.Value.EndsWith("unknown")).GroupBy(k => k.Value).Count(g => g.Count() > 1); KeyCount.Text = $"{allKeys.Count} / {conflicts}"; LibrarySummary.Text = $"资源包：{(settings.PackLibrary.Length == 0 ? "未设置" : settings.PackLibrary)}\n光影包：{(settings.ShaderLibrary.Length == 0 ? "未设置" : settings.ShaderLibrary)}"; }

    private static Dictionary<string, string[][]> CreateKeyboardLayouts()
    {
        string[] Row(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var compact = new[] { Row("ESC 1 2 3 4 5 6 7 8 9 0 - = BACKSPACE"), Row("TAB Q W E R T Y U I O P [ ] \\"), Row("CAPS A S D F G H J K L ; ' ENTER"), Row("LSHIFT Z X C V B N M , . / RSHIFT"), Row("LCTRL LWIN LALT SPACE RALT FN RCTRL") };
        var layouts = new Dictionary<string, string[][]>
        {
            ["60 / 61 键"] = compact,
            ["65 / 68 键"] = compact.Select((r,i) => i == 4 ? r.Concat(Row("LEFT DOWN RIGHT")).ToArray() : r).ToArray(),
            ["75 / 84 键"] = new[] { Row("ESC F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12 DEL"), Row("` 1 2 3 4 5 6 7 8 9 0 - = BACKSPACE"), Row("TAB Q W E R T Y U I O P [ ] \\"), Row("CAPS A S D F G H J K L ; ' ENTER"), Row("LSHIFT Z X C V B N M , . / RSHIFT UP"), Row("LCTRL LWIN LALT SPACE RALT FN RCTRL LEFT DOWN RIGHT") },
            ["80 / 87 键 TKL"] = new[] { Row("ESC F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12 PRTSC SCRLK PAUSE"), Row("` 1 2 3 4 5 6 7 8 9 0 - = BACKSPACE INS HOME PGUP"), Row("TAB Q W E R T Y U I O P [ ] \\ DEL END PGDN"), Row("CAPS A S D F G H J K L ; ' ENTER"), Row("LSHIFT Z X C V B N M , . / RSHIFT UP"), Row("LCTRL LWIN LALT SPACE RALT FN RCTRL LEFT DOWN RIGHT") },
            ["96 / 98 键"] = new[] { Row("ESC F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12 DEL HOME END"), Row("` 1 2 3 4 5 6 7 8 9 0 - = BACKSPACE NUMLOCK / *"), Row("TAB Q W E R T Y U I O P [ ] \\ 7 8 9"), Row("CAPS A S D F G H J K L ; ' ENTER 4 5 6"), Row("LSHIFT Z X C V B N M , . / RSHIFT UP 1 2 3"), Row("LCTRL LWIN LALT SPACE RALT FN RCTRL LEFT DOWN RIGHT 0 .") },
            ["104 键全尺寸"] = FullKeyboard(false), ["108 键全尺寸"] = FullKeyboard(true)
        }; return layouts;
    }
    private static string[][] FullKeyboard(bool extra)
    {
        string[] R(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries); var top = extra ? "ESC F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12 M1 M2 M3 M4 PRTSC SCRLK PAUSE" : "ESC F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12 PRTSC SCRLK PAUSE";
        return new[] { R(top), R("` 1 2 3 4 5 6 7 8 9 0 - = BACKSPACE INS HOME PGUP NUMLOCK / * -"), R("TAB Q W E R T Y U I O P [ ] \\ DEL END PGDN 7 8 9 +"), R("CAPS A S D F G H J K L ; ' ENTER 4 5 6 +"), R("LSHIFT Z X C V B N M , . / RSHIFT UP 1 2 3 ENTER"), R("LCTRL LWIN LALT SPACE RALT FN RCTRL LEFT DOWN RIGHT 0 . ENTER") };
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private void EnableMica() { try { var hwnd = new WindowInteropHelper(this).Handle; var enabled = 1; DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)); var backdrop = 2; DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int)); } catch { } }
}
