# 首轮玻璃窗口验证

- 日期：2026-10-11。
- 分支：`spike/glass-window`。
- 程序：`experiments/GlassWindow/GlassDesk.GlassWindow.csproj`。
- Windows：`10.0.22631.0`。
- 项目内 SDK：10.0.401；运行时：10.0.12。
- 窗口 DPI：144，即 150% 缩放；WPF 渲染层级：2；高对比度关闭。
- 当前结论：程序检查与本机视觉初查通过；V1 整体验收仍未完成。

## 构建与运行

Release 构建通过，零警告、零错误。SDK ZIP 的 SHA-512 与微软官方版本元数据一致。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1 -Verify -InputTest -NoBuild
```

首次在普通沙箱中读取桌面像素失败；改在可访问交互桌面的运行环境中执行本项目验证程序后，截图成功。验证过程使用程序自有的背景窗口，不重启 Explorer，也不改系统透明设置。

## 已确认

- 启动器与 Dock 均为非 layered HWND；Acrylic、客户区扩展和系统圆角调用返回成功。
- Dock 的 no-activate 样式及 `WM_MOUSEACTIVATE` 返回值符合预期；显现 Dock 不改变前台窗口。
- Shell 索引达到实验上限 196 个入口；图标从后台 STA 线程传到 UI 前已冻结。
- 搜索过滤、分页、排序正反向移动、取消恢复、越界拒绝与重复导入检查通过。
- 实验 JSON 排序保存/恢复、损坏状态文件回退与保存失败反馈检查通过。
- Escape 绑定有效，可隐藏启动器并保留 Dock。
- 注入真实指针事件后的同页拖动排序与 Escape 取消通过，没有触发外部启动；结束后恢复鼠标位置。
- 显式圆角区域可应用；深浅主题截图中能观察到受控背景的模糊色彩变化。

本次 23 项程序检查通过，明细见 [smoke-report.json](smoke-report.json)。这份报告不包含本机快捷方式的完整路径。

## 视觉证据

| 图片 | 观察 |
| --- | --- |
| [深色面板](launcher-dark.png) | 图标和标签可读，受控背景呈模糊变化 |
| [浅色面板](launcher-light.png) | 布局一致，文字和边界可辨 |
| [系统圆角 Dock](dock-system-corners.png) | UI 胶囊边框与系统窗口轮廓存在差异 |
| [显式胶囊裁切](dock-capsule.png) | 可生成胶囊轮廓，尚需验证区域外点击和阴影 |

截图只截取自有窗口的边界；外部阴影不在完整截图范围内，因此不能据此判定阴影验收通过。非激活 Dock 的材质比活动面板更接近均匀着色，仍需进一步比较和优化。

## 未完成

- 人工连续拖动、快速反向和右键菜单操作验收；已完成的注入指针测试仅覆盖同页释放排序与 Escape 取消。
- 自定义轮廓外部命中、阴影、不同缩放与窗口尺寸比较。
- 动态背景、系统透明开关、高对比度实际切换。
- 多屏、100%/200% 与混合 DPI、显示器拔插和休眠恢复。
- Shell 带参数/工作目录样例、协议、失效入口、打包应用与虚拟项目拖入。
- 性能、资源释放的长期观测，以及完整应用级测试。

## 后续决策

继续 WPF 实验，先补足 V1 的交互与轮廓验收。Dock 暂取前 8 个入口，JSON 仅用于实验；正式应用的独立固定项与 SQLite 仍按原计划实现。
