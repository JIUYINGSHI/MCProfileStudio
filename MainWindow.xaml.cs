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
    private ComboBox? keyProfileCombo;
    private bool switchingKeyProfile;
    private CheckBox? countConflictCheck;
    private bool syncingConflictCheck;
    private string? selectedPhysicalKey;
    private string draftSelectedShader = "";
    private CancellationTokenSource? shaderPreviewRefresh;

    private static readonly Dictionary<string, string[][]> KeyboardLayouts = CreateKeyboardLayouts();

    public MainWindow()
    {
        EnsurePackProfiles(); EnsureKeyProfiles(); EnsureFavoriteModProfiles(); draftSelectedShader = settings.SelectedShader; InitializeComponent(); ApplyMinecraftNavIcons(); PackList.ItemsSource = packs; ShaderList.ItemsSource = shaders; BuildPackManager(); BuildDraftControls(); BuildModConfigPage(); BuildFavoriteModsPage(); BuildDataToolsCard(); EnableHomeScrolling();
        LayoutCombo.ItemsSource = KeyboardLayouts.Keys; LayoutCombo.SelectedItem = KeyboardLayouts.ContainsKey(settings.KeyboardLayout) ? settings.KeyboardLayout : "108 键全尺寸";
        SourceInitialized += (_, _) => EnableMica(); Loaded += (_, _) => { ReloadLibraries(); RefreshSummary(); FitKeyboard(); }; SizeChanged += (_, _) => FitKeyboard();
    }

    private void ApplyMinecraftNavIcons()
    {
        var labels = new Dictionary<string, string> { ["0"] = "概览", ["1"] = "资源包", ["2"] = "光影包", ["3"] = "键位配置" };
        foreach (var item in labels) if (FindNavButton(item.Key) is { } button) button.Content = CreateMinecraftNavContent(item.Key, item.Value);
    }

    private static FrameworkElement CreateMinecraftNavContent(string tag, string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(CreateMinecraftPixelIcon(tag));
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), FontWeight = FontWeights.SemiBold });
        return panel;
    }

    private static Canvas CreateMinecraftPixelIcon(string tag)
    {
        var canvas = new Canvas { Width = 28, Height = 28, SnapsToDevicePixels = true, UseLayoutRounding = true };
        void Pixel(double x, double y, double w, double h, string color)
        {
            var block = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Fill = (Brush)new BrushConverter().ConvertFromString(color)!, SnapsToDevicePixels = true };
            Canvas.SetLeft(block, x); Canvas.SetTop(block, y); canvas.Children.Add(block);
        }
        Pixel(1, 1, 26, 26, "#1A2732");
        switch (tag)
        {
            case "0": // Creeper face
                Pixel(3, 3, 22, 22, "#5FAE48"); Pixel(3, 3, 22, 3, "#7BC963"); Pixel(5, 7, 3, 3, "#78C45C"); Pixel(19, 5, 4, 3, "#4B923B");
                Pixel(6, 9, 6, 6, "#17351F"); Pixel(17, 9, 5, 6, "#17351F"); Pixel(11, 14, 7, 5, "#17351F"); Pixel(8, 18, 5, 5, "#17351F"); Pixel(17, 18, 5, 5, "#17351F");
                Pixel(5, 21, 3, 3, "#4A8E39"); Pixel(22, 16, 2, 6, "#76C35B"); break;
            case "1": // Grass block
                Pixel(3, 7, 22, 18, "#82512D"); Pixel(3, 3, 22, 7, "#62AB45"); Pixel(3, 8, 5, 4, "#55953D"); Pixel(10, 7, 4, 5, "#6DB64D"); Pixel(19, 8, 6, 4, "#4D8D38");
                Pixel(6, 13, 5, 4, "#A56F3E"); Pixel(14, 11, 4, 5, "#654024"); Pixel(20, 15, 4, 6, "#A16A3A"); Pixel(4, 21, 6, 3, "#654024"); Pixel(11, 18, 5, 5, "#B07A46"); Pixel(18, 22, 3, 3, "#5B3922"); break;
            case "2": // Eye of Ender
                Pixel(3, 12, 3, 5, "#64418A"); Pixel(6, 9, 3, 11, "#8756AD"); Pixel(9, 6, 4, 17, "#A070C4"); Pixel(13, 4, 4, 21, "#72D0B9");
                Pixel(17, 6, 4, 17, "#A070C4"); Pixel(21, 9, 3, 11, "#8756AD"); Pixel(24, 12, 2, 5, "#64418A"); Pixel(11, 10, 3, 9, "#4DAA96"); Pixel(14, 9, 5, 11, "#152C31"); Pixel(16, 11, 3, 7, "#071719"); Pixel(19, 8, 2, 4, "#C294DB"); break;
            case "3": // Redstone repeater
                Pixel(3, 15, 22, 9, "#A9A69F"); Pixel(3, 15, 22, 3, "#D7D4CC"); Pixel(5, 20, 18, 2, "#85827D"); Pixel(7, 9, 4, 8, "#8C292F"); Pixel(17, 6, 4, 11, "#A9363B");
                Pixel(6, 6, 6, 4, "#E34D51"); Pixel(7, 5, 4, 2, "#FF817A"); Pixel(16, 3, 6, 4, "#E34D51"); Pixel(17, 2, 4, 2, "#FF817A"); Pixel(11, 18, 6, 2, "#B82F37"); Pixel(13, 17, 2, 5, "#E24A4F"); break;
            default: // Crafting table
                Pixel(3, 3, 22, 22, "#9C6231"); Pixel(3, 3, 22, 6, "#D09045"); Pixel(5, 5, 4, 2, "#654022"); Pixel(12, 4, 3, 4, "#724626"); Pixel(19, 5, 4, 2, "#654022");
                Pixel(5, 11, 18, 3, "#5C381F"); Pixel(5, 18, 18, 3, "#5C381F"); Pixel(10, 9, 3, 15, "#5C381F"); Pixel(18, 9, 3, 15, "#5C381F"); Pixel(6, 14, 3, 4, "#BD7E3C"); Pixel(14, 14, 3, 4, "#7D4B27"); Pixel(22, 14, 2, 4, "#C48640"); break;
        }
        return canvas;
    }

    private void BuildDraftControls()
    {
        if (FindLogicalParent<WrapPanel>(FindButtonByContent(HomePage, "选择固定资源包目录")) is { } homeButtons)
        {
            var import = new Button { Content = "导入 options.txt", Margin = new Thickness(0, 0, 10, 0) }; import.Click += ImportOptions_Click; homeButtons.Children.Insert(0, import);
        }
        if (FindLogicalParent<StackPanel>(CaptureButton) is not { } editor) return;
        KeyOccupancyList.Visibility = Visibility.Collapsed;
        var box = new Border { Background = new SolidColorBrush(Color.FromArgb(56, 26, 42, 56)), CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Margin = new Thickness(0, 8, 0, 10) };
        var content = new StackPanel(); content.Children.Add(new TextBlock { Text = "键位配置草稿", Foreground = new SolidColorBrush(Color.FromRgb(175, 199, 221)), Margin = new Thickness(0, 0, 0, 7) });
        keyProfileCombo = new ComboBox(); keyProfileCombo.SelectionChanged += KeyProfile_SelectionChanged; content.Children.Add(keyProfileCombo);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; var add = new Button { Content = "新建", Padding = new Thickness(12, 6, 12, 6) }; add.Click += AddKeyProfile_Click; var rename = new Button { Content = "重命名", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(6, 0, 0, 0), Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)) }; rename.Click += RenameKeyProfile_Click; var delete = new Button { Content = "删除", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(6, 0, 0, 0), Background = new SolidColorBrush(Color.FromRgb(112, 48, 56)) }; delete.Click += DeleteKeyProfile_Click; var save = new Button { Content = "保存配置", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(6, 0, 0, 0) }; save.Click += SaveKeyProfile_Click; buttons.Children.Add(add); buttons.Children.Add(rename); buttons.Children.Add(delete); buttons.Children.Add(save); content.Children.Add(buttons); box.Child = content; editor.Children.Insert(3, box); RefreshKeyProfileSelector();
        countConflictCheck = new CheckBox { Content = "计入冲突检测", IsChecked = true, Margin = new Thickness(0, 8, 0, 8), ToolTip = "关闭后，这个功能即使与其他功能使用同一按键，也不会被标为冲突。" };
        countConflictCheck.Checked += ConflictParticipation_Changed; countConflictCheck.Unchecked += ConflictParticipation_Changed;
        if (FindButtonByContent(editor, "清除绑定") is { } clearButton) editor.Children.Insert(editor.Children.IndexOf(clearButton) + 1, countConflictCheck); else editor.Children.Add(countConflictCheck);
    }

    private static Button? FindButtonByContent(DependencyObject root, string text)
    {
        if (root is Button b && Equals(b.Content, text)) return b; foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { var found = FindButtonByContent(child, text); if (found != null) return found; } return null;
    }
    private static T? FindLogicalParent<T>(DependencyObject? child) where T : DependencyObject { while (child != null) { child = LogicalTreeHelper.GetParent(child); if (child is T found) return found; } return null; }

    private void Navigate(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return; var index = int.Parse((string)((RadioButton)sender).Tag);
        HomePage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed; PacksPage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed; ShadersPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed; KeysPage.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed; if (modConfigsPage != null) modConfigsPage.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed; if (favoriteModsPage != null) favoriteModsPage.Visibility = index == 5 ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = new[] { "概览", "资源包排序", "光影包覆盖", "可视化键位", "Mod 配置", "Mod 收藏与下载" }[index];
        PageSubtitle.Text = new[] { "选择整合包实例，然后统一应用资源与键位。", "拖动调整优先级；不会阻止版本不兼容的资源包。", "固定库存，一键覆盖到任意新整合包。", "从键盘占用定位冲突，再按 Mod 保存专属键位。", "管理不写入 options.txt 的独立快捷键、开关和列表配置。", "从 Modrinth、CurseForge 与 GitHub 收藏 Mod，并为新实例选择兼容版本。" }[index];
    }

    private string? PickFolder(string title) { var d = new OpenFolderDialog { Title = title, Multiselect = false }; return d.ShowDialog(this) == true ? d.FolderName : null; }
    private void PickInstance_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFolder("选择包含 options.txt、mods 的 MC 游戏文件夹"); if (path == null) return;
        if (!File.Exists(Path.Combine(path, "options.txt"))) { AppDialog.Show(this, "该目录没有 options.txt。请选实例的游戏目录，并确保游戏至少启动过一次。", "无法导入", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        instance = path; InstanceLabel.Text = path; LoadInstance();
    }
    private void ImportOptions_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "选择要导入的 options.txt", Filter = "Minecraft options.txt|options.txt|文本文件|*.txt|所有文件|*.*", Multiselect = false }; if (picker.ShowDialog(this) != true) return;
        var choice = ShowImportChoice(); if (choice == null) return; var options = MinecraftConfig.ReadOptionsFile(picker.FileName); var directory = Path.GetDirectoryName(picker.FileName) ?? "";
        if (choice.Value.Packs && options.TryGetValue("resourcePacks", out var rawPacks))
        {
            var imported = MinecraftConfig.ParseResourcePacks(rawPacks); var rank = imported.AsEnumerable().Reverse().Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
            foreach (var pack in packs) pack.Enabled = rank.ContainsKey(pack.Name); var sorted = packs.OrderBy(p => rank.TryGetValue(p.Name, out var index) ? index : int.MaxValue).ToList(); packs.Clear(); foreach (var pack in sorted) packs.Add(pack); RefreshPackColumns();
        }
        if (choice.Value.Shader)
        {
            var shader = MinecraftConfig.ReadShaderSelection(directory); if (!string.IsNullOrWhiteSpace(shader)) { draftSelectedShader = shader; ShaderList.SelectedItem = shaders.FirstOrDefault(x => x.Name.Equals(shader, StringComparison.OrdinalIgnoreCase)); SelectedShaderName.Text = shader; }
        }
        if (choice.Value.Keys) { instance = directory; InstanceLabel.Text = directory; LoadKeysFromOptions(options, directory, true); }
        StatusText.Text = $"已导入 {Path.GetFileName(picker.FileName)}（尚未保存配置）";
    }
    private (bool Packs, bool Shader, bool Keys)? ShowImportChoice()
    {
        var dialog = AppDialog.CreateWindow(this, "选择导入内容", 430, 358, false);
        var root = new StackPanel { Margin = new Thickness(24) }; root.Children.Add(new TextBlock { Text = "从 options.txt 获取哪些内容？", FontSize = 20, FontWeight = FontWeights.SemiBold }); root.Children.Add(new TextBlock { Text = "导入结果只进入当前草稿，不会自动保存配置。", Foreground = new SolidColorBrush(Color.FromRgb(174, 190, 207)), Margin = new Thickness(0, 6, 0, 14) });
        var packsBox = new CheckBox { Content = "资源包启用状态与排序", IsChecked = true }; var shaderBox = new CheckBox { Content = "光影包选择（同时检查同目录 Iris / OptiFine 配置）", IsChecked = true }; var keysBox = new CheckBox { Content = "全部键位配置", IsChecked = true }; root.Children.Add(packsBox); root.Children.Add(shaderBox); root.Children.Add(keysBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) }; var cancel = new Button { Content = "取消", Width = 90, Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)), Margin = new Thickness(0, 0, 8, 0) }; cancel.Click += (_, _) => dialog.DialogResult = false; var ok = new Button { Content = "导入", Width = 100 }; ok.Click += (_, _) => dialog.DialogResult = true; buttons.Children.Add(cancel); buttons.Children.Add(ok); root.Children.Add(buttons); AppDialog.SetBody(dialog, root);
        return dialog.ShowDialog() == true ? (packsBox.IsChecked == true, shaderBox.IsChecked == true, keysBox.IsChecked == true) : null;
    }
    private void LoadInstance()
    {
        var options = MinecraftConfig.ReadOptions(instance); LoadKeysFromOptions(options, instance, false);
    }
    private void LoadKeysFromOptions(Dictionary<string, string> options, string sourceDirectory, bool includeInDraft)
    {
        mods = MinecraftConfig.ScanMods(sourceDirectory); allKeys.Clear(); selectedPhysicalKey = null;
        foreach (var pair in options.Where(p => p.Key.StartsWith("key_", StringComparison.Ordinal)))
        {
            var mod = MinecraftConfig.MatchKeyToMod(pair.Key, mods); var translationKey = pair.Key[4..];
            mod.EnglishTranslations.TryGetValue(translationKey, out var english); mod.ChineseTranslations.TryGetValue(translationKey, out var chinese);
            english = string.IsNullOrWhiteSpace(english) ? Humanize(pair.Key) : english; chinese ??= "";
            var item = new KeyBindingItem { OptionKey = pair.Key, DisplayName = string.IsNullOrWhiteSpace(chinese) ? english : chinese, FunctionEnglish = english, FunctionChinese = chinese, ModId = mod.Id, ModDisplayName = mod.DisplayName, IsLibrary = mod.IsLibrary, OriginalValue = pair.Value, Value = pair.Value, Remember = includeInDraft };
            if (!includeInDraft && settings.KeyProfiles.TryGetValue(settings.ActiveKeyProfile, out var keyProfile)) { if (keyProfile.ModBindings.TryGetValue(mod.Id, out var savedMap) && savedMap.TryGetValue(pair.Key, out var saved)) { item.Value = saved; item.Remember = true; } item.CountsAsConflict = !keyProfile.ConflictExcluded.Contains(pair.Key); } allKeys.Add(item);
        }
        ModFilter.ItemsSource = new[] { "全部有键位的 Mod" }.Concat(allKeys.GroupBy(k => k.ModId).Select(g => g.First().ModDisplayName).Order()).ToList(); ModFilter.SelectedIndex = 0; RefreshKeyList(); BuildKeyboard(); RefreshModConfigPage(); RefreshSummary(); DetectAndSelectInstanceEnvironment(true); RefreshFavoriteModStatus(); StatusText.Text = $"已导入 {Path.GetFileName(instance)}：{allKeys.Select(k => k.ModId).Distinct().Count()} 个有键位 Mod，{allKeys.Count} 个键位";
    }
    private static string Humanize(string key) => key.Replace("key_key.", "").Replace("key_", "").Replace('.', ' ').Replace('_', ' ');
    private void PickPackLibrary_Click(object sender, RoutedEventArgs e) { var p = PickFolder("选择固定资源包存放目录"); if (p != null) { settings.PackLibrary = p; SaveAndReload(); } }
    private void PickShaderLibrary_Click(object sender, RoutedEventArgs e) { var p = PickFolder("选择固定光影包存放目录"); if (p != null) { settings.ShaderLibrary = p; SaveAndReload(); } }
    private void SaveAndReload() { SettingsStore.Save(settings); ReloadLibraries(); RefreshSummary(); }
    private void ReloadLibraries()
    {
        var profile = settings.PackProfiles[settings.ActivePackProfile]; LoadPacks(settings.PackLibrary, packs, false); var order = profile.PackOrder.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i, StringComparer.OrdinalIgnoreCase); var sorted = packs.OrderBy(p => order.TryGetValue(p.Name, out var i) ? i : int.MaxValue).ThenBy(p => p.Name).ToList(); packs.Clear(); foreach (var p in sorted) { p.Enabled = profile.EnabledPacks.Contains(p.Name, StringComparer.OrdinalIgnoreCase); packs.Add(p); }
        LoadPacks(settings.ShaderLibrary, shaders, true); ShaderList.SelectedItem = shaders.FirstOrDefault(s => s.Name.Equals(draftSelectedShader, StringComparison.OrdinalIgnoreCase)); BeginOnlineShaderPreviewRefresh(); PackPathText.Text = string.IsNullOrWhiteSpace(settings.PackLibrary) ? "尚未设置" : settings.PackLibrary;
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
        if (ShaderList.SelectedItem is not PackItem p) return; draftSelectedShader = p.Name; SelectedShaderName.Text = p.Name; SetImage(ShaderPreview, p.PreviewPath); ShaderPreviewEmpty.Visibility = string.IsNullOrWhiteSpace(p.PreviewPath) ? Visibility.Visible : Visibility.Collapsed;
    }
    private void PickShaderPreview_Click(object sender, RoutedEventArgs e)
    {
        if (ShaderList.SelectedItem is not PackItem p) { AppDialog.Show(this, "请先选择一个光影包。"); return; }
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
        if (e.Data.GetData(typeof(PackItem)) is not PackItem source || sender is not ListBox destination) return; source.Enabled = destination == enabledPackList; var element = destination.InputHitTest(e.GetPosition(destination)) as DependencyObject; while (element != null && element is not ListBoxItem) element = VisualTreeHelper.GetParent(element); var target = (element as ListBoxItem)?.DataContext as PackItem; if (target != null && source != target) { var index = packs.IndexOf(target); packs.Remove(source); packs.Insert(Math.Max(0, index), source); } RefreshPackColumns(); StatusText.Text = "资源包配置已修改（尚未保存）";
    }
    private void SavePackProfile() { var profile = settings.PackProfiles[settings.ActivePackProfile]; profile.PackOrder = packs.Select(p => p.Name).ToList(); profile.EnabledPacks = packs.Where(p => p.Enabled).Select(p => p.Name).ToList(); settings.PackOrder = profile.PackOrder.ToList(); settings.EnabledPacks = profile.EnabledPacks.ToList(); settings.SelectedShader = draftSelectedShader; SettingsStore.Save(settings); StatusText.Text = $"已保存资源包配置：{settings.ActivePackProfile}"; }

    private void BuildPackManager()
    {
        var template = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(PackCard)) }; PacksPage.Children.Clear(); PacksPage.ColumnDefinitions.Clear(); PacksPage.RowDefinitions.Clear(); PacksPage.RowDefinitions.Add(new() { Height = GridLength.Auto }); PacksPage.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        PacksPage.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); PacksPage.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var profileBar = new DockPanel { Margin = new Thickness(0, 0, 0, 14), LastChildFill = false };
        var profileTools = new StackPanel { Orientation = Orientation.Horizontal }; profileTools.Children.Add(new TextBlock { Text = "资源包配置", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), FontSize = 15 }); packProfileCombo = new ComboBox { Width = 210, ItemsSource = settings.PackProfiles.Keys.Order().ToList(), SelectedItem = settings.ActivePackProfile }; packProfileCombo.SelectionChanged += PackProfile_SelectionChanged; var add = new Button { Content = "新建", Margin = new Thickness(8, 0, 0, 0) }; add.Click += AddPackProfile_Click; var rename = new Button { Content = "重命名", Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)), Margin = new Thickness(6, 0, 0, 0) }; rename.Click += RenamePackProfile_Click; var delete = new Button { Content = "删除", Background = new SolidColorBrush(Color.FromRgb(112, 48, 56)), Margin = new Thickness(6, 0, 0, 0) }; delete.Click += DeletePackProfile_Click; var saveProfile = new Button { Content = "保存配置", Margin = new Thickness(6, 0, 0, 0) }; saveProfile.Click += (_, _) => SavePackProfile(); profileTools.Children.Add(packProfileCombo); profileTools.Children.Add(add); profileTools.Children.Add(rename); profileTools.Children.Add(delete); profileTools.Children.Add(saveProfile);
        var batchTools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(batchTools, Dock.Right); var enable = new Button { Content = "全部启用 →", Padding = new Thickness(13, 7, 13, 7) }; enable.Click += (_, _) => { foreach (var p in packs) p.Enabled = true; RefreshPackColumns(); StatusText.Text = "资源包配置已修改（尚未保存）"; }; var disable = new Button { Content = "全部关闭", Padding = new Thickness(13, 7, 13, 7), Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)), Margin = new Thickness(8, 0, 0, 0) }; disable.Click += (_, _) => { foreach (var p in packs) p.Enabled = false; RefreshPackColumns(); StatusText.Text = "资源包配置已修改（尚未保存）"; }; batchTools.Children.Add(enable); batchTools.Children.Add(disable); profileBar.Children.Add(batchTools); profileBar.Children.Add(profileTools); Grid.SetColumnSpan(profileBar, 2); PacksPage.Children.Add(profileBar);
        disabledPackList = CreatePackColumn("未应用的资源包", template, false, 0); enabledPackList = CreatePackColumn("已应用的资源包（上方优先）", template, true, 1);
    }
    private ListBox CreatePackColumn(string title, DataTemplate template, bool enabled, int column)
    {
        var list = new ListBox { ItemTemplate = template, AllowDrop = true }; ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled); list.PreviewMouseLeftButtonDown += PackList_MouseDown; list.Drop += PackList_Drop;
        var panel = new DockPanel(); var header = new TextBlock { Text = title, FontSize = 18, Foreground = Brushes.White, Margin = new Thickness(4, 0, 0, 12) }; DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header); panel.Children.Add(list);
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is PackItem item) { item.Enabled = !enabled; RefreshPackColumns(); StatusText.Text = "资源包配置已修改（尚未保存）"; } };
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
    private void EnsureKeyProfiles()
    {
        if (settings.KeyProfiles.Count == 0) settings.KeyProfiles["默认键位"] = new KeyProfile { ModBindings = settings.ModKeyProfiles.ToDictionary(x => x.Key, x => new Dictionary<string, string>(x.Value, StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase) };
        foreach (var profile in settings.KeyProfiles.Values) profile.ConflictExcluded ??= new HashSet<string>(StringComparer.Ordinal);
        if (!settings.KeyProfiles.ContainsKey(settings.ActiveKeyProfile)) settings.ActiveKeyProfile = settings.KeyProfiles.Keys.First();
    }
    private void RefreshKeyProfileSelector() { if (keyProfileCombo == null) return; switchingKeyProfile = true; keyProfileCombo.ItemsSource = settings.KeyProfiles.Keys.Order().ToList(); keyProfileCombo.SelectedItem = settings.ActiveKeyProfile; switchingKeyProfile = false; }
    private void KeyProfile_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (switchingKeyProfile || keyProfileCombo?.SelectedItem is not string name || name == settings.ActiveKeyProfile) return;
        if (IsKeyProfileDirty() && AppDialog.Show(this, "当前键位配置有尚未保存的修改。切换后这些草稿会丢失，仍要切换吗？", "未保存的键位草稿", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { switchingKeyProfile = true; keyProfileCombo.SelectedItem = settings.ActiveKeyProfile; switchingKeyProfile = false; return; }
        settings.ActiveKeyProfile = name; ApplyKeyProfileDraft(); StatusText.Text = $"已切换键位配置：{name}";
    }
    private void ApplyKeyProfileDraft()
    {
        settings.KeyProfiles.TryGetValue(settings.ActiveKeyProfile, out var profile); foreach (var key in allKeys) { key.Value = key.OriginalValue; key.Remember = false; key.CountsAsConflict = profile?.ConflictExcluded.Contains(key.OptionKey) != true; if (profile?.ModBindings.TryGetValue(key.ModId, out var map) == true && map.TryGetValue(key.OptionKey, out var value)) { key.Value = value; key.Remember = true; } } BuildKeyboard(); RefreshKeyList();
    }
    private void AddKeyProfile_Click(object? sender, RoutedEventArgs e)
    {
        var n = 1; string suggestion; do suggestion = $"键位 {n++}"; while (settings.KeyProfiles.ContainsKey(suggestion)); var name = PromptForProfileName("新建键位配置", suggestion); if (name == null || settings.KeyProfiles.ContainsKey(name)) return; settings.KeyProfiles[name] = new KeyProfile(); settings.ActiveKeyProfile = name; RefreshKeyProfileSelector(); ApplyKeyProfileDraft(); StatusText.Text = "新键位配置尚未保存";
    }
    private void RenameKeyProfile_Click(object? sender, RoutedEventArgs e)
    {
        var old = settings.ActiveKeyProfile; var name = PromptForProfileName("重命名键位配置", old); if (name == null || name == old || settings.KeyProfiles.ContainsKey(name)) return; var profile = settings.KeyProfiles[old]; settings.KeyProfiles.Remove(old); settings.KeyProfiles[name] = profile; settings.ActiveKeyProfile = name; RefreshKeyProfileSelector(); StatusText.Text = "键位配置名称尚未保存";
    }
    private void DeleteKeyProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (settings.KeyProfiles.Count <= 1) { AppDialog.Show(this, "至少需要保留一套键位配置。", "无法删除", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var name = settings.ActiveKeyProfile; if (AppDialog.Show(this, $"确定删除键位配置“{name}”吗？", "删除键位配置", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        settings.KeyProfiles.Remove(name); settings.ActiveKeyProfile = settings.KeyProfiles.Keys.Order().First(); SettingsStore.Save(settings); RefreshKeyProfileSelector(); ApplyKeyProfileDraft(); StatusText.Text = $"已删除键位配置：{name}";
    }
    private void SaveKeyProfile_Click(object? sender, RoutedEventArgs e)
    {
        var profile = new KeyProfile { ConflictExcluded = allKeys.Where(k => !k.CountsAsConflict).Select(k => k.OptionKey).ToHashSet(StringComparer.Ordinal) }; foreach (var group in allKeys.Where(k => k.Remember).GroupBy(k => k.ModId)) profile.ModBindings[group.Key] = group.ToDictionary(k => k.OptionKey, k => k.Value, StringComparer.Ordinal); settings.KeyProfiles[settings.ActiveKeyProfile] = profile; settings.ModKeyProfiles = profile.ModBindings.ToDictionary(x => x.Key, x => new Dictionary<string, string>(x.Value, StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase); SettingsStore.Save(settings); StatusText.Text = $"已保存键位配置：{settings.ActiveKeyProfile}";
    }
    private void PackProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (switchingProfile || packProfileCombo?.SelectedItem is not string name || name == settings.ActivePackProfile) return;
        if (IsPackProfileDirty() && AppDialog.Show(this, "当前资源包配置有尚未保存的修改。切换后这些草稿会丢失，仍要切换吗？", "未保存的资源包草稿", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { switchingProfile = true; packProfileCombo.SelectedItem = settings.ActivePackProfile; switchingProfile = false; return; }
        settings.ActivePackProfile = name; ReloadLibraries(); StatusText.Text = $"已切换资源包配置：{name}";
    }

    private bool IsPackProfileDirty()
    {
        if (!settings.PackProfiles.TryGetValue(settings.ActivePackProfile, out var profile)) return packs.Count > 0;
        return !profile.PackOrder.SequenceEqual(packs.Select(item => item.Name), StringComparer.OrdinalIgnoreCase)
            || !profile.EnabledPacks.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(packs.Where(item => item.Enabled).Select(item => item.Name));
    }

    private bool IsKeyProfileDirty()
    {
        if (!settings.KeyProfiles.TryGetValue(settings.ActiveKeyProfile, out var profile)) return allKeys.Any(item => item.Remember || !item.CountsAsConflict);
        var excluded = allKeys.Where(item => !item.CountsAsConflict).Select(item => item.OptionKey).ToHashSet(StringComparer.Ordinal);
        if (!excluded.SetEquals(profile.ConflictExcluded ?? [])) return true;
        var current = allKeys.Where(item => item.Remember).ToDictionary(item => item.ModId + "\0" + item.OptionKey, item => item.Value, StringComparer.Ordinal);
        var saved = profile.ModBindings.SelectMany(mod => mod.Value.Select(item => new KeyValuePair<string, string>(mod.Key + "\0" + item.Key, item.Value))).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        return current.Count != saved.Count || current.Any(item => !saved.TryGetValue(item.Key, out var value) || value != item.Value);
    }
    private void AddPackProfile_Click(object sender, RoutedEventArgs e)
    {
        var n = 1; string suggestion; do suggestion = $"配置 {n++}"; while (settings.PackProfiles.ContainsKey(suggestion)); var name = PromptForProfileName("新建资源包配置", suggestion); if (name == null) return; if (settings.PackProfiles.ContainsKey(name)) { AppDialog.Show(this, "已经存在同名配置。", "无法新建"); return; } settings.PackProfiles[name] = new PackProfile(); settings.ActivePackProfile = name; RefreshProfileSelector(); ReloadLibraries(); StatusText.Text = "新资源包配置尚未保存";
    }
    private void RenamePackProfile_Click(object sender, RoutedEventArgs e)
    {
        var old = settings.ActivePackProfile; var name = PromptForProfileName("重命名资源包配置", old); if (name == null || name == old) return; if (settings.PackProfiles.ContainsKey(name)) { AppDialog.Show(this, "已经存在同名配置。", "无法重命名"); return; } var profile = settings.PackProfiles[old]; settings.PackProfiles.Remove(old); settings.PackProfiles[name] = profile; settings.ActivePackProfile = name; RefreshProfileSelector();
    }
    private void DeletePackProfile_Click(object sender, RoutedEventArgs e)
    {
        if (settings.PackProfiles.Count <= 1) { AppDialog.Show(this, "至少需要保留一套资源包配置。", "无法删除", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var name = settings.ActivePackProfile; if (AppDialog.Show(this, $"确定删除资源包配置“{name}”吗？", "删除资源包配置", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        settings.PackProfiles.Remove(name); settings.ActivePackProfile = settings.PackProfiles.Keys.Order().First(); var profile = settings.PackProfiles[settings.ActivePackProfile]; settings.PackOrder = profile.PackOrder.ToList(); settings.EnabledPacks = profile.EnabledPacks.ToList(); SettingsStore.Save(settings); RefreshProfileSelector(); ReloadLibraries(); StatusText.Text = $"已删除资源包配置：{name}";
    }
    private void RefreshProfileSelector() { if (packProfileCombo == null) return; switchingProfile = true; packProfileCombo.ItemsSource = settings.PackProfiles.Keys.Order().ToList(); packProfileCombo.SelectedItem = settings.ActivePackProfile; switchingProfile = false; }

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
        if (KeyList == null) return; IEnumerable<KeyBindingItem> q = allKeys; if (!string.IsNullOrWhiteSpace(selectedPhysicalKey)) q = q.Where(k => NormalizeKeyLabel(k.KeyLabel.Split(':')[0]).Equals(selectedPhysicalKey, StringComparison.OrdinalIgnoreCase)); if (!string.IsNullOrWhiteSpace(KeySearch.Text)) q = q.Where(k => (k.FunctionDisplay + k.OptionKey + k.ModDisplayName + k.ModId + k.KeyLabel).Contains(KeySearch.Text, StringComparison.OrdinalIgnoreCase)); if (ModFilter.SelectedItem is string mod && mod != "全部有键位的 Mod") q = q.Where(k => k.ModDisplayName == mod); if (ConflictOnly.IsChecked == true) { var c = allKeys.Where(k => k.CountsAsConflict && !k.Value.EndsWith("unknown")).GroupBy(k => k.Value).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(); q = q.Where(k => k.CountsAsConflict && c.Contains(k.Value)); } KeyList.ItemsSource = q.ToList();
    }
    private void BuildKeyboard()
    {
        KeyboardPanel.Children.Clear(); var active = allKeys.Where(k => !k.Value.Contains("unknown", StringComparison.OrdinalIgnoreCase)).ToList(); var counts = active.GroupBy(k => NormalizeKeyLabel(k.KeyLabel.Split(':')[0])).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase); var conflictKeys = active.Where(k => k.CountsAsConflict).GroupBy(k => k.Value, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => NormalizeKeyLabel(g.First().KeyLabel.Split(':')[0])).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedLayout = LayoutCombo.SelectedItem as string ?? "108 键全尺寸"; if (!KeyboardLayouts.TryGetValue(selectedLayout, out var rows)) rows = KeyboardLayouts["108 键全尺寸"];
        foreach (var row in rows)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var key in row)
            {
                counts.TryGetValue(key, out var used); var width = key switch { "SPACE" => 245, "BACKSPACE" or "RSHIFT" or "LSHIFT" or "ENTER" => 92, "CAPS" or "TAB" => 72, _ => 48 };
                panel.Children.Add(CreateInputKeyButton(key, key, used, conflictKeys.Contains(key), width, 46));
            }
            KeyboardPanel.Children.Add(panel);
        }
        BuildMouse(counts, conflictKeys);
    }

    private Button CreateInputKeyButton(string physicalKey, string label, int used, bool conflict, double width, double height)
    {
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new TextBlock { Text = label, FontSize = label.Length > 5 ? 8 : 10.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, LineHeight = 11 });
        if (used > 0) content.Children.Add(new TextBlock { Text = used.ToString(), FontSize = 10.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, LineHeight = 12, Margin = new Thickness(0, 1, 0, 0) });
        var selected = selectedPhysicalKey?.Equals(physicalKey, StringComparison.OrdinalIgnoreCase) == true;
        var button = new Button { Content = content, ToolTip = label, Margin = new Thickness(3), Padding = new Thickness(3, 2, 3, 2), Width = width, Height = height, Background = new SolidColorBrush(conflict ? Color.FromRgb(190, 64, 74) : used > 0 ? Color.FromRgb(0, 120, 212) : Color.FromRgb(48, 61, 75)), BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(91, 205, 255) : Colors.Transparent), BorderThickness = selected ? new Thickness(3) : new Thickness(0), Tag = physicalKey };
        button.Click += KeyboardKey_Click; return button;
    }

    private void BuildMouse(IReadOnlyDictionary<string, int> counts, IReadOnlySet<string> conflictKeys)
    {
        MousePanel.Children.Clear();
        var body = new Border { Width = 124, Height = 202, CornerRadius = new CornerRadius(54), Background = new SolidColorBrush(Color.FromArgb(120, 22, 34, 45)), BorderBrush = new SolidColorBrush(Color.FromRgb(76, 101, 124)), BorderThickness = new Thickness(2) }; MousePanel.Children.Add(body); Canvas.SetLeft(body, 16); Canvas.SetTop(body, 7);
        var mouseKeys = new[]
        {
            ("鼠标 LEFT", "左键", 54d, 64d, 20d, 12d), ("鼠标 RIGHT", "右键", 54d, 64d, 82d, 12d),
            ("鼠标 MIDDLE", "中", 20d, 48d, 66d, 15d), ("鼠标 4", "侧键 1", 34d, 48d, 3d, 82d), ("鼠标 5", "侧键 2", 34d, 48d, 3d, 136d)
        };
        foreach (var item in mouseKeys)
        {
            counts.TryGetValue(item.Item1, out var used); var button = CreateInputKeyButton(item.Item1, item.Item2, used, conflictKeys.Contains(item.Item1), item.Item3, item.Item4); button.Margin = new Thickness(0); MousePanel.Children.Add(button); Canvas.SetLeft(button, item.Item5); Canvas.SetTop(button, item.Item6);
        }
        var caption = new TextBlock { Text = "鼠标", Foreground = new SolidColorBrush(Color.FromRgb(166, 185, 203)), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }; MousePanel.Children.Add(caption); Canvas.SetLeft(caption, 68); Canvas.SetTop(caption, 178);
    }

    private void FitKeyboard() { if (KeyboardPanel != null) KeyboardPanel.LayoutTransform = Transform.Identity; }
    private static string NormalizeKeyLabel(string key) => key.Replace("LEFT ", "L").Replace("RIGHT ", "R").Replace("LEFT", "L").Replace("RIGHT", "R").Replace("RETURN", "ENTER").Replace("OEM", "").Trim();
    private void KeyboardKey_Click(object sender, RoutedEventArgs e) { var key = (string)((Button)sender).Tag; selectedPhysicalKey = selectedPhysicalKey?.Equals(key, StringComparison.OrdinalIgnoreCase) == true ? null : key; KeyList.SelectedItem = null; RefreshKeyList(); var matches = string.IsNullOrWhiteSpace(selectedPhysicalKey) ? [] : allKeys.Where(k => NormalizeKeyLabel(k.KeyLabel.Split(':')[0]).Equals(selectedPhysicalKey, StringComparison.OrdinalIgnoreCase)).ToList(); SelectedKeyName.Text = selectedPhysicalKey == null ? "未选择键帽：左侧显示全部键位功能" : $"{selectedPhysicalKey}：左侧显示 {matches.Count} 个占用功能"; SelectedKeyMod.Text = selectedPhysicalKey == null ? "" : "请在左侧选择具体功能进行编辑；再次点击该键帽可取消筛选。"; BuildKeyboard(); }
    private void LayoutCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!IsLoaded || LayoutCombo.SelectedItem is not string layout) return; settings.KeyboardLayout = layout; SettingsStore.Save(settings); BuildKeyboard(); }
    private KeyBindingItem? SelectedKey => KeyList.SelectedItem as KeyBindingItem;
    private void KeyList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (SelectedKey is not { } k) return; SelectedKeyName.Text = k.FunctionDisplay; SelectedKeyMod.Text = k.ModDisplayName + (k.IsLibrary ? "  ·  前置/依赖库" : ""); RememberKey.IsChecked = k.Remember; syncingConflictCheck = true; if (countConflictCheck != null) countConflictCheck.IsChecked = k.CountsAsConflict; syncingConflictCheck = false; CaptureButton.Content = $"当前：{k.KeyLabel}（点击重新绑定）"; }
    private void CaptureButton_Click(object sender, RoutedEventArgs e) { if (SelectedKey == null) return; capturing = true; CaptureButton.Content = "请按主键，支持 Ctrl / Shift / Alt + 主键"; CaptureButton.Focus(); Keyboard.Focus(CaptureButton); }
    private void CaptureButton_KeyDown(object sender, KeyEventArgs e) { if (!capturing || SelectedKey == null) return; e.Handled = true; var key = e.Key == Key.System ? e.SystemKey : e.Key; if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt) { CaptureButton.Content = $"已按下 {key}，请继续按主键"; return; } var minecraftKey = ToMinecraftKey(key); var modifier = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? ":CONTROL" : (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? ":SHIFT" : (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? ":ALT" : ""; SelectedKey.Value = "key.keyboard." + minecraftKey + modifier; capturing = false; CaptureButton.Content = $"当前：{SelectedKey.KeyLabel}（点击重新绑定）"; BuildKeyboard(); RefreshKeyList(); }
    private static string ToMinecraftKey(Key key) => key switch { Key.Oem1 => "semicolon", Key.Oem2 => "slash", Key.Oem3 => "grave.accent", Key.Oem4 => "left.bracket", Key.Oem5 => "backslash", Key.Oem6 => "right.bracket", Key.Oem7 => "apostrophe", Key.OemComma => "comma", Key.OemPeriod => "period", Key.OemMinus => "minus", Key.OemPlus => "equal", Key.Return => "enter", Key.Back => "backspace", Key.Space => "space", Key.LeftShift => "left.shift", Key.RightShift => "right.shift", Key.LeftCtrl => "left.control", Key.RightCtrl => "right.control", Key.LeftAlt => "left.alt", Key.RightAlt => "right.alt", >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(), >= Key.NumPad0 and <= Key.NumPad9 => "keypad." + ((int)key - (int)Key.NumPad0), _ => key.ToString().ToLowerInvariant() };
    private void ClearKey_Click(object sender, RoutedEventArgs e) { if (SelectedKey == null) return; SelectedKey.Value = "key.keyboard.unknown"; BuildKeyboard(); RefreshKeyList(); }
    private void RememberKey_Changed(object sender, RoutedEventArgs e) { if (SelectedKey == null) return; SelectedKey.Remember = RememberKey.IsChecked == true; StatusText.Text = "键位配置已修改（尚未保存）"; }
    private void ConflictParticipation_Changed(object sender, RoutedEventArgs e) { if (syncingConflictCheck || SelectedKey == null) return; SelectedKey.CountsAsConflict = countConflictCheck?.IsChecked == true; StatusText.Text = "冲突检测设置已修改（尚未保存）"; BuildKeyboard(); RefreshKeyList(); RefreshSummary(); }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(instance)) { AppDialog.Show(this, "请先导入一个 MC 游戏文件夹。", "尚未选择实例"); return; }
        try { MinecraftConfig.MirrorLibrary(settings.PackLibrary, Path.Combine(instance, "resourcepacks")); MinecraftConfig.MirrorLibrary(settings.ShaderLibrary, Path.Combine(instance, "shaderpacks")); var changes = allKeys.ToDictionary(k => k.OptionKey, k => k.Value, StringComparer.Ordinal); changes["resourcePacks"] = MinecraftConfig.ResourcePackValue(packs); MinecraftConfig.PatchOptions(instance, changes); ApplyShaderSelection(); StatusText.Text = $"应用完成：{DateTime.Now:HH:mm:ss}（配置草稿未自动保存）"; AppDialog.Show(this, "当前草稿已写入实例；资源包和键位模板未自动保存。", "应用完成", MessageBoxButton.OK, MessageBoxImage.Information); } catch (Exception ex) { AppDialog.Show(this, ex.Message, "应用失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void ApplyShaderSelection() { if (string.IsNullOrWhiteSpace(draftSelectedShader)) return; PatchProperty(Path.Combine(instance, "config", "iris.properties"), "shaderPack", draftSelectedShader); PatchProperty(Path.Combine(instance, "optionsof.txt"), "ofShaderPack", draftSelectedShader); }
    private static void PatchProperty(string file, string key, string value) { if (!File.Exists(file)) return; File.Copy(file, file + ".mcprofilestudio.bak", true); var lines = File.ReadAllLines(file).ToList(); var i = lines.FindIndex(x => x.StartsWith(key + "=", StringComparison.Ordinal)); if (i >= 0) lines[i] = key + "=" + value; else lines.Add(key + "=" + value); File.WriteAllLines(file, lines); }
    private void RefreshSummary() { PackCount.Text = packs.Count.ToString(); ModCount.Text = allKeys.Select(k => k.ModId).Distinct().Count().ToString(); var conflicts = allKeys.Where(k => k.CountsAsConflict && !k.Value.EndsWith("unknown")).GroupBy(k => k.Value).Count(g => g.Count() > 1); KeyCount.Text = $"{allKeys.Count} / {conflicts}"; LibrarySummary.Text = $"资源包：{(settings.PackLibrary.Length == 0 ? "未设置" : settings.PackLibrary)}\n光影包：{(settings.ShaderLibrary.Length == 0 ? "未设置" : settings.ShaderLibrary)}"; }

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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleWindowState(); return; }
        if (WindowState == WindowState.Maximized)
        {
            var mouse = PointToScreen(e.GetPosition(this)); var ratio = e.GetPosition(this).X / Math.Max(1, ActualWidth);
            WindowState = WindowState.Normal; Left = mouse.X - Width * ratio; Top = Math.Max(0, mouse.Y - 24);
        }
        try { DragMove(); } catch { }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) => ToggleWindowState();
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
    private void ToggleWindowState() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private void EnableMica() { try { var hwnd = new WindowInteropHelper(this).Handle; var enabled = 1; DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)); var backdrop = 2; DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int)); } catch { } }
}
