# Widget 与列表

以 [UIBase](../../../../Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs) 为准。
所有创建泛型要求 `where T : UIWidget, new()`。

| 来源 | 调用 |
|---|---|
| 已有子节点 | `CreateWidget<T>(goPath)`、`CreateWidget<T>(parentTrans, goPath)` |
| 已有对象 | `CreateWidget<T>(goRoot)` |
| 已确认资源地址 | `CreateWidgetByPath<T>(parent, location)` 或 await `CreateWidgetByPathAsync<T>` |
| Prefab 模板 | `CreateWidgetByPrefab<T>(prefab, parent)` |
| 类型名等于资源地址 | `CreateWidgetByType<T>`、`CreateWidgetByTypeAsync<T>` |

同步 `AdjustIconNum(list, number, parent, prefab)` 复用列表并销毁多余项，随后逐项刷新数据。
参数名是 **number**，不是 count。它声明了 `assetPath` 但当前实现未使用，不要提供这种误导性示例。
无 prefab 时走 `CreateWidgetByType`；需要明确地址时自行 await `CreateWidgetByPathAsync`。

`AsyncAdjustIconNum` 返回 void，内部 Forget，不能 await；`maxNumPerFrame` 必须正数。
重复调用可能重叠；需要可等待、取消或原子替换时使用业务拥有的串行循环。
不要通过 `list.Clear()` 代替销毁 Widget；不要重复包装同一个 GameObject。
销毁入口及父子顺序查 [UIWidget](../../../../Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWidget.cs)。

节点查找用 `FindChild`/`FindChildComponent<T>`，按钮用项目注册辅助方法。
Prefab 绑定路径、组件类型、命名前缀必须与实际对象一致；资源验证交给 [unity-cli](../../unity-cli/SKILL.md)。
