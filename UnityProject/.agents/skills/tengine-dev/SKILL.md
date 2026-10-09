---
name: tengine-dev
description: TEngine 框架开发与问题定位。修改 UIWindow/UIWidget、GameEvent、GameModule、YooAsset 资源生命周期、HybridCLR 热更代码时使用。配置数据维护交给 luban-dev，Editor 对象操作和实际验证交给 unity-cli；普通文案或无框架依赖的算法不触发。
---

# TEngine 开发指导

TEngine 是基于 HybridCLR + YooAsset + UniTask + Luban 的 Unity 游戏框架。
先定位相关实现和调用方，再按下表读需要的主题。参考文档是导航，不是 API 保证；实际源码、程序集定义和测试结果是最终依据。

## 核心红线

1. **异步优先**：业务 IO 用 `UniTask`，处理取消、失败和销毁竞争；不引入 Coroutine。
2. **模块访问**：业务代码通过 `GameModule.XXX`；框架启动层保留现有模块初始化方式。
3. **资源必须释放**：`LoadAssetAsync` 对应 `UnloadAsset`，GameObject 用 `LoadGameObjectAsync`
4. **热更边界**：`GameEntry`、`Procedure`、`Launcher` 不热更；`GameScripts/HotFix/` 按 asmdef 划分热更程序集。
5. **事件解耦**：模块间用 `GameEvent`，UI 内部用 `AddUIEvent`

## 文档路由

根据任务类型，读取对应的 reference 文档：

| 任务类型 | 必读文档 | 进阶文档 | 优先级 |
|---------|---------|---------|--------|
| UI 开发 | [ui-lifecycle.md](references/ui-lifecycle.md) | [ui-patterns.md](references/ui-patterns.md) | P0 |
| 事件系统 | [event-system.md](references/event-system.md) | [event-antipatterns.md](references/event-antipatterns.md) | P0 |
| 资源加载 | [resource-api.md](references/resource-api.md) | [resource-patterns.md](references/resource-patterns.md) | P0 |
| 模块使用 | [modules.md](references/modules.md) | — | P0 |
| 热更代码 | [hotfix-workflow.md](references/hotfix-workflow.md) | — | P1 |
| 代码规范 | [naming-rules.md](references/naming-rules.md) | — | P1 |
| Luban 配置 | [luban-config.md](references/luban-config.md) | — | P1 |
| 项目结构 | [architecture.md](references/architecture.md) | — | P2 |
| 问题排查 | [troubleshooting.md](references/troubleshooting.md) | — | P2 |

## 实现与交付

- 输入：需求、相关源码、资源地址和验收条件；缺失的 API/地址先搜索，不凭名称补造。
- 修改调用前核实方法签名、返回类型和取消语义。生成代码回溯到模板。
- 配置数据及导出读取 [luban-dev](../luban-dev/SKILL.md)。序列化、资源导入和 Editor 验证读取 [unity-cli](../unity-cli/SKILL.md)。
- C# 修改至少执行完整解决方案构建和相关测试；Editor 不可用时明确剩余验证。
- 输出：实现变更、运行证据、资源/事件所有权、兼容性影响和未完成的验证。不得只凭代码片段或关键词评测宣称正确。
