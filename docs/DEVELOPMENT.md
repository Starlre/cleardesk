# 开发指南

## 当前环境

检查日期：2026-10-11。

| 项目 | 检查结果 |
| --- | --- |
| 工作目录 | `F:\glass desk` |
| Windows 版本号 | `10.0.22631.0`，Windows 11 构建系列 |
| PowerShell | `5.1.22621.6133` |
| Git | `2.55.0.windows.5` |
| .NET SDK | 项目内 `.tools/dotnet/` 已准备 10.0.401，官方 SHA-512 校验通过 |
| 本地 .NET 运行时 | SDK 包含 10.0.12；系统原有 .NET 8 等运行时保持可用 |

当前可构建和运行玻璃窗口实验。正式源码与测试项目尚未创建。

## 环境准备

使用 `global.json` 固定的 .NET 10 SDK，建议使用支持 .NET 10 的 Visual Studio，或 VS Code 配合 C# 工具。SDK 下载入口：<https://dotnet.microsoft.com/download/dotnet/10.0>。

SDK 准备完成后执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup-sdk.ps1
& .\.tools\dotnet\dotnet.exe --list-sdks
& .\.tools\dotnet\dotnet.exe --info
git --version
```

setup 脚本下载微软官方 SDK 到项目内并校验 SHA-512，不注册全局 PATH。当前固定 10.0.401，允许同一功能带的稳定补丁滚动；WPF 实验尚无第三方 NuGet 依赖。SQLite 驱动、MVVM 库等依赖在实际引入时记录版本。

## 首轮实现顺序

1. 在 `experiments/GlassWindow` 创建最小 WPF 实验，完成 VALIDATION 的 V1。
2. 在 `experiments/ShellIntegration` 完成 V2 的枚举、图标、拖入和启动链路。
3. 确认界面技术决策后创建正式解决方案和三个源码项目。
4. 提取可复用服务，再实现启动器网格和 Dock；实验代码不直接充当正式架构。

不要在玻璃与窗口行为尚未确认时，先完成整套界面再返工框架。

## Git 工作流

- 初始稳定分支为 `main`，文档基线形成第一个提交。
- 后续实验使用 `spike/glass-window`、`spike/shell-integration` 等短分支。
- 正式功能使用 `feat/launcher-grid`、`feat/dock` 等分支。
- 有需要并行开发时再创建额外 worktree；当前只建立一个工作目录中的本地仓库。
- 初始仓库不配置远程地址；远程托管目标确定后再添加。
- 每个提交保持范围明确，代码、相关文档和验证结果一起提交。

建议提交信息：`docs: ...`、`feat: ...`、`fix: ...`、`chore: ...`。

## 构建与验证

`GlassDesk.sln` 目前包含 `experiments/GlassWindow/GlassDesk.GlassWindow.csproj`。从仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1 -NoBuild
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1 -Verify -NoBuild
```

默认 Release。验证模式短暂显示自有背景、面板和 Dock，最多等待 60 秒，自动退出并生成 `artifacts/GlassWindow/verification/`。隔离桌面中 GDI 截图可能失败；本次在可访问交互桌面的运行环境中完成截图。实验状态使用 `artifacts/GlassWindow/state.json`，不写入正式应用数据目录。

附加 `-InputTest` 可验证实际鼠标事件链，验证期间会移动鼠标，并在结束后恢复位置。该模式拦截全部外部应用启动；日常检查只需 `-Verify`。

对排序、取消拖动、搜索和持久化迁移等业务规则编写必要的测试。玻璃材质、焦点、多屏、系统拖入与窗口切换按 VALIDATION 人工复核。不得把编译成功记为视觉或系统集成验证通过。

实验结果至少记录：Windows 构建、SDK、硬件/显示器缩放、步骤、预期、实际结果、证据、通过或失败，以及失败后的决策。

## 代码约定

- 使用 `.editorconfig` 中的 UTF-8、LF 与缩进规则。
- C# 开启可空引用类型；异步操作支持取消和可理解的失败反馈。
- ViewModel 不直接调用 Win32；Windows 适配实现集中在 Infrastructure。
- 原生句柄、图标、COM 对象和事件钩子明确释放；应用退出时注销热键与监听。
- 界面中使用 DIP，原生坐标与 DPI 转换集中处理。
- 日志、数据库、缓存、证书和构建产物不进入版本库；验证证据单独保存在 `docs/evidence/`。
