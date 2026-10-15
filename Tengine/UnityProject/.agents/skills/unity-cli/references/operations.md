# Unity CLI 操作规程

所有示例从 Unity 项目运行。工作流脚本无交互、无自动安装，本地和 CI 相同。

## 查询

```powershell
python .agents/scripts/workflow.py doctor
python .agents/scripts/workflow.py unity list
python .agents/scripts/workflow.py unity query list_open_scenes
python .agents/scripts/workflow.py unity query get_player_settings
```

参数放在 JSON 文件中，通过 `--params-file` 传入，避免 PowerShell 转义差异。
直接查询底层 CLI 时固定 `--project-path` 和 `--format json`：

```powershell
& ./Tools/unity.exe command --project-path . list_tests --mode all --format json
```

## 预览、授权、执行

```powershell
python .agents/scripts/workflow.py unity preview tengine_bake_ugui --params-file request.json
python .agents/scripts/workflow.py approve --plan <preview.json> --by <approver> --reason <confirmation-reference> --high-risk
python .agents/scripts/workflow.py unity apply --plan <preview.json> --approval <approval.json>
```

先向用户展示 preview，再在确认后执行 approve。approve 不会替用户决定是否批准。
审批绑定项目、命令、参数、输入内容和预览时的工作树状态，并有有效期；代码、输入或目标变化后重新预览。
一次审批只允许一次尝试；网络超时或中途失败也消耗审批，先检查实际状态再决定下一次动作。
CI 的审批由上游人工审批阶段产生；记录不是密码学签名，不能保护有本地文件修改权的恶意调用者。

入口只支持已审核命令；不提供任意透传。需要新增能力时检查源码、风险和返回契约，再扩充目录和回归测试。
命令支持 dry_run 时预览实际执行 dry_run，否则只展示静态操作范围并注明未验证。
删除、覆盖、设置、包、构建类 preview 标记 high，approve 还需 `--high-risk`。
部分底层设置命令会调用全局 `AssetDatabase.SaveAssets`，并非只保存目标字段。
预览时说明可能保存其他脏资源；存在未处理的用户改动时先停止，不能把修改一个设置的确认扩大成保存所有资源。

## 验证与结果

```powershell
python .agents/scripts/workflow.py verify --profile code
python .agents/scripts/workflow.py verify --profile unity --test-filter TEngine.Workflow --filter-type assembly
python .agents/scripts/workflow.py report --run <run-directory>
```

默认测试过滤器只选择本次工作流测试，不等于项目全部业务测试。
验证指定资产用可重复的 `--asset Assets/...`。未指定资产时资产阶段标记 skipped，而不是通过。
输出在 `.agents/runs/<run-id>`，包括 `report.json`、`report.md` 和脱敏命令记录。
退出码 `0=passed`、`1=failed`、`2=blocked`；运行中断仍留下未完成阶段，不能被 report 提升为通过。

## 故障边界

- `STATUS_NO_INSTANCES`：请维护者打开本工程并确认 Pipeline Server；入口不会启动、安装或重置 Editor。
- 域重载：只对查询/轮询有限重试，重新检查同项目实例；不重复发送起始命令。
- 缺少测试：检查测试程序集导入与当前注册清单，不改成运行不相关的第三方测试凑数。
- 结果显示 completed 但业务结果失败：报告失败。传输成功不是测试、构建或烘焙成功。
- Player：使用显式 `--runtime-path`，先读 `runtime_status`，检查其 workingDirectory，不能用 Player 结果替代 Editor 验证。
- 不输出 `Library/Pipeline/.unity-pipeline-port` 内容。连接鉴权由底层 CLI 管理。

## 图像证据

先阅读包内 `commands/capture.md` 和实时描述。当前 `capture_game_view` 是相机 RenderTexture 渲染，
不等于真正的 GameView 截屏，也不能证明 ScreenSpaceOverlay UGUI 已渲染。
本工作流尚未将 capture 纳入只读白名单，因为它支持磁盘写入；不要通过 query 或 eval 绕过这一限制。
浏览器烘焙截图由 `ui bake`/`ui test` 存到 runs；Unity UGUI 的真实 GameView/设备截图和人工确认单独留证。
场景、相机或窗口状态不得为了截图未经确认修改，安全区和触摸必须在实际目标环境验收。
