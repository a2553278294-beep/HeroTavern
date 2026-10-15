# 运行时配置

本项目使用二进制与 YooAsset，不安装另一套 Luban.Runtime 或添加 Resources.Load 示例。
模板位于 `Configs/GameConfig/CustomTemplate`，输出为 GameProto 中的加载器及 GameConfig 类。

`ConfigSystem.Tables` 首次访问会 Load；它依赖 ResourceModule 已初始化。不能凭目录名假定某个启动步骤已调用 Load。
普通生成会在构造中加载表；懒加载模板在访问时加载，分片接口见 [集成](tengine-integration.md)。
`GameProto` 不依赖 GameLogic，因此加载器使用 ModuleSystem 获取资源接口，而不是业务 GameModule。

当前模板缺少 TextAsset 配对释放，这是现有集成限制，不属于“ClearPartitions 已经释放”的功能。
若任务修改此处，需要确认 ByteBuf 是否拷贝/持有 bytes、卸载后数据存活、重复 Load 和分片释放边界，修改模板再授权导出。
表内引用可能延迟建立；访问生成属性前检查返回 null 与跨分片引用行为。
热更前验证代码、bytes 与 index 同版本；发布和缓存淘汰需要独立授权。
