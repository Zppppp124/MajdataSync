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
        _webRoot = Path.Combine(AppContext.BaseDirectory, "Web");
        _controller = new AppController(this);
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
        Loaded += OnLoaded;
        Closed += (_, _) => _controller.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MajdataSync");
            Directory.CreateDirectory(dataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: Path.Combine(dataFolder, "WebView2"));

            await WebView.EnsureCoreWebView2Async(environment);
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
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
