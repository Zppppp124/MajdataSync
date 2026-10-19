# Majdata Sync

[![build](https://github.com/Zppppp124/MajdataSync/actions/workflows/build.yml/badge.svg)](https://github.com/Zppppp124/MajdataSync/actions/workflows/build.yml)

Majdata Sync 是一个 Windows 桌面工具，用于从 [majdata.net](https://majdata.net) 下载 MajdataPlay 谱面，并整理为游戏可直接读取的目录结构。

## 功能

- 自动寻找 MajdataPlay 的 `MaiCharts\Original`，支持选择游戏根目录或 Original 文件夹，不绑定盘符。
- 全量索引站点曲目，按上传日期查询；通过曲目 ID 与站点哈希增量同步新谱面和新版谱面。
- 每首曲目并行下载曲目、封面、谱面与可选 PV，默认 6 个并行任务，可在 1-12 之间调整。
- 直接生成 `Original\曲名\`，文件位于曲目根目录，不保留 ZIP。
- 导入已有 ZIP 时，以 ZIP 文件名建立曲目文件夹，并自动展开单层同名目录。
- 校验 ZIP 路径，阻止 `../` 等目录穿越内容。
- 在 `Original\.majdata-sync-index.json` 记录同步状态，在每首曲目保存 `id` 与 `.majdata-sync.json`。
- 可优先使用用户提供的同源加速镜像 `maj-3.moyingmoe.top`，不可用时依次回退 `maj-2`、`maj-1` 与主站；设置中可关闭镜像。

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
- 加速镜像通过相同曲目 ID 和 `/api3/api/maichart/...` 接口读取数据；程序逐曲目校验必要文件，镜像失败时回退主站。
- 下载采用临时目录写入，只有必需文件齐全后才移动到 `Original`，避免产生半成品曲目。
- 下载与 ZIP 导入使用 `Original` 同盘的隐藏工作目录 `.majdata-work`，因此跨盘安装无需搬运巨型临时文件；成功提交后自动清理对应缓存。
- 全量索引没有站点总数接口，程序读取到连续空页后停止；索引缓存位于 `%LOCALAPPDATA%\MajdataSync\catalog.json`。
- ZIP 导入要求包内包含 `maidata.txt`。

## 从源码构建

需要 .NET 8 SDK：

```powershell
dotnet build .\MajdataSync\MajdataSync.csproj -c Release
dotnet publish .\MajdataSync\MajdataSync.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o .\MajdataSync\publish
```

程序界面使用 WebView2；Windows 10/11 通常已预装 Microsoft Edge WebView2 Runtime。
