using System;
using System.Windows;

namespace MajdataSync;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLogger.Write(
            $"Application starting, version {typeof(App).Assembly.GetName().Version}, " +
            $"path={AppContext.BaseDirectory}");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            AppLogger.Write("Unhandled AppDomain exception", args.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Write("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Write("Unhandled UI exception", args.Exception);
            System.Windows.MessageBox.Show(args.Exception.Message, "Majdata Sync", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        new MainWindow().Show();
    }
}
