using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace MajdataSync;

public partial class MainWindow : Window
{
    private readonly AppController _controller;
    private readonly string _webRoot;
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();
        if (IsTemporaryLaunchDirectory(AppContext.BaseDirectory))
        {
            AppLogger.Write("Application launched from a temporary archive directory: " + AppContext.BaseDirectory);
        }
        _webRoot = Path.Combine(AppContext.BaseDirectory, "Web");
        _controller = new AppController(this);
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            AppLogger.Write("Main window closed");
            _controller.Dispose();
        };
    }

    public static bool IsTemporaryLaunchDirectory(string directory)
    {
        var normalized = Path.GetFullPath(directory).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return normalized.StartsWith(
                   temp + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains(
                   Path.Combine("Temp", "Rar$"),
                   StringComparison.OrdinalIgnoreCase);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (IsTemporaryLaunchDirectory(AppContext.BaseDirectory))
            {
                System.Windows.MessageBox.Show(
                    "检测到你正在从压缩包临时目录运行 Majdata Sync。\n\n" +
                    "请先把整个发布包解压到普通文件夹，再运行 MajdataSync.exe；" +
                    "否则 Windows 或解压软件可能在下载途中清理临时文件，导致窗口消失。",
                    "Majdata Sync",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MajdataSync");
            var webViewDataFolder = Environment.GetEnvironmentVariable("MAJDATA_SYNC_DEVTOOLS") == "1"
                ? Path.Combine(Path.GetTempPath(), "MajdataSync", "WebView2Test")
                : Path.Combine(dataFolder, "WebView2");
            Directory.CreateDirectory(dataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: webViewDataFolder,
                options: new CoreWebView2EnvironmentOptions(
                    additionalBrowserArguments: Environment.GetEnvironmentVariable("MAJDATA_SYNC_DEVTOOLS") == "1"
                        ? "--remote-debugging-port=9223"
                        : null));

            await WebView.EnsureCoreWebView2Async(environment);
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            var devTools = Environment.GetEnvironmentVariable("MAJDATA_SYNC_DEVTOOLS") == "1";
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = devTools;
            WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "majdata.sync", _webRoot, CoreWebView2HostResourceAccessKind.DenyCors);
            WebView.Source = new Uri("https://majdata.sync/index.html");
        }
        catch (Exception ex)
        {
            StartupShade.Visibility = Visibility.Visible;
            System.Windows.MessageBox.Show(
                "界面组件启动失败。请安装 Microsoft Edge WebView2 Runtime 后重试。\n\n" + ex.Message,
                "Majdata Sync",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!e.Uri.StartsWith("https://majdata.sync/", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp))
            {
                return;
            }

            var type = typeProp.GetString();
            var payload = root.TryGetProperty("payload", out var p) ? p : default;

            if (type == "ready")
            {
                _ready = true;
                StartupShade.Visibility = Visibility.Collapsed;
                await _controller.InitializeAsync();
                return;
            }

            if (!_ready)
            {
                return;
            }

            await _controller.HandleAsync(type ?? string.Empty, payload);
        }
        catch (Exception ex)
        {
            await _controller.SendAsync("toast", new { kind = "error", message = "操作失败：" + ex.Message });
        }
    }

    public Task SendAsync(string type, object payload)
    {
        var json = JsonSerializer.Serialize(new { type, payload }, JsonOptions.Default);
        if (Dispatcher.CheckAccess())
        {
            WebView.CoreWebView2.PostWebMessageAsJson(json);
            return Task.CompletedTask;
        }

        return Dispatcher.InvokeAsync(() => WebView.CoreWebView2.PostWebMessageAsJson(json)).Task;
    }
}
