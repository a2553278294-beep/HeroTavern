# AI 工作流 Agent 通用化

- ID: 20261009-agent-neutral-workflow
- 状态: complete（Tools/ 缺失导致的 Luban/Editor 阶段阻塞见剩余风险，待维护者决策）
- 负责人: 项目维护者 / AI agent

## 目标与验收

- [x] `UnityProject/.codex` 迁移为 agent 中立的 `UnityProject/.agents`，仓库内不再存在 Codex/OpenAI 专属路径引用。
- [x] 删除 4 个技能下的 Codex 专属 `agents/openai.yaml`，`checks.py` 结构校验同步移除该强制项（SKILL.md frontmatter 校验保留）。
- [x] 文档、脚本、.gitignore、技能 references 全部指向新路径；措辞不再绑定特定客户端。
- [x] `check`、`verify --profile docs` 在新路径下通过（结构校验 + 13 个单测）；`doctor`/`full` 受环境阻塞，见剩余风险。

## 范围与决策

- 修改范围: `UnityProject/.codex` 整目录迁移；根/Unity `AGENTS.md`、`README.md`、`Books/AI-Development-Workflow.md`、`UnityProject/.gitignore`、`workflow_lib/*.py`、四项技能的 SKILL.md 与 references。
- 不修改: 历史任务文档内容（含 20260917 负责人记录）、业务源码、Unity 资产、包与项目设置。
- 关键选择: 目录名 `.agents` 对齐 AGENTS.md 生态的 agent 中立惯例（用户确认）；不添加 CLAUDE.md 等转发入口，保持 AGENTS.md 单一维护源（用户确认）；openai.yaml 删除而非保留（用户确认）。

## 授权

- 用户原始请求: "将当前项目的 ai 工作流，修改成所有 ai agent 通用的"。
- 目录名、openai.yaml 删除、不加兼容入口、验证范围 full 均经交互确认。
- 不包含提交、推送、发布、包/项目设置变更。

## 实施与证据

- [x] 目录迁移 `.codex` → `.agents`（UnityProject 未被 git 追踪，使用 Move-Item）。
- [x] 15 个文件 `.codex` → `.agents` 引用替换；`UnityProject/AGENTS.md` 客户端措辞中立化。
- [x] 删除 4×`agents/openai.yaml` 及空目录；`checks.py` 移除对应校验。
- [x] 残留扫描：全仓仅历史任务文档 `负责人: Codex` 一处，属历史记录，保留。
- [x] 重建最小单测集 `.agents/tests/test_core.py`、`test_structure.py`（13 用例，经用户授权；非恢复原 65 用例）。
- [x] 实测发现并修复 `core.py redact()` 缺陷：`Authorization: Bearer <token>` 中令牌因赋值分支先匹配而外泄，现 Bearer 脱敏独立先行（单测覆盖）。
- [x] 实测发现并修正 `tengine-integration.md` 断链 `SHARDED_TABLES.md`（目标已被删除，属迁移前既有问题）。
- [x] 按用户授权全量安装 requirements（PyYAML 6.0.3 / markdown-it-py 4.2.0 / openpyxl / playwright 1.63.0）。
- 运行报告: `.agents/runs/20261009T154521Z-03b98677`（check passed）、`.agents/runs/20261009T154741Z-2fec9665`（verify docs passed）。
- 实际命令: `python -X utf8 .agents/scripts/workflow.py check / verify --profile docs`，均 exit 0。

## 剩余风险

- `.agents/tests` 为授权重建的最小集（13 用例），非恢复原 65 用例；覆盖面较原套件窄。
- 其他 AI agent 客户端对 `.agents/skills` 的自动发现能力各异，文档已注明按 AGENTS 显式路由读取。
- ~~`Tools/unity.exe` 缺失~~ 已按 Pipeline README 官方脚本安装 Unity CLI 1.0.0-beta.13 并部署到 `UnityProject/Tools/unity.exe`（用户授权）。
- Luban 恢复：about.txt 指明 luban-next 源码构建；GitHub 直连 TLS 被重置，经用户既有 gh-proxy.com 镜像浅克隆 main 至 `G:\project\luban`（进程级 -c 代理绕过，未改用户配置），再由 `Tools/build-luban.bat` 构建。
- 实测发现并修复 `core.py write_json` 在 Windows AV 瞬时锁下 `os.replace` PermissionError 导致运行误报 failed 的缺陷（10 次×200ms 重试）。
- doctor 复验：environment passed（CLI 已识别）；editor-connection 在无运行 Editor 时诚实 blocked。
- Luban 构建完成：`Tools/Luban/Luban.dll` 及全套模块 dll，0 警告 0 错误（luban-next main）。
- 已启动项目 Editor（GUI）供 Pipeline 连接；verify full 终跑等待 CLI 报告 STATUS_READY 后执行。
- [verify full 首跑](../runs/20261009T162427Z-2e8d462f/report.md)：structure/单测/build/browser **passed**，editor-capabilities/compilation **passed**；luban 两阶段与 editor/playmode 测试 failed。
- 修复：4 个导出脚本硬编码覆盖 `LUBAN_DLL` 环境变量导致隔离导出找不到 dll → 改为 `if not defined`/默认参数形式（standard 回归随后通过）。
- Luban 定格 v5.1.0（用户要求最新版）。查明：上游 main 全历史与 classic 均无 partition/sharded 实现；luban-next 支持目录插件机制，原分片能力来自已丢失的定制插件 dll——用户决定从备份找回。
- 修正 lazyload 脚本漂移：按文档补回 `-d bin-sharded` 与 `-x dataExporter=sharded`（插件归位后即可验证）。
- Pipeline 升级（用户授权包变更）：CLI 要求 ≥0.6.0-exp.1；`unity pipeline upgrade` 将 manifest 升至 0.8.0-exp.1；嵌入旧包 0.3.1 移至 `.agents/runs/backup-com.unity.pipeline-0.3.1-exp.1`（嵌入包会遮蔽 manifest 版本，必须移出 Packages/）。
- 运维教训：Stop-Process 强杀 Unity 后 `Temp/UnityLockfile` 残留，看守循环不可只等锁文件消失，需先确认进程退出再删残留锁。
- [verify unity](../runs/20261009T164737Z-21a5c123/report.md)：capabilities/**compilation passed**；editor/playmode 测试因 TEngine.Workflow 程序集缺失按零用例规则诚实 blocked（待用户恢复 C# 层）。
- Pipeline 0.8.0 适配（`unity.py`）：discovery 需显式 `--detail compact`（默认仅 tags 汇总）；`get_console_logs` 更名 `console`（双名兼容）；新增 `test_unity_schema.py` 回归（compact/tags-only/legacy 三种形态）。
- 重大发现：9 月任务交付的 C# 工作流层（`TEngine.Workflow` 程序集、`HtmlToUGUIBakeService`、`tengine_validate_assets`/`tengine_bake_ugui` 命令、Editor 测试）在当前工作区整体缺失，与 Tools/、tests/ 缺失同源；无法从上游恢复。

## 附录：Unity 版本变更排查（2026-10-09）

- 项目实际版本降为 6000.0.23f1（ProjectVersion.txt），Books 基线 6000.0.56f1 已过时并修正；Pipeline 仍为 0.3.1-exp.1。
- `verify --profile code` 实测：structure/单测 passed；solution-build failed——`HybridCLR` 命名空间缺失（CS0246 ×2）。
- 根因：git 全局代理 127.0.0.1:7890 未运行，Unity 无法从 gitee 克隆 hybridclr_unity，PackageCache 无该包，生成的 TEngine.Editor.csproj 缺引用。
- 修复（经用户授权）：`git config --global http.https://gitee.com.proxy ""` 仅对 gitee 绕过；ls-remote 直连验证通过。
- Playwright Chromium：官方源下载 900s 超时失败，改用 npmmirror 镜像 17s 完成（经验已补入 Books 工作流文档）。
- 用户关闭 Editor 后，看守任务自动执行 batchmode：日志确认 `Exiting batchmode successfully now!`，`com.code-philosophy.hybridclr` 已进入 PackageCache，工程文件已重建。日志 `.agents/runs/unity-batchmode-resolve-20261009.log`。
- [verify full](../runs/20261009T160418Z-a405e3a0/report.md)：structure、单测、**solution-build（CS0246 消除）**、**browser-regression（Chromium 三端+零值+确定性+缺失拒绝）** 全部 passed；luban 两阶段与 Editor 三阶段因 Tools/ 缺失 blocked/skipped，整体 exit 2，记录诚实。
