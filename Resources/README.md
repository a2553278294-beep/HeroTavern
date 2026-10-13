# 美术资源

美术资源使用英文目录，按用途分类。主图与对应 Mask 放在同一目录，文件名与配置表保持一致。

```text
Resources/
├── Characters/    角色立绘、Q 版角色、动作图与 Mask
├── Icons/         物品图标
├── manifest.json  文件清单、尺寸、大小及 SHA-256
└── README.md      资源目录与命名说明
```

2026-10-13 导入：`Characters` 共 40 张 PNG（20 张主图及 20 张 Mask），`Icons` 共 24 张 PNG。

## 角色命名

格式为 `character_<表现形式>_<动作>_<部位>_<两位编号>[_m].png`。

| 部分 | 当前取值 |
| --- | --- |
| 表现形式 | `c`：Q 版；`r`：立绘 |
| 动作 | `idle`：待机；`walk`：行走 |
| 部位 | `body`：身体；`face`：脸；`hair`：头发 |
| 编号 | `01`、`02` 等两位编号 |
| Mask | 在同名主图末尾加 `_m` |

例如，`character_c_walk_body_01.png` 对应 `character_c_walk_body_01_m.png`。

`Excel/CharacterPart.xlsx` 的 `id` 保存不含扩展名的资源名；`color_id` 保存文本配色序号 `01—06`。本批 Q 版行走部件的 `counterpart_id` 对应同部位、同编号的 `character_r_idle_*` 立绘。表中预留的部件与配色，以实际交付图片和已填写数值为准。

## 图标命名

`Excel/Item.xlsx` 的 `icon` 保存包含 `.png` 扩展名的实际文件名。本批对应物品 ID 1—24。以下四个文件保留原始末尾下划线，配置表使用相同名称：

- `icon_2_.png`
- `icon_4_.png`
- `icon_8_.png`
- `icon_15_.png`

## 后续目录

新增类型按需建立 `UI/`（界面素材）、`Environments/`（场景素材）、`Effects/`（特效贴图）和 `SourceArt/`（PSD、Aseprite 等可编辑源文件）。这些目录在对应资源首次交付时创建。

更新时提交解压后的可用资源，刷新 `manifest.json`；资源名发生变化时，同步对应配置表。角色主图及 Mask 成对更新。
