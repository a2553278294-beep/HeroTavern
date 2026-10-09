# 模块导航

[GameModule](../../../../Assets/GameScripts/HotFix/GameLogic/GameModule.cs) 缓存业务模块访问；
UI 属性为 `UIModule`，不是虚构的 IUIModule。启动层和 GameProto 按自身依赖边界取模块。

| 任务 | 先读取的源码 |
|---|---|
| 计时器 | [ITimerModule](../../../../Assets/TEngine/Runtime/Module/TimerModule/ITimerModule.cs) |
| 场景 | [ISceneModule](../../../../Assets/TEngine/Runtime/Module/SceneModule/ISceneModule.cs) |
| 音频 | [IAudioModule](../../../../Assets/TEngine/Runtime/Module/AudioModule/IAudioModule.cs) |
| FSM | [IFsm](../../../../Assets/TEngine/Runtime/Module/FsmModule/IFsm.cs)、[FsmState](../../../../Assets/TEngine/Runtime/Module/FsmModule/FsmState.cs) |
| 资源 | [资源 API](resource-api.md) |
| UI | [UI 生命周期](ui-lifecycle.md) |

`Timer.AddTimer(callback, time, ...)` 回调在前；持有 timerId，在所属生命周期结束时 RemoveTimer。
场景加载的进度参数当前拼作 `progressCallBack`。不要用默认 SceneManager 加载替代 YooAsset 场景管理。
FSM 的状态切换位置、数据约束取实际接口，不照搬其他框架的 public ChangeState 示例。
MemoryPool 对象归还前重置、归还后不访问、不重复归还。
GameModule.Shutdown/全局计时器清理只用于全局生命周期，不能做局部错误恢复。
