# 开发文档索引

更新日期：2026-10-12。项目名称：澄桌（ClearDesk）。

## 阅读顺序

1. [FEATURES.md](FEATURES.md)：了解当前软件功能、使用方法和限制。
2. [PRODUCT.md](PRODUCT.md)：理解产品范围和交互。
3. [ARCHITECTURE.md](ARCHITECTURE.md)：理解当前实现和正式模块目标。
4. [DEVELOPMENT.md](DEVELOPMENT.md)：准备本机环境和开发流程。
5. [VALIDATION.md](VALIDATION.md)：查看已验证与待验收场景。
6. [ROADMAP.md](ROADMAP.md)：更新任务进度和阶段验收。
7. [完整桌面截图](screenshots/README.md)：查看真实壁纸下的磨砂、清透、图标池、设置与 Dock 效果。
8. [最新玻璃与桌面图标验证](evidence/2026-10-12-glass-and-desktop-icons.md)：查看 142 项检查、普通启动和最终轻量 EXE 的验证结果。

[decisions/0001-platform-and-stack.md](decisions/0001-platform-and-stack.md) 记录初始选型。后续重大技术调整使用新的 ADR 记录原因、结果与影响。

## 文档约定

- 区分“计划”“已验证”和“已交付”，未执行的验证保持待验证状态。
- 任务完成须提供代码、测试结果或人工验证记录，不能仅依据文档描述勾选完成。
- 技术验证证据存入 `evidence/`，命名建议为 `YYYY-MM-DD-topic.md`；截图可用相同前缀。
- 软件功能展示图存入 `screenshots/`，使用完整桌面实拍，保留壁纸、任务栏和窗口位置，并注明场景与环境；技术局部截图留在验证证据中。
- 文档中的软件版本和环境结果来自明确检查；升级后重新记录。
- 有效产品范围以 PRODUCT 为准，进度以 ROADMAP 为准，技术取舍以 ADR 为准。
