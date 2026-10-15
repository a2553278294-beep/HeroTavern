---
name: luban-dev
description: 维护本仓库 Luban 游戏配置的表、数据、枚举、Bean、校验、普通或分片导出及运行时集成。编辑游戏配置数据时使用；普通应用设置、Unity ProjectSettings 或单纯讨论游戏技能设计不触发。
---

# Luban 开发

## 输入与边界

先确定目标表/主键、结构或数据变更、客户端/服务端分组、导出模式和兼容性要求。
源位于仓库 `Configs/GameConfig`，即从 Unity 项目出发的 `../Configs/GameConfig`。
命名空间为 `GameConfig`。生成 C# 和 bytes 不是手工编辑入口。
不要安装工具、重建 Luban 或用普通导出掩盖分片失败。

## 闭环

1. 读表注册、字段、数据和引用，确认输入结构，避免按关键词推断客户端/服务端分组。
2. 展示要修改的记录、字段和引用影响，得到集中确认。删除/重命名需单独说明兼容性影响。
3. 使用 [helper](scripts/luban_helper.py) 的真实 `--help` 操作；缺少 openpyxl 时阻塞，不自动安装。
4. 运行 `python .agents/scripts/workflow.py luban validate`。它保存 helper 诊断，再用当前 Luban 在 runs 隔离目录校验并导出，不写生产产物。
5. 运行 `luban preview --mode lazyload`，审查脚本/写入目标，确认后 approve（high-risk）和 `luban apply`。
6. 导出成功后检查产物模式、模板复制和数量，再执行完整解决方案构建、Editor 编译及相关运行测试。

`lazyload` 复用现有 `bin-sharded`、`dataExporter=sharded` 和自定义模板；普通表仍为单文件懒加载。
`standard` 必须显式选择，使用普通 `bin`；不是分片模式的故障回退。
helper 原始 CRUD 不受工作流授权文件控制，调用者仍必须取得用户确认。helper 对多行 Bean 表头等语义有局限，
其诊断仅供辅助；`validate` 以实际 Luban 的类型/引用校验和隔离产物为准，不代表运行时业务正确。

## 按需读取

| 任务 | 参考 |
|---|---|
| 项目导出、分片和加载器 | [tengine-integration.md](references/tengine-integration.md) |
| helper 参数、Excel 修改 | [operating-guide.md](references/operating-guide.md) |
| 类型和 Schema | [type-system.md](references/type-system.md)、[schema.md](references/schema.md) |
| 配置文件、生成参数 | [luban-conf.md](references/luban-conf.md)、[command-reference.md](references/command-reference.md) |
| 数据源 | [excel-format.md](references/excel-format.md)、[data-sources.md](references/data-sources.md) |
| 校验和运行时 | [validators.md](references/validators.md)、[runtime.md](references/runtime.md) |

输出：数据/模板变更、引用检查、导出模式、生成日志、编译与 Editor 证据。
失败保留输入和日志，不回滚用户已有数据，不将缺失产物或 Editor 离线写成通过。
