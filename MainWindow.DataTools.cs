using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace McProfileStudio;

public partial class MainWindow
{
    private TextBlock? cloudBackupSummary;

    private void EnableHomeScrolling()
    {
        if (HomePage.Children.OfType<StackPanel>().FirstOrDefault() is not { } content) return;
        HomePage.Children.Remove(content);
        HomePage.Children.Add(new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly,
            CanContentScroll = false
        });
    }

    private void BuildDataToolsCard()
    {
        if (HomePage.Children.OfType<StackPanel>().FirstOrDefault() is not { } home) return;
        if (home.Children.OfType<Border>().LastOrDefault()?.Child is not StackPanel host) return;
        host.Children.Add(new Separator { Opacity = .18, Margin = new Thickness(0, 16, 0, 16) });
        var root = new Grid(); root.ColumnDefinitions.Add(new ColumnDefinition()); root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = "数据与诊断", Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold });
        copy.Children.Add(new TextBlock { Text = "导出脱敏诊断日志，或通过 WebDAV 备份全部配置与 Mod 配置方案。", Foreground = new SolidColorBrush(Color.FromRgb(186, 199, 216)), Margin = new Thickness(0, 7, 20, 0), TextWrapping = TextWrapping.Wrap });
        cloudBackupSummary = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(49, 183, 255)), Margin = new Thickness(0, 6, 20, 0), TextWrapping = TextWrapping.Wrap };
        copy.Children.Add(cloudBackupSummary); root.Children.Add(copy);
        var actions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var logs = new Button { Content = "导出软件日志", Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)) }; logs.Click += ExportLogs_Click;
        var cloud = new Button { Content = "WebDAV 云备份" }; cloud.Click += OpenWebDav_Click;
        actions.Children.Add(logs); actions.Children.Add(cloud); Grid.SetColumn(actions, 1); root.Children.Add(actions);
        host.Children.Add(root); RefreshCloudBackupSummary();
    }

    private void RefreshCloudBackupSummary() => cloudBackupSummary!.Text = settings.LastWebDavBackup is { } time ? $"上次上传：{time.ToLocalTime():yyyy-MM-dd HH:mm}" : "尚未上传云端备份";

    private void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog { Title = "导出 MC Profile Studio 诊断包", Filter = "ZIP 压缩包|*.zip", FileName = $"MCProfileStudio-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip", AddExtension = true };
        if (picker.ShowDialog(this) != true) return;
        try { AppLog.Info("用户导出诊断包"); CloudBackupService.ExportDiagnostics(picker.FileName, settings); AppDialog.Show(this, $"诊断包已导出：\n{picker.FileName}\n\n其中的设置、日志和用户目录均已脱敏。", "导出完成"); }
        catch (Exception ex) { AppLog.Error("导出诊断包失败", ex); AppDialog.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void OpenWebDav_Click(object sender, RoutedEventArgs e)
    {
        var dialog = AppDialog.CreateWindow(this, "WebDAV 云端设置备份", 650, 610, false);
        var root = new Grid { Margin = new Thickness(24, 18, 24, 22) };
        root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var form = new StackPanel();
        form.Children.Add(new TextBlock { Text = "WebDAV 云备份", FontSize = 22, FontWeight = FontWeights.SemiBold });
        form.Children.Add(new TextBlock { Text = "备份资源包、键位、Mod 收藏及 Mod 配置方案。API Key、GitHub Token 与 WebDAV 密码不会上传。", Foreground = new SolidColorBrush(Color.FromRgb(174, 190, 207)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 16) });
        TextBox AddText(string label, string value, string hint)
        {
            form.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(186, 199, 216)), Margin = new Thickness(0, 8, 0, 5) });
            var box = new TextBox { Text = value, ToolTip = hint }; form.Children.Add(box); return box;
        }
        var url = AddText("服务器地址", settings.WebDavUrl, "例如 https://dav.example.com/remote.php/dav/files/用户名/");
        var username = AddText("用户名", settings.WebDavUsername, "WebDAV 登录用户名");
        form.Children.Add(new TextBlock { Text = "密码", Foreground = new SolidColorBrush(Color.FromRgb(186, 199, 216)), Margin = new Thickness(0, 8, 0, 5) });
        var password = new PasswordBox { Password = SecretProtector.Unprotect(settings.WebDavPasswordProtected), ToolTip = "仅使用 Windows 当前用户加密后保存在本机" }; form.Children.Add(password);
        var remotePath = AddText("远程目录", string.IsNullOrWhiteSpace(settings.WebDavRemotePath) ? "MCProfileStudio" : settings.WebDavRemotePath, "相对于服务器地址的目录，会自动创建");
        var status = new TextBlock { Text = "使用 HTTPS 可避免 Basic 登录凭据在传输过程中暴露。", Foreground = new SolidColorBrush(Color.FromRgb(49, 183, 255)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) }; form.Children.Add(status); root.Children.Add(form);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var test = new Button { Content = "测试连接", Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)), Margin = new Thickness(0, 0, 8, 0) };
        var upload = new Button { Content = "上传备份", Margin = new Thickness(0, 0, 8, 0) };
        var restore = new Button { Content = "恢复备份", Background = new SolidColorBrush(Color.FromRgb(112, 48, 56)), Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "保存设置", Background = new SolidColorBrush(Color.FromRgb(56, 71, 86)) };
        actions.Children.Add(test); actions.Children.Add(upload); actions.Children.Add(restore); actions.Children.Add(save); Grid.SetRow(actions, 1); root.Children.Add(actions);
        WebDavOptions Current() => new(url.Text.Trim(), username.Text.Trim(), password.Password, remotePath.Text.Trim());
        void SaveLocal()
        {
            var value = Current(); settings.WebDavUrl = value.Url; settings.WebDavUsername = value.Username; settings.WebDavPasswordProtected = SecretProtector.Protect(value.Password); settings.WebDavRemotePath = value.RemotePath; SettingsStore.Save(settings);
        }
        async Task RunAsync(Button button, string busy, Func<Task> operation)
        {
            var buttons = new[] { test, upload, restore, save }; foreach (var item in buttons) item.IsEnabled = false; status.Text = busy;
            try { await operation(); }
            catch (Exception ex) { AppLog.Error(busy + "失败", ex); status.Text = ex.Message; AppDialog.Show(dialog, ex.Message, "WebDAV 操作失败", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { foreach (var item in buttons) item.IsEnabled = true; }
        }
        test.Click += async (_, _) => await RunAsync(test, "正在测试连接…", async () => { await CloudBackupService.TestAsync(Current(), CancellationToken.None); status.Text = "连接成功，服务器支持 WebDAV。"; });
        upload.Click += async (_, _) => await RunAsync(upload, "正在创建并上传备份…", async () => { SaveLocal(); await CloudBackupService.UploadAsync(Current(), settings, CancellationToken.None); settings.LastWebDavBackup = DateTimeOffset.Now; SettingsStore.Save(settings); RefreshCloudBackupSummary(); status.Text = "上传完成，云端备份已更新。"; AppLog.Info("WebDAV 备份上传完成"); });
        restore.Click += async (_, _) =>
        {
            if (AppDialog.Show(dialog, "恢复会覆盖本机已保存的资源包、键位、Mod 收藏和 Mod 配置方案。\n\n恢复前会在本机自动创建安全备份，是否继续？", "确认恢复云端备份", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await RunAsync(restore, "正在下载并恢复备份…", async () => { SaveLocal(); var archive = await CloudBackupService.DownloadAsync(Current(), CancellationToken.None); var safety = CloudBackupService.Restore(archive, settings); AppLog.Info("WebDAV 备份恢复完成"); status.Text = "恢复完成，重启软件后生效。"; AppDialog.Show(dialog, $"云端配置已恢复。请关闭并重新打开软件以载入配置。\n\n恢复前的本机安全备份：\n{safety}", "恢复完成"); });
        };
        save.Click += (_, _) => { try { SaveLocal(); status.Text = "WebDAV 设置已保存在本机，密码已使用当前 Windows 用户加密。"; } catch (Exception ex) { AppDialog.Show(dialog, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error); } };
        AppDialog.SetBody(dialog, root); dialog.ShowDialog();
    }
}
