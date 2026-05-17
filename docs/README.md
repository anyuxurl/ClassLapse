# ClassLapse · 课堂延时

> 静默运行于希沃白板的自动拍照工具，按计划用指定摄像头拍照存盘，方便你后期把一节课/一学期合成延时视频。

## 设计目标

- **完全静默**——不弹窗、不出声、不闪屏，老师上课无感知
- **极低占用**——闲置 CPU 0%、内存 < 50 MB，对希沃白板硬件友好
- **可选摄像头**——避开展台等老师上课会调用的设备
- **被占用即让**——目标摄像头被希沃主软件抢走时静默跳过 + 写日志，绝不抢设备
- **可自定义计划**——周几 + 起止时间 + 拍照间隔

## 当前状态

`M0 项目脚手架已完成，尚未实现任何业务逻辑。`

后续里程碑见 [`/home/qeeryyu/.claude/plans/windows-1-2-3-4-fluttering-bee.md`](../../../.claude/plans/windows-1-2-3-4-fluttering-bee.md)（如果你看不到此路径，说明你不是开发者本人）。

## 仓库结构

```
ClassLapse/
├── ClassLapse.sln
├── src/
│   ├── ClassLapse/             # WPF 主程序（.exe）
│   └── ClassLapse.Tests/       # xUnit 测试
├── docs/                       # 你正在看的文档
├── publish.ps1                 # 发布单文件 .exe 的脚本
└── .gitignore
```

## 开发环境

- Windows 10/11 推荐（能直接 build/run）
- 或 Linux 编辑代码 + Windows 上 build；需要 Microsoft 官方 .NET SDK 才能在 Linux 跨编译 WPF

## 构建

```powershell
# Windows
dotnet build -c Release
./publish.ps1
```

发布产物在 `publish/` 下，单 .exe（依赖系统 .NET 8 Runtime）约 5 MB；自包含版约 65 MB。

## 后期合成延时视频

`captures/2026-05-17/` 目录用 FFmpeg 一行合成 30fps MP4：

```bash
ffmpeg -framerate 30 -pattern_type glob -i '*.jpg' -c:v libx264 -pix_fmt yuv420p out.mp4
```

或者拖进剪映/PR 直接做。

## 许可

私有项目，未授权。
