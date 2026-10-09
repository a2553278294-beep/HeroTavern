---
name: html-to-ugui
description: 将 UI-DSL HTML 烘焙为 JSON 并通过 Unity CLI 生成 UGUI Prefab，处理本地图片、控件和 PC/mobile/pad 布局。用于 Unity UGUI 原型或 HTML 转 Prefab；普通网站、UI Toolkit 或仅修改既有窗口业务逻辑不触发。
---

# HTML 到 UGUI

## 输入

确认 UI 目标、设计分辨率、本地 HTML/图片/字体、配置资源和明确的输出 Prefab 路径。
先读 [UI-DSL](references/ui-dsl-spec.md)，涉及兼容性再读 [JSON](references/json-schema.md) 和 [控件](references/control-mapping.md)。
不是任意网页转换器：不执行用户脚本，不联网拉取资源，不把 CSS 字体文件自动变为 TMP 字体。

## 执行

1. HTML 只有一个命名根；需要生成的节点声明 type/name。图片只用 image/div，适配意图优先显式。
2. 从 Unity 项目运行 `python .agents/scripts/workflow.py ui bake path/to/input.html --width 1920 --height 1080`。
3. 烘焙等待字体和图片，保存 JSON 与浏览器截图到 runs；缺失必需图片失败。检查零值、层级和文字，不只检查 JSON 存在。
4. 读取 [unity-cli](../unity-cli/SKILL.md)，执行 `ui preview --json <ui.json> --config <Assets/...asset> --prefab <Assets/...prefab>`。
5. 展示 Prefab 覆盖、图片复制/importer 变更及输入文件列表。明确确认后 approve（`--high-risk`）再 `ui apply`。
6. 查询 `tengine_validate_assets` 并运行相关 EditMode/PlayMode 测试。PC/mobile/pad 截图和人工视觉验收分别记录。

CLI `tengine_bake_ugui` 与窗口共用 `HtmlToUGUIBakeService`。自动化在 PreviewScene 生成，不保存当前场景。
同路径重跑保存现有 Prefab GUID；这不保证手工修改的内容或任意组件 fileID 会保留，覆盖前必须说明。
预览返回解析后的图片资源列表、整体内容哈希、覆盖状态及输入文件清单（inputFiles）。保存的是生成根节点，不包含临时 Canvas；
按返回的 `canvasIntegration` 配置宿主 CanvasScaler，并验证实际框架 Canvas 下的效果。
窗口交互仍可指定当前 Canvas，不能把窗口操作当作隔离自动化。
v1 保留固定坐标；v2 使用 anchor/layoutHint。safeArea 当前只转为拉伸意图，不生成运行时安全区组件，须在框架运行环境验收。

## 失败与交付

缺图、无 TMP 默认字体、路径越界、同级重名、输出冲突在预检失败；不生成“看似成功”的残缺 Prefab。
默认文字模式为 TMP；只有用户明确选择 UGUI Text 时才在 `ui preview` 加 `--legacy-text`，该选择写入审批，不自动降级。
写入中断先检查已报告路径、GUID 和实际资源，不自动重试或回滚整个工程。
输出包含 HTML/JSON、截图、Prefab 路径/GUID、节点/图片统计、问题列表和验证报告。
浏览器截图不证明 Unity 视觉正确；Editor 不连接时 Prefab、字体匹配和设备适配验收标 blocked。
