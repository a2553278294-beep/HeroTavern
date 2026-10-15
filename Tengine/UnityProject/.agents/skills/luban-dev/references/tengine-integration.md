# TEngine 集成与分片

权威输入：[luban.conf](../../../../../Configs/GameConfig/luban.conf)、
[加载器模板](../../../../../Configs/GameConfig/CustomTemplate/ConfigSystem.cs)。
路径从 Unity 项目出发为 `../Configs/GameConfig`。

| 模式 | 现有脚本 | 产物 |
|---|---|---|
| lazyload | gen_code_bin_to_project_lazyload.bat/.sh | cs-bin + bin-sharded + dataExporter=sharded，自定义懒加载模板 |
| standard | gen_code_bin_to_project.bat/.sh | cs-bin + bin，标准 Tables |

两者都写 `Assets/GameScripts/HotFix/GameProto` 与 `Assets/AssetRaw/Configs/bytes`。
工作流固定脚本 cwd，Windows 用参数数组调用 `cmd.exe /d /c`，设置 AI_MODE 并保留失败返回码。
不要把 Git Bash 的 `cmd //c` 示例搬进 PowerShell。
已有工具通过 `LUBAN_DLL` 显式指定，生产入口绑定仓库工具；隔离回归使用同一二进制，不重新构建。

```powershell
python .agents/scripts/workflow.py luban validate
python .agents/scripts/workflow.py luban preview --mode lazyload
# 展示预览，收到真实确认后才执行：
python .agents/scripts/workflow.py approve --plan <preview.json> --by <reviewer> --reason <confirmation> --high-risk
python .agents/scripts/workflow.py luban apply --plan <preview.json> --approval <approval.json>
python .agents/scripts/workflow.py luban test
```

## 分片契约

tags 支持：

```text
partition=field#partition_field=levelId
partition=range#partition_field=id#partition_size=1000
partition=count#partition_field=id#partition_size=1000
```

分片字段为导出的非空 int/long，禁止负值。range/count 的 map 主键必须就是分片字段。
field 可用于按关卡聚合的 list 表，示例复合索引 `levelId+waveId`。
range 从 key 计算分片；count 排序后分组，插入早期 key 可使后续分片重排，另有路由 index。
field 表不能作为生成 ref 的目标，因为仅凭行 key 无法推导分片。

生成 API 以实际表名为准：field 通过 `LoadTbXPartition(key)` 显式取表；
range/count 通过 accessor 保留 key 查询，再用对应 `ReleaseTbXPartition`。
`ClearPartitions` 清除分片与路由索引托管缓存，不清普通懒加载表，也不自动释放 YooAsset 句柄。
当前 ConfigSystem 模板仍持有 TextAsset 加载引用；释放策略需由加载器集成实现并测试，不改生成代码掩盖问题。

## 兼容性

cs-bin 按生成代码布局读行。新增字段也不能无条件宣称兼容旧客户端。
代码、数据、分片策略、模板与路由索引要作为同版本产物验证；普通/分片切换属于显式决策。
回归使用 [最小可执行 fixture](../examples/regression/luban.conf)，覆盖普通表、field/range/count，而非缺文件的演示目录。
