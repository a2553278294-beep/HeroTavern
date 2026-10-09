# 事件生命周期检查

与 [事件 API](event-system.md) 配合使用；不凭“有 Remove”判断生命周期正确。

| 场景 | 必查行为 |
|---|---|
| 重复 Show/Refresh | 不重复注册；首次 RegisterEvent 与后续刷新分离 |
| Hide | 监听仍有效；回调不能假定窗口可见 |
| Close | 窗口先清理事件，再销毁子 Widget，最后自身 OnDestroy |
| Widget.Destroy | Widget 的 OnDestroy 与内部清理是不同阶段，核实实际调用链 |
| Lambda | 保存同一委托或交给局部 EventMgr；新 Lambda 无法解绑旧实例 |
| 接口无响应 | 查 Init、事件组、生成程序集和发送/监听类型 |
| 回调递归 | 更新状态与广播有去重条件，不在同类回调内无条件重发 |
| 高频通知 | 依据 Profiler 节流；计时器也要在销毁时释放 |

验收用重复开关、隐藏期间广播、销毁后广播、多次初始化覆盖，而不是只检查某个方法名出现。
