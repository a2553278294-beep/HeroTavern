---
name: unity-cli
description: 使用本项目 Unity CLI 查询和验证 Unity Editor/Development Player，操作场景、Prefab、组件、资源数据库，执行 Unity 测试、编译和构建预检。涉及实际 Unity 状态时使用；纯文本、普通算法或只读源码定位不必连接 Editor。
---

# Unity CLI

## 执行入口

从 Unity 项目执行 `python .agents/scripts/workflow.py doctor`，或通过绝对路径调用该脚本。路径由脚本定位，不借用其他打开的工程。
CLI 为 `Tools/unity.exe`，不是 Unity Editor 自身的同名可执行文件。

先运行 `unity list` 获取当前注册命令，再按需阅读 [操作规程](references/operations.md)。
示例：`python .agents/scripts/workflow.py unity query list_open_scenes`。
编译/测试：`python .agents/scripts/workflow.py verify --profile unity`。

## 不变量

- 开始新操作前确认指定工程唯一且 ready。编译、导入、测试和写入串行；状态轮询不要求 ready，否则无法观察忙操作。
- 源码用代码编辑工具修改，不用 Pipeline 文本写入、eval 或 menu 绕过编辑/授权规则。
- 查询与写入不能只看名字前缀。未知命令、菜单、eval 默认不允许通过自动化入口执行。
- 写入先 preview，展示目标、输入和影响范围；得到用户明确确认后 approve，最后 apply。高风险单独确认。
- `dry_run`/`confirm` 是各命令自行实现的约定，必须核实命令实际支持；Undo 不能恢复所有资产、设置和包操作。
- 请求发送后超时视作结果未知，查询状态或检查实际目标，禁止自动重放写入。
- 测试先发现用例，分别运行 EditMode/PlayMode，轮询终态并检查实际执行数。零用例、全部跳过、失败、取消、结果不匹配均不能通过。
- 没有连接、命令、授权或必需依赖时报告 blocked；不安装、不升级、不连接其他项目兜底。

## 权威资料

根据当前任务读取 `Packages/com.unity.pipeline/Documentation~/` 中：

| 场景 | 包内文档 |
|---|---|
| 连接、鉴权、Player | `connectivity.md`、`runtime-setup.md` |
| 安全、路径、Undo | `safety-and-mutations.md`、`authoring-commands.md` |
| 编译、测试、构建 | `commands/build-and-compilation.md` |
| 资源、对象、Prefab | `commands/assets-and-files.md`、`commands/gameobjects-and-components.md`、`commands/prefabs.md` |
| 视觉、场景、设置 | `commands/capture.md`、`commands/scenes.md`、`commands/project-settings.md` |
| 新增项目命令 | `creating-commands.md` |

实时命令描述优先于复制的示例。Pipeline 更新后重新验证契约，不假定版本兼容。
截图是证据，不等于视觉验收通过。输出报告需区分机器验证和人工确认。
