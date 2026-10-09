# 故障定位

| 现象 | 最小诊断证据 |
|---|---|
| 编译失败 | 完整解决方案输出、实际编译输入、Editor 编译终态 |
| Editor 不可连接 | doctor 报告、明确项目路径；不读取鉴权描述文件内容 |
| UI 空白 | 资源地址、节点组件、Canvas/RectTransform、字体和图片引用、截图 |
| 事件不触发 | 注册/发送 ID 与参数、Init、窗口隐藏/销毁状态 |
| 内存增长 | 加载/释放次数、重复刷新竞争、池引用、Profiler 数据 |
| 配置失败 | 表注册、字段类型、引用、导出返回码和产物模式 |
| 真机热更失败 | 主包/热更版本配对、目标平台、AOT/裁剪、完整堆栈 |

保留失败现场；不要默认删除 Library、persistentDataPath、缓存、包锁或重置用户场景。
没有连接时静态定位可继续，但 Editor 结论标 blocked。
全局强制卸载、缓存清空和平台构建需单独说明目标并授权。
重要诊断写入当前任务的一份记录；小改仅交付摘要，不新建长期记忆体系。

资源竞争见 [resource-patterns.md](resource-patterns.md)，事件见 [event-antipatterns.md](event-antipatterns.md)，
Editor 操作见 [unity-cli](../../unity-cli/SKILL.md)。
