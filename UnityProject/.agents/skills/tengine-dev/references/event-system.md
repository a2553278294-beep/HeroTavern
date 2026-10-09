# 事件系统

源码：[GameEvent](../../../../Assets/TEngine/Runtime/Core/GameEvent/GameEvent.cs)、
[GameEventMgr](../../../../Assets/TEngine/Runtime/Core/GameEvent/GameEventMgr.cs)、
[UIBase](../../../../Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs)。

- `GameEvent` 支持 int/string ID。发送和监听的参数类型、顺序必须完全一致。
- `AddUIEvent` 只接收 **int** ID，支持 0..4 个泛型参数；没有 string 重载。
- 在 `RegisterEvent()` 内注册 UI 事件。框架销毁路径清理监听，隐藏窗口不会解绑。
- 非 UI 对象用 `GameEventMgr.AddEvent` 和实例 `Clear()` 成对管理，或保存原委托调用 `RemoveEventListener`。
- `GameEvent.Shutdown()` 是全局生命周期操作，不能用于关闭单个窗口。
- 接口事件使用项目现有 `[EventInterface(...)]` 和生成的 `Xxx_Event` ID；不要手写生成类。
- `GameEventHelper.Init()` 在当前 `GameApp.Entrance(object[])` 中执行。新增事件先检查组定义和生成器结果，不造一个不存在的事件组。

框架调用片段，`eventId` 与回调由业务定义：

```csharp
protected override void RegisterEvent()
{
    AddUIEvent<int>(eventId, OnValueChanged);
}
private void OnValueChanged(int value) { /* 更新本窗口 */ }
```

字符串事件确有需求时先 `RuntimeId.ToRuntimeId(name)`，再给 UI 注册；不要把字符串直接传入 `AddUIEvent`。
`GameEvent` 没有 `UnRegisterAll`、`RegisterListener`、`ClearAll`。
更多检查见 [event-antipatterns.md](event-antipatterns.md)。
