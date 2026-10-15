# TEngine AI 开发工作流

维护基线：2026-10-09。版本以 doctor 的实际输出为准。
入口是[仓库 AGENTS](../AGENTS.md)及[Unity 项目 AGENTS](../UnityProject/AGENTS.md)。
本地与 CI 共用 Python 入口，不依赖 OpenSpec、Unity MCP、个人技能或记忆服务。

## 研发闭环

需求确认 -> 读取源码与技能 -> 实现 -> 自动验证 -> 审查 -> 证据交付。

小修改直接实施并交付摘要。跨模块、高风险或多阶段任务使用[一份任务模板](../UnityProject/.agents/templates/task.md)，记录目标、验收、决策、授权和证据；不创建多文档审批体系。
先检查工作区已有修改；不恢复用户已删除文件，不修改第三方 Pipeline 包来迎合工作流。

| 任务 | 技能 |
|---|---|
| 框架、生命周期、事件、资源、热更 | [tengine-dev](../UnityProject/.agents/skills/tengine-dev/SKILL.md) |
| Editor/Player、场景、资源、测试和构建预检 | [unity-cli](../UnityProject/.agents/skills/unity-cli/SKILL.md) |
| 配置结构、数据、普通/分片导出 | [luban-dev](../UnityProject/.agents/skills/luban-dev/SKILL.md) |
| HTML/UI-DSL 到 UGUI Prefab | [html-to-ugui](../UnityProject/.agents/skills/html-to-ugui/SKILL.md) |

`.agents/skills` 是唯一维护源，通过 AGENTS 按任务显式读取，不假设所有客户端原生发现该目录。
跨技能只组合必要内容，例如配置驱动 UI 同时涉及 Luban、框架和 Editor 验证；普通文案不触发全套 Unity 检查。
参考文档仅导航；源码、程序集和实时 Editor 结果有优先权。

## 环境与预检

当前实际版本：Unity 6000.0.23f1（以 `ProjectSettings/ProjectVersion.txt` 为准）、Pipeline 0.3.1-exp.1（以 `Packages/com.unity.pipeline/package.json` 为准）、CLI 以 doctor 实测为准，不要求升级。
需要 Python 3.11+、Git、项目对应 .NET SDK、Unity 生成的完整解决方案。
[requirements.txt](../UnityProject/.agents/scripts/requirements.txt) 描述可选领域依赖；缺工具时报告阻塞，由维护者决定安装。
浏览器转换另需已安装的 Playwright Chromium；脚本不会自动下载。官方源不可达时可设 `PLAYWRIGHT_DOWNLOAD_HOST=https://cdn.npmmirror.com/binaries/playwright` 后手动执行 `python -m playwright install chromium`。

以下命令从 `UnityProject` 执行，也可以从任意目录调用脚本绝对路径：

```powershell
python .agents/scripts/workflow.py doctor
python .agents/scripts/workflow.py check
python .agents/scripts/workflow.py unity list
```

CLI 位于 `Tools/unity.exe`，不是 Unity Editor 的同名二进制。
每次 Editor 操作绑定规范化项目路径；新操作要求该项目唯一且 ready。忙时只允许已审核的状态/诊断查询；
轮询始终重新确认项目唯一性，错误项目、多实例或持续断开均阻塞。
不通过读取/打印 `Library/Pipeline` 描述文件诊断鉴权，不自动启动其他项目代替。
Linux/macOS CI 可做离线检查；Editor 验收需要可运行该项目 CLI 的受支持 runner。CLI 需按 Pipeline 包 README 安装到 `Tools/unity.exe`，仓库不自带该二进制。

## 验证矩阵

```powershell
python .agents/scripts/workflow.py verify --profile docs
python .agents/scripts/workflow.py verify --profile code
python .agents/scripts/workflow.py verify --profile unity --test-filter TEngine.Workflow --filter-type assembly
python .agents/scripts/workflow.py verify --profile full --asset Assets/TEngine/Extension/HtmlToUGUI/HtmlToUGUIConfig.asset
```

| Profile | 必需阶段 |
|---|---|
| docs | 四技能结构、引用、退役依赖检查、Python 测试 |
| code | docs 检查加完整解决方案构建；生成工程需包含项目源码 |
| unity | 实际能力发现、Editor 编译、明确筛选的 EditMode/PlayMode 测试；给定资产的验证 |
| full | 上述全部，加隔离的浏览器、Luban 普通/分片回归和当前配置源实际生成校验 |

默认测试筛选器仅选本次工作流测试，**不代表全部游戏测试**。改业务时显式选择相关程序集、分类或名称。
没有资产目标时资产阶段 skipped，不代表检查过整个工程；资源任务必须传 `--asset`。
新 asmdef/C# 文件需要 Unity 导入并重新生成工程。旧 csproj 的绿色结果不能证明新文件已编译。
异步启动必须确认收到响应，持续轮询最终结果，核对用例身份、数量、每项结果和汇总。
零用例、全跳过、失败、过期结果、编译错误不能通过。

## 写入与授权

查询和预览不等于授权。写入前展示具体项目、操作、参数、输入和影响路径；范围变化重新确认。
不自动运行任意菜单、eval、文本写入或名称像查询的未知命令。

```powershell
python .agents/scripts/workflow.py ui preview --json input.json --config Assets/TEngine/Extension/HtmlToUGUI/HtmlToUGUIConfig.asset --prefab Assets/AssetRaw/UI/Example.prefab
python .agents/scripts/workflow.py approve --plan <preview.json> --by <reviewer> --reason <actual-confirmation> --high-risk
python .agents/scripts/workflow.py ui apply --plan <preview.json> --approval <approval.json>
```

`<...>` 为上一步输出/真实确认引用，不能原样执行。approve 只能在用户或 CI 人工批准后调用，不允许代理自行写“已同意”。
审批绑定工作树、项目、命令、输入哈希，1 小时有效、一次尝试。审批记录防误操作，不是抗本地恶意修改的密码学权限系统。
同项目使用锁串行；不要删除有效锁。异常退出留下锁时核实进程和在途操作后由维护者处理。
查询/轮询可以有限重试；写入超时视作未知结果，先查目标/状态，不自动重放。
删除、覆盖、项目/包设置、平台切换、真实构建必须单独批准。不自动安装、升级、提交、推送、签名、上传或发布。

## 领域流程

Luban：结构与引用预检 -> 确认源数据修改 -> validate -> preview/approve/apply -> 产物检查 -> 编译和 Editor 验证。
`validate` 不写生产产物，而在 runs 内对当前配置实际生成；旧 helper 对多行 Bean 表头的诊断仅供辅助。
`luban preview --mode lazyload` 保留分片导出；`--mode standard` 显式选择普通导出，不自动回退。
标准模式也写同一产物目录，不能在生产工作区随便对比两个模式；回归应使用隔离副本。
`luban test` 在 runs 内运行两种真实导表；apply 先取得本次源数据的隔离导出，再逐文件比对生产产物的路径与哈希，
不接受旧文件、错误模式或不完整产物冒充成功。
二进制兼容性要验证代码/数据组合。分片缓存释放与 YooAsset 资源释放不是同一件事。

HTML：本地被动 UI-DSL -> 浏览器烘焙（等待字体和图片）-> JSON v1/v2 -> CLI 预检 -> 授权 -> PreviewScene 生成 Prefab -> 引用/序列化/布局测试。
图像导入可能修改 importer；同路径重跑保持 GUID，但会覆盖 Prefab 内容。失败报告可能写入路径，不自动全局清理。
`ui test` 验证浏览器三端尺寸、字体/图片加载、零值及重复生成确定性。默认 TMP 模式需要已配置字体资源；
明确选择 UGUI Text 时使用 `ui preview --legacy-text`，不自动改用另一种文字系统。
Prefab 不包含临时 Canvas，按返回的 canvasIntegration 设置宿主 CanvasScaler。`safeArea` 只是布局意图，不会生成安全区组件。
Unity 字体、PC/mobile/pad 布局、安全区、触摸与视觉仍需实际 Editor/设备验收，不能用浏览器截图代替。

## CI 与证据

CI 调用相同的非交互命令并保留退出码；不要使用 `|| true` 或空测试集使门禁变绿。
运行目录 `.agents/runs/<run-id>` 已忽略提交，保存脱敏 JSON/Markdown、命令输出及截图。
将所需运行目录作为 CI 受控 artifact 保留即可，工作流不会上传。分享前仍检查个人路径与业务数据。

| 状态 | 含义 | 退出码 |
|---|---|---|
| passed | 本次选定必需阶段都有成功证据 | 0 |
| failed | 命令/业务/测试或结构检查失败 | 1 |
| blocked | 环境、授权、连接或必需阶段缺失 | 2 |
| skipped | 未运行的步骤，必须有原因 | 必需步骤跳过时整体 2 |

```powershell
python .agents/scripts/workflow.py report --run <run-directory>
```

report 仅重排已执行证据，不补写成功。中断报告保持 blocked。
构建、测试通过也不等于视觉/发布验收完成；交付列明实际门禁、未运行项、用户既有变更是否保留。
技能行为回归见[场景集](../UnityProject/.agents/evals/scenarios.json)：检查真实工具选择和产物，不能用关键词命中代替正确性。
