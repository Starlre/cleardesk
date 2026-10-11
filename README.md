# Glass Desk

Windows 桌面应用启动器与 Dock。以玻璃质感、快速启动和自然的图标拖拽让位为核心体验。

## 当前状态

- 阶段：P1 技术验证，已有可运行的玻璃窗口原型。
- 首发平台：Windows 11；系统背景材质首先验证 Windows 11 22H2 及以上。
- 主技术路线：C#、.NET 10 LTS、WPF/MVVM、Win32/Shell COM、SQLite。
- 界面框架决策：WPF 的背景模糊已有本机截图证据；胶囊阴影、命中与多屏验证完成后再最终确认。
- `GlassDesk.sln` 当前包含 `experiments/GlassWindow`；正式 `src/` 与核心测试项目仍是目录骨架。
- 本机已准备项目内 .NET SDK 10.0.401；没有更改系统 SDK 安装或全局 PATH。

## 启动原型

当前电脑可直接双击 [start.cmd](start.cmd)，或从仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

首次克隆且尚未准备 SDK 时，先运行 `scripts/setup-sdk.ps1`。验证版支持本机快捷方式、搜索分页、同页拖动让位、文件路径导入和 Dock 启动；详情见 [实验说明](experiments/GlassWindow/README.md)。

自动检查与截图：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1 -Verify
```

验证会短暂显示自有窗口并自动退出。实际运行记录见 [首轮验证结果](docs/evidence/2026-10-11-glass-window.md)。

面板使用带圆角裁切的 Windows Composition 背景模糊，启动器为 32 DIP、Dock 为 40 DIP；玻璃背景与窗口轮廓一致，见 [最新验证记录](docs/evidence/2026-10-11-clipped-backdrop.md)。

## 文档入口

| 文档 | 内容 |
| --- | --- |
| [产品范围](docs/PRODUCT.md) | MVP、交互约定与后续能力 |
| [技术架构](docs/ARCHITECTURE.md) | 模块边界、数据模型与 Windows 集成 |
| [开发指南](docs/DEVELOPMENT.md) | 环境准备、目录约定、Git 工作流 |
| [阶段计划](docs/ROADMAP.md) | 任务状态、依赖与交付标准 |
| [技术验证](docs/VALIDATION.md) | 玻璃窗口、Shell、Dock、多屏验证 |
| [技术决策 ADR-0001](docs/decisions/0001-platform-and-stack.md) | 选型依据与切换条件 |
| [变更记录](docs/CHANGELOG.md) | 已完成的项目变更 |

## 项目目录

```text
glass desk/
├── README.md
├── GlassDesk.sln
├── global.json
├── start.cmd
├── .editorconfig
├── .gitattributes
├── .gitignore
├── docs/
│   ├── README.md
│   ├── PRODUCT.md
│   ├── ARCHITECTURE.md
│   ├── DEVELOPMENT.md
│   ├── ROADMAP.md
│   ├── VALIDATION.md
│   ├── CHANGELOG.md
│   ├── decisions/
│   │   └── 0001-platform-and-stack.md
│   └── evidence/
│       └── 首轮验证记录、报告与窗口截图
├── scripts/
│   ├── common.ps1
│   ├── setup-sdk.ps1
│   ├── build.ps1
│   └── run.ps1
├── src/
│   ├── GlassDesk.App/
│   │   └── .gitkeep
│   ├── GlassDesk.Core/
│   │   └── .gitkeep
│   └── GlassDesk.Infrastructure/
│       └── .gitkeep
├── experiments/
│   ├── GlassWindow/
│   │   ├── GlassDesk.GlassWindow.csproj
│   │   ├── README.md
│   │   └── WPF 窗口、原生适配、索引与验证源码
│   ├── ShellIntegration/
│   │   └── .gitkeep
│   └── DockBehavior/
│       └── .gitkeep
├── tests/
│   └── GlassDesk.Core.Tests/
│       └── .gitkeep
└── assets/
    └── .gitkeep
```

Git 不追踪空目录，因此用 `.gitkeep` 保留尚未实现的模块。首次创建模块文件时可删除对应占位文件。

## 下一步

按 [技术验证](docs/VALIDATION.md) 继续检查胶囊裁切后的阴影、鼠标操作、透明设置降级和多屏 DPI；补全 Shell 启动语义与打包应用验证后，再提取正式应用模块。当前原型不代表完整 MVP 已交付。
