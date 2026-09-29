using System.Configuration;
using System.Data;
using System.Windows;

namespace McProfileStudio;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppLog.Initialize();
        DispatcherUnhandledException += (_, args) => AppLog.Error("UI 线程发生未处理异常", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppLog.Error("进程发生未处理异常", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => { AppLog.Error("后台任务发生未观察异常", args.Exception); args.SetObserved(); };
        AppLog.Info($"应用启动，版本 {typeof(App).Assembly.GetName().Version}");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info($"应用退出，代码 {e.ApplicationExitCode}");
        base.OnExit(e);
    }
}

