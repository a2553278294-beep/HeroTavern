# 英雄酒馆

策划文档、配置表与美术资源统一维护在 `main` 分支。

| 目录 | 内容 |
| --- | --- |
| [Resources/Characters](Resources/Characters/) | 角色立绘、Q 版角色、动作图及对应 Mask |
| [Resources/Icons](Resources/Icons/) | 物品图标 |
| [Excel](Excel/) | 配置表及文档索引 |
| [Word](Word/) | 系统策划文档 |
| [Luban](Luban/) | 配置定义、枚举、来源清单与校验记录 |
| [Tools/Luban](Tools/Luban/) | Luban 生成工具 |

当前美术资源共 64 张 PNG：角色主图 20 张、Mask 20 张、物品图标 24 张。资源命名、配置表对应关系及后续目录规划见 [美术资源说明](Resources/README.md)。

在已有工作副本中获取最新内容：

```powershell
git switch main
git pull --ff-only origin main
```

配置生成在仓库根目录运行 `./Generate-Luban.ps1`，使用方法与验证记录见 [Luban 说明](Luban/README.md)。
