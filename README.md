# 英雄酒馆

游戏工程、策划文档、配置表与美术资源统一维护在 `HeroTavern` 分支。

| 目录 | 内容 |
| --- | --- |
| [Tengine](Tengine/) | 程序工程与配套工具；Unity 工程入口为 `Tengine/UnityProject` |
| [Resources/Characters](Resources/Characters/) | 角色立绘、Q 版角色、动作图及对应 Mask |
| [Resources/Monsters](Resources/Monsters/) | 怪物单只图及对应群组图 |
| [Resources/Icons](Resources/Icons/) | 物品图标 |
| [Excel](Excel/) | 配置表及文档索引 |
| [Word](Word/) | 系统策划文档 |
| [Luban](Luban/) | 配置定义、枚举、来源清单与校验记录 |
| [Tools/Luban](Tools/Luban/) | Luban 生成工具 |

当前角色、怪物和物品图标共 532 张 PNG：角色资源 464 张、怪物资源 27 张、物品图标 41 张。2026-10-20 新增 17 张酒类图标，并将 Item 对应记录更新为实际 PNG 文件名。既有 UI 资源位于 `Resources/ui01/`，不计入上述三类资源清单。Q 版按动作、方向和帧拆分；当前怪物表含 15 条配置，12 种有单只图和群组图，另有 3 种单体怪物图。资源命名、配置表对应关系及后续目录规划见 [美术资源说明](Resources/README.md)。

在已有工作副本中获取最新内容：

```powershell
git switch HeroTavern
git pull --ff-only origin HeroTavern
```

使用 Unity 打开 `Tengine/UnityProject`。工程记录的 Unity 版本为 `6000.0.23f1`。

根目录 `Excel/` 保存腾讯文档导出的策划源表。策划源表校验在仓库根目录运行 `./Generate-Luban.ps1`，使用方法与验证记录见 [Luban 说明](Luban/README.md)。程序适配的运行配置及生成入口位于 `Tengine/Configs/GameConfig`，沿用该目录的字段结构和生成流程。
