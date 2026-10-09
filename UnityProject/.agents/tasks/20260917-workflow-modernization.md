# TEngine 开发工作流重整

- ID: 20260917-workflow-modernization
- 状态: implemented / automated gates passed；生产字体与设备视觉验收未完成
- 负责人: Codex / 项目维护者

## 目标与验收

- [x] 统一 AGENTS 入口和四项项目技能，清除退出使用的旧资料。
- [x] 本地与 CI 共用 doctor/check/verify/domain/report 命令，失败不误报通过。
- [x] 授权、轮询、日志脱敏和项目隔离具备可执行回归测试。
- [x] HTML 共用烘焙服务支持隔离 Prefab 生成、预览和资源验证。
- [x] 校正文档、技能引用及配置分片说明。
- [x] 完整解决方案及可连接 Editor 下的实际验证完成。

## 范围与决策

- 以当前工作区为基线，保留已有配置分片、HybridCLR、场景和设置改动。
- 四项技能保持单一维护源，不修改用户级配置和第三方 Pipeline 包。
- 使用 Python 3.11 标准库实现工作流；领域工具依赖按需检查，不自动安装。
- 重要任务仅此一份记录，临时日志和审批位于忽略提交的 runs 目录。

## 授权

- 用户在本线程明确要求实施完整方案，确认直接删除项目旧规范资料。
- 允许工作流/技能/关联文档、必要 Editor 适配及测试代码变更。
- 不包含真实构建、包/项目设置修改、平台切换、签名、上传、提交或推送。
- 自动测试仅允许隔离的临时测试输入和自身创建的测试对象，不操作用户场景。

## 实施与证据

- 规划基线: Unity 6000.0.56f1 / CLI 1.0.0-beta.3 / Pipeline 0.3.1-exp.1。
- 规划阶段 `dotnet build UnityProject.sln --nologo -v:q -clp:ErrorsOnly`: 0 错误，41 警告。
- 实施开始 `status --project-path . --format json`: STATUS_NO_INSTANCES。
- [环境预检](../runs/20260917T044727Z-00b1f1f8/report.md)：Editor 已恢复；Python 3.11.9、.NET SDK 10.0.301，Unity/CLI/Pipeline 与基线一致。
- [本地 full](../runs/20260917T045107Z-fd2b9630/report.md)：结构、Python、解决方案、浏览器、普通/分片导出、Editor 编译、资源检查及两种测试全部 passed。
- [最终 CI 模式 full](../runs/20260917T045858Z-22e30ee5/report.md)：11 个必需阶段全部 passed。65 个 Python 测试；28 个 C# 工程/1098 个源码输入，最后一次完整解决方案 build 为 0 错误、2 警告；10 个 EditMode、1 个 PlayMode 用例，无跳过。
- 最终回归在真实中文加空格路径下运行浏览器及两种导表；浏览器 PC/mobile/pad、图片/字体等待、缺失资源拒绝、零值和确定性通过。Unity 回归包括 v1/v2、图片导入、字体/滑条、预览无写入、输出冲突、Prefab GUID 重跑及三种尺寸布局。
- 当前项目配置源实际隔离生成校验 passed，19 个生成文件保存在 runs，没有覆盖业务产物。最终报告明确保留旧 helper 的多行表头误报作为辅助诊断。
- [真实 CLI 烘焙预览](../runs/20260917T044727Z-01fcc487/report.md)：7 节点、1 图片，返回 importer 前后状态、内容哈希及宿主 Canvas 配置；未执行生产 Prefab 写入。
- [独立技能行为审查](../runs/skill-behavior-20260917.md)：两轮共 11 个只读决策练习，覆盖正向、近似误触发、负向和跨技能；不等同自动执行全部场景。
- 实测发现并修复：CLI 状态/嵌套结果形状、域重载连接状态、类别精确匹配、旧结果/零测试、防重复写入、审批后 Editor 状态漂移、UTF-8、滑块零值截图、图片 importer 明细及缺 TMP 的明确失败。
- 当前 item 表使用多行 Bean 表头，旧 helper 会误报；[真实配置隔离生成](../runs/20260917T045638Z-3e56e321/report.md)通过。validate 现以真实 Luban 为最终依据，保留 helper 辅助诊断。
- 以上运行与规划基线分开保留；失败及中断现场未删除、未改写成通过。
- 四项 skill-creator quick_validate 均通过（Windows 使用 `python -X utf8`）；维护范围 `git diff --check` 通过。运行证据和 Python 缓存已验证被 Git 忽略。

## 剩余风险

- Editor 已恢复。当前项目缺少 TMP Settings/默认字体，自动化不得导入或自动降级；新增明确 Legacy Text 选项。
- 浏览器和 Unity 测试覆盖隔离用例，不等于真实业务 UI、触摸、安全区或发布平台验收。
- 当前 ConfigSystem 模板未配对释放 TextAsset；这是已有集成限制，本次未修改业务加载器或生产配置产物。
- CI 验证是在本机设置 CI=true 后调用同一个非交互入口，没有部署或声称执行远端 runner。
- 旧规范文件已清除；空目录清理由执行环境策略拒绝。Git 不追踪空目录，结构检查只允许无内容的目录壳。
- 未自动安装/升级工具、未修改全局技能或第三方 Pipeline、未进行生产打包/平台切换/提交/推送/发布。
