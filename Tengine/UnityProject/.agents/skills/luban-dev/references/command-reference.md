# 生成命令契约

生产导出只走 [项目脚本](tengine-integration.md)。以下为核验线索，不是自动安装或自组生产命令的许可。
当前本地 `dotnet ../Tools/Luban/Luban.dll --help` 可查询真实参数；help 本身可能返回 1，不能套用导出的成功判断。

| 参数 | 本项目用法 |
|---|---|
| --conf | luban.conf，路径相对受控 cwd |
| -t | client；server 需独立范围 |
| -c | cs-bin |
| -d | bin 或 bin-sharded |
| --customTemplateDir | lazyload 的自定义模板目录 |
| -x | outputCodeDir、outputDataDir、code.lineEnding、dataExporter |
| --validationFailAsError | 验证失败策略，禁止为放过错误而关闭 |
| --variant、--timeZone | 仅业务明确需要时使用并验证产物 |

不要将 `-o` 当作通用输出目录；该参数是 outputTable。
不要把仅生成代码、零输出或 watcher 启动成功当导表完成。
参数或版本变化后先在隔离 fixture 验证，更新入口契约与测试，不改第三方工具包。
