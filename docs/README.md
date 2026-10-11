# 开发文档索引

文档基线：2026-10-11。项目工作名：澄桌（ClearDesk）。

## 阅读顺序

1. [PRODUCT.md](PRODUCT.md)：理解产品范围和交互。
2. [ARCHITECTURE.md](ARCHITECTURE.md)：理解模块职责与技术边界。
3. [DEVELOPMENT.md](DEVELOPMENT.md)：准备本机环境和开发流程。
4. [VALIDATION.md](VALIDATION.md)：执行首轮技术验证。
5. [ROADMAP.md](ROADMAP.md)：更新任务进度和阶段验收。
6. [首轮验证结果](evidence/2026-10-11-glass-window.md)：查看当前原型的检查结果和窗口截图。

[decisions/0001-platform-and-stack.md](decisions/0001-platform-and-stack.md) 记录初始选型。后续重大技术调整使用新的 ADR 记录原因、结果与影响。

## 文档约定

- 区分“计划”“已验证”和“已交付”，未执行的验证保持待验证状态。
- 任务完成须提供代码、测试结果或人工验证记录，不能仅依据文档描述勾选完成。
- 技术验证证据存入 `evidence/`，命名建议为 `YYYY-MM-DD-topic.md`；截图可用相同前缀。
- 文档中的软件版本和环境结果来自明确检查；升级后重新记录。
- 有效产品范围以 PRODUCT 为准，进度以 ROADMAP 为准，技术取舍以 ADR 为准。
