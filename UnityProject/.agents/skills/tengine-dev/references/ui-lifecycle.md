# UI 生命周期

源码：[UIBase](../../../../Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs)、
[UIWindow](../../../../Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs)、
[UIModule](../../../../Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs)、
[WindowAttribute](../../../../Assets/GameScripts/HotFix/GameLogic/Module/UIModule/WindowAttribute.cs)。

- 首次创建：Inject、ScriptGenerator、BindMemberProperty、RegisterEvent、OnCreate。
- 每次显示刷新：OnRefresh；`UserData` 为第一个用户参数，`UserDatas` 为全部参数。
- 显隐：OnSetVisible(bool)。Hide 保留实例和监听，可能按 hideTimeToClose 超时关闭。
- 关闭：RemoveAllUIEvent、子 Widget 销毁、窗口 OnDestroy、GameObject 销毁。
- 生命周期回调无 userData 参数，没有 OnClose。避免 async void，转到返回 UniTask 的私有方法并处理异常。

```csharp
[Window(UILayer.UI, "ActualCollectedPrefabLocation")]
public class ExampleUI : UIWindow
{
    protected override void OnRefresh()
    {
        if (UserData is int itemId) { /* 按已确认的业务参数刷新 */ }
    }
}
// 异步打开并等待实例：
var window = await GameModule.UI.ShowUIAsyncAwait<ExampleUI>();
GameModule.UI.HideUI<ExampleUI>();
GameModule.UI.CloseUI<ExampleUI>();
```

UILayer 包括 Bottom/UI/Top/Tips/System。资源地址必须与收集器相符。
OnUpdate 只在需要逐帧行为时覆写；没有必要把所有刷新放进 Update。
`AddUIEvent` 的限制见 [事件](event-system.md)，Widget 创建见 [ui-patterns.md](ui-patterns.md)。
