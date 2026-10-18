using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic.UI;
using GameLogic.World;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 主界面 HUD（OnGUI 残留部分）：厨房酒单/调酒、悬赏板、交易/查看弹出面板、游戏结束遮罩。
    /// 顶栏（时钟/档位/暂停）、货币、结算分栏、NPC 交互气泡已迁移到 <see cref="MainHudUI"/>（uGUI）；
    /// 对话/交易已迁移到 <see cref="DialogueUI"/> / <see cref="TradeUI"/>（uGUI；对话销毁时回调 <see cref="OnDialogueClosed"/> 恢复走时）。
    ///
    /// 对话/交易/查看已迁移 <see cref="DialogueUI"/>/<see cref="TradeUI"/>/<see cref="CharacterUI"/>（uGUI）；
    /// 剩余弹出面板（<see cref="BountyPanel"/>）遵循统一约定：<c>Open(...)</c> / <c>Close()</c> / <c>IsOpen</c> /
    /// <c>Bounds</c> / <c>CloseRequested</c> / <c>AcknowledgeClose()</c>。
    /// 面板自己只管画，<b>暂停 / 恢复走时由本文件做</b>（见 <see cref="HandlePanelCloseRequests"/>）。
    /// </summary>
    public sealed class WorldHud
    {
        private enum Panel
        {
            None,
            KitchenChoice, // 经营案：点击厨房浮现酒单/调酒两个小型 UI（无独立采购入口）
            Brew,
        }

        private readonly WorldSceneBootstrap _boot;
        private readonly List<Rect> _panelRects = new List<Rect>();

        // 弹出面板实例（仅余悬赏板 OnGUI；对话/交易/查看已迁移 uGUI，关闭由窗口销毁回调处理）。
        private readonly BountyPanel _bountyPanel = new BountyPanel();
        private Panel _panel = Panel.None;
        private CharacterData _selected;
        private SettlementReport _lastReport;
        private bool _gameOver;

        // M13：当前绑定事件的世界实例。WorldModule.Restart 会换一个新实例，HUD 必须每帧检出并重绑，
        // 否则结算分栏 / 破产遮罩会一直收上一局旧实例的事件（重开后不再刷新）。
        private WorldSimulation _hookedSim;

        // M13：破产瞬间的快照。破产后 Sim 仍会 Tick（Time 继续走），所以存活天数 / 总流水必须锁定在结束那一刻，
        // 否则遮罩上的天数会一直往上跳。
        private int _gameOverDay;
        private long _gameOverRevenue;
        private int _gameOverBountyCompleted;
        private SettlementReport _gameOverReport;

        // M13：重开按钮的防连点闩锁（点一次即进入新对局）。
        private bool _gameOverRestarted;

        // 调酒面板选择状态。
        private int _baseIdx;
        private int _ingAIdx;
        private int _ingBIdx;
        private bool _ice;
        private string _brewMessage = string.Empty;

        public WorldHud(WorldSceneBootstrap boot)
        {
            _boot = boot;
            AttachSim(Sim);
        }

        /// <summary>
        /// M13：把结算 / 破产事件绑到指定的世界实例（先解绑旧实例再绑新的）。
        /// 构造时绑一次；<see cref="WorldModule.Restart"/> 换了新实例后由 <see cref="EnsureSimHooked"/> 每帧检出并重绑。
        /// 换实例同时清空上一局的结算报告 / 破产快照与所有面板，避免旧对局的 UI 残留。
        /// </summary>
        private void AttachSim(WorldSimulation sim)
        {
            if (_hookedSim != null)
            {
                _hookedSim.SettlementReady -= OnSettlementReady;
                _hookedSim.GameOver -= OnGameOver;
            }

            _hookedSim = sim;
            _lastReport = null;
            _gameOver = false;
            _gameOverDay = 0;
            _gameOverRevenue = 0L;
            _gameOverBountyCompleted = 0;
            _gameOverReport = null;
            _gameOverRestarted = false;
            _selected = null;
            _panel = Panel.None;

            // 只关面板、不动暂停位：新对局的暂停状态由新的 WorldSimulation 自己决定。
            _bountyPanel.Close();
            GameModule.UI.CloseUI<DialogueUI>();
            GameModule.UI.CloseUI<TradeUI>();
            GameModule.UI.CloseUI<CharacterUI>();

            if (sim != null)
            {
                sim.SettlementReady += OnSettlementReady;
                sim.GameOver += OnGameOver;
            }
        }

        /// <summary>M13：每帧检出 <see cref="WorldModule.Restart"/> 换过世界实例并重新绑定事件。</summary>
        private void EnsureSimHooked()
        {
            if (!ReferenceEquals(_hookedSim, Sim))
            {
                AttachSim(Sim);
            }
        }

        public CharacterData SelectedCharacter => _selected;

        private WorldSimulation Sim => _boot.Sim;

        /// <summary>气泡「交流」：选中角色并开对话框（对话案 3.3 的「静止」侧由 StartDialogue 做）。</summary>
        public void BeginInteractionTalk(CharacterData character)
        {
            _selected = character;
            _panel = Panel.None;
            StartDialogue();
        }

        /// <summary>气泡「交易」：开交易面板，不夺走走时。</summary>
        public void BeginInteractionTrade(CharacterData character)
        {
            _selected = character;
            _panel = Panel.None;
            StartTrade();
        }

        /// <summary>气泡「查看」：开角色面板（属性 / 背包两页），不夺走走时。</summary>
        public void BeginInteractionView(CharacterData character)
        {
            _selected = character;
            _panel = Panel.None;
            OpenCharacterDetail();
        }

        public void OpenKitchenChoice()
        {
            _panel = Panel.KitchenChoice;
        }

        /// <summary>直接开调酒面板（厨房选择的二级；视觉验收截图等不经点击的路径用）。</summary>
        public void OpenBrewPanel()
        {
            _panel = Panel.Brew;
        }

        /// <summary>点悬赏栏建筑：开悬赏板，并把世界停住（悬赏案 2.1 的「静止」侧由本文件做）。</summary>
        public void OpenBoardPanel()
        {
            _bountyPanel.Open();
            GameModule.World.SetPaused(true);
        }

        /// <summary>关掉所有 HUD 面板（点空白 / 面板上的「关闭」/ 选中角色离场）。</summary>
        public void ClosePanels()
        {
            _panel = Panel.None;
            _selected = null;
            ClosePopupPanels();
            MainHudUI.Current?.HideBubble();
        }

        /// <summary>
        /// 关掉四个弹出面板；若刚才开着的是暂停类面板（悬赏板 / 对话）则顺手恢复走时，
        /// 否则保持玩家自己的暂停选择（交易 / 查看面板本身不夺走走时）。
        /// </summary>
        private void ClosePopupPanels()
        {
            bool wasPausing = _bountyPanel.IsOpen || DialogueUI.Current != null;
            _bountyPanel.Close();
            GameModule.UI.CloseUI<DialogueUI>();
            GameModule.UI.CloseUI<TradeUI>();
            GameModule.UI.CloseUI<CharacterUI>();
            if (wasPausing)
            {
                GameModule.World.SetPaused(false);
            }
        }

        /// <summary>鼠标是否落在任一 HUD 面板上（防止穿透点击世界）。</summary>
        public bool IsPointerOverPanel(Vector2 mouseScreenPosition)
        {
            var guiPos = new Vector2(mouseScreenPosition.x, Screen.height - mouseScreenPosition.y);
            for (int i = 0; i < _panelRects.Count; i++)
            {
                if (_panelRects[i].Contains(guiPos))
                {
                    return true;
                }
            }

            // 四个面板的 Bounds 独立于 _panelRects（由面板自己刷新），按打开态纳入防穿透；
            // 关掉的面板 Bounds 可能仍是上次的位置，故必须先看 IsOpen。
            return _bountyPanel.IsOpen && _bountyPanel.Bounds.Contains(guiPos);
        }

        public void Draw()
        {
            var sim = Sim;
            if (sim == null || !sim.IsRunning)
            {
                return;
            }

            _panelRects.Clear();

            // M13：重开对局后世界实例会换新，先确认事件仍绑在当前实例上。
            EnsureSimHooked();

            // 先处理上一帧 GUI 里点出来的关闭请求，再画：面板 Draw 开头会把 CloseRequested 清零。
            HandlePanelCloseRequests();

            switch (_panel)
            {
                case Panel.KitchenChoice: DrawKitchenChoice(); break;
                case Panel.Brew: DrawBrew(sim); break;
            }

            DrawPopupPanels(sim);
            if (_gameOver)
            {
                DrawGameOver(sim);
            }
        }

        /// <summary>
        /// 每帧检查四个面板的关闭请求（统一约定：面板只置位，宿主来关）。
        /// - 悬赏板 / 对话：期间世界是停住的，关掉即 <c>SetPaused(false)</c> 恢复走时；
        /// - 交易 / 查看：<b>不暂停</b>，收起面板时不动暂停位（经营案「暂停与否由玩家决定」，
        ///   面板开着也能照常看时间，代价只是酒保不会来打断）。
        /// </summary>
        private void HandlePanelCloseRequests()
        {
            if (_bountyPanel.CloseRequested)
            {
                _bountyPanel.Close();
                _bountyPanel.AcknowledgeClose();
                GameModule.World.SetPaused(false);
            }

        }

        /// <summary>画四个独立面板：矩形默认居中由面板自己的 DefaultArea 给，本文件只转发矩形与世界实例。</summary>
        private void DrawPopupPanels(WorldSimulation sim)
        {
            if (_bountyPanel.IsOpen)
            {
                _bountyPanel.Draw(BountyPanel.DefaultArea(), sim);
                AddPanelRect(_bountyPanel.Bounds);
            }

        }

        /// <summary>查看窗口（uGUI CharacterUI）销毁时回调：清选中角色（查看不暂停，无需恢复走时）。</summary>
        public void OnCharacterPanelClosed()
        {
            _selected = null;
        }

        /// <summary>对话窗口（uGUI DialogueUI）销毁时回调：恢复走时（对话案 3.3「静止」侧收尾）。</summary>
        public void OnDialogueClosed()
        {
            GameModule.World.SetPaused(false);
        }

        private void AddPanelRect(Rect rect)
        {
            if (rect.width > 0f && rect.height > 0f)
            {
                _panelRects.Add(rect);
            }
        }

        // ==================== 厨房：酒单 / 调酒 ====================

        private void DrawKitchenChoice()
        {
            var rect = CenterRect(220, 90);
            BeginPanel(rect, "厨房", "common/ui_common_frame_01");
            if (GUI.Button(new Rect(20, 30, 80, 40), "酒单", ReadableButton))
            {
                // 经营案：酒单为独立窗口（uGUI DrinkMenuUI），营业中也可打开查看（锁定只读）。
                _panel = Panel.None;
                GameModule.UI.ShowUIAsync<DrinkMenuUI>();
            }

            if (GUI.Button(new Rect(120, 30, 80, 40), "调酒", ReadableButton))
            {
                _panel = Panel.Brew;
            }

            EndPanel();
        }

        private void DrawBrew(WorldSimulation sim)
        {
            var rect = CenterRect(560, 360);
            BeginPanel(rect, "调酒", "common/ui_common_frame_02");
            GUILayout.BeginArea(new Rect(10, 30, rect.width - 20, rect.height - 40));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("返回", ReadableButton, GUILayout.Width(60)))
            {
                _panel = Panel.KitchenChoice;
            }

            if (GUILayout.Button("关闭", ReadableButton, GUILayout.Width(60)))
            {
                ClosePanels();
            }

            GUILayout.EndHorizontal();

            var bases = AvailableMaterials(sim, ItemKind.BaseWine);
            var ings = AvailableMaterials(sim, ItemKind.Ingredient);
            int iceCount = sim.Warehouse.Count(ItemCatalog.IceCube);

            DrawSelector("基酒", bases, ref _baseIdx);
            DrawSelector("配料1", ings, ref _ingAIdx);
            DrawSelector("配料2", ings, ref _ingBIdx);

            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && iceCount > 0;
            if (GUILayout.Button($"加冰：{(_ice ? "开" : "关")}（库存 {iceCount}）", ReadableButton))
            {
                _ice = !_ice;
            }

            GUI.enabled = wasEnabled;

            bool canBrew = bases.Count > 0 && ings.Count > 0;
            GUI.enabled = wasEnabled && canBrew; // 三槽都有内容才点亮（调酒案）。
            if (GUILayout.Button("调酒", ReadableButton))
            {
                var result = sim.Brewing.Brew(sim.Warehouse, bases[_baseIdx].Id, ings[_ingAIdx].Id, ings[_ingBIdx].Id, _ice);
                switch (result.Outcome)
                {
                    case BrewOutcome.NewDiscovery:
                        _brewMessage = $"酿出了 {ItemCatalog.Get(result.Recipe.ProductWineId).Name}！新酒已入图鉴";
                        break;
                    case BrewOutcome.AlreadyKnown:
                        _brewMessage = "该酒已研发，材料已返还";
                        break;
                    default:
                        _brewMessage = $"得到一杯神秘鸡尾酒（{result.MysteryTags}）";
                        break;
                }

                ClampSelectors(bases.Count, ings.Count);
            }

            GUI.enabled = wasEnabled;

            if (!string.IsNullOrEmpty(_brewMessage))
            {
                GUILayout.Label(_brewMessage, InkLabel);
            }

            GUILayout.Label("已解锁酒单：" + BuildWineListText(sim), InkLabel);
            GUILayout.EndArea();
            EndPanel();
        }

        private static List<ItemDef> AvailableMaterials(WorldSimulation sim, ItemKind kind)
        {
            var list = new List<ItemDef>();
            foreach (var def in ItemCatalog.Items)
            {
                if (def.Kind == kind && sim.Warehouse.Count(def.Id) > 0)
                {
                    list.Add(def);
                }
            }

            return list;
        }

        private void DrawSelector(string label, List<ItemDef> options, ref int index)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, InkLabel, GUILayout.Width(50));
            if (options.Count == 0)
            {
                GUILayout.Label("（仓库无存货）", InkLabel);
            }
            else
            {
                ClampIndex(options.Count, ref index);
                var def = options[index];
                if (GUILayout.Button($"{def.Name} ×{Sim.Warehouse.Count(def.Id)}", ReadableButton, GUILayout.Width(160)))
                {
                    index = (index + 1) % options.Count;
                }
            }

            GUILayout.EndHorizontal();
        }

        private static void ClampIndex(int count, ref int index)
        {
            if (count <= 0)
            {
                index = 0;
            }
            else if (index >= count)
            {
                index = count - 1;
            }
        }

        private void ClampSelectors(int baseCount, int ingCount)
        {
            ClampIndex(baseCount, ref _baseIdx);
            ClampIndex(ingCount, ref _ingAIdx);
            ClampIndex(ingCount, ref _ingBIdx);
        }

        private static string BuildWineListText(WorldSimulation sim)
        {
            var parts = new List<string>();
            foreach (int wineId in sim.Brewing.UnlockedWines)
            {
                var def = ItemCatalog.Get(wineId);
                if (def != null)
                {
                    parts.Add($"{def.Name}(价{def.Price}/标签:{string.Join("/", def.Tags)})");
                }
            }

            return parts.Count > 0 ? string.Join("、", parts) : "（空）";
        }

        /// <summary>交谈：开对话框并停住世界（对话案 3.3 的「静止」侧由本文件做）。</summary>
        private void StartDialogue()
        {
            var sim = Sim;
            if (sim == null || _selected == null)
            {
                return;
            }

            _panel = Panel.None;
            DialogueUI.PendingSession = sim.Dialogue.StartChat(_selected, sim.Time.DayOverall);
            DialogueUI.PendingNpc = _selected;
            GameModule.UI.ShowUIAsync<DialogueUI>();
            GameModule.World.SetPaused(true);
        }

        /// <summary>交易：开交易面板，不夺走走时（玩家可自己决定要不要先暂停）。</summary>
        private void StartTrade()
        {
            var sim = Sim;
            if (sim == null || _selected == null)
            {
                return;
            }

            _panel = Panel.None;
            TradeUI.PendingNpc = _selected;
            GameModule.UI.ShowUIAsync<TradeUI>();
        }

        /// <summary>查看：开角色面板（属性 / 背包两页），同样不夺走走时。</summary>
        private void OpenCharacterDetail()
        {
            if (_selected == null)
            {
                return;
            }

            _panel = Panel.None;
            CharacterUI.PendingNpc = _selected;
            GameModule.UI.ShowUIAsync<CharacterUI>();
        }

        // ==================== 结算分栏 / 游戏结束 ====================

        private void OnSettlementReady(SettlementReport report)
        {
            // 仅留作破产遮罩快照（结算分栏展示已迁移到 MainHudUI）。
            _lastReport = report;
        }

        private void OnGameOver()
        {
            _gameOver = true;
            _gameOverRestarted = false;

            // 锁定破产瞬间的快照：破产后 Sim 仍会 Tick，直接读 sim 会让天数 / 流水在遮罩上继续变化。
            var sim = Sim;
            _gameOverDay = sim != null && sim.Time != null ? sim.Time.DayOverall : 0;
            _gameOverRevenue = sim != null && sim.Tavern != null ? sim.Tavern.TotalRevenue : 0L;
            _gameOverBountyCompleted = sim != null && sim.Bounty != null ? sim.Bounty.CompletedCount : 0;
            // 同一次 0:00 里 Tavern 先抛 Settled（报告已到 _lastReport）再抛 GameOver，此处取到的就是破产当天的报告。
            _gameOverReport = _lastReport;

            GameModule.World.SetPaused(true);
        }

        /// <summary>把当日失败悬赏合并成一条文案（悬赏案 3.3-8：「悬赏名 + 发布费已返还」），供破产遮罩使用。</summary>
        private static string BuildFailedBountyText(SettlementReport report)
        {
            return string.Join("、", report.FailedBountyNames.ToArray())
                   + $"：悬赏失败，发布费已返还 {report.BountyRefund} G。";
        }

        /// <summary>
        /// 破产遮罩（M13 结算评分）。口径：
        /// - 评分 = 玩家酒馆<b>总流水</b> <see cref="TavernSystem.TotalRevenue"/>（立项规划「依据总流水评分」）；
        /// - 分档阈值来自 master-plan M13 的<b>占位表</b>（《经营案》明确「总流水口径与评分表不在本篇」），
        ///   常量上已标注「评分表未出，待策划」，正式定档后只改 <see cref="GradeRankS"/> 以下四个阈值即可；
        /// - 存活天数取破产瞬间的 <see cref="GameTime.DayOverall"/> 快照（见 <see cref="OnGameOver"/>），
        ///   不用会继续走动的当前时刻。
        /// </summary>
        private void DrawGameOver(WorldSimulation sim)
        {
            var rect = new Rect(0, 0, Screen.width, Screen.height);
            _panelRects.Add(rect);
            GUI.Box(rect, GUIContent.none);

            var center = CenterRect(460, 300);
            HudSkin.DrawFrame(center, "main/ui_main_frame_02"); // 贴图缺失时 HudSkin 回退 GUI.Box。
            GUI.Box(center, "游戏结束");
            GUILayout.BeginArea(new Rect(center.x + 20, center.y + 40, center.width - 40, center.height - 50));

            GUILayout.Label("酒馆无法支付工资与成本，经营失败。");
            GUILayout.Label($"总流水：{_gameOverRevenue}");
            GUILayout.Label($"经营评分：{GradeOf(_gameOverRevenue)}（评分表未出，待策划）");
            GUILayout.Label($"存活天数：{_gameOverDay}");
            GUILayout.Label($"累计悬赏完成：{_gameOverBountyCompleted} 条");

            // 当日报告要点（破产当天的结算报告已由结算事件送到 _lastReport，同一次 OnDaySettled 里先发报告再发 GameOver）。
            var report = _gameOverReport;
            if (report != null)
            {
                GUILayout.Label($"末日要点（第 {report.DayOverall} 天）：售出 {report.SoldCups} 杯 / 未售 {report.UnsoldCups} 杯");
                GUILayout.Label($"末日要点：酒水收入 {report.Income}，悬赏赏金 {report.BountyReward}，发布费返还 {report.BountyRefund}");
                GUILayout.Label($"末日要点：营业成本 {report.Cost}，酒保工资 {report.Wage}");
                if (report.FailedBountyNames != null && report.FailedBountyNames.Count > 0)
                {
                    GUILayout.Label(BuildFailedBountyText(report));
                }
            }

            GUILayout.FlexibleSpace();
            if (!_gameOverRestarted && GUI.Button(new Rect(0, center.height - 70, center.width - 40, 28), "重开一局（Restart）"))
            {
                _gameOverRestarted = true;
                // M13：WorldModule.Restart = Unhook 现有事件 → StartNewGame 重开；
                // 新实例会在下一帧 Draw 的 EnsureSimHooked 里被检出并重绑（AttachSim 已清空本局快照）。
                GameModule.World.Restart();
            }

            GUILayout.EndArea();
        }

        /// <summary>
        /// 经营评分分档（<b>占位</b>）。出处《经营案》「总流水口径与评分表不在本篇」，
        /// master-plan M13 给的占位档：S ≥ 10000 / A ≥ 5000 / B ≥ 2000 / C ≥ 1000 / D 以下。
        /// 评分表未出，待策划定档。
        /// </summary>
        private const long GradeRankS = 10000;
        private const long GradeRankA = 5000;
        private const long GradeRankB = 2000;
        private const long GradeRankC = 1000;

        /// <summary>按总流水取档位字母（占位评分表，待策划）。</summary>
        private static string GradeOf(long totalRevenue)
        {
            if (totalRevenue >= GradeRankS)
            {
                return "S";
            }

            if (totalRevenue >= GradeRankA)
            {
                return "A";
            }

            if (totalRevenue >= GradeRankB)
            {
                return "B";
            }

            if (totalRevenue >= GradeRankC)
            {
                return "C";
            }

            return "D";
        }

        // ==================== 工具 ====================

        private static Rect CenterRect(float width, float height)
        {
            return new Rect(Screen.width / 2f - width / 2f, Screen.height / 2f - height / 2f, width, height);
        }

        private static GUIStyle s_panelTitle;
        private static GUIStyle s_readableButton;

        /// <summary>面板标题样式：居中墨色（线性空间默认皮肤文字发灰，在羊皮纸框上对比不足）。</summary>
        private static GUIStyle PanelTitle
        {
            get
            {
                if (s_panelTitle == null)
                {
                    s_panelTitle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 14,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                    };
                    s_panelTitle.normal.textColor = new Color(0.24f, 0.18f, 0.11f);
                }

                return s_panelTitle;
            }
        }

        /// <summary>可读按钮：透明底 + 墨色文字（默认按钮贴图在线性空间呈深灰块）。点击区域不受影响。</summary>
        private static GUIStyle ReadableButton
        {
            get
            {
                if (s_readableButton == null)
                {
                    s_readableButton = new GUIStyle(GUI.skin.button)
                    {
                        fontSize = 14,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                    };
                    var ink = new Color(0.24f, 0.18f, 0.11f);
                    s_readableButton.normal.textColor = ink;
                    s_readableButton.hover.textColor = ink;
                    s_readableButton.active.textColor = ink;
                    // background=null 对 GUI.Button 不生效（仍画默认深色块），改用程序化羊皮纸底。
                    s_readableButton.normal.background = ParchmentTex;
                    s_readableButton.hover.background = ParchmentTex;
                    s_readableButton.active.background = ParchmentTex;
                }

                return s_readableButton;
            }
        }

        private static GUIStyle s_inkLabel;
        private static Texture2D s_parchmentTex;

        /// <summary>面板正文标签：墨色（默认 label 在线性空间呈浅灰，羊皮纸上不可读）。</summary>
        private static GUIStyle InkLabel
        {
            get
            {
                if (s_inkLabel == null)
                {
                    s_inkLabel = new GUIStyle(GUI.skin.label);
                    s_inkLabel.normal.textColor = new Color(0.24f, 0.18f, 0.11f);
                }

                return s_inkLabel;
            }
        }

        /// <summary>程序化羊皮纸底（2×2 纯色），供 IMGUI 按钮当浅色背景用。</summary>
        private static Texture2D ParchmentTex
        {
            get
            {
                if (s_parchmentTex == null)
                {
                    s_parchmentTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    var c = new Color(0.97f, 0.94f, 0.85f, 1f);
                    s_parchmentTex.SetPixels(new[] { c, c, c, c });
                    s_parchmentTex.Apply();
                }

                return s_parchmentTex;
            }
        }

        /// <summary>
        /// 画一块 HUD 面板：先按 <paramref name="framePath"/> 铺底框贴图（可空，缺贴图时 HudSkin 回退 GUI.Box），
        /// 再叠标题框，最后把矩形登记进防穿透列表。
        /// </summary>
        private void BeginPanel(Rect rect, string title, string framePath = null)
        {
            if (!string.IsNullOrEmpty(framePath) && HudSkin.Tex(framePath) != null)
            {
                HudSkin.DrawFrame(rect, framePath);
                // 有框贴图时不再盖默认深色 Box（线性空间下发黑、内容不可读），标题改墨色 label。
                GUI.Label(new Rect(rect.x, rect.y + 2f, rect.width, 24f), title, PanelTitle);
            }
            else
            {
                GUI.Box(rect, title);
            }

            _panelRects.Add(rect);
            // 内容坐标统一为面板局部：BeginPanel 后子控件用 (0,0)=面板左上角 的局部坐标。
            // 修复前 DrawKitchenChoice / DrawBrew 的 BeginArea/GUI.Button 用的是屏幕绝对坐标，
            // 面板框居中、内容却渲染在屏幕左上角（错位）。
            GUILayout.BeginArea(rect);
        }

        private void EndPanel()
        {
            GUILayout.EndArea();
        }
    }
}
