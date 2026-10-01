using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace McProfileStudio;

public partial class MainWindow : Window
{
    private readonly AppSettings settings = SettingsStore.Load();
    private readonly ObservableCollection<PackItem> packs = [];
    private readonly ObservableCollection<PackItem> shaders = [];
    private readonly ObservableCollection<KeyBindingItem> allKeys = [];
    private readonly ObservableCollection<PackItem> disabledPackView = [];
    private readonly ObservableCollection<PackItem> enabledPackView = [];
    private string instance = "";
    private Dictionary<string, ModInfo> mods = new(StringComparer.OrdinalIgnoreCase);
    private Point dragStart; private bool capturing;
    private ListBox? disabledPackList, enabledPackList;
    private readonly DispatcherTimer packDragScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(45) };
    private ListBox? packDragScrollList;
    private int packDragScrollDirection;
    private bool packDragInProgress;
    private IntPtr packMouseHook;
    private readonly LowLevelMouseProc packMouseHookProc;
    private Popup? packDragPreview;
    private PackItem? draggedPack;
    private int draggedPackOriginalIndex;
    private bool draggedPackOriginalEnabled;
    private ListBox? lastPreviewList;
    private PackItem? lastPreviewTarget;
    private bool lastPreviewAfter;
    private Point packDragPosition;
    private ComboBox? packProfileCombo;
    private bool switchingProfile;
    private ComboBox? keyProfileCombo;
    private Button? keyProfileManagerButton;
    private Border? keyProfileManagerPanel;
    private bool switchingKeyProfile;
    private CheckBox? countConflictCheck;
    private bool syncingConflictCheck;
    private string? selectedPhysicalKey;
    private string draftSelectedShader = "";
    private CancellationTokenSource? shaderPreviewRefresh;
    private CancellationTokenSource? toastCancellation;
    private Border? toastHost;
    private TextBlock? toastIcon;
    private TextBlock? toastTitle;
    private TextBlock? toastMessage;
    private int activePageIndex;
    private bool deferredUiInitialized;

    private sealed record KeyboardKeySpec(string Key, double X, double Y, double Width = 1, double Height = 1);
    private sealed record KeyboardLayoutSpec(double Width, double Height, IReadOnlyList<KeyboardKeySpec> Keys);
    private static readonly Dictionary<string, KeyboardLayoutSpec> KeyboardLayouts = CreateKeyboardLayouts();

    public MainWindow()
    {
        packMouseHookProc = PackMouseHookCallback;
        EnsurePackProfiles(); EnsureKeyProfiles(); EnsureFavoriteModProfiles(); draftSelectedShader = settings.SelectedShader; InitializeComponent(); InitializeToastLayer(); packDragScrollTimer.Tick += PackDragScrollTimer_Tick; ApplyMinecraftNavIcons(); PackList.ItemsSource = packs; ShaderList.ItemsSource = shaders; BuildPackManager(); BuildDraftControls(); BuildDataToolsCard(); EnableHomeScrolling();
        LayoutCombo.ItemsSource = KeyboardLayouts.Keys; LayoutCombo.SelectedItem = KeyboardLayouts.ContainsKey(settings.KeyboardLayout) ? settings.KeyboardLayout : "108 键全尺寸";
        SourceInitialized += (_, _) => EnableMica(); Loaded += (_, _) => { RefreshSummary(); FitKeyboard(); }; ContentRendered += InitializeDeferredUi; SizeChanged += (_, _) => FitKeyboard();
    }

    private async void InitializeDeferredUi(object? sender, EventArgs e)
    {
        if (deferredUiInitialized) return;
        deferredUiInitialized = true;
        ContentRendered -= InitializeDeferredUi;
        await Dispatcher.Yield(DispatcherPriority.Background);
        BuildModConfigPage();
        await Dispatcher.Yield(DispatcherPriority.Background);
        BuildFavoriteModsPage();
        await Dispatcher.Yield(DispatcherPriority.Background);
        await ReloadLibrariesAsync();
        RefreshSummary(); FitKeyboard();
    }

    private void ApplyMinecraftNavIcons()
    {
        var labels = new Dictionary<string, string> { ["0"] = "概览", ["1"] = "资源包", ["2"] = "光影包", ["3"] = "键位配置" };
        foreach (var item in labels) if (FindNavButton(item.Key) is { } button) button.Content = CreateMinecraftNavContent(item.Key, item.Value);
    }

    private static FrameworkElement CreateMinecraftNavContent(string tag, string label)
    {
        var icons = new Dictionary<string, string> { ["0"] = "⌂", ["1"] = "▤", ["2"] = "◉", ["3"] = "⌨", ["4"] = "⚙", ["5"] = "☆" };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = icons.GetValueOrDefault(tag, "•"), FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 16, Width = 22, Foreground = new SolidColorBrush(Color.FromRgb(198, 213, 227)), TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontWeight = FontWeights.SemiBold });
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
        if (FindLogicalParent<StackPanel>(FindButtonByContent(ShadersPage, "更改光影包目录")) is { } shaderTools)
        {
            var import = new Button { Content = "从 options.txt 导入光影选择", Margin = new Thickness(0, 9, 0, 0), Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)) }; import.Click += ImportShaderOptions_Click; shaderTools.Children.Add(import);
            if (FindParent<Border>(shaderTools) is { } shaderCard && shaderCard.Child == shaderTools) { shaderCard.Child = null; shaderCard.Child = new ScrollViewer { Content = shaderTools, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; }
        }
        if (FindLogicalParent<StackPanel>(CaptureButton) is not { } editor) return;
        if (KeysPage.Children.OfType<Grid>().FirstOrDefault(grid => Grid.GetRow(grid) == 2) is { ColumnDefinitions.Count: >= 2 } lowerGrid) { lowerGrid.ColumnDefinitions[0].Width = new GridLength(1.35, GridUnitType.Star); lowerGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star); }
        KeyOccupancyList.Visibility = Visibility.Collapsed;
        var box = new Border { Background = new SolidColorBrush(Color.FromArgb(56, 26, 42, 56)), CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Margin = new Thickness(0, 8, 0, 10), Visibility = Visibility.Collapsed };
        var content = new StackPanel(); content.Children.Add(new TextBlock { Text = "键位配置草稿", Foreground = new SolidColorBrush(Color.FromRgb(175, 199, 221)), Margin = new Thickness(0, 0, 0, 7) });
        keyProfileCombo = new ComboBox(); keyProfileCombo.SelectionChanged += KeyProfile_SelectionChanged; content.Children.Add(keyProfileCombo);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; var importKeys = new Button { Content = "导入 options.txt", Padding = new Thickness(12, 6, 12, 6), Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)) }; importKeys.Click += ImportKeyOptions_Click; var add = new Button { Content = "新建", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(6, 0, 0, 0) }; add.Click += AddKeyProfile_Click; var rename = new Button { Content = "重命名", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(6, 0, 0, 0), Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)) }; rename.Click += RenameKeyProfile_Click; var delete = new Button { Content = "删除", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(6, 0, 0, 0), Background = new SolidColorBrush(Color.FromRgb(112, 48, 56)) }; delete.Click += DeleteKeyProfile_Click; var save = new Button { Content = "保存配置", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(6, 0, 0, 0) }; save.Click += SaveKeyProfile_Click; buttons.Children.Add(importKeys); buttons.Children.Add(add); buttons.Children.Add(rename); buttons.Children.Add(delete); buttons.Children.Add(save); content.Children.Add(buttons); box.Child = content; keyProfileManagerPanel = box;
        keyProfileManagerButton = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Color.FromRgb(47, 47, 47)), Margin = new Thickness(0, 10, 0, 0) }; keyProfileManagerButton.Click += (_, _) => { if (keyProfileManagerPanel == null) return; keyProfileManagerPanel.Visibility = keyProfileManagerPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; keyProfileManagerButton.Content = $"键位方案：{settings.ActiveKeyProfile}  {(keyProfileManagerPanel.Visibility == Visibility.Visible ? "▲" : "▼")}"; };
        var languagePanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        languagePanel.Children.Add(new TextBlock { Text = "键位名称显示", Foreground = new SolidColorBrush(Color.FromRgb(175, 199, 221)), Margin = new Thickness(0, 0, 0, 6) });
        var languageCombo = new ComboBox { ItemsSource = new[] { "中文", "English", "中英双语" }, SelectedItem = settings.KeyDisplayLanguage };
        languageCombo.SelectionChanged += (_, _) => { if (languageCombo.SelectedItem is not string language) return; settings.KeyDisplayLanguage = language; foreach (var key in allKeys) key.DisplayLanguage = language; SettingsStore.Save(settings); RefreshKeyList(); };
        languagePanel.Children.Add(languageCombo);
        editor.Children.Insert(3, languagePanel); editor.Children.Insert(4, keyProfileManagerButton); editor.Children.Insert(5, box); RefreshKeyProfileSelector();
        BuildKeyListTemplate();
        countConflictCheck = new CheckBox { Content = "计入冲突检测", IsChecked = true, Margin = new Thickness(0, 8, 0, 8), ToolTip = "关闭后，这个功能即使与其他功能使用同一按键，也不会被标为冲突。" };
        countConflictCheck.Checked += ConflictParticipation_Changed; countConflictCheck.Unchecked += ConflictParticipation_Changed;
        if (FindButtonByContent(editor, "清除绑定") is { } clearButton) editor.Children.Insert(editor.Children.IndexOf(clearButton) + 1, countConflictCheck); else editor.Children.Add(countConflictCheck);
    }

    private void BuildKeyListTemplate()
    {
        var template = new DataTemplate(typeof(KeyBindingItem));
        var grid = new FrameworkElementFactory(typeof(DockPanel));
        var function = new FrameworkElementFactory(typeof(TextBlock));
        function.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(KeyBindingItem.FunctionDisplay)));
        function.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(247, 250, 255)));
        function.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        function.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var mod = new FrameworkElementFactory(typeof(TextBlock));
        mod.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(KeyBindingItem.ModDisplayName)));
        mod.SetValue(DockPanel.DockProperty, Dock.Right); mod.SetValue(FrameworkElement.WidthProperty, 245d); mod.SetValue(FrameworkElement.MarginProperty, new Thickness(14, 0, 14, 0)); mod.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(99, 201, 255))); mod.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        var key = new FrameworkElementFactory(typeof(TextBlock));
        key.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(KeyBindingItem.KeyLabel)));
        key.SetValue(DockPanel.DockProperty, Dock.Right); key.SetValue(FrameworkElement.WidthProperty, 110d); key.SetValue(TextBlock.ForegroundProperty, Brushes.White); key.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right); key.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.AppendChild(key); grid.AppendChild(mod); grid.AppendChild(function);
        template.VisualTree = grid; KeyList.ItemTemplate = template;
    }

    private void InitializeToastLayer()
    {
        if (Content is not Grid root) return;
        var body = new StackPanel();
        toastIcon = new TextBlock { Text = "✓", FontSize = 19, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        toastTitle = new TextBlock { Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold };
        toastMessage = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(205, 220, 234)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        body.Children.Add(toastTitle); body.Children.Add(toastMessage);
        var row = new DockPanel(); row.Children.Add(toastIcon); row.Children.Add(body);
        toastHost = new Border { Child = row, Background = new SolidColorBrush(Color.FromArgb(246, 24, 24, 24)), BorderBrush = new SolidColorBrush(Color.FromRgb(40, 169, 235)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(16, 12, 20, 12), MinWidth = 380, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20, 66, 20, 0), Visibility = Visibility.Collapsed, Opacity = 0, RenderTransform = new TranslateTransform(0, -12) };
        Panel.SetZIndex(toastHost, 10000); root.Children.Add(toastHost);
    }

    private async void ShowToast(string title, string message, bool success, int milliseconds = 3600)
    {
        if (toastHost == null || toastTitle == null || toastMessage == null || toastIcon == null) return;
        toastCancellation?.Cancel(); toastCancellation?.Dispose(); toastCancellation = new CancellationTokenSource(); var token = toastCancellation.Token;
        toastTitle.Text = title; toastMessage.Text = message; toastIcon.Text = success ? "✓" : "!";
        var accent = new SolidColorBrush(success ? Color.FromRgb(48, 203, 125) : Color.FromRgb(255, 103, 112)); toastIcon.Foreground = accent; toastHost.BorderBrush = accent;
        toastHost.Visibility = Visibility.Visible;
        toastHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        if (toastHost.RenderTransform is TranslateTransform transform) transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-12, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        try { await Task.Delay(milliseconds, token); } catch (OperationCanceledException) { return; }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)); fade.Completed += (_, _) => { if (!token.IsCancellationRequested) toastHost.Visibility = Visibility.Collapsed; }; toastHost.BeginAnimation(OpacityProperty, fade);
    }

    private static Button? FindButtonByContent(DependencyObject root, string text)
    {
        if (root is Button b && Equals(b.Content, text)) return b; foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { var found = FindButtonByContent(child, text); if (found != null) return found; } return null;
    }
    private static T? FindLogicalParent<T>(DependencyObject? child) where T : DependencyObject { while (child != null) { child = LogicalTreeHelper.GetParent(child); if (child is T found) return found; } return null; }

    private void Navigate(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return; var index = int.Parse((string)((RadioButton)sender).Tag);
        activePageIndex = index;
        ApplyInstanceButton.Visibility = index is >= 1 and <= 3 ? Visibility.Visible : Visibility.Collapsed;
        HomePage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed; PacksPage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed; ShadersPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed; KeysPage.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed; if (modConfigsPage != null) modConfigsPage.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed; if (favoriteModsPage != null) favoriteModsPage.Visibility = index == 5 ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = new[] { "概览", "资源包排序", "光影包覆盖", "可视化键位", "Mod 配置", "Mod 收藏与下载" }[index];
        PageSubtitle.Text = new[] { "选择整合包实例，然后统一应用资源与键位。", "拖动调整优先级；不会阻止版本不兼容的资源包。", "固定库存，一键覆盖到任意新整合包。", "从键盘占用定位冲突，再按 Mod 保存专属键位。", "管理不写入 options.txt 的独立快捷键、开关和列表配置。", "从 Modrinth、CurseForge 与 GitHub 收藏 Mod，并为新实例选择兼容版本。" }[index];
    }

    private string? PickFolder(string title) { var d = new OpenFolderDialog { Title = title, Multiselect = false }; return d.ShowDialog(this) == true ? d.FolderName : null; }
    private void PickInstance_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFolder("选择 Minecraft 游戏文件夹"); if (path == null) return;
        instance = path; InstanceLabel.Text = path; LoadInstance();
        if (!File.Exists(Path.Combine(path, "options.txt"))) ShowToast("实例已导入", "首次应用时将创建 options.txt。", true, 3600);
    }
    private void ImportOptions_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "选择要导入的 options.txt", Filter = "Minecraft options.txt|options.txt|文本文件|*.txt|所有文件|*.*", Multiselect = false }; if (picker.ShowDialog(this) != true) return;
        var choice = ShowImportChoice(); if (choice == null) return; var options = MinecraftConfig.ReadOptionsFile(picker.FileName); var directory = Path.GetDirectoryName(picker.FileName) ?? "";
        if (options.Count == 0) { AppDialog.Show(this, "没有从该文件读取到有效的 Minecraft 设置。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var importedPackCount = 0; var changedKeyCount = 0;
        if (choice.Value.Packs && options.TryGetValue("resourcePacks", out var rawPacks))
        {
            var imported = MinecraftConfig.ParseResourcePacks(rawPacks); importedPackCount = imported.Count; ApplyImportedPackDraft(imported, directory);
        }
        if (choice.Value.Shader)
        {
            var shader = MinecraftConfig.ReadShaderSelection(directory); if (!string.IsNullOrWhiteSpace(shader)) { draftSelectedShader = shader; ShaderList.SelectedItem = shaders.FirstOrDefault(x => x.Name.Equals(shader, StringComparison.OrdinalIgnoreCase)); SelectedShaderName.Text = shader; }
        }
        if (choice.Value.Keys)
        {
            changedKeyCount = CountImportedKeyDifferences(options);
            instance = directory; InstanceLabel.Text = directory; LoadKeysFromOptions(options, directory, true);
            KeySearch.Clear(); ConflictOnly.IsChecked = false; selectedPhysicalKey = null; if (ModFilter.Items.Count > 0) ModFilter.SelectedIndex = 0; RefreshKeyList();
        }
        if (packProfileCombo != null) packProfileCombo.ToolTip = "当前页面正在显示从 options.txt 导入的未保存草稿";
        if (keyProfileCombo != null) keyProfileCombo.ToolTip = "当前页面正在显示从 options.txt 导入的未保存草稿";
        var packResult = choice.Value.Packs ? $"{importedPackCount} 个启用资源包" : "未导入资源包";
        var keyResult = choice.Value.Keys ? $"{changedKeyCount} 个键位与已保存方案不同" : "未导入键位";
        StatusText.Text = $"已导入 {Path.GetFileName(picker.FileName)}：{packResult}，{keyResult}（尚未保存）";
    }

    private string? PickOptionsFile(string purpose)
    {
        var picker = new OpenFileDialog { Title = $"选择要导入{purpose}的 options.txt", Filter = "Minecraft options.txt|options.txt|文本文件|*.txt|所有文件|*.*", Multiselect = false };
        return picker.ShowDialog(this) == true ? picker.FileName : null;
    }

    private void ImportPackOptions_Click(object? sender, RoutedEventArgs e)
    {
        var file = PickOptionsFile("资源包配置"); if (file == null) return; var options = MinecraftConfig.ReadOptionsFile(file);
        if (!options.TryGetValue("resourcePacks", out var raw)) { AppDialog.Show(this, "该文件中没有 resourcePacks 配置。", "无法导入", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var imported = MinecraftConfig.ParseResourcePacks(raw); ApplyImportedPackDraft(imported, Path.GetDirectoryName(file) ?? ""); if (packProfileCombo != null) packProfileCombo.ToolTip = "当前显示从 options.txt 导入的资源包草稿";
        StatusText.Text = $"已从 {Path.GetFileName(file)} 导入 {imported.Count} 个启用资源包（尚未保存）";
    }

    private void ImportShaderOptions_Click(object? sender, RoutedEventArgs e)
    {
        var file = PickOptionsFile("光影选择"); if (file == null) return; var shader = MinecraftConfig.ReadShaderSelection(Path.GetDirectoryName(file) ?? "");
        if (string.IsNullOrWhiteSpace(shader)) { AppDialog.Show(this, "没有在 options.txt 同目录的 Iris 或 OptiFine 配置中找到光影选择。", "无法导入", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        draftSelectedShader = shader; ShaderList.SelectedItem = shaders.FirstOrDefault(item => item.Name.Equals(shader, StringComparison.OrdinalIgnoreCase)); SelectedShaderName.Text = shader;
        StatusText.Text = $"已导入光影选择：{shader}（尚未保存）";
    }

    private void ImportKeyOptions_Click(object? sender, RoutedEventArgs e)
    {
        var file = PickOptionsFile("键位配置"); if (file == null) return; var options = MinecraftConfig.ReadOptionsFile(file);
        if (!options.Keys.Any(key => key.StartsWith("key_", StringComparison.Ordinal))) { AppDialog.Show(this, "该文件中没有键位配置。", "无法导入", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var changed = CountImportedKeyDifferences(options); var directory = Path.GetDirectoryName(file) ?? ""; instance = directory; InstanceLabel.Text = directory; LoadKeysFromOptions(options, directory, true);
        KeySearch.Clear(); ConflictOnly.IsChecked = false; selectedPhysicalKey = null; if (ModFilter.Items.Count > 0) ModFilter.SelectedIndex = 0; RefreshKeyList(); if (keyProfileCombo != null) keyProfileCombo.ToolTip = "当前显示从 options.txt 导入的键位草稿";
        StatusText.Text = $"已从 {Path.GetFileName(file)} 导入键位：{changed} 项与已保存方案不同（尚未保存）";
    }

    private int CountImportedKeyDifferences(IReadOnlyDictionary<string, string> importedOptions)
    {
        if (!settings.KeyProfiles.TryGetValue(settings.ActiveKeyProfile, out var profile)) return importedOptions.Keys.Count(key => key.StartsWith("key_", StringComparison.Ordinal));
        var saved = profile.ModBindings.SelectMany(group => group.Value).GroupBy(item => item.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
        var imported = importedOptions.Where(item => item.Key.StartsWith("key_", StringComparison.Ordinal)).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        return saved.Keys.Concat(imported.Keys).Distinct(StringComparer.Ordinal).Count(key => !saved.TryGetValue(key, out var savedValue) || !imported.TryGetValue(key, out var importedValue) || !savedValue.Equals(importedValue, StringComparison.Ordinal));
    }

    private void ApplyImportedPackDraft(IReadOnlyList<string> imported, string sourceDirectory)
    {
        var available = packs.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var instancePacks = new ObservableCollection<PackItem>(); LoadPacks(Path.Combine(sourceDirectory, "resourcepacks"), instancePacks, false);
        foreach (var item in instancePacks) available.TryAdd(item.Name, item);
        foreach (var name in imported) available.TryAdd(name, new PackItem { Name = name, Description = "options.txt 引用了该资源包，但固定库与实例目录中均未找到文件。" });
        var rank = imported.AsEnumerable().Reverse().Select((name, index) => (name, index)).ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
        var sorted = available.Values.OrderBy(item => rank.TryGetValue(item.Name, out var index) ? index : int.MaxValue).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        packs.Clear(); foreach (var item in sorted) { item.Enabled = rank.ContainsKey(item.Name); packs.Add(item); }
        RefreshPackColumns(); RefreshSummary();
    }
    private (bool Packs, bool Shader, bool Keys)? ShowImportChoice()
    {
        var dialog = AppDialog.CreateWindow(this, "选择导入内容", 430, 358, false);
        var root = new StackPanel { Margin = new Thickness(24) }; root.Children.Add(new TextBlock { Text = "从 options.txt 获取哪些内容？", FontSize = 20, FontWeight = FontWeights.SemiBold }); root.Children.Add(new TextBlock { Text = "导入结果只进入当前草稿，不会自动保存配置。", Foreground = new SolidColorBrush(Color.FromRgb(174, 190, 207)), Margin = new Thickness(0, 6, 0, 14) });
        var packsBox = new CheckBox { Content = "资源包启用状态与排序", IsChecked = true }; var shaderBox = new CheckBox { Content = "光影包选择（同时检查同目录 Iris / OptiFine 配置）", IsChecked = true }; var keysBox = new CheckBox { Content = "全部键位配置", IsChecked = true }; root.Children.Add(packsBox); root.Children.Add(shaderBox); root.Children.Add(keysBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) }; var cancel = new Button { Content = "取消", Width = 90, Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)), Margin = new Thickness(0, 0, 8, 0) }; cancel.Click += (_, _) => dialog.DialogResult = false; var ok = new Button { Content = "导入", Width = 100 }; ok.Click += (_, _) => dialog.DialogResult = true; buttons.Children.Add(cancel); buttons.Children.Add(ok); root.Children.Add(buttons); AppDialog.SetBody(dialog, root);
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
            var item = new KeyBindingItem { OptionKey = pair.Key, DisplayName = string.IsNullOrWhiteSpace(chinese) ? english : chinese, FunctionEnglish = english, FunctionChinese = chinese, DisplayLanguage = settings.KeyDisplayLanguage, ModId = mod.Id, ModDisplayName = mod.DisplayName, IsLibrary = mod.IsLibrary, OriginalValue = pair.Value, Value = pair.Value, Remember = includeInDraft };
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
        ApplyLibrarySnapshot(ScanPacks(settings.PackLibrary, false), ScanPacks(settings.ShaderLibrary, true));
    }

    private async Task ReloadLibrariesAsync()
    {
        var packLibrary = settings.PackLibrary; var shaderLibrary = settings.ShaderLibrary;
        StatusText.Text = "正在后台读取资源包与光影包…";
        var snapshot = await Task.Run(() => (Packs: ScanPacks(packLibrary, false), Shaders: ScanPacks(shaderLibrary, true)));
        ApplyLibrarySnapshot(snapshot.Packs, snapshot.Shaders);
        StatusText.Text = "就绪";
    }

    private void ApplyLibrarySnapshot(List<PackItem> scannedPacks, List<PackItem> scannedShaders)
    {
        var profile = settings.PackProfiles[settings.ActivePackProfile]; var order = profile.PackOrder.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i, StringComparer.OrdinalIgnoreCase); var sorted = scannedPacks.OrderBy(p => order.TryGetValue(p.Name, out var i) ? i : int.MaxValue).ThenBy(p => p.Name).ToList(); packs.Clear(); foreach (var p in sorted) { p.Enabled = profile.EnabledPacks.Contains(p.Name, StringComparer.OrdinalIgnoreCase); packs.Add(p); }
        shaders.Clear(); foreach (var shader in scannedShaders) shaders.Add(shader); ShaderList.SelectedItem = shaders.FirstOrDefault(s => s.Name.Equals(draftSelectedShader, StringComparison.OrdinalIgnoreCase)); BeginOnlineShaderPreviewRefresh(); PackPathText.Text = string.IsNullOrWhiteSpace(settings.PackLibrary) ? "尚未设置" : settings.PackLibrary; RefreshPackColumns();
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
        target.Clear(); foreach (var item in ScanPacks(folder, shader)) target.Add(item);
    }

    private List<PackItem> ScanPacks(string folder, bool shader)
    {
        var result = new List<PackItem>(); if (!Directory.Exists(folder)) return result;
        foreach (var path in Directory.EnumerateFileSystemEntries(folder).Where(p => Directory.Exists(p) || Path.GetExtension(p).Equals(".zip", StringComparison.OrdinalIgnoreCase)))
        { var name = Path.GetFileName(path); var preview = shader && settings.ShaderPreviews.TryGetValue(name, out var saved) ? saved : shader ? FindSidecarPreview(path) : MinecraftConfig.ExtractPackPreview(path); var banner = shader ? "" : PackBannerGenerator.GetOrCreate(path); result.Add(new() { Name = name, FullPath = path, PreviewPath = preview, PreviewImage = shader ? LoadThumbnail(preview, 360) : LoadCroppedPackIcon(preview, 128), BannerImage = LoadThumbnail(banner, 720), IsFontBanner = !string.IsNullOrWhiteSpace(banner), Description = shader ? "光影效果预览" : MinecraftConfig.ReadPackDescription(path) }); }
        return result;
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
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); var list = e.OriginalSource is DependencyObject d ? FindParent<ListBox>(d) : null;
        if (e.LeftButton != MouseButtonState.Pressed || list?.SelectedItem is not PackItem item) return;
        var p = e.GetPosition(null); if (Math.Abs(p.X - dragStart.X) + Math.Abs(p.Y - dragStart.Y) <= 8) return;
        var result = DragDropEffects.None;
        try
        {
            packDragInProgress = true; draggedPack = item; draggedPackOriginalIndex = packs.IndexOf(item); draggedPackOriginalEnabled = item.Enabled;
            lastPreviewList = null; lastPreviewTarget = null; BeginPackDragPreview(list, item); InstallPackMouseHook();
            result = DragDrop.DoDragDrop(list, item, DragDropEffects.Move);
        }
        finally
        {
            if (result == DragDropEffects.None && draggedPack != null)
            {
                packs.Remove(draggedPack); draggedPack.Enabled = draggedPackOriginalEnabled; packs.Insert(Math.Clamp(draggedPackOriginalIndex, 0, packs.Count), draggedPack); RefreshPackColumns();
            }
            EndPackDragPreview(); draggedPack = null; RemovePackMouseHook(); packDragInProgress = false; StopPackDragAutoScroll();
        }
    }
    private void PackList_Drop(object sender, DragEventArgs e)
    {
        StopPackDragAutoScroll(); if (e.Data.GetData(typeof(PackItem)) is not PackItem || sender is not ListBox) return;
        e.Effects = DragDropEffects.Move; e.Handled = true; StatusText.Text = "资源包配置已修改（尚未保存）";
    }
    private void SavePackProfile() { var profile = settings.PackProfiles[settings.ActivePackProfile]; profile.PackOrder = packs.Select(p => p.Name).ToList(); profile.EnabledPacks = packs.Where(p => p.Enabled).Select(p => p.Name).ToList(); settings.PackOrder = profile.PackOrder.ToList(); settings.EnabledPacks = profile.EnabledPacks.ToList(); settings.SelectedShader = draftSelectedShader; SettingsStore.Save(settings); if (packProfileCombo != null) packProfileCombo.ToolTip = null; StatusText.Text = $"已保存资源包配置：{settings.ActivePackProfile}"; }

    private void RefreshPackLibrary()
    {
        var draft = packs.Select((item, index) => (item.Name, item.Enabled, index)).ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var oldNames = draft.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase); var scanned = new ObservableCollection<PackItem>(); LoadPacks(settings.PackLibrary, scanned, false);
        var profile = settings.PackProfiles[settings.ActivePackProfile]; var savedOrder = profile.PackOrder.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        var ordered = scanned.OrderBy(item => draft.TryGetValue(item.Name, out var state) ? 0 : savedOrder.ContainsKey(item.Name) ? 1 : 2)
            .ThenBy(item => draft.TryGetValue(item.Name, out var state) ? state.index : savedOrder.TryGetValue(item.Name, out var index) ? index : int.MaxValue)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        packs.Clear();
        foreach (var item in ordered)
        {
            item.Enabled = draft.TryGetValue(item.Name, out var state) ? state.Enabled : profile.EnabledPacks.Contains(item.Name, StringComparer.OrdinalIgnoreCase);
            packs.Add(item);
        }
        RefreshPackColumns(); RefreshSummary();
        var newNames = packs.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase); var added = newNames.Except(oldNames, StringComparer.OrdinalIgnoreCase).Count(); var removed = oldNames.Except(newNames, StringComparer.OrdinalIgnoreCase).Count();
        StatusText.Text = $"已刷新资源包：共 {packs.Count} 个，新增 {added} 个，移除 {removed} 个（当前草稿未保存）";
        ShowToast("资源包已刷新", $"新增 {added} 个，移除 {removed} 个。", true, 2600);
    }

    private void BuildPackManager()
    {
        var template = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(PackCard)) }; PacksPage.Children.Clear(); PacksPage.ColumnDefinitions.Clear(); PacksPage.RowDefinitions.Clear(); PacksPage.RowDefinitions.Add(new() { Height = GridLength.Auto }); PacksPage.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        PacksPage.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); PacksPage.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var profileBar = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var profileTools = new WrapPanel(); profileTools.Children.Add(new TextBlock { Text = "资源包配置", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), FontSize = 15 }); packProfileCombo = new ComboBox { Width = 210, ItemsSource = settings.PackProfiles.Keys.Order().ToList(), SelectedItem = settings.ActivePackProfile }; packProfileCombo.SelectionChanged += PackProfile_SelectionChanged; var add = new Button { Content = "新建", Margin = new Thickness(8, 0, 0, 0) }; add.Click += AddPackProfile_Click; var rename = new Button { Content = "重命名", Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)), Margin = new Thickness(6, 0, 0, 0) }; rename.Click += RenamePackProfile_Click; var delete = new Button { Content = "删除", Background = new SolidColorBrush(Color.FromRgb(112, 48, 56)), Margin = new Thickness(6, 0, 0, 0) }; delete.Click += DeletePackProfile_Click; var saveProfile = new Button { Content = "保存配置", Margin = new Thickness(6, 0, 0, 0) }; saveProfile.Click += (_, _) => SavePackProfile(); profileTools.Children.Add(packProfileCombo); profileTools.Children.Add(add); profileTools.Children.Add(rename); profileTools.Children.Add(delete); profileTools.Children.Add(saveProfile);
        var batchTools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 9, 0, 0) }; var import = new Button { Content = "导入 options.txt", Padding = new Thickness(13, 7, 13, 7), Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)), Margin = new Thickness(0, 0, 8, 0) }; import.Click += ImportPackOptions_Click; var refresh = new Button { Content = "刷新资源包", Padding = new Thickness(13, 7, 13, 7), Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)), Margin = new Thickness(0, 0, 8, 0), ToolTip = "重新扫描资源包目录，并保留当前草稿的排序与启用状态" }; refresh.Click += (_, _) => RefreshPackLibrary(); var enable = new Button { Content = "全部启用 →", Padding = new Thickness(13, 7, 13, 7) }; enable.Click += (_, _) => { foreach (var p in packs) p.Enabled = true; RefreshPackColumns(); StatusText.Text = "资源包配置已修改（尚未保存）"; }; var disable = new Button { Content = "全部关闭", Padding = new Thickness(13, 7, 13, 7), Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)), Margin = new Thickness(8, 0, 0, 0) }; disable.Click += (_, _) => { foreach (var p in packs) p.Enabled = false; RefreshPackColumns(); StatusText.Text = "资源包配置已修改（尚未保存）"; }; batchTools.Children.Add(import); batchTools.Children.Add(refresh); batchTools.Children.Add(enable); batchTools.Children.Add(disable); profileBar.Children.Add(profileTools); profileBar.Children.Add(batchTools); Grid.SetColumnSpan(profileBar, 2); PacksPage.Children.Add(profileBar);
        disabledPackList = CreatePackColumn("未应用的资源包", template, false, 0); enabledPackList = CreatePackColumn("已应用的资源包（上方优先）", template, true, 1);
    }
    private ListBox CreatePackColumn(string title, DataTemplate template, bool enabled, int column)
    {
        var list = new ListBox { ItemTemplate = template, AllowDrop = true }; ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled); list.PreviewMouseLeftButtonDown += PackList_MouseDown; list.PreviewMouseWheel += PackList_MouseWheel; list.DragOver += PackList_DragOver; list.DragLeave += (_, _) => { if (packDragScrollList == list) StopPackDragAutoScroll(); }; list.Drop += PackList_Drop;
        var panel = new DockPanel(); var header = new TextBlock { Text = title, FontSize = 18, Foreground = Brushes.White, Margin = new Thickness(4, 0, 0, 12) }; DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header); panel.Children.Add(list);
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is PackItem item) { if (!enabled) { packs.Remove(item); item.Enabled = true; packs.Insert(0, item); } else item.Enabled = false; RefreshPackColumns(); enabledPackList?.ScrollIntoView(item); StatusText.Text = "资源包配置已修改（尚未保存）"; } };
        var border = new Border { Background = new SolidColorBrush(Color.FromArgb(190, 27, 27, 27)), BorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(14), Margin = column == 0 ? new Thickness(0, 0, 7, 14) : new Thickness(7, 0, 0, 14), Child = panel }; Grid.SetColumn(border, column); Grid.SetRow(border, 1); PacksPage.Children.Add(border); return list;
    }
    private void RefreshPackColumns()
    {
        if (disabledPackList == null || enabledPackList == null) return;
        SyncPackView(disabledPackView, packs.Where(p => !p.Enabled).ToList()); SyncPackView(enabledPackView, packs.Where(p => p.Enabled).ToList());
        if (disabledPackList.ItemsSource != disabledPackView) disabledPackList.ItemsSource = disabledPackView;
        if (enabledPackList.ItemsSource != enabledPackView) enabledPackList.ItemsSource = enabledPackView;
    }

    private static void SyncPackView(ObservableCollection<PackItem> view, IReadOnlyList<PackItem> desired)
    {
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < view.Count && ReferenceEquals(view[i], desired[i])) continue;
            var existing = view.IndexOf(desired[i]);
            if (existing >= 0) view.Move(existing, i); else view.Insert(i, desired[i]);
        }
        while (view.Count > desired.Count) view.RemoveAt(view.Count - 1);
    }

    private void BeginPackDragPreview(ListBox source, PackItem item)
    {
        var card = new ContentPresenter { Content = item, ContentTemplate = source.ItemTemplate, Width = Math.Max(220, source.ActualWidth - 54), Opacity = 0.88, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.90, 0.90) };
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 7, Opacity = 0.55, Color = Colors.Black };
        packDragPreview = new Popup { AllowsTransparency = true, IsHitTestVisible = false, Placement = PlacementMode.AbsolutePoint, Child = card, IsOpen = true };
    }

    private void EndPackDragPreview()
    {
        if (packDragPreview != null) packDragPreview.IsOpen = false;
        packDragPreview = null; lastPreviewList = null; lastPreviewTarget = null;
    }

    private void UpdatePackDragPreview(ListBox list, Point position)
    {
        if (packDragPreview == null) return;
        var screen = list.PointToScreen(position); packDragPreview.HorizontalOffset = screen.X + 14; packDragPreview.VerticalOffset = screen.Y + 14;
    }

    private Dictionary<PackItem, double> CapturePackPositions()
    {
        var result = new Dictionary<PackItem, double>();
        foreach (var list in new[] { disabledPackList, enabledPackList })
        {
            if (list == null) continue;
            foreach (var item in list.Items.OfType<PackItem>()) if (list.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container) result[item] = container.PointToScreen(new Point()).Y;
        }
        return result;
    }

    private void AnimatePackPositions(IReadOnlyDictionary<PackItem, double> previous)
    {
        foreach (var list in new[] { disabledPackList, enabledPackList })
        {
            if (list == null) continue;
            list.UpdateLayout();
            foreach (var item in list.Items.OfType<PackItem>())
            {
                if (!previous.TryGetValue(item, out var oldY) || list.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container) continue;
                var offset = oldY - container.PointToScreen(new Point()).Y; if (Math.Abs(offset) < 1) continue;
                var transform = new TranslateTransform(0, offset); container.RenderTransform = transform;
                transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }
    }

    private void PreviewPackPosition(ListBox destination, Point position)
    {
        if (draggedPack == null) return;
        PackItem? target = null; var after = false;
        foreach (var item in destination.Items.OfType<PackItem>())
        {
            if (destination.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem candidate) continue;
            var top = candidate.TranslatePoint(new Point(), destination).Y;
            if (position.Y >= top + candidate.ActualHeight) continue;
            target = item; after = position.Y > top + candidate.ActualHeight / 2; break;
        }
        if (ReferenceEquals(target, draggedPack)) return;
        if (lastPreviewList == destination && lastPreviewTarget == target && lastPreviewAfter == after) return;
        lastPreviewList = destination; lastPreviewTarget = target; lastPreviewAfter = after;
        var previous = CapturePackPositions(); draggedPack.Enabled = destination == enabledPackList; packs.Remove(draggedPack);
        int index;
        if (target != null && packs.Contains(target)) index = packs.IndexOf(target) + (after ? 1 : 0);
        else
        {
            var sameColumn = packs.Where(p => p.Enabled == draggedPack.Enabled).ToList(); index = sameColumn.Count == 0 ? packs.Count : packs.IndexOf(sameColumn[^1]) + 1;
        }
        packs.Insert(Math.Clamp(index, 0, packs.Count), draggedPack); RefreshPackColumns(); AnimatePackPositions(previous);
    }

    private void PackList_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ListBox list || FindVisualChild<ScrollViewer>(list) is not { } scroll) return;
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta / 3.0); e.Handled = true;
    }

    private void InstallPackMouseHook()
    {
        if (packMouseHook == IntPtr.Zero) packMouseHook = SetWindowsHookEx(14, packMouseHookProc, IntPtr.Zero, 0);
    }

    private void RemovePackMouseHook()
    {
        if (packMouseHook == IntPtr.Zero) return;
        UnhookWindowsHookEx(packMouseHook); packMouseHook = IntPtr.Zero;
    }

    private IntPtr PackMouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        const int wmMouseWheel = 0x020A;
        if (code >= 0 && wParam == (IntPtr)wmMouseWheel && packDragInProgress)
        {
            var data = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            var delta = unchecked((short)(data.MouseData >> 16));
            if (ScrollPackListAtScreenPoint(new Point(data.Point.X, data.Point.Y), delta)) return (IntPtr)1;
        }
        return CallNextHookEx(packMouseHook, code, wParam, lParam);
    }

    private bool ScrollPackListAtScreenPoint(Point screenPoint, int delta)
    {
        var list = IsScreenPointInside(disabledPackList, screenPoint) ? disabledPackList : IsScreenPointInside(enabledPackList, screenPoint) ? enabledPackList : null;
        if (list == null || FindVisualChild<ScrollViewer>(list) is not { } scroll) return false;
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset - delta / 3.0); list.UpdateLayout();
        packDragPosition = list.PointFromScreen(screenPoint); PreviewPackPosition(list, packDragPosition);
        return true;
    }

    private static bool IsScreenPointInside(FrameworkElement? element, Point screenPoint)
    {
        if (element == null || !element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        var local = element.PointFromScreen(screenPoint);
        return local.X >= 0 && local.Y >= 0 && local.X < element.ActualWidth && local.Y < element.ActualHeight;
    }

    private void PackList_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not ListBox list || !e.Data.GetDataPresent(typeof(PackItem))) { StopPackDragAutoScroll(); return; }
        var position = e.GetPosition(list); packDragPosition = position; UpdatePackDragPreview(list, position); PreviewPackPosition(list, position);
        e.Effects = DragDropEffects.Move; e.Handled = true;
        var y = position.Y;
        const double edge = 58;
        var direction = list.ActualHeight > edge * 2 ? y < edge ? -1 : y > list.ActualHeight - edge ? 1 : 0 : 0;
        if (direction == 0) { StopPackDragAutoScroll(); return; }
        packDragScrollList = list; packDragScrollDirection = direction;
        if (!packDragScrollTimer.IsEnabled) packDragScrollTimer.Start();
    }

    private void PackDragScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (packDragScrollList == null || packDragScrollDirection == 0 || FindVisualChild<ScrollViewer>(packDragScrollList) is not { } scroll) { StopPackDragAutoScroll(); return; }
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + packDragScrollDirection * 18); packDragScrollList.UpdateLayout(); PreviewPackPosition(packDragScrollList, packDragPosition);
    }

    private void StopPackDragAutoScroll() { packDragScrollTimer.Stop(); packDragScrollList = null; packDragScrollDirection = 0; }
    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T found) return found; if (FindVisualChild<T>(child) is { } nested) return nested; } return null; }
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
    private void RefreshKeyProfileSelector() { if (keyProfileCombo == null) return; switchingKeyProfile = true; keyProfileCombo.ItemsSource = settings.KeyProfiles.Keys.Order().ToList(); keyProfileCombo.SelectedItem = settings.ActiveKeyProfile; switchingKeyProfile = false; if (keyProfileManagerButton != null) keyProfileManagerButton.Content = $"键位方案：{settings.ActiveKeyProfile}  ▼"; }
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
        var profile = new KeyProfile { ConflictExcluded = allKeys.Where(k => !k.CountsAsConflict).Select(k => k.OptionKey).ToHashSet(StringComparer.Ordinal) }; foreach (var group in allKeys.Where(k => k.Remember).GroupBy(k => k.ModId)) profile.ModBindings[group.Key] = group.ToDictionary(k => k.OptionKey, k => k.Value, StringComparer.Ordinal); settings.KeyProfiles[settings.ActiveKeyProfile] = profile; settings.ModKeyProfiles = profile.ModBindings.ToDictionary(x => x.Key, x => new Dictionary<string, string>(x.Value, StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase); SettingsStore.Save(settings); if (keyProfileCombo != null) keyProfileCombo.ToolTip = null; StatusText.Text = $"已保存键位配置：{settings.ActiveKeyProfile}";
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
        var root = new Border { Background = new SolidColorBrush(Color.FromArgb(248, 25, 25, 25)), BorderBrush = new SolidColorBrush(Color.FromArgb(100, 92, 92, 92)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(22), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 35, ShadowDepth = 8, Opacity = .45, Color = Colors.Black } };
        var rows = new Grid(); rows.RowDefinitions.Add(new() { Height = GridLength.Auto }); rows.RowDefinitions.Add(new() { Height = GridLength.Auto }); rows.RowDefinitions.Add(new() { Height = GridLength.Auto }); rows.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new DockPanel(); var close = new Button { Content = "×", Width = 34, Height = 30, Padding = new Thickness(0), Background = Brushes.Transparent, FontSize = 20 }; close.Click += (_, _) => dialog.DialogResult = false; DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close); heading.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }); Grid.SetRow(heading, 0); rows.Children.Add(heading);
        var hint = new TextBlock { Text = "请输入配置名称", Foreground = new SolidColorBrush(Color.FromRgb(174, 190, 207)), Margin = new Thickness(0, 14, 0, 7) }; Grid.SetRow(hint, 1); rows.Children.Add(hint);
        var input = new TextBox { Text = initial, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(210, 43, 43, 43)), BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), BorderThickness = new Thickness(1), FontSize = 15 }; Grid.SetRow(input, 2); rows.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; var cancel = new Button { Content = "取消", Width = 90, Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)), Margin = new Thickness(0, 0, 9, 0) }; cancel.Click += (_, _) => dialog.DialogResult = false; var ok = new Button { Content = "确定", Width = 100 }; ok.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = true; }; buttons.Children.Add(cancel); buttons.Children.Add(ok); Grid.SetRow(buttons, 3); rows.Children.Add(buttons); root.Child = rows; dialog.Content = root; dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); }; return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private void KeyFilterChanged(object sender, EventArgs e) { if (IsLoaded) RefreshKeyList(); }
    private void RefreshKeyList()
    {
        if (KeyList == null) return; IEnumerable<KeyBindingItem> q = allKeys; if (!string.IsNullOrWhiteSpace(selectedPhysicalKey)) q = q.Where(k => GetPhysicalKey(k.Value).Equals(selectedPhysicalKey, StringComparison.OrdinalIgnoreCase)); if (!string.IsNullOrWhiteSpace(KeySearch.Text)) q = q.Where(k => (k.FunctionChinese + k.FunctionEnglish + k.OptionKey + k.ModDisplayName + k.ModId + k.KeyLabel).Contains(KeySearch.Text, StringComparison.OrdinalIgnoreCase)); if (ModFilter.SelectedItem is string mod && mod != "全部有键位的 Mod") q = q.Where(k => k.ModDisplayName == mod); if (ConflictOnly.IsChecked == true) { var c = allKeys.Where(k => k.CountsAsConflict && !k.Value.EndsWith("unknown")).GroupBy(k => k.Value).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(); q = q.Where(k => k.CountsAsConflict && c.Contains(k.Value)); } KeyList.ItemsSource = q.ToList();
    }
    private void BuildKeyboard()
    {
        KeyboardPanel.Children.Clear(); var active = allKeys.Where(k => !k.Value.Contains("unknown", StringComparison.OrdinalIgnoreCase)).ToList(); var counts = active.GroupBy(k => GetPhysicalKey(k.Value)).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase); var conflictKeys = active.Where(k => k.CountsAsConflict).GroupBy(k => k.Value, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => GetPhysicalKey(g.First().Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedLayout = LayoutCombo.SelectedItem as string ?? "108 键全尺寸"; if (!KeyboardLayouts.TryGetValue(selectedLayout, out var layout)) layout = KeyboardLayouts["108 键全尺寸"];
        const double pitchX = 54, pitchY = 52;
        var canvas = new Canvas { Width = layout.Width * pitchX, Height = layout.Height * pitchY, ClipToBounds = false };
        foreach (var key in layout.Keys)
        {
            counts.TryGetValue(key.Key, out var used);
            var button = CreateInputKeyButton(key.Key, GetPhysicalKeyLabel(key.Key), used, conflictKeys.Contains(key.Key), key.Width * pitchX - 6, key.Height * pitchY - 6);
            button.Margin = new Thickness(0);
            canvas.Children.Add(button); Canvas.SetLeft(button, key.X * pitchX + 3); Canvas.SetTop(button, key.Y * pitchY + 3);
        }
        KeyboardPanel.Children.Add(canvas);
        BuildMouse(counts, conflictKeys);
    }

    private Button CreateInputKeyButton(string physicalKey, string label, int used, bool conflict, double width, double height)
    {
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new TextBlock { Text = label, FontSize = label.Length > 5 ? 8 : 10.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, LineHeight = 11 });
        if (used > 0) content.Children.Add(new TextBlock { Text = used.ToString(), FontSize = 10.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, LineHeight = 12, Margin = new Thickness(0, 1, 0, 0) });
        var selected = selectedPhysicalKey?.Equals(physicalKey, StringComparison.OrdinalIgnoreCase) == true;
        var button = new Button { Content = content, ToolTip = label, Margin = new Thickness(3), Padding = new Thickness(3, 2, 3, 2), Width = width, Height = height, Background = new SolidColorBrush(conflict ? Color.FromRgb(190, 64, 74) : used > 0 ? Color.FromRgb(76, 76, 76) : Color.FromRgb(48, 48, 48)), BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(91, 205, 255) : Colors.Transparent), BorderThickness = selected ? new Thickness(3) : new Thickness(0), Tag = physicalKey };
        button.Click += KeyboardKey_Click; return button;
    }

    private void BuildMouse(IReadOnlyDictionary<string, int> counts, IReadOnlySet<string> conflictKeys)
    {
        MousePanel.Children.Clear(); MousePanel.Width = 190; MousePanel.Height = 224;
        Brush RegionBrush(string key)
        {
            counts.TryGetValue(key, out var used); return new SolidColorBrush(conflictKeys.Contains(key) ? Color.FromRgb(190, 64, 74) : used > 0 ? Color.FromRgb(76, 76, 76) : Color.FromRgb(44, 44, 44));
        }
        var shell = new System.Windows.Shapes.Path { Data = Geometry.Parse("M95,5 C137,5 163,35 163,80 L163,143 C163,190 137,216 95,219 C53,216 27,190 27,143 L27,80 C27,35 53,5 95,5 Z"), Fill = new SolidColorBrush(Color.FromRgb(28, 28, 28)), Stroke = new SolidColorBrush(Color.FromRgb(92, 92, 92)), StrokeThickness = 2 }; MousePanel.Children.Add(shell);
        void AddRegion(string key, string geometry, string label, double labelX, double labelY)
        {
            var region = new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry), Fill = RegionBrush(key), Stroke = new SolidColorBrush(Color.FromRgb(90, 118, 141)), StrokeThickness = 1, Cursor = Cursors.Hand, Tag = key };
            region.MouseLeftButtonDown += (_, e) => { e.Handled = true; SelectPhysicalKey((string)region.Tag); }; ToolTipService.SetToolTip(region, label); MousePanel.Children.Add(region);
            counts.TryGetValue(key, out var used); var text = new TextBlock { Text = used > 0 ? $"{label}\n{used}" : label, Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, IsHitTestVisible = false }; MousePanel.Children.Add(text); Canvas.SetLeft(text, labelX); Canvas.SetTop(text, labelY);
        }
        AddRegion("鼠标 LEFT", "M30,78 C33,38 56,12 90,11 L90,78 Z", "左键", 48, 34);
        AddRegion("鼠标 RIGHT", "M100,11 C134,12 157,38 160,78 L100,78 Z", "右键", 119, 34);
        var divider = new System.Windows.Shapes.Line { X1 = 95, Y1 = 9, X2 = 95, Y2 = 80, Stroke = new SolidColorBrush(Color.FromRgb(9, 17, 24)), StrokeThickness = 3, IsHitTestVisible = false }; MousePanel.Children.Add(divider);
        var wheelTrack = new Border { Width = 29, Height = 68, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromRgb(22, 22, 22)), BorderBrush = new SolidColorBrush(Color.FromRgb(82, 82, 82)), BorderThickness = new Thickness(1) }; MousePanel.Children.Add(wheelTrack); Canvas.SetLeft(wheelTrack, 80.5); Canvas.SetTop(wheelTrack, 18);
        counts.TryGetValue("鼠标 MIDDLE", out var middleUsed); var wheel = new Border { Width = 17, Height = 39, CornerRadius = new CornerRadius(8), Background = RegionBrush("鼠标 MIDDLE"), Cursor = Cursors.Hand, Tag = "鼠标 MIDDLE", ToolTip = "中键 / 滚轮按下" }; wheel.MouseLeftButtonDown += (_, e) => { e.Handled = true; SelectPhysicalKey("鼠标 MIDDLE"); }; MousePanel.Children.Add(wheel); Canvas.SetLeft(wheel, 86.5); Canvas.SetTop(wheel, 25);
        for (var y = 31; y <= 55; y += 6) { var groove = new System.Windows.Shapes.Line { X1 = 90, X2 = 100, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromArgb(120, 220, 235, 248)), StrokeThickness = 1, IsHitTestVisible = false }; MousePanel.Children.Add(groove); }
        var middleLabel = new Border { MinWidth = 42, Height = 18, CornerRadius = new CornerRadius(9), Background = RegionBrush("鼠标 MIDDLE"), IsHitTestVisible = false, Child = new TextBlock { Text = middleUsed > 0 ? $"中键 {middleUsed}" : "中键", Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }; MousePanel.Children.Add(middleLabel); Canvas.SetLeft(middleLabel, 74); Canvas.SetTop(middleLabel, 70);
        void AddSide(string key, string label, double top)
        {
            counts.TryGetValue(key, out var used); var side = new Border { Width = 45, Height = 27, CornerRadius = new CornerRadius(5, 11, 11, 5), Background = RegionBrush(key), BorderBrush = new SolidColorBrush(Color.FromRgb(90, 118, 141)), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Tag = key, ToolTip = label, Child = new TextBlock { Text = used > 0 ? $"{label}  {used}" : label, Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }; side.MouseLeftButtonDown += (_, e) => { e.Handled = true; SelectPhysicalKey(key); }; MousePanel.Children.Add(side); Canvas.SetLeft(side, 30); Canvas.SetTop(side, top);
        }
        AddSide("鼠标 4", "侧键 4", 103); AddSide("鼠标 5", "侧键 5", 137);
        var palmLine = new System.Windows.Shapes.Path { Data = Geometry.Parse("M58,174 C79,187 111,187 132,174"), Stroke = new SolidColorBrush(Color.FromArgb(75, 112, 151, 181)), StrokeThickness = 1.4, IsHitTestVisible = false }; MousePanel.Children.Add(palmLine);
    }

    private void FitKeyboard() { if (KeyboardPanel != null) KeyboardPanel.LayoutTransform = Transform.Identity; }
    private static string GetPhysicalKey(string value)
    {
        var main = value.Split(':', 2)[0];
        if (main.StartsWith("key.mouse.", StringComparison.OrdinalIgnoreCase)) return "鼠标 " + main[10..].ToUpperInvariant();
        var key = main.StartsWith("key.keyboard.", StringComparison.OrdinalIgnoreCase) ? main[13..].ToLowerInvariant() : main.ToLowerInvariant();
        if (key.StartsWith("keypad.")) return "NUM" + key[7..] switch { "decimal" => ".", "add" => "+", "subtract" => "-", "multiply" => "*", "divide" => "/", "enter" => "ENTER", "equal" => "=", var name => name.ToUpperInvariant() };
        return key switch
        {
            "grave.accent" or "grave" => "`", "apostrophe" or "quote" => "'", "semicolon" => ";", "left.bracket" => "[", "right.bracket" => "]", "backslash" => "\\",
            "comma" => ",", "period" => ".", "slash" => "/", "minus" => "-", "equal" => "=", "left.shift" => "LSHIFT", "right.shift" => "RSHIFT",
            "left.control" => "LCTRL", "right.control" => "RCTRL", "left.alt" => "LALT", "right.alt" => "RALT", "left.super" => "LWIN", "right.super" => "RWIN",
            "return" => "ENTER", "escape" => "ESC", "delete" => "DEL", "insert" => "INS", "caps.lock" => "CAPS", "num.lock" => "NUMLOCK",
            "page.up" => "PGUP", "page.down" => "PGDN", "print.screen" => "PRTSC", "scroll.lock" => "SCRLK", var name => name.ToUpperInvariant()
        };
    }
    private static string GetPhysicalKeyLabel(string key) => key.StartsWith("NUM", StringComparison.Ordinal) && key != "NUMLOCK" ? key[3..] : key;
    private void KeyboardKey_Click(object sender, RoutedEventArgs e) => SelectPhysicalKey((string)((Button)sender).Tag);
    private void SelectPhysicalKey(string key) { selectedPhysicalKey = selectedPhysicalKey?.Equals(key, StringComparison.OrdinalIgnoreCase) == true ? null : key; KeyList.SelectedItem = null; RefreshKeyList(); var matches = string.IsNullOrWhiteSpace(selectedPhysicalKey) ? [] : allKeys.Where(k => GetPhysicalKey(k.Value).Equals(selectedPhysicalKey, StringComparison.OrdinalIgnoreCase)).ToList(); SelectedKeyName.Text = selectedPhysicalKey == null ? "未选择键帽：左侧显示全部键位功能" : $"{GetPhysicalKeyLabel(selectedPhysicalKey)}：左侧显示 {matches.Count} 个占用功能"; SelectedKeyMod.Text = selectedPhysicalKey == null ? "" : "请在左侧选择具体功能进行编辑；再次点击该键帽可取消筛选。"; BuildKeyboard(); }
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
        if (string.IsNullOrWhiteSpace(instance)) { ShowToast("无法覆盖", "请先选择实例。", false, 4200); return; }
        try
        {
            var applied = activePageIndex switch
            {
                1 => ApplyResourcePacksOnly(),
                2 => ApplyShadersOnly(),
                3 => ApplyKeysOnly(),
                _ => ""
            };
            if (applied.Length == 0) return;
            StatusText.Text = $"{applied}已覆盖：{DateTime.Now:HH:mm:ss}（草稿未保存）";
            ShowToast($"{applied}已覆盖", "已写入当前实例。", true, 2800);
        }
        catch (Exception ex)
        {
            StatusText.Text = "覆盖失败";
            ShowToast("覆盖失败", ex.Message, false, 5200);
        }
    }
    private string ApplyResourcePacksOnly() { MinecraftConfig.MirrorLibrary(settings.PackLibrary, Path.Combine(instance, "resourcepacks")); MinecraftConfig.PatchOptions(instance, new Dictionary<string, string>(StringComparer.Ordinal) { ["resourcePacks"] = MinecraftConfig.ResourcePackValue(packs) }); return "资源包"; }
    private string ApplyShadersOnly() { MinecraftConfig.MirrorLibrary(settings.ShaderLibrary, Path.Combine(instance, "shaderpacks")); ApplyShaderSelection(); return "光影"; }
    private string ApplyKeysOnly()
    {
        var changes = allKeys.ToDictionary(k => k.OptionKey, k => k.Value, StringComparer.Ordinal);
        if (changes.Count == 0 && settings.KeyProfiles.TryGetValue(settings.ActiveKeyProfile, out var profile))
            foreach (var binding in profile.ModBindings.SelectMany(pair => pair.Value)) changes[binding.Key] = binding.Value;
        if (changes.Count == 0) throw new InvalidOperationException("当前没有可写入的键位配置。");
        MinecraftConfig.PatchOptions(instance, changes); return "键位";
    }
    private void ApplyShaderSelection() { if (string.IsNullOrWhiteSpace(draftSelectedShader)) return; PatchProperty(Path.Combine(instance, "config", "iris.properties"), "shaderPack", draftSelectedShader); PatchProperty(Path.Combine(instance, "optionsof.txt"), "ofShaderPack", draftSelectedShader); }
    private static void PatchProperty(string file, string key, string value) { if (!File.Exists(file)) return; File.Copy(file, file + ".mcprofilestudio.bak", true); var lines = File.ReadAllLines(file).ToList(); var i = lines.FindIndex(x => x.StartsWith(key + "=", StringComparison.Ordinal)); if (i >= 0) lines[i] = key + "=" + value; else lines.Add(key + "=" + value); File.WriteAllLines(file, lines); }
    private void RefreshSummary() { PackCount.Text = packs.Count.ToString(); ModCount.Text = allKeys.Select(k => k.ModId).Distinct().Count().ToString(); var conflicts = allKeys.Where(k => k.CountsAsConflict && !k.Value.EndsWith("unknown")).GroupBy(k => k.Value).Count(g => g.Count() > 1); KeyCount.Text = $"{allKeys.Count} / {conflicts}"; LibrarySummary.Text = $"资源包：{(settings.PackLibrary.Length == 0 ? "未设置" : settings.PackLibrary)}\n光影包：{(settings.ShaderLibrary.Length == 0 ? "未设置" : settings.ShaderLibrary)}"; }

    private static Dictionary<string, KeyboardLayoutSpec> CreateKeyboardLayouts()
    {
        return new Dictionary<string, KeyboardLayoutSpec>
        {
            ["60 / 61 键"] = CompactLayout(false, false),
            ["65 / 68 键"] = CompactLayout(true, false),
            ["75 / 84 键"] = CompactLayout(true, true),
            ["80 / 87 键 TKL"] = FullKeyboard(false, false),
            ["96 / 98 键"] = CompactNumpadLayout(),
            ["104 键全尺寸"] = FullKeyboard(true, false),
            ["108 键全尺寸"] = FullKeyboard(true, true)
        };
    }

    private static KeyboardLayoutSpec CompactLayout(bool arrows, bool functionRow)
    {
        var keys = new List<KeyboardKeySpec>(); var y = functionRow ? 1 : 0;
        AddAnsiBlock(keys, y);
        if (functionRow)
        {
            AddSequence(keys, 0, 0, "ESC F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12 DEL");
            keys.Add(new("INS", 18.25, 0)); keys.Add(new("PGUP", 18.25, 1)); keys.Add(new("PGDN", 18.25, 2)); keys.Add(new("HOME", 18.25, 3)); keys.Add(new("END", 18.25, 4));
            keys.Add(new("UP", 16.25, 4)); keys.Add(new("LEFT", 15.25, 5)); keys.Add(new("DOWN", 16.25, 5)); keys.Add(new("RIGHT", 17.25, 5));
        }
        else if (arrows)
        {
            keys.Add(new("UP", 16.25, y + 3)); keys.Add(new("LEFT", 15.25, y + 4)); keys.Add(new("DOWN", 16.25, y + 4)); keys.Add(new("RIGHT", 17.25, y + 4));
        }
        return new KeyboardLayoutSpec(functionRow ? 19.25 : arrows ? 18.25 : 15, functionRow ? 6 : 5, keys);
    }

    private static KeyboardLayoutSpec FullKeyboard(bool numpad, bool extra)
    {
        var keys = new List<KeyboardKeySpec>();
        AddAnsiBlock(keys, 1);
        AddSequence(keys, 0, 0, "ESC"); AddSequence(keys, 2, 0, "F1 F2 F3 F4"); AddSequence(keys, 6.5, 0, "F5 F6 F7 F8"); AddSequence(keys, 11, 0, "F9 F10 F11 F12");
        AddSequence(keys, 15.5, 0, "PRTSC SCRLK PAUSE");
        keys.Add(new("INS", 15.5, 1)); keys.Add(new("HOME", 16.5, 1)); keys.Add(new("PGUP", 17.5, 1));
        keys.Add(new("DEL", 15.5, 2)); keys.Add(new("END", 16.5, 2)); keys.Add(new("PGDN", 17.5, 2));
        keys.Add(new("UP", 16.5, 4)); keys.Add(new("LEFT", 15.5, 5)); keys.Add(new("DOWN", 16.5, 5)); keys.Add(new("RIGHT", 17.5, 5));
        if (numpad) AddNumpad(keys, 19, 1);
        if (extra) AddSequence(keys, 19, 0, "M1 M2 M3 M4");
        return new KeyboardLayoutSpec(numpad ? 23 : 18.5, 6, keys);
    }

    private static KeyboardLayoutSpec CompactNumpadLayout()
    {
        var keys = new List<KeyboardKeySpec>(); AddAnsiBlock(keys, 1);
        AddSequence(keys, 0, 0, "ESC F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12 DEL HOME END");
        AddSequence(keys, 19, 0, "PRTSC SCRLK PAUSE INS");
        keys.Add(new("UP", 16.5, 4)); keys.Add(new("LEFT", 15.5, 5)); keys.Add(new("DOWN", 16.5, 5)); keys.Add(new("RIGHT", 17.5, 5));
        AddNumpad(keys, 19, 1);
        return new KeyboardLayoutSpec(23, 6, keys);
    }

    private static void AddAnsiBlock(List<KeyboardKeySpec> keys, double y)
    {
        AddSequence(keys, 0, y, "` 1 2 3 4 5 6 7 8 9 0 - ="); keys.Add(new("BACKSPACE", 13, y, 2));
        keys.Add(new("TAB", 0, y + 1, 1.5)); AddSequence(keys, 1.5, y + 1, "Q W E R T Y U I O P [ ]"); keys.Add(new("\\", 13.5, y + 1, 1.5));
        keys.Add(new("CAPS", 0, y + 2, 1.75)); AddSequence(keys, 1.75, y + 2, "A S D F G H J K L ; '"); keys.Add(new("ENTER", 12.75, y + 2, 2.25));
        keys.Add(new("LSHIFT", 0, y + 3, 2.25)); AddSequence(keys, 2.25, y + 3, "Z X C V B N M , . /"); keys.Add(new("RSHIFT", 12.25, y + 3, 2.75));
        keys.Add(new("LCTRL", 0, y + 4, 1.25)); keys.Add(new("LWIN", 1.25, y + 4, 1.25)); keys.Add(new("LALT", 2.5, y + 4, 1.25)); keys.Add(new("SPACE", 3.75, y + 4, 6.25)); keys.Add(new("RALT", 10, y + 4, 1.25)); keys.Add(new("RWIN", 11.25, y + 4, 1.25)); keys.Add(new("MENU", 12.5, y + 4, 1.25)); keys.Add(new("RCTRL", 13.75, y + 4, 1.25));
    }

    private static void AddNumpad(List<KeyboardKeySpec> keys, double x, double y)
    {
        AddSequence(keys, x, y, "NUMLOCK NUM/ NUM* NUM-");
        AddSequence(keys, x, y + 1, "NUM7 NUM8 NUM9"); keys.Add(new("NUM+", x + 3, y + 1, 1, 2));
        AddSequence(keys, x, y + 2, "NUM4 NUM5 NUM6");
        AddSequence(keys, x, y + 3, "NUM1 NUM2 NUM3"); keys.Add(new("NUMENTER", x + 3, y + 3, 1, 2));
        keys.Add(new("NUM0", x, y + 4, 2)); keys.Add(new("NUM.", x + 2, y + 4));
    }

    private static void AddSequence(List<KeyboardKeySpec> keys, double x, double y, string values)
    {
        foreach (var key in values.Split(' ', StringSplitOptions.RemoveEmptyEntries)) { keys.Add(new(key, x, y)); x += 1; }
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

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MsllHookStruct { public NativePoint Point; public uint MouseData; public uint Flags; public uint Time; public IntPtr ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private void EnableMica() { try { var hwnd = new WindowInteropHelper(this).Handle; var enabled = 1; DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)); var backdrop = 2; DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int)); } catch { } }
}
