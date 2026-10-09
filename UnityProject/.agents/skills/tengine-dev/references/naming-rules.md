# 命名与绑定

- 跟随相邻代码：类型/方法 PascalCase，普通私有字段 `_camelCase`；不要批量改名无关代码。
- 窗口使用项目 `[Window]`，Widget 继承 UIWidget；事件接口用现有事件组及生成器。
- 异步业务方法返回 UniTask，Async 后缀；调用方 await 或显式 Forget 并安排异常观察。
- 节点前缀来自 `ScriptGeneratorSetting` 的实际规则，查找该资源和生成器源码再生成绑定。
- TMP_InputField、TMP_Dropdown 等长前缀优先于 TMP；不要只依据旧前缀表修改 Prefab。
- HTML 烘焙按当前规则确定性归一名称；首次归一可能改变原节点名，生成后重新核对绑定路径。

业务资源优先框架 API；Editor 内置字体/配置加载与业务资源有不同职责，不全仓替换 `Resources.Load` 或 `Instantiate`。
业务跨模块通信通常用事件或已有公开接口，不为追求规则而引入虚构抽象。
禁止编造 `SetSpriteAsync`、`ReleaseSprite`、`OnClose`、string `AddUIEvent`。
先核实源码，再在当前任务范围内修正引用；不恢复删除的规范或个人技能。
