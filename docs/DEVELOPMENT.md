# 开发指南

## 当前环境

更新日期：2026-10-12；环境版本检查日期：2026-10-11。

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

setup 脚本下载微软官方 SDK 到项目内并校验 SHA-512，不注册全局 PATH。当前固定 10.0.401，允许同一功能带的稳定补丁滚动。玻璃背景引入微软 Win2D.uwp 1.28.2 和 Microsoft.VCRTForwarders.140 1.1.0；Windows SDK 引用由目标框架还原。脚本将 NuGet 缓存保存在 `.tools/nuget-packages/`，首次构建需要访问 NuGet。SQLite 驱动、MVVM 库等依赖在实际引入时记录版本。

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
- GitHub 远程仓库：`https://github.com/Starlre/cleardesk`，公开；`origin` 使用 HTTPS。
- 每个提交保持范围明确，代码、相关文档和验证结果一起提交。

Git 提交说明和推送更新备注使用中文，例如：`修复：裁切玻璃背景的圆角`、`文档：更新开发指南`。该约定同时记录在根目录 `AGENTS.md`。

仅在用户明确要求更新 Git 时提交和推送。功能说明、技术文档和验证证据随对应实现更新；实验本地配置与构建产物不提交。

## 构建与验证

`ClearDesk.sln` 目前包含 `experiments/GlassWindow/ClearDesk.GlassWindow.csproj`。从仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1 -NoBuild
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1 -Verify -NoBuild
```

默认 Release、x64，输出为 `experiments/GlassWindow/bin/x64/Release/net10.0-windows10.0.22621.0/`。验证模式短暂显示自有背景、面板、图标池和 Dock，最多等待 60 秒，自动退出并生成 `artifacts/GlassWindow/verification/`。隔离桌面中 GDI 截图可能失败；本次在可访问交互桌面的运行环境中完成截图。实验状态使用 `artifacts/GlassWindow/panel-state.json` 保存手动选择与排序，不写入正式应用数据目录；旧版 `state.json` 保留但不自动恢复到面板。

附加 `-InputTest` 可验证实际鼠标事件链，验证期间会移动鼠标，并在结束后恢复位置。该模式拦截全部外部应用启动；日常检查只需 `-Verify`。

普通验证同时在输出目录创建隔离的真实快捷方式，检查跨路径去重、参数大小写、工作目录、不同目标、管理员标志、URL 查询参数、解析失败和旧排序迁移。测试不写用户桌面或开始菜单，不启动测试目标。

图标池验证检查空面板启动、取消不添加、搜索后保留选择、复选框绑定、确认添加、移除后重新添加，以及重启仅恢复已选择入口。截图包含 `launcher-empty.png`、`dock-empty.png` 和 `icon-pool.png`。验证模式使用独立测试状态，图标池操作与外部启动均不会改动用户面板状态。

托盘使用原生 `Shell_NotifyIcon`，图标资源随构建复制到 `Assets/ClearDesk.ico`。验证创建独立临时托盘标识，检查实际 Shell 注册、右键菜单、关闭隐藏、恢复和图标清理，并通过托盘退出菜单结束验证进程。Windows 通知区域的折叠设置由系统管理，应用不修改用户的托盘显示偏好。

最小化按钮和原生最小化只收起主面板及附属设置/图标池，Dock 与其玻璃保持显示；九宫格或托盘恢复面板。Escape、关闭和托盘隐藏命令隐藏全部窗口。Dock 空白处和九宫格支持鼠标捕获移动，九宫格通过系统拖动距离区分单击与移动；右侧 Thumb 等比缩放，右键菜单与 Ctrl + 滚轮提供尺寸调整。原生恢复尺寸通知完成后重新同步背景。

`SingleInstance` 在创建窗口前取得每用户每会话的 Mutex，第二进程通过命名 EventWaitHandle 唤回已有实例并正常退出；运行脚本检测已有实例时跳过构建，避免占用中的 exe 复制失败。验证以隔离名称启动第二进程，检查退出、Dispatcher 通知和释放后可再次启动。托盘首次注册失败时定时重试，不将临时 Shell 不可用当作致命错误；托盘未注册时禁止隐藏全部窗口。

`DockLayoutStore` 将位置和比例原子写入 `artifacts/GlassWindow/dock-layout.json`，结束拖动时保存，启动时恢复。损坏数据回退默认值，越界位置和比例限制在有效范围；目前仅支持主屏工作区域。验证使用隔离布局文件，不改变用户 Dock 设置；`-InputTest` 检查移动与缩放不抢焦点、不触发应用启动。

`SettingsWindow` 替代原窗口验证页，常用外观设置与高级诊断分开。透明度滑动条范围为 0–100，支持键盘和可访问的 RangeValue 接口；仅修改共享 `SurfaceTint` 颜色的 alpha，不调整窗口、图标或文字的整体不透明度。`AppearanceSettingsStore` 原子保存到 `appearance-settings.json`，350 ms 防抖并在关闭设置及退出时补充保存；启动在窗口创建前读取上一次值。实色降级不覆盖偏好。验证采用隔离状态，覆盖滑动条实时效果、设置重开、主题切换、托盘恢复与配置加载。

对排序、取消拖动、搜索和持久化迁移等业务规则编写必要的测试。玻璃材质、焦点、多屏、系统拖入与窗口切换按 VALIDATION 人工复核。不得把编译成功记为视觉或系统集成验证通过。

实验结果至少记录：Windows 构建、SDK、硬件/显示器缩放、步骤、预期、实际结果、证据、通过或失败，以及失败后的决策。

## 代码约定

- 使用 `.editorconfig` 中的 UTF-8、LF 与缩进规则。
- C# 开启可空引用类型；异步操作支持取消和可理解的失败反馈。
- ViewModel 不直接调用 Win32；Windows 适配实现集中在 Infrastructure。
- 原生句柄、图标、COM 对象和事件钩子明确释放；应用退出时注销热键与监听。
- 界面中使用 DIP，原生坐标与 DPI 转换集中处理。
- 日志、数据库、缓存、证书和构建产物不进入版本库；验证证据单独保存在 `docs/evidence/`。
