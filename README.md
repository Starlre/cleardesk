# Glass Desk

Windows 桌面应用启动器与 Dock。以玻璃质感、快速启动和自然的图标拖拽让位为核心体验。

## 当前状态

- 阶段：开发准备，文档与仓库初始化。
- 首发平台：Windows 11；系统背景材质首先验证 Windows 11 22H2 及以上。
- 主技术路线：C#、.NET 10 LTS、WPF/MVVM、Win32/Shell COM、SQLite。
- 界面框架决策：WPF 为主方案，完成玻璃窗口验证后确认；WinUI 3 为备选。
- 当前目录中的 `src/`、`tests/`、`experiments/` 是目录骨架，尚未创建 `.sln`、`.csproj` 或可运行程序。
- 已检查的本机环境：Git 可用；缺少 .NET SDK，因此当前不能构建应用。

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
│       └── .gitkeep
├── src/
│   ├── GlassDesk.App/
│   │   └── .gitkeep
│   ├── GlassDesk.Core/
│   │   └── .gitkeep
│   └── GlassDesk.Infrastructure/
│       └── .gitkeep
├── experiments/
│   ├── GlassWindow/
│   │   └── .gitkeep
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

准备 .NET 10 SDK，然后按 [技术验证](docs/VALIDATION.md) 开始 `experiments/GlassWindow`：验证背景材质、圆角、阴影、命中区域和焦点行为。验证结果决定正式应用是否继续采用 WPF。
