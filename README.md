# ClassLapse · 课堂延时

> 静默运行于希沃白板（或任何 Windows 教室一体机）的自动拍照工具。按计划用指定摄像头拍照存盘，方便后期把一节课/一学期合成延时视频。

## 功能

- **完全静默**——不弹窗、不出声、不闪屏，老师上课无感知
- **极低占用**——闲置 CPU 0%、内存 < 60 MB
- **可选摄像头**——避开展台等老师上课会调用的设备
- **被占用即让**——目标摄像头被希沃主软件抢走时静默跳过 + 写日志，绝不抢设备
- **按条目的计划**——每个条目独立配置：间隔模式（时段内每 N 秒）/ 定时模式（在指定时刻各拍一张）、生效星期、时段或时间点；上午/下午/晚自习、打铃/升旗时刻都能表达，条目之间允许重叠
- **首次运行向导**——开箱即用，引导选摄像头/路径/计划
- **开机自启**——HKCU 注册表，无需管理员权限
- **自动清理**——可选按保留天数 + 磁盘上限滚动删旧
- **设备最高分辨率**——默认按摄像头能输出的最高分辨率拍摄

## 截图

托盘菜单（运行中绿色 / 被占用黄色 / 错误红色，三色"CL"标志）：

```
[绿] ClassLapse · 运行中
今日已拍: 384 张
摄像头: USB Camera (HD Webcam)
─────────────────────────
⏸  暂停 1 小时
⏸  暂停今天剩余
▶  恢复
─────────────────────────
📷  立即拍一张（测试）
📂  打开输出文件夹
⚙️  设置...
📝  打开配置文件 (高级)
─────────────────────────
退出
```

## 部署到希沃白板

完整步骤、首次配置向导、烟雾验收清单、卸载流程见 **[docs/deployment.md](docs/deployment.md)**。

## 开发

### 环境

- **Windows 10/11** + **.NET 8 SDK**（推荐 — 能直接 build/run/test）
- 或 **Linux + Microsoft 官方 .NET 8 SDK**（注意：Arch 的 `dotnet-sdk-8.0` 拆分包不含 `Microsoft.NET.Sdk.WindowsDesktop` targets，build 不通过）

### 构建

```powershell
dotnet build -c Release
dotnet test                   # 期望 42 个 pass (30 schedule + 7 config + 5 migration)
```

### 发布

```powershell
./publish.ps1                 # 默认 self-contained (~65MB)，没装 .NET Runtime 也能跑
./publish.ps1 -FrameworkDependent   # ~5MB，但目标机需要 .NET 8 Runtime
./publish.ps1 -Zip            # 同时打 ClassLapse-vX.Y.Z-win-x64.zip
```

### 仓库结构

```
ClassLapse/
├── ClassLapse.sln
├── README.md                         # ← 你正在看
├── publish.ps1                       # 发布脚本
├── docs/
│   └── deployment.md                 # 部署到希沃白板的全流程
├── src/
│   ├── ClassLapse/
│   │   ├── App.xaml(.cs)             # 入口：CLI 模式 / 首次运行向导 / 正常托盘启动
│   │   ├── TrayApp.cs                # H.NotifyIcon 托盘 + Scheduler 订阅 + 拍照写盘
│   │   ├── Core/
│   │   │   ├── CaptureScheduler.cs   # System.Threading.Timer，1s 节拍 + 重入锁 + 每条目计时
│   │   │   ├── ScheduleDecision.cs   # 纯函数：(now, schedule, paused, lastByEntry) -> 哪些条目该拍
│   │   │   ├── LegacyScheduleMigration.cs # 旧全局计划 → 条目列表（确定性 id，幂等）
│   │   │   ├── CameraEnumerator.cs   # DirectShow VideoInputDevice 枚举
│   │   │   ├── CameraService.cs      # 异步 TryCaptureAsync，立即释放设备
│   │   │   ├── ConfigStore.cs        # JSON 读写 + 原子 .tmp+Replace + 损坏自愈 + 启动迁移
│   │   │   ├── AutoStartManager.cs   # HKCU\Run\ClassLapse
│   │   │   ├── StorageJanitor.cs     # 按天数+磁盘上限滚动清理
│   │   │   ├── Logger.cs             # 按天滚动文本日志
│   │   │   └── DevCli.cs             # --list-cameras / --capture <idx> <out>
│   │   ├── Models/                   # AppConfig / ScheduleConfig / ScheduleEntry / TimeWindow ...
│   │   └── Views/SettingsWindow      # 4 Tab 设置 + 首次运行双用
│   └── ClassLapse.Tests/             # xUnit (ScheduleDecision / ConfigStore / LegacyScheduleMigration)
└── .gitignore
```

### 关键决策

- **不用 MVVM 框架**——SettingsWindow 是纯 code-behind 表单，避免 CommunityToolkit.Mvvm 拉依赖
- **不用 Serilog/NLog**——自写 80 行 Logger，按天文本日志，省体积
- **不用 OpenCvSharp**——AForge.Video.DirectShow 250KB 够用，OpenCvSharp 拉 30MB+ 原生 DLL
- **静态 Log 门面**——`Log.Info/Warn/Error` 全局可用，省构造器穿透

### 命令行模式

`.exe` 不带参数 → 托盘模式。带 `--` 开头参数 → CLI 工具：

```powershell
ClassLapse.exe --list-cameras            # 列出所有 USB 摄像头
ClassLapse.exe --capture 0 test.jpg      # 用 0 号摄像头拍一张存到 test.jpg
ClassLapse.exe --help
```

> 从 PowerShell 跑看不到输出时用 `.\ClassLapse.exe --list-cameras | Out-Default`，或 `dotnet run --project src/ClassLapse -- --list-cameras`。

## 后期合成延时视频

输出目录下任一日期文件夹（例如 `2026-05-17/`）：

```bash
ffmpeg -framerate 30 -pattern_type glob -i '*.jpg' \
       -c:v libx264 -pix_fmt yuv420p -crf 23 out.mp4
```

或拖进剪映/PR/达芬奇等，自动按文件名（即拍摄时间）排序后导出。

## 许可

私有项目，未公开授权。
