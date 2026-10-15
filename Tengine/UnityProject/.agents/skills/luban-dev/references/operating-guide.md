# Helper 操作

从 Unity 项目执行：

```powershell
python .agents/skills/luban-dev/scripts/luban_helper.py --help
python .agents/skills/luban-dev/scripts/luban_helper.py --data-dir ../Configs/GameConfig/Datas table list
python .agents/skills/luban-dev/scripts/luban_helper.py --data-dir ../Configs/GameConfig/Datas table get <registered-name>
python .agents/skills/luban-dev/scripts/luban_helper.py --data-dir ../Configs/GameConfig/Datas field list <registered-name>
python .agents/skills/luban-dev/scripts/luban_helper.py --data-dir ../Configs/GameConfig/Datas ref <bean-or-enum>
```

`--data-dir` 在子命令之前；table/field/row 的名称是位置参数。写入前读子命令 `--help`，不要照抄猜测参数。
复杂 JSON 用命令支持的 `--file`，不手拼 PowerShell 转义。
表名、行索引和字段名都先查询。row update/delete 的行索引不是默认的主键值。
写入字段显式给出 group；helper 的关键词推断不是业务分组依据。

helper 支持 enum/bean/table/field/row、批量、导入导出、ref 和 type 等能力。
这是已有的低层编辑器，不是事务服务：不能把其成功输出/退出码当完整业务校验。
`validate_all()` 返回 total/valid/invalid/details。它不能完整解析多行 Bean 表头等语义；
工作流保留该诊断并拒绝零表，但以真实 Luban 隔离生成的类型/引用校验为最终依据。
复杂/多行表头不要使用假定单行扁平结构的 CRUD，先读源码，使用能保留单元格结构的编辑方式。
旧 `gen` 入口已禁用，导出统一走工作流；JSON 导入的 replace 未实现时明确失败，不能退化为 append。
删除 Bean/枚举先 ref，禁止为“完成任务”使用 force 忽略引用。
缓存是派生数据，不能覆盖源；不要执行任意目录的 cache clear。
操作后的源文件 diff、校验和导出证据一并交付。
