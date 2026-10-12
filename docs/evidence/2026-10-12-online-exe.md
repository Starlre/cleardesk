# 轻量联网 EXE 验证

日期：2026-10-12。环境：Windows 11 `10.0.22631.0`、x64、窗口 DPI 144，SDK 10.0.401。

轻量产物 `artifacts/releases/ClearDesk-Online.exe` 为 9,863,168 字节（9.41 MiB）；原离线版 72,856,576 字节（69.48 MiB），分发体积减少 86.5%。轻量 EXE SHA-256 为 `e2fafc3bb4c6cc47e51548bc7c12a3177991e1376ea0dfccbb5851e5753df8c2`。

应用以 Release、win-x64、framework-dependent 发布，包含玻璃组件而不包含 .NET 运行时。最终 publish 和启动器编译无警告、无错误。发布配置以 .NET 10.0.0 为框架最低版本，使用下载的 10.0.12 补丁。

下载地址和 SHA-512 与微软官方 `release-metadata/10.0/releases.json` 的 10.0.12 条目逐项比对一致，两个 ZIP 实际校验通过：核心运行时 36,960,258 字节，WindowsDesktop 39,147,915 字节，首次合计约 72.6 MiB。运行环境展开到用户私有缓存，不执行系统安装器。

## 实际 EXE 首次联网启动

以工作区隔离的 `--package-root` 启动实际轻量 EXE，指定 `--smoke-test --output`，工作目录为 `C:\Windows`，开发 `DOTNET_ROOT` 两项指向不存在的路径。实际通过微软 HTTPS 下载两个运行时，启动成功，退出码 0。

- 128 项应用检查全部通过，运行时 10.0.12。
- 主面板和 Dock 的玻璃实现为 Windows Composition 圆角裁切模糊，`FallbackReason` 均为 null。
- 覆盖空面板、图标池、托盘、最小化保留 Dock、透明度与固定/最近排序。

报告：[首次联网检查](online-exe-report.json)。原始截图位于 `artifacts/online-package-verification/first-run/`。

## 下载器与断网缓存

独立检查器加载实际轻量 EXE，14 项检查通过，包括 SHA-512 不匹配拒绝、ZIP 越界拒绝、取消解压、连接失败不写完成标记、不完整下载清理、失败后重新下载成功，以及下载窗口取消。已复核 [首次下载窗口](online-exe-download-dialog.png)，状态与取消按钮完整可见。

缓存复用检查将 .NET 默认代理设为不可达的 `http://127.0.0.1:1`，直接调用发布 EXE 的准备逻辑仍返回本地缓存。另将最终 EXE 复制到隔离目录，为其配置相同不可达代理，执行普通启动和重复启动，两次启动器均退出 0，保持原应用单实例；应用缓存与运行时 `.ready` 修改时间均保持不变。

进程模块 `coreclr.dll` 来自私有 `runtimes/10.0.12-win-x64/shared/Microsoft.NETCore.App/10.0.12/`，`Microsoft.Graphics.Canvas.dll` 来自应用包缓存。验证结束关闭隔离进程，没有修改用户开发配置。

证据：[下载器检查](online-exe-bootstrap-checks.txt)、[断网启动报告](online-exe-offline-report.json)。这里减少的是分发 EXE 的体积，本地运行时仍占用磁盘。未在其他电脑、混合 DPI 多屏或所有代理环境中实测。发布产物不进入版本库。
