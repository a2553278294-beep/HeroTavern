# 配置集成路由

维护配置数据、类型、表注册、模板和导出请读取 [luban-dev](../../luban-dev/SKILL.md)，不要在此维护第二套命令。
配置源在仓库 `Configs/GameConfig`；生成目录在 Unity 的 `Assets/GameScripts/HotFix/GameProto/GameConfig`。

- `GameProto` 不能反向引用 `GameLogic.GameModule`；加载器模板使用 `ModuleSystem.GetModule<IResourceModule>()`。
- `ConfigSystem.cs` 是模板复制产物，本工作区未导出时可能不存在，不宣称已初始化或可运行。
- 当前 `ConfigSystem` 模板加载 TextAsset 后未释放。生成表的 `ClearPartitions` 只清托管缓存，不代表 YooAsset 已卸载；修改所有权需单独测试，不以文档“保证已释放”掩盖。
- 二进制行读取依赖生成代码布局。新增、删除、改名、重排字段均需匹配代码/数据版本，不能宣称新增字段天然前向兼容。
- 普通和分片生成 API 不同。先读生成类，不编造 `TbItem`、`LoadTbWavePartition` 等当前未生成的表成员。
