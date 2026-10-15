# 配置文件

直接读取[项目配置](../../../../../Configs/GameConfig/luban.conf)，用 JSON parser 修改，保留无关字段。

- `dataDir` 是数据目录。
- `schemaFiles` 包括 Defines XML 和三张 Excel 定义表；`fileName`、`type` 必须与实际文件相符。
- `targets` 的 name 用作 `-t`，manager 为 Tables，客户端 topModule 为 GameConfig。
- `groups` 控制导出选择，新增字段和表均检查客户端/服务端影响。不要把 `"c,s"` 和 `["c","s"]` 机械互换。
- 扩展参数遵循本地工具版本格式。当前源码 `LubanConfig.Xargs` 为字符串列表，不使用旧文档中的对象数组示例。
- 相对路径以配置与脚本的实际解析上下文为准；空格/中文路径需隔离测试。

最小合法结构见 [regression/luban.conf](../examples/regression/luban.conf)；它是测试用配置，不可覆盖生产注册表。
