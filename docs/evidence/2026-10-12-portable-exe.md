# EXE 打包验证

日期：2026-10-12。环境：Windows 11 `10.0.22631.0`、x64、DPI 144；SDK 10.0.401，随包运行时 10.0.12。

产物：`artifacts/releases/ClearDesk.exe`，72,856,576 字节（69.5 MiB），版本 0.1.0.0。

SHA-256：`6fc505c72eb6b3d7fe7683d8de6037c90dce8c36c645c749f199be9fdfa43a45`。

以 Release、win-x64、自包含方式 publish，禁用 trimming 和 .NET 原生单文件打包，编译 0 个错误、0 个警告。随后执行 `scripts/package.ps1 -NoPublish`，把完整发布目录压缩为嵌入资源，通过 Windows .NET Framework 编译器生成带澄桌图标与中文产品信息的单文件启动器。

实际运行最终 EXE，以 `--package-root` 指定工作区隔离缓存，并转发 `--smoke-test --output` 指定报告目录。启动环境故意将 `DOTNET_ROOT` 与 `DOTNET_ROOT_X64` 指向不存在的运行时目录。

- 首次自动展开成功，最终 EXE 退出码为 0；128 项检查通过，运行时为随包的 10.0.12。
- 主面板与 Dock 使用 Windows Composition 圆角裁切玻璃背景，`FallbackReason` 均为 null；已复核打包版玻璃截图。
- 检查覆盖空面板、图标池、去重、托盘、最小化保留 Dock、透明度及 Dock 固定/最近排序，未启动外部应用。
- 从 `C:\Windows` 普通启动成功；再次启动保持原进程，两个启动器退出码均为 0。
- 二次启动没有重新展开缓存，`.ready` 修改时间保持不变。
- 运行进程的 `coreclr.dll` 与 `Microsoft.Graphics.Canvas.dll` 均来自展开的包目录，无需开发 SDK 路径。
- 发布 ZIP 中没有面板状态、透明度设置、Dock 布局/偏好或测试数据。

完整检查报告：[打包版检查](portable-exe-report.json)。普通启动与缓存证据：[启动报告](portable-exe-launch-report.json)。截图和原始报告保留在 `artifacts/package-verification/`。验证结束后关闭了隔离测试进程，没有修改用户开发配置。

本次只验证当前 Windows 11 x64 电脑，未在其他电脑或混合 DPI 多屏中测试。发布产物不进入版本库。
