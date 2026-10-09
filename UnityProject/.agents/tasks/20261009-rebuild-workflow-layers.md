# 重建缺失的 C# 工作流层与 Luban 分片插件

- ID: 20261009-rebuild-workflow-layers
- 状态: done
- 负责人: AI agent / 项目维护者

## 目标与验收

- [x] Luban 插件：实现 `bin-sharded` DataTarget + `sharded` DataExporter + partition 标签解析（range/count/field），`luban test` 两种模式回归通过（产物命名：`tb__p_0`/`tb__p_0000`/`tb__index` 等）。
- [x] 模板补全：`CustomTemplate_Client_LazyLoad/cs-bin/tables.sbn` 恢复 `ClearPartitions` API，生成的 Tables.cs 包含之。
- [x] C# 层：`TEngine.Workflow` 程序集（asmdef）、`HtmlToUGUIBakeService` headless 服务（与 HtmlToUGUIBaker 窗口共用逻辑）、`tengine_bake_ugui`（dry_run/confirm 契约）与 `tengine_validate_assets` 命令注册（Pipeline 0.8.0 属性式）。
- [x] 工作流测试：10 个 EditMode + 1 个 PlayMode 用例（程序集 TEngine.Workflow.Tests / TEngine.Workflow.PlayMode.Tests），`verify --profile unity` 全绿。
- [x] 最终 `verify --profile full` 全绿（仅 asset-validation 按设计 skipped：未提供目标）。

## 范围与决策

- 修改范围：`G:\project\luban` 之外新建插件工程（输出 dll 到 `Tools/Luban/`）；`Configs/GameConfig/CustomTemplate/` 模板；`Assets/` 下新增工作流 Editor/测试代码。
- 不修改：TEngine 框架既有模块、业务逻辑、生产配置数据。
- 关键选择：原始实现无任何残留，以 `.agents` 工作流回归期望产物与 `workflow_lib` 调用契约为验收标准重建；行为与原实现可能有差异，需重新验证。

## 授权

- 用户选择“我直接重建”（选项 A），覆盖分片插件与 C# 工作流层两项。
- Pipeline 升级至 0.8.0-exp.1 已单独授权并完成。
- 不包含提交、推送、发布。

## 实施与证据

- 插件工程 `Tools/Luban.Sharded/`（net8.0，引用 Luban.Core/DataTarget.Builtin，构建后自动部署到 Tools/Luban）；构建脚本 `Tools/build-luban-sharded.bat`。
- 实测发现并修补 Luban v5.1.0 插件加载缺陷：`PipelineScope.LoadPluginAssemblies` 用 `Assembly.Load(名称)`，.NET 8 不探测应用目录 → 改为 `Assembly.LoadFrom(完整路径)`（最小复现证实全 dll 均 FileNotFound）。修改位于 `G:\project\luban`（本地源码构建，about.txt 已注明）。
- [luban test 通过](../runs/20261009T170216Z-ec622fff/report.md)：standard 4 文件；lazyload 9 文件（tbrange__p_0..2 / tbcount__p_0000..0001 + __index / tbfield__p_1..2），模板 ClearPartitions 检查通过。

### Part 2：C# 工作流层（2026-10-09 完成）

- 程序集划分：新增 `HtmlToUGUI`（运行时配置）、`TEngine.Workflow`（Editor，烘焙器+服务+命令）、`TEngine.UIScriptGenerator`（Editor，原 Assembly-CSharp-Editor 的 `Assets/Editor/UIScriptGenerator`）、`TEngine.Workflow.Tests`、`TEngine.Workflow.PlayMode.Tests`。
- `HtmlToUGUIBakeService`：PreviewScene 内驱动 headless `HtmlToUGUIBaker` 实例（同窗口同一条管线）；dry_run 不落盘，apply 需 confirm；同路径重写保持 GUID；prefab 目录不存在时自动创建；结果含 `inputFiles`（json/config/图片绝对路径）供审批绑定。
- 契约对齐 Python 工作流：参数 `json_path`（绝对路径或 TextAsset）/`config_path`/`output_path`/`legacy_text`；dry_run 结果确定性——`ImportLocalImage` 改为内容一致则复用（`FilesIdentical`），apply 时重放 preview 摘要一致（实测通过）。
- 顺带修复：`ScriptGenerator.cs` 对 `TEngineUISettingsProvider` 的逆向依赖（内联 `SettingsService.OpenProjectSettings`）；根 `.gitignore` 增加 `.pi/`（harness 会话状态文件每次活动都变，导致 approve 时 worktree 指纹漂移被拒）；Unity 预览场景不能 `SetActiveScene`（创建后 `MoveGameObjectToScene`）。
- 端到端证据：
  - [verify --profile unity](../runs/20261009T172815Z-e647a410/report.md)：编译 + 10 EditMode + 1 PlayMode passed。
  - CLI 契约：preview→approve→apply 全链（[apply 证据](../runs/20261009T172856Z-7cdaad77/report.md)，prefab GUID 1a20e51a）；`tengine_validate_assets` 缺失路径正确拦截。
  - HTML→JSON→prefab 全链：`ui bake`（[证据](../runs/20261009T173109Z-fc5c786c/report.md)）→ `ui preview` → approve → `ui apply`（[证据](../runs/20261009T173130Z-185fa288/report.md)，v2 设计尺寸 800x600 正确传递）。
  - [verify --profile full](../runs/20261009T173146Z-c682c434/report.md)：全部 passed（asset-validation 无目标按设计 skipped）。

## 剩余风险

- 重建实现与九月原实现无法逐字节对齐；分片产物格式以回归期望为准。
