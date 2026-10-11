# 澄桌（ClearDesk）更名验证

项目从 Glass Desk 更名为「澄桌」（ClearDesk）。界面标题、应用名称、辅助功能名称、命名空间、程序集、解决方案、项目文件、源码目录和构建启动脚本已同步修改。

本机工作目录仍是原有的 `F:\glass desk`。实验状态路径保留为 `artifacts/GlassWindow/state.json`，现有排序数据可继续读取。历史截图与检查报告保留当时的项目名称。

Windows 11 构建 22631、150% 缩放；`scripts/build.ps1` 的 Release x64 构建为 0 错误、0 警告。执行 `scripts/run.ps1 -Verify -NoBuild`，44 项检查通过，覆盖启动、窗口圆角、四角像素、合成层显隐与位置、搜索、分页和排序恢复。本次更名未改动鼠标拖动逻辑，没有重复鼠标注入检查。

- [新名称界面截图](cleardesk-launcher.png)
- [自动检查报告](cleardesk-report.json)

GitHub 仓库名称同步为 `Starlre/cleardesk`。根目录 `AGENTS.md` 记录后续 Git 提交说明和更新备注使用中文的约定。
