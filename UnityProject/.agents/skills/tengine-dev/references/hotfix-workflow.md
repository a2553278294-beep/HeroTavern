# 热更开发与验证

先读 [程序集边界](architecture.md)、`ProjectSettings/HybridCLRSettings.asset`、
`Assets/TEngine/Runtime/Core/UpdateSetting.cs` 与当前主包加载流程。
本项目已有混淆/加密相关改动，不能覆盖或按旧文档重新生成。

1. 确定改动位于主包还是热更程序集，检查 asmdef 引用和条件编译。
2. 核对 `GameApp.Entrance(object[])` 的调用约定；接口事件初始化先于接口使用。
3. 运行完整解决方案构建，Editor 编译和相关测试。
4. 涉及新增泛型、反射、裁剪、序列化或原生接口时记录目标平台验证项。
5. HybridCLR 生成、切平台、资源打包、Player Build、签名和部署均需额外授权；不从普通开发任务推导发布许可。

下载流程需逐步核实版本请求、清单、下载器的状态和错误；等待 Task 完成不等于成功。
不附带清空缓存、升级 HybridCLR、全量重导入等“顺手修复”。
AOT/IL2CPP 支持范围以本项目安装包和目标平台为准，不笼统宣称某类 C# 特性永远不支持。
Editor 测试不替代真机热更；交付必须列明目标版本、平台和未验证项。
