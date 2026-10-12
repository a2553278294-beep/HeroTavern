# Luban 表格生成

腾讯文档来源为[英雄酒馆任务记录与文档汇总](https://docs.qq.com/sheet/DYk9pS3BScGt4VmdN?tab=BB08J2)。本次同步包含 11 个 Excel 文件和 11 个 Word 文件，保留网页原名。总表用于索引；其余 10 个工作簿中的 14 张数据表参与 Luban 生成。`Dialogue.xlsx` 和 `Dialogue01.xlsx` 分别注册，保留两者现有内容。

在仓库根目录执行：

```powershell
.\Generate-Luban.ps1
```

脚本使用仓库内的 Luban 5.1.0，需要可运行该版本的 .NET 环境。它以 `all` 目标进行严格校验，并在 `Generated/Luban` 下生成 C#、二进制和 JSON。输出及日志已加入 Git 忽略规则；表格定义、枚举、源文件和校验记录纳入版本管理。

`Defines/tables.xml` 显式注册工作簿与工作表。角色关系使用 `npc_a+npc_b` 联合索引，角色部件使用 `id+color_id` 联合索引，姓名表使用列表模式。`Defines/enums.xml` 沿用现有对话配置中的枚举与中文别名。

2026 年 10 月 12 日在网页端修正并重新导出：

- `Item / Item / O3`：补齐 `description` 字段的 `string` 类型。
- `Item / 参数对照 / B10`：补齐酒水类别编号 `6`，与物品主表一致。
- `Character / Character / M2`、`Item / Item / K2`、`moster / moster / G2`：智力字段键由 C# 保留字 `int` 改为 `intelligence`。`##type` 行仍使用整数类型 `int`。
- 角色系统与调酒系统文档的 4 处对应字段名同步更新。

严格生成通过，共 14 张表、515 条记录。全部 4,580 个导出字段值已与 Excel 源数据逐项核对；生成的 C# 代码在 .NET 10 中编译并成功读取全部二进制数据，已声明的对话事件关联全部解析成功。该验证覆盖配置生成及独立读取，不代表 Unity 内的玩法联调。

`Validation/source-manifest.json` 记录来源链接和文件 SHA-256；`Validation/validation-20261012.json` 记录修正位置、逐表数量及验证结果。后续更改源表后应重新运行生成脚本，历史校验记录只对应清单中的文件版本。
