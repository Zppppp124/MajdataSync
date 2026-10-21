using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace MajdataSync;

internal sealed class AppController : IDisposable
{
    private readonly MainWindow? _window;
    private readonly ChartApi _api = new();
    private readonly GameManager _game;
    private AppSettings _settings = SettingsStore.Load();
    private FolderInfo? _folder;
    private CancellationTokenSource? _operation;
    private bool _operationRunning;
    private List<ChartItem> _charts = [];
    private DateTimeOffset? _lastIndexAt;
    private SyncInventory? _inventory;
    private CatalogSummary? _catalogSummary;
    private List<GameHashEntry> _pendingGameUpdates = [];
    private bool _catalogRefreshRunning;

    public AppController(MainWindow? window)
    {
        _window = window;
        _game = new GameManager(_api);
    }

    public async Task InitializeAsync()
    {
        _folder = TryRestoreFolder() ?? FolderDetector.DetectNearby(AppContext.BaseDirectory);
        if (_folder is not null)
        {
            _settings.LastRootPath = _folder.RootPath;
            SettingsStore.Save(_settings);
        }

        await SendAsync("folder", FolderPayload(_folder));
        await SendSettingsAsync(firstRun: !_settings.FirstRunCompleted);
        await SendStateAsync();
        await LoadCachedCatalogAsync();
        await SendAsync("networkNode", new
        {
            node = "",
            mirrors = Array.Empty<string>(),
            message = "自动选择低延迟节点；下载时会测速并切换。"
        });

        if (_folder is null)
        {
            return;
        }

        await RefreshDashboardAsync();
        _ = MeasureNetworkInBackgroundAsync();
        if (_settings.AutoCheck)
        {
            _ = InitializeCatalogInBackgroundAsync();
        }
    }

    public async Task HandleAsync(string type, JsonElement payload)
    {
        switch (type)
        {
            case "chooseFolder":
                await ChooseFolderAsync();
                break;
            case "detectFolder":
                await DetectFolderAsync();
                break;
            case "openFolder":
                await OpenFolderAsync();
                break;
            case "refresh":
                await RefreshDashboardAsync();
                break;
            case "enableAutoCheck":
                await EnableAutoCheckAsync();
                break;
            case "loadIndex":
                _ = ReloadIndexInBackgroundAsync();
                break;
            case "queryCatalog":
                await SendCatalogPageAsync(payload, CancellationToken.None);
                break;
            case "sync":
                await RunSyncAsync(payload);
                break;
            case "cancel":
                _operation?.Cancel();
                break;
            case "importZip":
                await ImportZipAsync(payload);
                break;
            case "selectZip":
                await SelectZipAsync();
                break;
            case "saveSettings":
                await SaveSettingsAsync(payload);
                break;
            case "testNetwork":
                await TestNetworkAsync();
                break;
            case "testOnlineSources":
                await TestOnlineSourcesAsync();
                break;
            case "openLogs":
                AppLogger.OpenLogFolder();
                break;
            case "importCache":
                await ImportOnlineCacheAsync(payload);
                break;
            case "syncCacheAll":
                await SyncAllToCacheAsync();
                break;
            case "gameStatus":
                await SendGameStatusAsync();
                break;
            case "chooseGameFolder":
                await ChooseGameFolderAsync();
                break;
            case "installGame":
                await InstallGameAsync(payload);
                break;
            case "checkGameUpdate":
                await CheckGameUpdateAsync();
                break;
            case "updateGame":
                await UpdateGameAsync();
                break;
            case "launchGame":
                LaunchGame(payload);
                break;
            case "openGameFolder":
                OpenGameFolder();
                break;
            case "loadSkins":
                await LoadSkinsAsync();
                break;
            case "installSkin":
                await InstallSkinAsync(payload);
                break;
            case "applySkin":
                await ApplySkinAsync(payload);
                break;
            case "loadGameSettings":
                await LoadGameSettingsAsync();
                break;
            case "saveGameSettings":
                await SaveGameSettingsAsync(payload);
                break;
            case "saveGameSource":
                await SaveGameSourceAsync(payload);
                break;
            case "switchGameChannel":
                await SwitchGameChannelAsync(payload);
                break;
            case "openChart":
                OpenChart(payload);
                break;
            case "openSite":
                OpenUrl(payload);
                break;
            case "completeTour":
                _settings.FirstRunCompleted = true;
                SettingsStore.Save(_settings);
                await SendAsync("toast", new
                {
                    kind = "info",
                    message = "设置已保存。"
                });
                break;
            case "startIndex":
                _settings.FirstRunCompleted = true;
                SettingsStore.Save(_settings);
                await LoadIndexAsync(useCache: true);
                break;
            case "skipIndex":
                _settings.FirstRunCompleted = true;
                SettingsStore.Save(_settings);
                break;
        }
    }

    private FolderInfo? TryRestoreFolder()
    {
        return FolderDetector.DetectFrom(_settings.LastRootPath) ??
               FolderDetector.DetectFrom(_settings.LastExplicitOriginalPath);
    }

    private async Task ChooseFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择 Majdata 的 Original 文件夹，或游戏根目录",
            Multiselect = false,
            InitialDirectory = _folder?.RootPath ??
                               Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialog.ShowDialog(_window) != true)
        {
            return;
        }

        var folder = FolderDetector.DetectFrom(dialog.FolderName);
        if (folder is null)
        {
            await SendAsync("toast", new
            {
                kind = "error",
                message = "没有识别到 Majdata 目录。请选择 Original 文件夹，或包含 MaiCharts\\Original 的游戏根目录。"
            });
            return;
        }

        await SetFolderAsync(folder);
    }

    private async Task DetectFolderAsync()
    {
        var detected = FolderDetector.DetectNearby(_folder?.GameRoot ?? AppContext.BaseDirectory);
        if (detected is null)
        {
            await SendAsync("toast", new
            {
                kind = "warning",
                message = "没有自动找到 MajdataPlay，请手动选择 Original 文件夹。"
            });
            return;
        }

        await SetFolderAsync(detected);
    }

    private async Task SetFolderAsync(FolderInfo folder)
    {
        _folder = folder;
        _inventory = null;
        _catalogSummary = null;
        _charts = [];
        _lastIndexAt = null;
        _settings.LastRootPath = folder.RootPath;
        SettingsStore.Save(_settings);
        await SendAsync("folder", FolderPayload(folder));
        await RefreshDashboardAsync();
        await SendAsync("toast", new
        {
            kind = "success",
            message = $"已切换到曲目目录：{folder.RootPath}"
        });
    }

    private async Task SaveSettingsAsync(JsonElement payload)
    {
        _settings.Concurrency = Math.Clamp(
            GetInt(payload, "concurrency", _settings.Concurrency),
            1,
            12);
        _settings.IncludeVideo = GetBool(payload, "includeVideo", _settings.IncludeVideo);
        _settings.AutoCheck = GetBool(payload, "autoCheck", _settings.AutoCheck);
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("useMirrors", out var useMirrors) &&
            useMirrors.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _settings.UseMirrors = useMirrors.GetBoolean();
        }
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("storageTarget", out var storageTarget) &&
            storageTarget.ValueKind == JsonValueKind.String)
        {
            _settings.StorageTarget = ResolveStorageTarget(storageTarget.GetString() ?? "");
        }
        var mode = GetString(payload, "downloadMode");
        if (new[] { "auto", "single", "2", "4" }.Contains(mode))
        {
            _settings.DownloadMode = mode;
        }

        SettingsStore.Save(_settings);
        await SendSettingsAsync();
    }

    private async Task EnableAutoCheckAsync()
    {
        _settings.AutoCheck = true;
        SettingsStore.Save(_settings);
        await SendSettingsAsync();
        await SendAsync("autoCheckEnabled", new { autoCheck = true });
    }

    private async Task TestNetworkAsync()
    {
        await SendAsync("networkTesting", new { message = "正在并行测速站点与 DNS 节点…" });
        try
        {
            var routes = await _api.RefreshNetworkNodesAsync(CancellationToken.None);
            var available = routes.Where(route => route.Available).ToArray();
            await SendAsync("networkNode", new
            {
                routes = available.Select(route => new
                {
                    host = route.Host,
                    latencyMs = route.LatencyMs,
                    addresses = route.Addresses.Select(address => address.ToString()).ToArray()
                }).ToArray(),
                fastest = available.FirstOrDefault()?.Host ?? "",
                message = available.Length > 0
                    ? $"最快站点：{available[0].Host}，IP {available[0].Addresses.FirstOrDefault()}"
                    : "没有找到可用站点"
            });
        }
        catch (Exception ex)
        {
            await SendAsync("networkNode", new
            {
                routes = Array.Empty<object>(),
                fastest = "",
                message = "测速失败：" + ex.Message
            });
        }
    }

    private async Task TestOnlineSourcesAsync()
    {
        await SendAsync("onlineSourceTesting", new { message = "正在测试本站与镜像延迟…" });
        try
        {
            var routes = await _api.RefreshNetworkNodesAsync(CancellationToken.None);
            var sources = routes
                .Select(route => new
                {
                    host = route.Host,
                    url = MirrorSelector.ApiRoot(route.Host) + "/",
                    latencyMs = route.Available ? route.LatencyMs : (long?)null,
                    available = route.Available,
                    addresses = route.Addresses.Select(address => address.ToString()).ToArray()
                })
                .ToArray();
            await SendAsync("onlineSourceLatency", new
            {
                sources,
                fastest = sources
                    .Where(source => source.available)
                    .OrderBy(source => source.latencyMs)
                    .FirstOrDefault()?.host ?? "",
                testedAt = DateTimeOffset.Now
            });
        }
        catch (Exception ex)
        {
            await SendAsync("onlineSourceLatency", new
            {
                sources = Array.Empty<object>(),
                fastest = "",
                testedAt = DateTimeOffset.Now,
                error = ex.Message
            });
        }
    }

    private string? CurrentGameRoot =>
        !string.IsNullOrWhiteSpace(_settings.GameRootPath) &&
        Directory.Exists(_settings.GameRootPath)
            ? NormalizeGameRoot(_settings.GameRootPath)
            : _folder?.GameRoot;

    private static string NormalizeGameRoot(string root)
    {
        var exe = Directory.Exists(root)
            ? Directory.GetFiles(root, "MajdataPlay.exe", SearchOption.TopDirectoryOnly).FirstOrDefault()
            : null;
        return exe is null ? root : Path.GetDirectoryName(exe)!;
    }

    private async Task ChooseGameFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择 MajdataPlay 游戏目录",
            Multiselect = false,
            InitialDirectory = CurrentGameRoot ?? Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialog.ShowDialog(_window) != true)
        {
            return;
        }

        var selected = Path.GetFullPath(dialog.FolderName);
        var exe = Directory.GetFiles(selected, "MajdataPlay.exe", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (exe is not null)
        {
            selected = Path.GetDirectoryName(exe)!;
        }

        _settings.GameRootPath = selected;
        SettingsStore.Save(_settings);
        var detectedFolder = FolderDetector.DetectFrom(selected);
        if (detectedFolder is not null)
        {
            _folder = detectedFolder;
            _settings.LastRootPath = detectedFolder.RootPath;
            SettingsStore.Save(_settings);
            await SendAsync("folder", FolderPayload(detectedFolder));
        }
        await SendGameStatusAsync();
        await SendAsync("toast", new
        {
            kind = "success",
            message = GameManager.IsInstalled(selected)
                ? "已选择游戏目录：" + selected
                : "已选择安装目录，可继续下载游戏"
        });
    }

    private Task SaveGameSourceAsync(JsonElement payload)
    {
        _settings.GameDownloadSource = "github";
        SettingsStore.Save(_settings);
        return Task.CompletedTask;
    }

    private async Task SendGameStatusAsync()
    {
        var root = CurrentGameRoot;
        var prerelease = root is not null && GameManager.IsPrerelease(root);
        await SendAsync("gameStatus", new
        {
            gameRoot = root ?? "",
            installed = root is not null && GameManager.IsInstalled(root),
            source = _settings.GameDownloadSource,
            channel = prerelease ? "prerelease" : "stable",
            nightlyAvailable = root is not null && GameManager.HasNightlyUpdate(root),
            skins = root is null ? new List<SkinItem>() : GameManager.GetInstalledSkins(root)
        });
    }

    private async Task SwitchGameChannelAsync(JsonElement payload)
    {
        var root = CurrentGameRoot;
        if (root is null || _operationRunning)
        {
            return;
        }

        var request = payload.Deserialize<GameChannelRequest>(JsonOptions.Default);
        var prerelease = request?.Channel?.Equals("prerelease", StringComparison.OrdinalIgnoreCase) == true;
        if (!Directory.Exists(root))
        {
            await SendAsync("toast", new { kind = "warning", message = "请先选择游戏目录" });
            return;
        }

        if (GameManager.IsInstalled(root) && GameManager.IsPrerelease(root) == prerelease)
        {
            return;
        }

        BeginOperation();
        try
        {
            await SendAsync("operation", new
            {
                stage = "download",
                message = prerelease
                    ? "正在切换测试版，曲目、皮肤、缓存和设置将保留…"
                    : "正在切回稳定版，曲目、皮肤、缓存和设置将保留…",
                indeterminate = true
            });
            var status = new Progress<(string Message, double? Progress)>(value =>
                _ = SendAsync("operation", new
                {
                    stage = "download",
                    message = value.Message,
                    progress = value.Progress,
                    indeterminate = value.Progress is null
                }));
            if (GameManager.IsInstalled(root))
            {
                await _game.SwitchChannelAsync(root, prerelease, status, _operation!.Token);
            }
            else
            {
                await _game.InstallGameAsync(root, prerelease, status, _operation!.Token);
            }
            _settings.GameChannel = prerelease ? "prerelease" : "stable";
            SettingsStore.Save(_settings);
            await SendAsync("operation", new
            {
                stage = "done",
                message = prerelease ? "已切换到测试版" : "已切换到稳定版"
            });
            await SendGameStatusAsync();
        }
        catch (OperationCanceledException)
        {
            await SendAsync("operation", new { stage = "cancelled", message = "已取消版本切换" });
        }
        catch (Exception ex)
        {
            await SendAsync("operation", new { stage = "error", message = ex.Message });
            await SendAsync("toast", new { kind = "error", message = "版本切换失败：" + ex.Message });
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task InstallGameAsync(JsonElement payload)
    {
        if (_operationRunning)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择 MajdataPlay 安装目录",
            Multiselect = false,
            InitialDirectory = CurrentGameRoot ?? Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialog.ShowDialog(_window) != true)
        {
            return;
        }

        var requestedChannel = GetString(payload, "channel");
        var prerelease = requestedChannel.Equals("prerelease", StringComparison.OrdinalIgnoreCase) ||
                         (string.IsNullOrWhiteSpace(requestedChannel) &&
                          _settings.GameChannel.Equals("prerelease", StringComparison.OrdinalIgnoreCase));
        BeginOperation();
        try
        {
            var selected = Path.GetFullPath(dialog.FolderName);
            var gameAlreadyExists = Directory.GetFiles(
                selected,
                "MajdataPlay.exe",
                SearchOption.TopDirectoryOnly).Length > 0;
            var isGameFolder = string.Equals(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(selected)),
                "MajdataPlay",
                StringComparison.OrdinalIgnoreCase);
            var target = gameAlreadyExists || isGameFolder
                ? selected
                : Path.Combine(selected, "MajdataPlay");
            _settings.GameRootPath = target;
            _settings.GameDownloadSource = "github";
            _settings.GameChannel = prerelease ? "prerelease" : "stable";
            SettingsStore.Save(_settings);
            var status = new Progress<(string Message, double? Progress)>(value =>
                _ = SendAsync("operation", new
                {
                    stage = "download",
                    message = value.Message,
                    progress = value.Progress
                }));
            await _game.InstallGameAsync(target, prerelease, status, _operation!.Token);
            var detected = FolderDetector.DetectFrom(target);
            if (detected is not null)
            {
                _folder = detected;
                _settings.LastRootPath = detected.RootPath;
                SettingsStore.Save(_settings);
                await SendAsync("folder", FolderPayload(detected));
            }
            await SendAsync("operation", new { stage = "done", message = "游戏安装完成" });
            await SendGameStatusAsync();
            await SendAsync("toast", new
            {
                kind = "success",
                message = "MajdataPlay 已安装到 " + target
            });
        }
        catch (OperationCanceledException)
        {
            await SendAsync("operation", new { stage = "cancelled", message = "已取消安装" });
        }
        catch (Exception ex)
        {
            await SendAsync("operation", new { stage = "error", message = ex.Message });
            await SendAsync("toast", new { kind = "error", message = "安装失败：" + ex.Message });
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task CheckGameUpdateAsync()
    {
        var root = CurrentGameRoot;
        if (root is null || !GameManager.IsInstalled(root))
        {
            await SendAsync("toast", new { kind = "warning", message = "请先安装或选择游戏目录" });
            return;
        }

        BeginOperation();
        try
        {
            if (GameManager.IsPrerelease(root))
            {
                _pendingGameUpdates = [];
                await SendAsync("gameUpdates", new { count = 0, files = Array.Empty<string>() });
                await SendAsync("operation", new
                {
                    stage = "done",
                    message = "测试版使用 nightly 通道；点击“更新”可下载最新完整包"
                });
                await SendGameStatusAsync();
                return;
            }

            _pendingGameUpdates = (await _game.CheckGameUpdateAsync(
                root,
                useCnb: false,
                _operation!.Token)).Files;
            await SendAsync("gameUpdates", new
            {
                count = _pendingGameUpdates.Count,
                files = _pendingGameUpdates.Take(30).Select(item => item.FilePath).ToArray()
            });
            await SendAsync("operation", new
            {
                stage = "done",
                message = _pendingGameUpdates.Count == 0
                    ? "游戏已是最新版本"
                    : $"发现 {_pendingGameUpdates.Count} 个文件需要更新"
            });
        }
        catch (Exception ex)
        {
            await SendAsync("operation", new { stage = "error", message = ex.Message });
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task UpdateGameAsync()
    {
        var root = CurrentGameRoot;
        if (root is null || _operationRunning)
        {
            return;
        }

        BeginOperation();
        try
        {
            if (GameManager.IsPrerelease(root))
            {
                await SendAsync("operation", new
                {
                    stage = "download",
                    message = "正在下载测试版完整包，曲目、皮肤、缓存和设置将保留…",
                    indeterminate = true
                });
                var switchStatus = new Progress<(string Message, double? Progress)>(value =>
                    _ = SendAsync("operation", new
                    {
                        stage = "download",
                        message = value.Message,
                        progress = value.Progress,
                        indeterminate = value.Progress is null
                    }));
                await _game.SwitchChannelAsync(root, true, switchStatus, _operation!.Token);
                _settings.GameChannel = "prerelease";
                SettingsStore.Save(_settings);
                _pendingGameUpdates = [];
                await SendAsync("gameUpdates", new { count = 0, files = Array.Empty<string>() });
                await SendAsync("operation", new
                {
                    stage = "done",
                    message = "测试版已更新到最新 nightly"
                });
                await SendGameStatusAsync();
                await SendAsync("toast", new
                {
                    kind = "success",
                    message = "MajdataPlay 测试版已更新到最新 nightly"
                });
                return;
            }

            if (_pendingGameUpdates.Count == 0)
            {
                _pendingGameUpdates = (await _game.CheckGameUpdateAsync(
                    root,
                    _settings.GameDownloadSource != "github",
                    _operation!.Token)).Files;
            }
            if (_pendingGameUpdates.Count == 0)
            {
                await SendAsync("operation", new { stage = "done", message = "游戏已是最新版本" });
                await SendAsync("gameUpdates", new { count = 0, files = Array.Empty<string>() });
                return;
            }

            await SendAsync("operation", new
            {
                stage = "download",
                message = $"准备更新 {_pendingGameUpdates.Count:N0} 个游戏文件，断点会自动续传…",
                progress = 0
            });
            var status = new Progress<(string Message, double? Progress)>(value =>
                _ = SendAsync("operation", new
                {
                    stage = "download",
                    message = value.Message,
                    progress = value.Progress,
                    indeterminate = value.Progress is null
                }));
            await _game.UpdateGameAsync(
                root,
                _pendingGameUpdates,
                status,
                _operation!.Token);
            await SendAsync("operation", new
            {
                stage = "index",
                message = "文件更新完成，正在校验游戏完整性…",
                indeterminate = true
            });
            var remaining = (await _game.CheckGameUpdateAsync(
                root,
                useCnb: false,
                _operation!.Token)).Files;
            _pendingGameUpdates = remaining;
            await SendAsync("gameUpdates", new
            {
                count = remaining.Count,
                files = remaining.Take(30).Select(item => item.FilePath).ToArray()
            });
            await SendAsync("operation", new
            {
                stage = "done",
                message = remaining.Count == 0
                    ? "游戏更新完成，所有文件已校验通过"
                    : $"更新已执行，仍有 {remaining.Count:N0} 个文件未通过校验"
            });
            await SendGameStatusAsync();
            await SendAsync("toast", new
            {
                kind = remaining.Count == 0 ? "success" : "warning",
                message = remaining.Count == 0
                    ? "MajdataPlay 已更新到最新版本"
                    : $"还有 {remaining.Count:N0} 个文件需要继续更新"
            });
        }
        catch (OperationCanceledException)
        {
            await SendAsync("operation", new { stage = "cancelled", message = "已取消游戏更新" });
        }
        catch (Exception ex)
        {
            await SendAsync("operation", new { stage = "error", message = ex.Message });
            await SendAsync("toast", new
            {
                kind = "error",
                message = "游戏更新中断：" + ex.Message
            });
        }
        finally
        {
            EndOperation();
        }
    }

    private void LaunchGame(JsonElement payload)
    {
        var root = CurrentGameRoot;
        if (root is null)
        {
            _ = SendAsync("toast", new { kind = "warning", message = "请先设置游戏目录" });
            return;
        }

        try
        {
            GameManager.LaunchGame(root, GetString(payload, "option"));
            _ = SendAsync("toast", new { kind = "success", message = "游戏已启动" });
        }
        catch (Exception ex)
        {
            _ = SendAsync("toast", new { kind = "error", message = "启动失败：" + ex.Message });
        }
    }

    private void OpenGameFolder()
    {
        var root = CurrentGameRoot;
        if (root is not null && Directory.Exists(root))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{root}\"")
            {
                UseShellExecute = true
            });
        }
    }

    private async Task LoadSkinsAsync()
    {
        var root = CurrentGameRoot;
        if (root is null)
        {
            await SendAsync("toast", new { kind = "warning", message = "请先选择游戏目录" });
            return;
        }

        try
        {
            var skins = await GameManager.GetSkinsAsync(
                root,
                _settings.GameDownloadSource != "github",
                null,
                CancellationToken.None);
            await SendAsync("skins", new { online = skins, installed = GameManager.GetInstalledSkins(root) });
        }
        catch (Exception ex)
        {
            await SendAsync("toast", new { kind = "error", message = "读取皮肤列表失败：" + ex.Message });
        }
    }

    private async Task InstallSkinAsync(JsonElement payload)
    {
        var root = CurrentGameRoot;
        if (root is null || _operationRunning)
        {
            return;
        }

        var request = payload.Deserialize<SkinInstallRequest>(JsonOptions.Default);
        if (request is null || string.IsNullOrWhiteSpace(request.FileName))
        {
            return;
        }

        BeginOperation();
        try
        {
            var status = new Progress<string>(message => _ = SendAsync("operation", new
            {
                stage = "import",
                message,
                indeterminate = true
            }));
            await _game.InstallSkinAsync(
                root,
                new SkinItem
                {
                    Name = request.Name ?? Path.GetFileNameWithoutExtension(request.FileName),
                    FileName = request.FileName,
                    Size = request.Size
                },
                _settings.GameDownloadSource != "github",
                status,
                _operation!.Token);
            await SendAsync("operation", new { stage = "done", message = "皮肤安装完成" });
            await LoadSkinsAsync();
        }
        catch (Exception ex)
        {
            await SendAsync("operation", new { stage = "error", message = ex.Message });
            await SendAsync("toast", new { kind = "error", message = "皮肤安装失败：" + ex.Message });
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task LoadGameSettingsAsync()
    {
        var root = CurrentGameRoot;
        if (root is null)
        {
            await SendAsync("toast", new { kind = "warning", message = "请先选择游戏目录" });
            return;
        }

        try
        {
            await SendAsync("gameSettings", GameManager.LoadGameSettings(root));
        }
        catch (Exception ex)
        {
            await SendAsync("toast", new { kind = "warning", message = ex.Message });
        }
    }

    private async Task ApplySkinAsync(JsonElement payload)
    {
        var root = CurrentGameRoot;
        if (root is null)
        {
            await SendAsync("toast", new { kind = "warning", message = "请先选择游戏目录" });
            return;
        }

        var request = payload.Deserialize<SkinApplyRequest>(JsonOptions.Default);
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return;
        }

        try
        {
            var active = GameManager.ApplySkin(root, request.Name);
            await SendAsync("toast", new { kind = "success", message = "已应用皮肤：" + active });
            await SendGameStatusAsync();
            await LoadGameSettingsAsync();
            await LoadSkinsAsync();
        }
        catch (Exception ex)
        {
            await SendAsync("toast", new { kind = "error", message = "应用皮肤失败：" + ex.Message });
        }
    }

    private async Task SaveGameSettingsAsync(JsonElement payload)
    {
        var root = CurrentGameRoot;
        if (root is null)
        {
            return;
        }

        try
        {
            if (payload.TryGetProperty("settings", out var settings))
            {
                GameManager.SaveGameSettings(root, settings);
                await SendAsync("toast", new { kind = "success", message = "游戏设置已保存" });
                await LoadGameSettingsAsync();
            }
        }
        catch (Exception ex)
        {
            await SendAsync("toast", new { kind = "error", message = "保存设置失败：" + ex.Message });
        }
    }

    private async Task MeasureNetworkInBackgroundAsync()
    {
        await SendAsync("networkTesting", new { message = "正在测试站点与 DNS 节点…" });
        try
        {
            var routes = await _api.EnsureNetworkRoutesAsync(CancellationToken.None);
            var available = routes.Where(route => route.Available).ToArray();
            await SendAsync("networkNode", new
            {
                routes = available.Select(route => new
                {
                    host = route.Host,
                    latencyMs = route.LatencyMs,
                    addresses = route.Addresses.Select(address => address.ToString()).ToArray()
                }).ToArray(),
                fastest = available.FirstOrDefault()?.Host ?? "",
                message = available.Length > 0
                    ? $"启动测速完成，最快站点 {available[0].Host}（{available[0].LatencyMs} ms）"
                    : "启动测速完成，暂无可用站点"
            });
        }
        catch (Exception ex)
        {
            await SendAsync("networkNode", new
            {
                routes = Array.Empty<object>(),
                fastest = "",
                message = "启动测速失败：" + ex.Message
            });
        }
    }

    private async Task LoadIndexAsync(bool useCache)
    {
        if (!EnsureReady() || _operationRunning)
        {
            return;
        }

        var cachePath = CatalogCachePath;

        if (File.Exists(cachePath))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<List<ChartItem>>(
                    await File.ReadAllTextAsync(cachePath),
                    JsonOptions.Default);
                if (cached is { Count: > 0 })
                {
                    _charts = cached;
                    _lastIndexAt = File.GetLastWriteTime(cachePath);
                    await PublishCatalogAsync();
                    if (useCache)
                    {
                        _ = RefreshCatalogInBackgroundAsync(cachePath, knownCount: cached.Count);
                        return;
                    }
                }
            }
            catch
            {
                // A corrupt cache is replaced by a complete index.
            }
        }

        BeginOperation();
        try
        {
            var status = new Progress<string>(message => _ = SendAsync("catalogBackground", new
            {
                running = true,
                message
            }));
            _charts = await _api.GetAllChartsAsync(status, _operation!.Token);
            if (_charts.Count == 0)
            {
                throw new InvalidDataException(
                    "没有读取到完整曲目索引" +
                    (string.IsNullOrWhiteSpace(_api.LastIndexError) ? "" : "：" + _api.LastIndexError));
            }
            _lastIndexAt = DateTimeOffset.Now;
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllTextAsync(
                cachePath,
                JsonSerializer.Serialize(_charts, JsonOptions.Compact),
                _operation.Token);
            CatalogCacheStore.Save(cachePath, _charts);
            await PublishCatalogAsync();
            await SendAsync("catalogBackground", new
            {
                running = false,
                message = $"索引完成，共 {_charts.Count:N0} 首曲目"
            });
        }
        catch (OperationCanceledException)
        {
            await SendAsync("catalogBackground", new { running = false, message = "索引已停止" });
        }
        catch (Exception ex)
        {
            await SendAsync("catalogBackground", new
            {
                running = false,
                message = "读取索引失败：" + ex.Message
            });
            await SendAsync("toast", new
            {
                kind = "error",
                message = "读取索引失败：" + ex.Message
            });
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task ReloadIndexInBackgroundAsync()
    {
        if (!EnsureReady() || _catalogRefreshRunning)
        {
            return;
        }
        _catalogRefreshRunning = true;
        await SendAsync("catalogBackground", new
        {
            running = true,
            message = "正在刷新曲目索引…"
        });
        try
        {
            var refreshed = await _api.GetAllChartsAsync(new Progress<string>(_ => { }), CancellationToken.None);
            if (refreshed.Count == 0)
            {
                await SendAsync("catalogBackground", new
                {
                    running = false,
                    message = string.IsNullOrWhiteSpace(_api.LastIndexError)
                        ? "索引刷新未取得结果"
                        : _api.LastIndexError
                });
                return;
            }
            _charts = refreshed;
            _lastIndexAt = DateTimeOffset.Now;
            await File.WriteAllTextAsync(
                CatalogCachePath,
                JsonSerializer.Serialize(_charts, JsonOptions.Compact));
            CatalogCacheStore.Save(CatalogCachePath, _charts);
            await PublishCatalogAsync();
            await SendAsync("catalogUpdated", new { total = _charts.Count, indexedAt = _lastIndexAt });
            await SendAsync("catalogBackground", new
            {
                running = false,
                message = $"索引刷新完成 · {_charts.Count:N0} 首"
            });
            await SendAsync("toast", new
            {
                kind = "success",
                message = $"索引已刷新，共 {_charts.Count:N0} 首曲目"
            });
        }
        catch (Exception ex)
        {
            await SendAsync("catalogBackground", new
            {
                running = false,
                message = "索引刷新失败：" + ex.Message
            });
            await SendAsync("toast", new { kind = "error", message = "读取索引失败：" + ex.Message });
        }
        finally
        {
            _catalogRefreshRunning = false;
        }
    }

    private static string CatalogCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MajdataSync",
        "catalog.json");

    private async Task<bool> LoadCachedCatalogAsync()
    {
        var cachePath = CatalogCachePath;
        if (!File.Exists(cachePath))
        {
            await SendAsync("catalogLoading", new
            {
                message = "正在建立完整曲目索引…"
            });
            return false;
        }

        try
        {
            var cached = JsonSerializer.Deserialize<List<ChartItem>>(
                await File.ReadAllTextAsync(cachePath),
                JsonOptions.Default);
            if (cached is not { Count: > 0 })
            {
                return false;
            }

            _charts = cached;
            _lastIndexAt = File.GetLastWriteTime(cachePath);
            await PublishCatalogAsync();
            return true;
        }
        catch
        {
            await SendAsync("catalogLoading", new
            {
                message = "缓存无法读取，正在重新建立索引…"
            });
            return false;
        }
    }

    private async Task InitializeCatalogInBackgroundAsync()
    {
        if (_catalogRefreshRunning)
        {
            return;
        }

        _catalogRefreshRunning = true;
        try
        {
            if (_charts.Count > 0)
            {
                await RefreshCatalogInBackgroundAsync(CatalogCachePath, _charts.Count);
                return;
            }

            await LoadIndexAsync(useCache: true);
        }
        finally
        {
            _catalogRefreshRunning = false;
        }
    }

    private async Task ResetCatalogStateAsync()
    {
        _charts = [];
        _catalogSummary = null;
        _lastIndexAt = null;
        await PublishCatalogAsync();
    }

    private async Task LoadCatalogAfterFolderChangeAsync()
    {
        if (_catalogRefreshRunning)
        {
            return;
        }

        _catalogRefreshRunning = true;
        try
        {
            if (!await LoadCachedCatalogAsync())
            {
                await LoadIndexAsync(useCache: true);
            }
            else
            {
                _ = RefreshCatalogInBackgroundAsync(CatalogCachePath, _charts.Count);
            }
        }
        finally
        {
            _catalogRefreshRunning = false;
        }
    }

    private async Task RefreshCatalogInBackgroundAsync(string cachePath, int knownCount)
    {
        using var refreshCancellation = new CancellationTokenSource();
        try
        {
            var refreshed = await _api.GetAllChartsAsync(
                new Progress<string>(_ => { }),
                refreshCancellation.Token);
            if (refreshed.Count == 0)
            {
                return;
            }

            var previous = CatalogCacheStore.Load(cachePath);
            var changed = refreshed.Any(chart =>
                !previous.Charts.TryGetValue(chart.Id, out var hash) ||
                !string.Equals(hash, chart.Hash, StringComparison.Ordinal));
            var countChanged = refreshed.Count != knownCount;
            if (!changed && !countChanged)
            {
                _lastIndexAt = DateTimeOffset.Now;
                await SendAsync("catalogBackground", new
                {
                    running = false,
                    message = "索引已是最新"
                });
                return;
            }

            await SendAsync("catalogBackground", new
            {
                running = true,
                message = $"正在后台校验索引 · 当前可用 {knownCount:N0} 首"
            });

            _charts = refreshed;
            _lastIndexAt = DateTimeOffset.Now;
            await File.WriteAllTextAsync(
                cachePath,
                JsonSerializer.Serialize(_charts, JsonOptions.Compact),
                refreshCancellation.Token);
            CatalogCacheStore.Save(cachePath, _charts);
            await PublishCatalogAsync();
            await SendAsync("catalogUpdated", new { total = _charts.Count, indexedAt = _lastIndexAt });
            await SendAsync("catalogBackground", new
            {
                running = false,
                message = $"后台索引已更新 · {_charts.Count:N0} 首"
            });
        }
        catch
        {
            // The cached catalog remains usable if a refresh fails.
            await SendAsync("catalogBackground", new
            {
                running = false,
                message = "后台索引校验未完成，继续使用本地缓存"
            });
        }
    }

    private async Task RunSyncAsync(JsonElement payload)
    {
        if (!EnsureReady() || _operationRunning || _folder is null)
        {
            return;
        }

        var request = payload.Deserialize<SyncRequest>(JsonOptions.Default) ?? new SyncRequest();
        request.ChartIds ??= [];
        request.IncludeVideo = GetBool(payload, "includeVideo", _settings.IncludeVideo);
        request.Concurrency = Math.Clamp(request.Concurrency, 1, 12);
        request.StorageTarget = ResolveStorageTarget(
            GetString(payload, "storageTarget") is { Length: > 0 } requestedTarget
                ? requestedTarget
                : _settings.StorageTarget);
        BeginOperation();
        try
        {
            if (_charts.Count == 0)
            {
                var status = new Progress<string>(message => _ = SendAsync("operation", new
                {
                    stage = "index",
                    message,
                    indeterminate = true
                }));
                _charts = await _api.GetAllChartsAsync(status, _operation!.Token);
                _lastIndexAt = DateTimeOffset.Now;
                await PublishCatalogAsync();
            }

            var store = new InventoryStore(_folder.RootPath);
            var inventory = await store.LoadAsync(_operation!.Token);
            var selected = SelectCharts(_charts, inventory, request);
            if (request.Limit is > 0)
            {
                selected = selected.Take(request.Limit.Value).ToList();
            }

            if (selected.Count == 0)
            {
                await SendAsync("operation", new
                {
                    stage = "done",
                    message = request.UpdatedOnly ? "当前没有需要同步的新谱面" : "筛选结果为空"
                });
                await SendAsync("toast", new
                {
                    kind = "info",
                    message = request.UpdatedOnly ? "已是最新" : "没有符合条件的曲目"
                });
                return;
            }

            await SendAsync("operation", new
            {
                stage = "download",
                message = $"准备下载 {selected.Count:N0} 首曲目",
                total = selected.Count,
                completed = 0
            });

            var installer = new ChartInstaller(_api);
            var successful = 0;
            var failed = 0;
            var skipped = 0;
            var completed = 0;
            var semaphore = new SemaphoreSlim(request.Concurrency);
            var inventoryLock = new SemaphoreSlim(1, 1);
            var tasks = selected.Select(async chart =>
            {
                await semaphore.WaitAsync(_operation.Token);
                try
                {
                    var prior = inventory.Charts.GetValueOrDefault(chart.Id);
                    var cacheFolder = OnlineCacheWriter.CacheFolder(_folder, chart.Hash);
                    var cacheReady = Directory.Exists(cacheFolder) &&
                                     File.Exists(Path.Combine(cacheFolder, "maidata.txt")) &&
                                     File.Exists(Path.Combine(cacheFolder, "track.mp3")) &&
                                     File.Exists(Path.Combine(cacheFolder, "bg.jpg"));
                    if (prior is not null &&
                        string.Equals(prior.Hash, chart.Hash, StringComparison.Ordinal) &&
                        Directory.Exists(Path.Combine(_folder.RootPath, prior.Folder)) &&
                        (request.StorageTarget == "local" || cacheReady))
                    {
                        Interlocked.Increment(ref skipped);
                        return;
                    }

                    await SendAsync("task", new
                    {
                        id = chart.Id,
                        state = "downloading",
                        title = chart.Title,
                        subtitle = "正在下载曲目资源"
                    });
                    var record = await installer.DownloadChartAsync(
                        _folder,
                        chart,
                        request.IncludeVideo,
                        Math.Min(request.Concurrency, 4),
                        async (label, fraction) =>
                        {
                            await SendAsync("task", new
                            {
                                id = chart.Id,
                                state = "downloading",
                                title = chart.Title,
                                subtitle = label,
                                progress = fraction
                            });
                        },
                        _operation.Token);
                    if (record is null)
                    {
                        return;
                    }

                    if (_folder is not null && request.StorageTarget is "both" or "cache")
                    {
                        await SendAsync("task", new
                        {
                            id = chart.Id,
                            state = "downloading",
                            title = chart.Title,
                            subtitle = "正在写入 MajdataPlay 在线缓存",
                            progress = 1.0
                        });
                        await OnlineCacheWriter.PublishAsync(
                            _folder,
                            chart,
                            Path.Combine(_folder.RootPath, record.Folder),
                            _operation.Token);
                    }

                    if (_folder is not null && request.StorageTarget == "cache")
                    {
                        Directory.Delete(Path.Combine(_folder.RootPath, record.Folder), recursive: true);
                        record.Folder = Path.GetRelativePath(
                            _folder.RootPath,
                            OnlineCacheWriter.CacheFolder(_folder, chart.Hash));
                    }

                    await inventoryLock.WaitAsync(_operation.Token);
                    try
                    {
                        inventory.Charts[chart.Id] = record;
                        inventory.LastSyncAt = DateTimeOffset.Now;
                        try
                        {
                            await store.SaveAsync(inventory, _operation.Token);
                        }
                        catch (OperationCanceledException) when (_operation.IsCancellationRequested)
                        {
                            await store.SaveAsync(inventory, CancellationToken.None);
                        }
                    }
                    finally
                    {
                        inventoryLock.Release();
                    }

                    Interlocked.Increment(ref successful);
                    await SendAsync("task", new
                    {
                        id = chart.Id,
                        state = "done",
                        title = chart.Title,
                        subtitle = "已写入 " + record.Folder,
                        progress = 1.0
                    });
                    await SendAsync("chartUpdated", new { id = chart.Id, downloaded = true });
                }
                catch (OperationCanceledException)
                {
                    // Cached resources remain available for retry.
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    await SendAsync("task", new
                    {
                        id = chart.Id,
                        state = "error",
                        title = chart.Title,
                        subtitle = ex.Message
                    });
                }
                finally
                {
                    var processed = Interlocked.Increment(ref completed);
                    await SendAsync("operation", new
                    {
                        stage = "download",
                        message = $"正在处理：{chart.Title}",
                        completed = processed,
                        total = selected.Count,
                        succeeded = successful,
                        failed,
                        skipped
                    });
                    semaphore.Release();
                }
            }).ToArray();

            await Task.WhenAll(tasks);
            inventory.LastSyncAt = DateTimeOffset.Now;
            await store.SaveAsync(inventory, CancellationToken.None);
            await SendAsync("operation", new
            {
                stage = "done",
                message = $"同步完成：成功 {successful}，跳过 {skipped}，失败 {failed}",
                completed = selected.Count,
                total = selected.Count,
                succeeded = successful,
                failed,
                skipped
            });
            await SendAsync("toast", new
            {
                kind = failed == 0 ? "success" : "warning",
                message = failed == 0
                    ? $"已完成 {successful} 首曲目的下载"
                    : $"完成 {successful} 首，{failed} 首失败"
            });
            await RefreshDashboardAsync();
        }
        catch (OperationCanceledException)
        {
            await SendAsync("operation", new { stage = "cancelled", message = "已停止下载" });
        }
        catch (Exception ex)
        {
            await SendAsync("operation", new { stage = "error", message = ex.Message });
            await SendAsync("toast", new { kind = "error", message = "同步失败：" + ex.Message });
        }
        finally
        {
            EndOperation();
        }
    }

    private List<ChartItem> SelectCharts(
        IEnumerable<ChartItem> charts,
        SyncInventory inventory,
        SyncRequest request)
    {
        var query = charts.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(request.ChartId))
        {
            query = query.Where(chart =>
                chart.Id.Equals(request.ChartId, StringComparison.OrdinalIgnoreCase));
        }

        if (request.ChartIds.Count > 0)
        {
            var selectedIds = new HashSet<string>(
                request.ChartIds.Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);
            query = query.Where(chart => selectedIds.Contains(chart.Id));
        }

        if (request.UpdatedOnly)
        {
            query = query.Where(chart =>
            {
                if (!inventory.Charts.TryGetValue(chart.Id, out var record))
                {
                    return true;
                }

                return !string.Equals(record.Hash, chart.Hash, StringComparison.Ordinal) ||
                       !Directory.Exists(Path.Combine(_folder!.RootPath, record.Folder));
            });
        }

        if (TryParseDate(request.DateFrom, out var from))
        {
            query = query.Where(chart => chart.Timestamp.ToLocalTime().Date >= from.Date);
        }

        if (TryParseDate(request.DateTo, out var to))
        {
            query = query.Where(chart => chart.Timestamp.ToLocalTime().Date <= to.Date);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(chart =>
                chart.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                chart.Artist.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                chart.Uploader.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                chart.Designer.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        return query.OrderByDescending(chart => chart.Timestamp).ToList();
    }

    private async Task ImportZipAsync(JsonElement payload)
    {
        if (!EnsureReady())
        {
            return;
        }

        var request = payload.Deserialize<ImportZipRequest>(JsonOptions.Default);
        var zipPath = request?.ZipPath;
        if (string.IsNullOrWhiteSpace(zipPath))
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择要导入的 Majdata 谱面压缩包",
                Filter = "ZIP 压缩包 (*.zip)|*.zip",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(_window) != true)
            {
                return;
            }

            zipPath = dialog.FileName;
        }

        if (_operationRunning || _folder is null)
        {
            return;
        }

        BeginOperation();
        try
        {
            await SendAsync("operation", new
            {
                stage = "import",
                message = "正在导入 " + Path.GetFileName(zipPath),
                indeterminate = true
            });
            var installer = new ChartInstaller(_api);
            var status = new Progress<string>(message => _ = SendAsync("operation", new
            {
                stage = "import",
                message,
                indeterminate = true
            }));
            var record = await installer.ImportZipAsync(
                _folder,
                zipPath,
                status,
                _operation!.Token);
            await SendAsync("operation", new
            {
                stage = "done",
                message = $"已导入：{record.Folder}"
            });
            await SendAsync("toast", new
            {
                kind = "success",
                message = "ZIP 已解压到 Original\\" + record.Folder
            });
            await RefreshDashboardAsync();
        }
        catch (OperationCanceledException)
        {
            await SendAsync("operation", new { stage = "cancelled", message = "已取消导入" });
        }
        catch (Exception ex)
        {
            await SendAsync("operation", new { stage = "error", message = ex.Message });
            await SendAsync("toast", new { kind = "error", message = "导入失败：" + ex.Message });
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task SelectZipAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要导入的 Majdata 谱面压缩包",
            Filter = "ZIP 压缩包 (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(_window) == true)
        {
            await SendAsync("zipPicked", new { path = dialog.FileName });
        }
    }

    private async Task ImportOnlineCacheAsync(JsonElement payload)
    {
        if (!EnsureReady() || _folder is null || _operationRunning)
        {
            return;
        }

        var id = GetString(payload, "id");
        var chart = _charts.FirstOrDefault(item =>
            item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (chart is null)
        {
            await SendAsync("toast", new { kind = "warning", message = "请先刷新曲目索引" });
            return;
        }

        BeginOperation();
        try
        {
            await SendAsync("operation", new
            {
                stage = "import",
                message = "正在把本地曲目写入在线缓存：" + chart.Title,
                indeterminate = true
            });
            var inventory = await new InventoryStore(_folder.RootPath)
                .LoadAsync(_operation!.Token);
            if (!inventory.Charts.TryGetValue(chart.Id, out var record))
            {
                throw new InvalidDataException("请先下载该曲目到本地");
            }

            var source = Path.Combine(_folder.RootPath, record.Folder);
            if (!Directory.Exists(source))
            {
                throw new InvalidDataException("本地曲目文件夹不存在");
            }

            await OnlineCacheWriter.PublishAsync(
                _folder,
                chart,
                source,
                _operation.Token);
            await SendAsync("operation", new
            {
                stage = "done",
                message = "在线缓存已写入 Cache\\Net\\MajdataNET"
            });
            await SendAsync("toast", new
            {
                kind = "success",
                message = "已写入游戏在线缓存：" + chart.Title
            });
            await SendAsync("chartUpdated", new { id = chart.Id, downloaded = true });
        }
        catch (OperationCanceledException)
        {
            await SendAsync("operation", new { stage = "cancelled", message = "已取消" });
        }
        catch (Exception ex)
        {
            await SendAsync("operation", new { stage = "error", message = ex.Message });
            await SendAsync("toast", new { kind = "error", message = "写入缓存失败：" + ex.Message });
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task SyncAllToCacheAsync()
    {
        if (!EnsureReady() || _folder is null || _operationRunning)
        {
            return;
        }

        if (_charts.Count == 0)
        {
            await SendAsync("toast", new { kind = "warning", message = "请先刷新曲目索引" });
            return;
        }

        BeginOperation();
        try
        {
            var inventory = await new InventoryStore(_folder.RootPath)
                .LoadAsync(_operation!.Token);
            var localCharts = _charts
                .Where(chart => inventory.Charts.ContainsKey(chart.Id))
                .Where(chart => Directory.Exists(Path.Combine(
                    _folder.RootPath,
                    inventory.Charts[chart.Id].Folder)))
                .ToArray();
            await SendAsync("operation", new
            {
                stage = "import",
                message = $"准备写入 {localCharts.Length} 首曲目的在线缓存",
                total = localCharts.Length,
                completed = 0,
                indeterminate = false
            });
            var written = 0;
            var failed = 0;
            for (var index = 0; index < localCharts.Length; index++)
            {
                _operation.Token.ThrowIfCancellationRequested();
                var chart = localCharts[index];
                var record = inventory.Charts[chart.Id];
                try
                {
                    await OnlineCacheWriter.PublishAsync(
                        _folder,
                        chart,
                        Path.Combine(_folder.RootPath, record.Folder),
                        _operation.Token);
                    written++;
                }
                catch
                {
                    failed++;
                }

                await SendAsync("operation", new
                {
                    stage = "import",
                    message = $"正在写入在线缓存：{chart.Title}",
                    completed = index + 1,
                    total = localCharts.Length,
                    succeeded = written,
                    failed
                });
            }

            await SendAsync("operation", new
            {
                stage = "done",
                message = $"在线缓存完成：{written} 首成功，{failed} 首跳过"
            });
            await SendAsync("toast", new
            {
                kind = failed == 0 ? "success" : "warning",
                message = $"已预热 {written} 首在线缓存"
            });
        }
        catch (OperationCanceledException)
        {
            await SendAsync("operation", new { stage = "cancelled", message = "已停止写入缓存" });
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task RefreshDashboardAsync()
    {
        if (_folder is null)
        {
            return;
        }

        var inventory = await new InventoryStore(_folder.RootPath)
            .LoadAsync(CancellationToken.None);
        _inventory = inventory;
        _folder = FolderDetector.DetectFrom(_folder.RootPath) ?? _folder;
        await SendAsync("folder", FolderPayload(_folder));
        await SendAsync("stats", new
        {
            folderCount = _folder.ExistingChartFolders,
            trackedCount = inventory.Charts.Count,
            lastSyncAt = inventory.LastSyncAt,
            indexPath = Path.Combine(_folder.RootPath, ".majdata-sync-index.json")
        });
        await PublishCatalogAsync();
        await SendAsync("recent", ReadRecent(inventory));
    }

    private async Task PublishCatalogAsync()
    {
        if (_folder is null)
        {
            return;
        }

        var inventory = await new InventoryStore(_folder.RootPath)
            .LoadAsync(CancellationToken.None);
        _inventory = inventory;
        var hashes = new HashSet<string>(
            inventory.Charts.Values.Select(record => record.Hash),
            StringComparer.Ordinal);
        var items = _charts.Select(chart =>
        {
            var record = inventory.Charts.GetValueOrDefault(chart.Id);
            return new CatalogEntry(
                chart,
                record is not null || hashes.Contains(chart.Hash),
                record is not null &&
                !string.Equals(record.Hash, chart.Hash, StringComparison.Ordinal));
        }).ToArray();
        _catalogSummary = new CatalogSummary(items.Length, items.Count(item => item.Downloaded));
        await SendAsync("catalog", new
        {
            total = items.Length,
            downloaded = _catalogSummary.Downloaded,
            newCount = items.Count(item => !item.Downloaded || item.Stale),
            indexedAt = _lastIndexAt
        });
    }

    private async Task SendCatalogPageAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        if (_folder is null)
        {
            return;
        }

        if (_charts.Count == 0)
        {
            await SendAsync("catalogLoading", new { message = "正在准备曲目索引…" });
            _ = LoadCatalogAfterFolderChangeAsync();
            return;
        }

        var query = payload.Deserialize<CatalogQuery>(JsonOptions.Default) ?? new CatalogQuery();
        var pageSize = Math.Clamp(query.PageSize, 12, 48);
        var inventory = _inventory ?? await new InventoryStore(_folder.RootPath)
            .LoadAsync(cancellationToken);
        _inventory = inventory;
        var summary = _catalogSummary ?? new CatalogSummary(
            _charts.Count,
            _charts.Count(chart => inventory.Charts.ContainsKey(chart.Id)));
        var hashes = new HashSet<string>(
            inventory.Charts.Values.Select(record => record.Hash),
            StringComparer.Ordinal);
        var entries = _charts.Select(chart =>
        {
            var record = inventory.Charts.GetValueOrDefault(chart.Id);
            return new CatalogEntry(
                chart,
                record is not null || hashes.Contains(chart.Hash),
                record is not null &&
                !string.Equals(record.Hash, chart.Hash, StringComparison.Ordinal));
        });

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            entries = entries.Where(entry =>
                entry.Chart.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Chart.Artist.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Chart.Designer.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Chart.Uploader.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Chart.Tags.Concat(entry.Chart.PublicTags)
                    .Any(tag => tag.Contains(search, StringComparison.CurrentCultureIgnoreCase)));
        }

        if (query.Status.Equals("new", StringComparison.OrdinalIgnoreCase))
        {
            entries = entries.Where(entry => !entry.Downloaded || entry.Stale);
        }
        else if (query.Status.Equals("downloaded", StringComparison.OrdinalIgnoreCase))
        {
            entries = entries.Where(entry => entry.Downloaded);
        }

        if (TryParseDate(query.DateFrom, out var from))
        {
            entries = entries.Where(entry => entry.Chart.Timestamp.ToLocalTime().Date >= from.Date);
        }

        if (TryParseDate(query.DateTo, out var to))
        {
            entries = entries.Where(entry => entry.Chart.Timestamp.ToLocalTime().Date <= to.Date);
        }

        var filtered = entries.ToList();
        var pageCount = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)pageSize));
        var page = Math.Clamp(query.Page, 0, pageCount - 1);
        var items = filtered.Skip(page * pageSize).Take(pageSize).Select(entry => new
        {
            entry.Chart.Id,
            entry.Chart.Title,
            entry.Chart.Artist,
            entry.Chart.Designer,
            entry.Chart.Uploader,
            entry.Chart.DateText,
            entry.Chart.LevelText,
            entry.Chart.Hash,
            timestamp = entry.Chart.Timestamp,
            tags = entry.Chart.Tags.Concat(entry.Chart.PublicTags).Distinct().Take(4).ToArray(),
            image = entry.Chart.ImageUrl,
            entry.Downloaded,
            entry.Stale
        }).ToArray();

        await SendAsync("catalogPage", new
        {
            total = _charts.Count,
            filtered = filtered.Count,
            downloaded = summary.Downloaded,
            newCount = summary.Total - summary.Downloaded,
            page,
            pageSize,
            pageCount,
            items,
            indexedAt = _lastIndexAt
        });
    }

    private object[] ReadRecent(SyncInventory inventory)
    {
        if (_folder is null)
        {
            return [];
        }

        return inventory.Charts.Values
            .OrderByDescending(item => item.DownloadedAt)
            .Take(8)
            .Select(item =>
            {
                var folder = Path.Combine(_folder.RootPath, item.Folder);
                var title = ChartInstaller.FindMaidataTitle(folder) ?? item.Title;
                return (object)new
                {
                    id = item.ChartId,
                    title,
                    artist = item.Artist,
                    folder = item.Folder,
                    downloadedAt = item.DownloadedAt,
                    image = FindImage(folder),
                    hasVideo = item.HasVideo
                };
            })
            .ToArray();
    }

    private async Task OpenFolderAsync()
    {
        var path = _folder?.RootPath;
        if (path is not null && Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            {
                UseShellExecute = true
            });
        }

        await Task.CompletedTask;
    }

    private void OpenChart(JsonElement payload)
    {
        var id = GetString(payload, "id");
        if (_folder is null || string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var inventory = new InventoryStore(_folder.RootPath)
            .LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (inventory.Charts.TryGetValue(id, out var record))
        {
            var path = Path.Combine(_folder.RootPath, record.Folder);
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
                {
                    UseShellExecute = true
                });
            }
        }
    }

    private void OpenUrl(JsonElement payload)
    {
        var id = GetString(payload, "id");
        var url = string.IsNullOrWhiteSpace(id)
            ? ChartApi.SiteUrl
            : $"{ChartApi.SiteUrl}/song?id={Uri.EscapeDataString(id)}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static string? FindImage(string folder)
    {
        foreach (var name in new[] { "bg.jpg", "bg.png", "bg.jpeg" })
        {
            var path = Path.Combine(folder, name);
            if (File.Exists(path))
            {
                return new Uri(path).AbsoluteUri;
            }
        }

        return null;
    }

    private Task SendSettingsAsync(bool? firstRun = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["concurrency"] = Math.Clamp(_settings.Concurrency, 1, 12),
            ["includeVideo"] = _settings.IncludeVideo,
            ["autoCheck"] = _settings.AutoCheck,
            ["downloadMode"] = _settings.DownloadMode,
            ["useMirrors"] = _settings.UseMirrors,
            ["storageTarget"] = _settings.StorageTarget
        };
        if (firstRun is not null)
        {
            payload["firstRun"] = firstRun.Value;
        }

        return SendAsync("settings", payload);
    }

    private Task SendStateAsync()
    {
        return SendAsync("state", new AppStatus
        {
            OperationRunning = _operationRunning,
            Version = "1.8.1",
            Site = ChartApi.SiteUrl
        });
    }

    private object FolderPayload(FolderInfo? folder) => folder is null
        ? new { detected = false, rootPath = (string?)null }
        : new
        {
            detected = true,
            rootPath = folder.RootPath,
            chartsRoot = folder.ChartsRoot,
            gameRoot = folder.GameRoot,
            automatic = folder.DetectedAutomatically,
            folderCount = folder.ExistingChartFolders,
            trackedCount = folder.SyncedChartFolders
        };

    private bool EnsureReady()
    {
        if (_folder is not null)
        {
            return true;
        }

        _ = SendAsync("toast", new
        {
            kind = "warning",
            message = "请先选择 Majdata 的 Original 文件夹"
        });
        return false;
    }

    private void BeginOperation()
    {
        if (_operationRunning)
        {
            return;
        }

        _operation?.Dispose();
        _operation = new CancellationTokenSource();
        _operationRunning = true;
        _ = SendAsync("busy", new { running = true });
    }

    private void EndOperation()
    {
        if (!_operationRunning)
        {
            return;
        }

        _operationRunning = false;
        _ = SendAsync("busy", new { running = false });
        _ = SendStateAsync();
    }

    public Task SendAsync(string type, object payload) =>
        _window?.SendAsync(type, payload) ?? Task.CompletedTask;

    private static string GetString(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static bool GetBool(JsonElement payload, string name, bool fallback)
    {
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(name, out var value) &&
               value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
    }

    private static int GetInt(JsonElement payload, string name, int fallback)
    {
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(name, out var value) &&
               value.TryGetInt32(out var number)
            ? number
            : fallback;
    }

    private static bool TryParseDate(string? value, out DateTime date)
    {
        return DateTime.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static string ResolveStorageTarget(string value) =>
        value is "both" or "local" or "cache" ? value : "both";

    public async Task<int> LoadIndexCountForTestAsync(CancellationToken cancellationToken)
    {
        _charts = await _api.GetAllChartsAsync(null, cancellationToken);
        return _charts.Count;
    }

    public List<ChartItem> SelectChartsForTest(
        IEnumerable<ChartItem> charts,
        SyncInventory inventory,
        SyncRequest request)
        => SelectCharts(charts, inventory, request);

    public void Dispose()
    {
        _operationRunning = false;
        _operation?.Cancel();
        _operation?.Dispose();
        _api.Dispose();
    }

    private sealed record CatalogEntry(ChartItem Chart, bool Downloaded, bool Stale);
    private sealed record CatalogSummary(int Total, int Downloaded);
}
