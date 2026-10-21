# Majdata Sync

[![build](https://github.com/Zppppp124/MajdataSync/actions/workflows/build.yml/badge.svg)](https://github.com/Zppppp124/MajdataSync/actions/workflows/build.yml)
[![release](https://img.shields.io/github/v/release/Zppppp124/MajdataSync?label=release)](https://github.com/Zppppp124/MajdataSync/releases/latest)
[![download](https://img.shields.io/github/downloads/Zppppp124/MajdataSync/total?label=downloads)](https://github.com/Zppppp124/MajdataSync/releases/latest)

Majdata Sync 是一个 Windows 桌面工具，用于从 [majdata.net](https://majdata.net) 下载 MajdataPlay 谱面，并整理为游戏可直接读取的目录结构。

## 下载

当前版本：**v1.8.2**

[下载 Windows x64 自包含版本](https://github.com/Zppppp124/MajdataSync/releases/download/v1.8.2/MajdataSync-win-x64-v1.8.2.zip)

Windows 10/11 可直接运行，无需单独安装 .NET。程序界面使用 WebView2，大多数 Windows 10/11 已预装该运行时。

## 功能

- 自动寻找 MajdataPlay 的 `MaiCharts\Original`，支持选择游戏根目录或 Original 文件夹，不绑定盘符。
- 全量索引站点曲目，按上传日期查询；通过曲目 ID 与站点哈希增量同步新谱面和新版谱面。
- 索引使用站点 `limit=99999` 完整列表接口，并在本地保存曲目快照；启动时先秒开缓存，再在后台刷新。
- 每首曲目并行下载曲目、封面、谱面与可选 PV，默认 6 个并行任务，可在 1-12 之间调整。
- 曲目卡可勾选后批量下载；每首下载完成会立即写入同步索引，批量任务中断后已完成曲目不会丢失。
- 直接生成 `Original\曲名\`，文件位于曲目根目录，不保留 ZIP。
- 导入已有 ZIP 时，以 ZIP 文件名建立曲目文件夹，并自动展开单层同名目录。
- 校验 ZIP 路径，阻止 `../` 等目录穿越内容。
- 在 `Original\.majdata-sync-index.json` 记录同步状态，在每首曲目保存 `id` 与 `.majdata-sync.json`。
- 谱面索引与资源下载会测试 Majdata 本站和用户提供的同源镜像，优先使用低延迟线路；设置中可关闭镜像。
- 可选择仅写本地曲目库、仅写 MajdataPlay 在线缓存，或两者同时写入。
- 支持把现有本地曲目批量预热到 `Cache\Net\MajdataNET`，并按游戏规则生成 `.sha256`。
- 集成游戏管理：稳定版固定使用 `MajdataPlay_Build`，测试版使用 MajdataPlay nightly；两者均接入 GitHub 三路加速镜像并自动测速。
- 大型游戏包测速后会锁定最快且能持续传输的线路，不再在每个分片之间轮流切换镜像。
- 支持稳定版与测试版切换，重新下载程序文件时保留 `MaiCharts`、`Skins`、`Cache`、`RecordOutputs`、`Logs` 和 `settings.json`。
- 支持选择 D3D11、D3D12、OpenGL、Vulkan、测试模式或谱面编辑模式启动。
- 从 CNB 或 GitHub 读取社区皮肤列表，下载、重装并直接应用 `Display.Skin`。
- 内置常用游戏设置编辑器；高级设置会列出未展示字段，并提供中文说明、有效值与搜索。

## 运行

发布目录中的 `MajdataSync.exe` 可直接运行。首次打开如果自动识别失败，选择 `MajdataPlay.exe` 所在目录，程序会自动定位 `MaiCharts\Original`；也可以直接选择 `Original` 文件夹。

建议关闭 MajdataPlay 后再批量下载或替换曲目，以免曲目库正在读取时文件被占用。

## 下载结构

```text
Original
└─ 曲名
   ├─ maidata.txt
   ├─ track.mp3
   ├─ bg.jpg
   ├─ pv.mp4
   ├─ id
   └─ .majdata-sync.json
```

部分已有 MajdataPlay 谱面使用 `track.opus` 或 `bg.png`。程序不会改动无关的现有曲目，只会写入自己下载或导入的曲目目录。

## 说明

- 站点接口与页面资源来自 `https://majdata.net`。网络不可用、Cloudflare 限流或站点改版时，程序会显示具体错误，不会误删现有曲目。
- 谱面镜像通过相同曲目 ID 和 `/api3/api/maichart/...` 接口读取数据；程序逐曲目校验必要文件，镜像失败时回退主站。
- 稳定版不再使用 CNB 作为主下载源，固定从 `TeamMajdata/MajdataPlay_Build` 获取，通过 `ghfast.top`、`gh-proxy.com`、`ghproxy.net` 和直连 GitHub 自动测速后下载。
- 下载采用临时目录写入，只有必需文件齐全后才移动到 `Original`，避免产生半成品曲目。
- 下载与 ZIP 导入使用 `Original` 同盘的隐藏工作目录 `.majdata-work`，因此跨盘安装无需搬运巨型临时文件；成功提交后自动清理对应缓存。
- 索引缓存位于 `%LOCALAPPDATA%\MajdataSync\catalog.json`，完整接口不可用时会自动回退到分页查找。
- ZIP 导入要求包内包含 `maidata.txt`。
- 应用皮肤只会更新 `settings.json` 的 `Display.Skin`；高级设置只写入修改后的高级字段。
- 运行日志位于 `%LOCALAPPDATA%\MajdataSync\logs`，设置页可直接打开。

## 从源码构建

需要 .NET 8 SDK：

```powershell
dotnet build .\MajdataSync\MajdataSync.csproj -c Release
dotnet publish .\MajdataSync\MajdataSync.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o .\MajdataSync\publish
```

程序界面使用 WebView2；Windows 10/11 通常已预装 Microsoft Edge WebView2 Runtime。
