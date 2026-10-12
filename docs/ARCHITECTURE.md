# 技术架构

## 总体方案

更新日期：2026-10-12。当前代码集中在 `experiments/GlassWindow`，使用 WPF、Win32/Shell COM、Windows Composition 和 JSON。App/Core/Infrastructure 与 SQLite 是正式应用目标，尚未迁移。

## 当前原型实现

| 实现 | 职责 |
| --- | --- |
| `App` / `SingleInstance` | 启动组合、同用户同会话单实例、重复启动唤回、显隐和退出 |
| `MainWindow` / `LauncherViewModel` | 手选入口、搜索分页、排序取消与启动 |
| `IconPoolWindow` / `IconPoolViewModel` | 独立候选池、搜索多选、确认后添加 |
| `CatalogService` | 后台 STA 扫描与完整启动描述去重，图标冻结后传给 UI |
| `DockWindow` / `DockPreferencesStore` | 固定或最近使用排序（最多 8 项）、九宫格单击/拖动、缩放与屏内限制 |
| `SurfaceWindow` / `ClippedBackdrop` | 非 layered 前景、独立合成模糊层、圆角与位置/显隐/层级同步 |
| `SettingsWindow` / `AppearanceSettingsStore` | 实时透明度与保存、高级材质与诊断 |
| `TrayIcon` | 原生托盘菜单、Explorer 重启通知、注册重试与资源清理 |

### 窗口与事件

主面板允许激活，Dock 设置 `WS_EX_NOACTIVATE` 并返回 `MA_NOACTIVATE`。图标池和设置随主面板收起与恢复，所有窗口禁用任务栏按钮。最小化只收起面板及附属窗口，Dock 与其玻璃保持显示；关闭、Alt+F4、非排序状态 Escape 和托盘隐藏命令隐藏全部窗口。

原生最小化的隐藏通过 Dispatcher 延后，恢复先 Show 再设置 Normal，防止保留最小化尺寸。九宫格按下时捕获鼠标，未超过系统拖动阈值时松开作为单击；超过阈值后移动 Dock，释放只保存位置。应用按钮与缩放 Thumb 不参与空白拖动；位置和缩放通过屏幕坐标及起始 DPI 计算。

单实例使用每用户、每会话命名 Mutex 和 EventWaitHandle。已有实例监听显示事件，通过 Dispatcher 恢复窗口；第二进程发送事件并退出，避免固定托盘 GUID 争用。运行脚本发现已有实例时跳过构建，直接恢复已有窗口。

### 当前持久化

| 文件（`artifacts/GlassWindow/`） | 数据 | 保存时机 |
| --- | --- | --- |
| `panel-state.json` | 手动确认的原始入口路径与顺序 | 添加、移除或完成排序 |
| `dock-layout.json` | `Left`、`Top`、`Scale` | 结束拖动或尺寸/位置操作 |
| `appearance-settings.json` | `Transparency`（0–100） | 停止调整 350 ms、关闭设置或退出 |
| `dock-preferences.json` | `SortMode`、`RecentLaunchKeys` | 修改排序选项或成功启动入口 |

显示窗口前读取透明度和 Dock 布局；入口仅恢复手动选择，不自动采用扫描候选。旧 `state.json` 不迁移。配置原子写入，读取失败有默认值，保存失败有反馈。主题与材质仍为会话设置。

`LauncherViewModel` 维护最多 512 条启动身份的最近顺序，成功启动后移到首位，失败不记录；固定模式下记录使用但不改变 Dock。最近模式从已选择入口中按记录排序，未使用项稳定按主面板顺序补齐。启动身份沿用 `LaunchKey`，同目标但参数或权限不同的入口保留独立记录。排序只影响 `DockEntries`，不改动面板顺序；历史不会自动将未选择或已移除的应用加入 Dock。

## 正式应用目标

采用本地优先的 Windows 桌面架构。WPF 承担界面与输入，Core 定义业务规则和接口，Infrastructure 负责 Windows 与数据适配。

```mermaid
flowchart TD
    App[ClearDesk.App：启动器 / Dock / 设置] --> Core[ClearDesk.Core：模型 / 排序 / 搜索 / 服务接口]
    App --> Infra[ClearDesk.Infrastructure：服务实现与启动组合]
    Infra --> Core
    Infra --> Shell[Windows Shell：应用来源 / 图标 / 启动]
    Infra --> Native[Win32：窗口 / 热键 / 显示器 / 事件]
    Infra --> Storage[SQLite / 图标缓存]
```

这些项目尚未生成。依赖方向是后续创建解决方案时的约束：Core 不引用 WPF、Windows API 或 SQLite；App 在启动时组合服务；Infrastructure 实现 Core 接口。

## 模块职责

| 模块 | 主要职责 |
| --- | --- |
| ClearDesk.App | MVVM、窗口、视图模型、网格布局、拖动反馈、主题 |
| ClearDesk.Core | 启动入口模型、搜索规则、候选排序、配置模型与服务接口 |
| ClearDesk.Infrastructure | Shell COM、原生窗口服务、热键、持久化、缓存、日志 |
| experiments/GlassWindow | 当前可运行的入口池、面板、Dock、设置、托盘与验证 |
| experiments/ShellIntegration | 入口枚举、图标读取、拖入与启动验证 |
| experiments/DockBehavior | 焦点、显隐、窗口激活与显示环境验证 |
| ClearDesk.Core.Tests | 有实际意义的排序、搜索和数据规则测试 |

## 窗口与材质

- 启动器、Dock 和设置采用独立窗口，共享服务和数据。
- 启动器允许输入；Dock 的显现与悬停采用不主动激活的行为。
- WPF 首先验证非 layered 原生 HWND 与 DWM 背景材质的组合。
- Windows 11 22H2+ 的 `DWMWA_SYSTEMBACKDROP_TYPE` / `DWMSBT_TRANSIENTWINDOW` 是 Desktop Acrylic 的文档化验证路线。
- `AllowsTransparency=true` 会改变窗口渲染路径，不能假定与系统材质、阴影和圆角无条件兼容。
- DWM 默认圆角不等于参考图的大圆角胶囊；轮廓、裁切、透明区命中和阴影必须一起验证。
- Mica 主要依据壁纸形成材质，不能直接作为任意下层窗口内容的实时模糊方案。
- 系统材质不可用时，使用产品允许的降级外观。

本机后续验证发现系统 Acrylic 背景仍会画满矩形，不遵守自定义 HWND 大圆角区域。当前实验关闭矩形系统背景，使用独立无激活合成窗口承载 Gaussian blur，并通过圆角几何裁切效果；WPF 前景保持清晰，两层同步位置、显隐和窗口顺序。对应像素检查与鼠标输入结果见 [合成背景验证](evidence/2026-10-11-clipped-backdrop.md)。这仍属于实验实现，多屏和阴影需继续验收。

若 WPF 验证不能达到核心要求，评估 WinUI 3 的 `DesktopAcrylicBackdrop`，保留 Core 和 Infrastructure。框架切换依据记录到 ADR。

## 启动入口

建议的逻辑模型如下；字段和数据库表在 Shell 验证后定稿。

| 模型 | 核心字段 |
| --- | --- |
| LaunchEntry | Id、DisplayName、LaunchKind、SourcePath、Target、Arguments、WorkingDirectory、AppUserModelId、IconCacheKey |
| LauncherPlacement | EntryId、SortOrder；页号由当前网格容量计算 |
| DockPlacement | EntryId、SortOrder |
| AppSettings | 主题、材质、图标大小、快捷键、可选启动设置 |

`LaunchKind` 区分快捷方式、文件/目录、协议、可执行文件和打包应用。保留原始快捷方式路径，通过 Shell 启动时保留它的参数和工作目录；解析后的信息主要用于展示和匹配。

桌面与开始菜单路径通过 Known Folder API 获取；打包应用从 AppsFolder 枚举并按 AUMID 启动。导入重复项的判定需结合入口类型和完整启动描述，不能只比较 exe 文件名。

外部拖入首轮支持文件路径类数据，Shell 虚拟项目另行验证。图标使用 Shell 图标接口提取，后台读取并缓存；损坏或失效入口保持可编辑，并给出状态反馈。

## 拖拽排序

维护已提交顺序与拖动中的候选顺序。只有候选插入位变化时更新布局目标，逐帧动画只改变 Transform。快速改变方向时取消旧动画并从当前视觉位置继续。

松手后以事务保存最终顺序；取消操作不写入数据库。窗口尺寸或网格容量变化时重算分页，避免排序依赖物理像素或固定页号。

## 系统适配

- 以 Per-Monitor V2 DPI 为目标，在第一个窗口实验中确认清单与运行时行为；处理负屏幕坐标和混合缩放。
- 全局热键采用注册机制，冲突时反馈并允许更换。
- 初始运行状态通过窗口枚举建立，后续使用 WinEvent 事件更新；识别可见性、cloaked 状态和应用标识。
- 不依赖启动时返回的 PID 判定全部运行状态，不仅按进程名归组。
- 激活已有窗口遵守 Windows 前台权限规则；无法切换时提供可理解的反馈。
- 浮动 Dock 默认不预留系统工作区；若后续需要预留空间，评估 AppBar。
- 处理显示器拔插、主屏切换、任务栏变化、Explorer 重启和休眠恢复。

## 数据与线程

运行数据计划存储在 `%LocalAppData%\ClearDesk\`，SQLite 数据、图标缓存和日志分目录保存。UI 线程只承担界面工作；COM/Shell 操作按其线程模型执行，使用后台 STA 工作线程时不得直接跨线程传递 COM 对象。

数据库迁移带版本号，迁移前备份；配置写入和排序写入使用事务。首次实现时定义日志轮转策略，避免记录敏感启动参数。当前尚未创建数据库或应用数据目录。
