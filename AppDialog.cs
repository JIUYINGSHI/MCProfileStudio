using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace McProfileStudio;

internal static class AppDialog
{
    private static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(25, 25, 25));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(174, 190, 207));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(49, 183, 255));

    public static MessageBoxResult Show(string message, string title = "提示", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
        => Show(Application.Current?.MainWindow, message, title, buttons, image);

    public static MessageBoxResult Show(Window? owner, string message, string title = "提示", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var result = buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : buttons == MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.Cancel;
        var estimatedLines = message.Split('\n').Sum(line => Math.Max(1, (line.Length + 43) / 44));
        var dialog = CreateWindow(owner, title, 620, Math.Clamp(230 + estimatedLines * 20, 300, 680), false);
        var body = new Grid { Margin = new Thickness(24, 20, 24, 22) };
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var messageRow = new Grid();
        messageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        messageRow.ColumnDefinitions.Add(new ColumnDefinition());
        var (symbol, color) = GetSymbol(image);
        messageRow.Children.Add(new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B)),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = symbol, Foreground = new SolidColorBrush(color), FontSize = 21, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        var text = new TextBlock { Text = message, Foreground = Brushes.White, FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, LineHeight = 22 };
        var messageScroll = new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 500 };
        Grid.SetColumn(messageScroll, 1); messageRow.Children.Add(messageScroll); body.Children.Add(messageRow);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        void AddButton(string label, MessageBoxResult value, bool primary = false)
        {
            var button = new Button { Content = label, MinWidth = 94, Margin = new Thickness(8, 0, 0, 0), Background = new SolidColorBrush(primary ? Color.FromRgb(74, 74, 74) : Color.FromRgb(52, 52, 52)) };
            button.Click += (_, _) => { result = value; dialog.DialogResult = true; };
            actions.Children.Add(button);
        }
        switch (buttons)
        {
            case MessageBoxButton.OKCancel: AddButton("取消", MessageBoxResult.Cancel); AddButton("确定", MessageBoxResult.OK, true); break;
            case MessageBoxButton.YesNo: AddButton("否", MessageBoxResult.No); AddButton("是", MessageBoxResult.Yes, true); break;
            case MessageBoxButton.YesNoCancel: AddButton("取消", MessageBoxResult.Cancel); AddButton("否", MessageBoxResult.No); AddButton("是", MessageBoxResult.Yes, true); break;
            default: AddButton("确定", MessageBoxResult.OK, true); break;
        }
        Grid.SetRow(actions, 1); body.Children.Add(actions);
        SetBody(dialog, body);
        dialog.ShowDialog();
        return result;
    }

    public static Window CreateWindow(Window? owner, string title, double width, double height, bool resizable = true)
    {
        return new Window
        {
            Owner = owner,
            Title = title,
            Width = width,
            Height = height,
            MinWidth = Math.Min(width, 520),
            MinHeight = Math.Min(height, 260),
            ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.NoResize,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            FontFamily = (FontFamily)Application.Current.Resources["AppFont"]
        };
    }

    public static void SetBody(Window dialog, UIElement body)
    {
        var root = new Border
        {
            Background = Panel,
            BorderBrush = new SolidColorBrush(Color.FromArgb(110, 92, 92, 92)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        layout.RowDefinitions.Add(new RowDefinition());
        var header = new Grid { Background = new SolidColorBrush(Color.FromArgb(220, 34, 34, 34)) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) dialog.DragMove(); };
        header.Children.Add(new TextBlock { Text = dialog.Title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Content = "×", Width = 48, Height = 48, Padding = new Thickness(0), FontSize = 20, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        close.Click += (_, _) => dialog.Close(); Grid.SetColumn(close, 1); header.Children.Add(close); layout.Children.Add(header);
        var content = new Border { Child = body, Background = Panel }; Grid.SetRow(content, 1); layout.Children.Add(content); root.Child = layout; dialog.Content = root;
    }

    private static (string Symbol, Color Color) GetSymbol(MessageBoxImage image) => image switch
    {
        MessageBoxImage.Error => ("×", Color.FromRgb(255, 105, 120)),
        MessageBoxImage.Warning => ("!", Color.FromRgb(255, 190, 90)),
        MessageBoxImage.Question => ("?", Color.FromRgb(49, 183, 255)),
        _ => ("i", Color.FromRgb(49, 183, 255))
    };
}
