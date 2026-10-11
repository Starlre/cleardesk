# 玻璃窗口验证版

这是独立的 WPF 技术实验，不是最终应用。使用 .NET 10、非 layered WPF 窗口与独立 Windows Composition 玻璃背景层。当前构建目标为 Windows 11 22H2+、x64；背景效果依赖微软 Win2D.uwp 1.28.2 和 Microsoft.VCRTForwarders.140 1.1.0。

## 运行

从仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup-sdk.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

已有 SDK 时，setup 会直接返回。SDK 放在 `.tools/dotnet/`，不注册系统环境变量；仓库中的脚本使用本地 SDK。

## 当前行为

- 后台 STA 线程扫描桌面和开始菜单，按启动语义去重后保留最多 196 个 `.lnk` / `.url` 入口及真实图标。
- 相同目标、参数、工作目录、窗口显示方式与权限标志的普通快捷方式合并；相同 URL 合并。解析失败、安装器广告快捷方式和环境变量目标保守保留。再次导入和恢复旧排序也执行去重。
- 应用面板提供名称搜索、每页 28 个入口、添加文件以及 Explorer 文件路径拖入。
- 点击入口通过 Shell 打开原始文件，保留快捷方式自带的启动语义。
- 同页拖动排序，邻近图标以 180 ms 位移动画让位；Escape 取消拖动。图标右键菜单提供前后移动。
- Dock 暂时显示排序中的前 8 个入口，支持点击启动与显示/隐藏面板；尚未实现独立固定项。
- 窗口验证页切换 Glass、实色、深浅主题和自定义圆角裁切，展示实际背景实现与原生接口结果。
- 默认采用半透明玻璃着色和轻微高光，启动器的可见边界与实际窗口均为 32 DIP 四角圆角；Dock 使用 40 DIP 圆角。原生裁切默认开启。
- Escape 隐藏面板，Dock 保持显示；面板右上角关闭按钮退出全部窗口。

实验排序保存在 `artifacts/GlassWindow/state.json`。手动导入的有效路径可在下次启动恢复。此 JSON 仅为实验数据；正式应用仍计划使用 SQLite。

## 自动验证

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1 -Verify
```

验证会短暂显示自有背景窗口、面板和 Dock，记录 Shell 图标、合成层、焦点、搜索、分页、排序/取消、重复导入与保存恢复，并截图后自动退出。四角检查会比较窗口显示和隐藏时的实际背景像素，并临时恢复矩形系统背景，确认能检测旧问题。输出在 `artifacts/GlassWindow/verification/`。

需要检查真实鼠标事件链时运行 `scripts/run.ps1 -Verify -InputTest`。该模式会短暂在验证窗口内移动鼠标，检查释放排序与 Escape 取消，然后恢复鼠标位置；全部外部启动请求被拦截。普通 `-Verify` 不模拟鼠标输入。

接口成功、截图生成与模型验证不能替代真实鼠标拖动、阴影比较、系统透明开关、动态背景、混合 DPI 和多屏人工验证。当前不支持 AppsFolder 虚拟入口、跨页拖动、自动隐藏、运行状态或独立 Dock 固定配置。
