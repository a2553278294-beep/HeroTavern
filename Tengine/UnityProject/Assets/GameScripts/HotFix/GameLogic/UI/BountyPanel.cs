using System.Collections.Generic;
using GameLogic.World;
using UnityEngine;

namespace GameLogic.UI
{
    /// <summary>
    /// 悬赏板面板（M12 OnGUI 白模，出处《英雄酒馆-悬赏系统需求案》「二、界面」2.1-2.5）。
    ///
    /// 界面结构（2.2 整体布局）：
    /// <list type="bullet">
    /// <item>左栏：标题固定「悬赏板」→ 槽区（3 列 × 2 行，槽数取 <c>sim.Bounty.SlotCount</c>，
    ///       槽数不满 3 的倍数时最后一行留空，不另做滚动）→ 列表（一行一条，三列
    ///       <b>名称 · 线索 · 报酬</b> / <b>状态</b> / <b>动作字</b>，比槽区窄）；</item>
    /// <item>右栏：常驻详情，八个字段全部只读（发布前也不能在本界面改数值），底部左侧「发布」按钮，
    ///       按钮下方状态行报第一项不满足的原因；</item>
    /// <item>空态：<b>没有任何已点亮悬赏且没有任何占用槽</b>时，槽区与列表整体隐藏，
    ///       左栏只显示「还没有可张贴的悬赏。线索到了，空槽才会亮。」</item>
    /// </list>
    ///
    /// 槽态映射（2.3 槽位表：角标 / 主文案 / 辅文案 / 操作）集中在 <see cref="DescribeSlotCard"/> 一处维护，
    /// 槽区卡片与列表行共用同一套状态文案（2.3-3：「可发布」「线索不足」在槽区和列表都会出现，
    /// 但它们<b>不提前占空位</b>——真正占槽的只有进行中和已完成）：
    /// <list type="bullet">
    /// <item>进行中：角标「进行中」/ 名称 / 「报酬 · 剩余」/ 点开详情，不能再发布到此槽；</item>
    /// <item>可发布：角标「可发布」/ 名称 / 「线索 · 报酬」/ 点开详情，指定空槽后才能发布，本卡不占发布位；</item>
    /// <item>已完成：角标「已完成」/ 名称 / 「下一天入账」/ 不能领奖，0:00 入账后变空槽；</item>
    /// <item>线索不足：角标「线索不足」/ 名称 / 「不可发布」/ <b>整卡禁用</b>，不占可发布的空位；</item>
    /// <item>空槽：角标「空槽」/「张贴」/「位置」（已被指定为下一次发布位置时辅文案改「已指定发布」）。</item>
    /// </list>
    ///
    /// 发布（3.2）：四项校验一律走 <see cref="BountySystem.CheckPublish"/>，本面板不复制任何规则；
    /// 任一项不满足时按钮禁用，且不扣发布费、不扣线索、槽仍为空（是否扣改全在 <see cref="BountySystem"/> 里）。
    ///
    /// 暂停（2.1-2）：本界面期间时间暂停，但<b>暂停由调用方做</b>——<c>WorldHud</c> 打开时
    /// <c>GameModule.World.SetPaused(true)</c>、关闭时 <c>SetPaused(false)</c>；本类只负责画与改悬赏状态。
    ///
    /// 装饰（master-plan M12）：框线走 <see cref="HudSkin"/> 的 common 组 frame 贴图，
    /// 贴图缺失时 <see cref="HudSkin.DrawFrame"/> 内置回退 <c>GUI.Box</c>，界面不会开天窗。
    /// </summary>
    public sealed class BountyPanel
    {
        // ==================== 需求案原文文案 ====================

        /// <summary>左栏标题固定文案（2.2）。</summary>
        public const string TitleText = "悬赏板";

        /// <summary>右栏详情标题（2.2：未选中时标题为「详情」，八个字段为空白占位）。</summary>
        public const string DetailTitleText = "详情";

        /// <summary>空态文案（2.2-1：仅在无占用槽且列表无条目时出现）。</summary>
        public const string EmptyStateText = "还没有可张贴的悬赏。线索到了，空槽才会亮。";

        /// <summary>列表区标题（2.4：槽位下方，比槽区窄）。</summary>
        public const string ListTitleText = "列表";

        /// <summary>详情底部发布按钮文案（2.2 发布区）。</summary>
        public const string PublishButtonText = "发布";

        /// <summary>线索未持有时的旁标（2.5 详情「线索」行）。</summary>
        public const string NotHeldText = "未持有";

        /// <summary>无物资 / 无安定度变动时的显示（2.5）。</summary>
        public const string NoneText = "无";

        /// <summary>未选中详情时的空白占位（2.2：八个字段为空白占位）。</summary>
        public const string BlankFieldText = "—";

        // 角标（2.3 角标列 = 2.5 状态列）
        private const string BadgeAvailable = "可发布";
        private const string BadgeIntelLack = "线索不足";
        private const string BadgeRunning = "进行中";
        private const string BadgeCompleted = "已完成";
        private const string BadgeEmpty = "空槽";

        // 主文案 / 辅文案
        private const string EmptyMainText = "张贴";
        private const string EmptySubIdle = "位置";
        private const string EmptySubSelected = "已指定发布";
        private const string IntelLackSubText = "不可发布";
        private const string CompletedSubText = "下一天入账";

        // 操作说明（2.3「操作」列；卡面太窄，完整文案进 tooltip）
        private const string HintRunning = "点开详情；不能再发布到此槽";
        private const string HintAvailable = "点开详情；指定空槽后才能发布，本卡不占发布位";
        private const string HintCompleted = "点开详情；不能领奖；0:00 入账后变空槽";
        private const string HintIntelLack = "整卡禁用，只表示这条还不能发；不占可发布的空位";
        private const string HintEmpty = "点选后，下一次发布放到这里";

        // ==================== 布局常量（白模只保证可读，不对齐视觉稿） ====================

        private const float AreaPad = 10f;
        private const float HeaderHeight = 26f;
        private const float ColumnGap = 8f;
        private const float ColumnInset = 6f;
        private const float LeftColumnRatio = 0.56f;
        private const int SlotColumns = 3;
        private const float SlotCardHeight = 66f;
        private const float SlotCardMinWidth = 72f;
        private const float ListRowHeight = 22f;
        private const float StateColumnWidth = 56f;
        private const float ActionColumnWidth = 60f;
        private const float FieldLabelWidth = 50f;
        private const float PublishButtonWidth = 96f;
        private const float PublishButtonHeight = 26f;
        private const float ListNarrowRatio = 0.92f;

        /// <summary>默认面板宽度（调用方 <c>WorldHud</c> 用 <see cref="DefaultArea"/> 取居中矩形）。</summary>
        public const float DefaultWidth = 900f;

        /// <summary>默认面板高度。</summary>
        public const float DefaultHeight = 560f;

        // ==================== 贴图（common 组，缺失由 HudSkin 自动回退 GUI.Box） ====================

        private const string MainFramePath = "common/ui_common_frame_02";
        private const string LeftFramePath = "common/ui_common_frame_03";
        private const string RightFramePath = "common/ui_common_frame_04";
        private const string SlotFramePath = "common/ui_common_frame_05";
        private const string ListFramePath = "common/ui_common_frame_01";

        private static readonly Color NormalColor = Color.white;
        private static readonly Color DetailHighlightColor = new Color(1f, 0.94f, 0.55f, 1f);
        private static readonly Color SlotSelectedColor = new Color(0.62f, 0.9f, 0.62f, 1f);

        // ==================== 运行时状态 ====================

        private bool _isOpen;
        private BountyEntry _detail;
        private string _statusLine = string.Empty;
        private bool _statusIsInteraction;
        private bool _closeRequested;
        private Vector2 _listScroll;
        private Rect _bounds;

        private static GUIStyle _titleStyle;
        private static GUIStyle _sectionStyle;
        private static GUIStyle _cardStyle;
        private static GUIStyle _rowStyle;
        private static GUIStyle _stateStyle;
        private static GUIStyle _fieldStyle;
        private static GUIStyle _fieldLabelStyle;
        private static GUIStyle _statusStyle;
        private static GUIStyle _emptyStyle;
        private static GUIStyle _hintStyle;

        /// <summary>面板是否处于打开态（<see cref="Draw"/> 只在打开态画）。</summary>
        public bool IsOpen
        {
            get { return _isOpen; }
        }

        /// <summary>详情当前指向的条目；未选中为 null（详情区显示八个空白占位）。</summary>
        public BountyEntry DetailEntry
        {
            get { return _detail; }
        }

        /// <summary>状态行当前文案（交互提示，或四校验里第一项不满足的原因）。</summary>
        public string StatusLine
        {
            get { return _statusLine; }
        }

        /// <summary>
        /// 本次 <see cref="Draw"/> 占用的整块矩形，每次绘制刷新。
        /// 供 <c>WorldHud.IsPointerOverPanel</c> 把悬赏板纳入防点击穿透判定。
        /// </summary>
        public Rect Bounds
        {
            get { return _bounds; }
        }

        /// <summary>本次 OnGUI 里点过「关闭」；调用方读到后取消暂停（<see cref="Open"/> 的对称面）。</summary>
        public bool CloseRequested
        {
            get { return _closeRequested; }
        }

        /// <summary>
        /// 打开悬赏板（2.1-1：主界面悬赏栏点一次打开；关闭后回主界面，已占用槽位保留）。
        /// 顺带预载 common 组贴图。<b>暂停由调用方做</b>（WorldHud 在打开面板的位置
        /// <c>GameModule.World.SetPaused(true)</c>），本类不碰世界时钟。
        /// </summary>
        public void Open()
        {
            _isOpen = true;
            _closeRequested = false;
            _detail = null;
            _statusLine = string.Empty;
            _statusIsInteraction = false;
            _listScroll = Vector2.zero;
            HudSkin.PreloadCommon();
        }

        /// <summary>关闭悬赏板（清掉详情选中与状态行；槽位与系统状态不动，再开后仍在）。</summary>
        public void Close()
        {
            _isOpen = false;
            _closeRequested = false;
            _detail = null;
            _statusLine = string.Empty;
            _statusIsInteraction = false;

            // 关闭后把矩形清空，避免 IsPointerOverPanel 把已经关掉的板子算成面板区域。
            _bounds = new Rect(0f, 0f, 0f, 0f);
        }

        /// <summary>关掉「关闭」请求标记（调用方处理完暂停恢复后调用一次即可）。</summary>
        public void AcknowledgeClose()
        {
            _closeRequested = false;
        }

        /// <summary>面板默认矩形（屏幕居中，供调用方直接传进 <see cref="Draw"/>）。</summary>
        public static Rect DefaultArea()
        {
            float w = Mathf.Min(DefaultWidth, Screen.width - 40f);
            float h = Mathf.Min(DefaultHeight, Screen.height - 40f);
            return new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        }

        /// <summary>
        /// 画整个悬赏板（OnGUI 白模）：标题栏 → 左栏（槽区 + 列表 / 空态）+ 右栏常驻详情。
        /// 未打开、<paramref name="sim"/> 为 null 或没有悬赏系统时直接返回。
        /// </summary>
        public void Draw(Rect area, WorldSimulation sim)
        {
            var bounty = sim != null ? sim.Bounty : null;
            if (!_isOpen || bounty == null)
            {
                return;
            }

            EnsureStyles();
            _bounds = area;
            _closeRequested = false;

            // 外框：common 组 frame 贴图，缺失时 HudSkin 内部回退 GUI.Box。
            HudSkin.DrawFrame(area, MainFramePath);

            var headerRect = new Rect(area.x + AreaPad, area.y + AreaPad, area.width - AreaPad * 2f, HeaderHeight);
            DrawHeader(headerRect);

            if (!_isOpen)
            {
                // 本帧点了「关闭」：剩下的区域不再画（调用方读到 CloseRequested 后取消暂停）。
                return;
            }

            float bodyHeight = area.height - AreaPad * 2f - HeaderHeight;
            if (bodyHeight <= 60f || area.width <= 240f)
            {
                return;
            }

            var body = new Rect(area.x + AreaPad, headerRect.yMax, area.width - AreaPad * 2f, bodyHeight);
            float leftWidth = body.width * LeftColumnRatio - ColumnGap * 0.5f;
            var leftRect = new Rect(body.x, body.y, leftWidth, body.height);
            var rightRect = new Rect(body.x + leftWidth + ColumnGap, body.y, body.width - leftWidth - ColumnGap, body.height);

            DrawLeftColumn(leftRect, sim, bounty);
            DrawDetailColumn(rightRect, sim, bounty);
        }

        // ==================== 标题栏（2.2 左栏标题固定「悬赏板」） ====================

        private void DrawHeader(Rect headerRect)
        {
            float closeWidth = 56f;
            var titleRect = new Rect(headerRect.x, headerRect.y, headerRect.width - closeWidth - 4f, headerRect.height);
            var closeRect = new Rect(headerRect.xMax - closeWidth, headerRect.y, closeWidth, headerRect.height);

            GUILayout.BeginArea(titleRect);
            GUILayout.Label(TitleText, _titleStyle);
            GUILayout.EndArea();

            if (GUI.Button(closeRect, "关闭", _hintStyle))
            {
                Close();
                _closeRequested = true;
            }
        }

        // ==================== 左栏：槽区 + 列表（2.3 / 2.4） ====================

        private void DrawLeftColumn(Rect leftRect, WorldSimulation sim, BountySystem bounty)
        {
            HudSkin.DrawFrame(leftRect, LeftFramePath);
            var inner = Inset(leftRect, ColumnInset);

            if (IsEmptyState(bounty))
            {
                // 2.2-1：空态时槽区与列表整体隐藏，左栏只留这一句。
                GUILayout.BeginArea(inner);
                GUILayout.Space(6f);
                GUILayout.Label(EmptyStateText, _emptyStyle);
                GUILayout.EndArea();
                return;
            }

            int slotCount = bounty.SlotCount;
            float cardWidth = Mathf.Max(SlotCardMinWidth, (inner.width - ColumnGap * (SlotColumns - 1)) / SlotColumns);
            int rows = slotCount > 0 ? (slotCount + SlotColumns - 1) / SlotColumns : 0;

            // 槽区：固定 SlotCount 张卡片，3 列 2 行，不是拖放目标（2.3）。
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < SlotColumns; col++)
                {
                    int slotIndex = row * SlotColumns + col;
                    if (slotIndex >= slotCount)
                    {
                        // 5.2：槽数不满 3 的倍数时最后一行留空，不另做滚动。
                        continue;
                    }

                    var cardRect = new Rect(
                        inner.x + col * (cardWidth + ColumnGap),
                        inner.y + row * (SlotCardHeight + ColumnGap),
                        cardWidth,
                        SlotCardHeight);
                    HudSkin.DrawFrame(cardRect, SlotFramePath);
                    GUILayout.BeginArea(Inset(cardRect, 3f));
                    DrawSlotCard(sim, bounty, slotIndex);
                    GUILayout.EndArea();
                }
            }

            // 列表：槽位下方，比槽区窄（2.4-1）。
            float listTop = inner.y + rows * (SlotCardHeight + ColumnGap) + 4f;
            var listRect = new Rect(inner.x, listTop, inner.width * ListNarrowRatio, Mathf.Max(60f, inner.yMax - listTop - 2f));
            DrawList(listRect, sim, bounty);
        }

        /// <summary>
        /// 空态判定（2.2-1 / 验收 9）：<b>没有任何已点亮悬赏</b>且<b>没有任何占用槽</b>时才出现那句文案；
        /// 有任意占用槽或列表有任意条目都不显示。
        /// </summary>
        public static bool IsEmptyState(BountySystem bounty)
        {
            if (bounty == null)
            {
                return true;
            }

            if (bounty.OccupiedSlotCount > 0)
            {
                return false;
            }

            var entries = bounty.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Revealed)
                {
                    return false;
                }
            }

            return true;
        }

        private void DrawSlotCard(WorldSimulation sim, BountySystem bounty, int slotIndex)
        {
            var entry = bounty.GetSlot(slotIndex);
            bool empty = entry == null;
            BountyState state = empty ? BountyState.Available : bounty.QueryState(entry);
            bool selectedForPublish = empty && bounty.SelectedEmptySlot == slotIndex;
            bool detailHere = !empty && ReferenceEquals(entry, _detail);

            string badge;
            string main;
            string sub;
            string hint;
            bool interactive;
            DescribeSlotCard(sim, state, entry, empty, selectedForPublish, out badge, out main, out sub, out hint, out interactive);

            // 线索不足：整卡禁用（2.3 表「整卡禁用」）。
            if (!interactive)
            {
                GUILayout.Label(badge + "\n" + main + "\n" + sub, _cardStyle);
                return;
            }

            GUI.backgroundColor = selectedForPublish ? SlotSelectedColor : (detailHere ? DetailHighlightColor : NormalColor);
            bool clicked = GUILayout.Button(new GUIContent(badge + "\n" + main + "\n" + sub, hint), _cardStyle);
            GUI.backgroundColor = NormalColor;

            if (clicked)
            {
                OnSlotClicked(sim, bounty, slotIndex, entry, empty, selectedForPublish);
            }
        }

        private void OnSlotClicked(WorldSimulation sim, BountySystem bounty, int slotIndex, BountyEntry entry, bool empty, bool selectedForPublish)
        {
            if (empty)
            {
                // 点空槽＝指定下一次发布的位置；同一时间只指定一个，再点另一个改指定（2.3-2）。
                if (selectedForPublish)
                {
                    bounty.ClearSlotSelection();
                    SetInteractionStatus("已取消第 " + (slotIndex + 1) + " 号槽的发布位置指定。");
                }
                else
                {
                    bounty.SelectEmptySlot(slotIndex);
                    SetInteractionStatus("已指定第 " + (slotIndex + 1) + " 号槽，下一次发布放到这里。");
                }

                return;
            }

            // 点已占用槽只看详情，不能覆盖空槽指定（2.3-2）。
            _detail = entry;
            SetInteractionStatus(StateText(bounty.QueryState(entry)) + "：《" + entry.Name + "》。");
        }

        /// <summary>
        /// 槽态 → （角标 / 主文案 / 辅文案 / 操作说明 / 是否可交互）唯一映射（2.3 槽位表）。
        /// 槽区卡片与列表行共用本表的状态文案，保证槽区和列表表述一致（2.3-3）。
        /// </summary>
        private static void DescribeSlotCard(WorldSimulation sim, BountyState state, BountyEntry entry, bool emptySlot, bool selectedForPublish,
            out string badge, out string main, out string sub, out string hint, out bool interactive)
        {
            interactive = true;

            if (emptySlot)
            {
                badge = BadgeEmpty;
                main = EmptyMainText;
                sub = selectedForPublish ? EmptySubSelected : EmptySubIdle;
                hint = HintEmpty;
                return;
            }

            badge = StateText(state);
            main = entry != null ? entry.Name : string.Empty;

            switch (state)
            {
                case BountyState.Available:
                    sub = IntelLabelOf(sim, entry) + " · " + GoldOf(entry) + " G";
                    hint = HintAvailable;
                    break;
                case BountyState.InProgress:
                    sub = GoldOf(entry) + " G · 剩余 " + DaysOf(entry) + " 天";
                    hint = HintRunning;
                    break;
                case BountyState.CompletedPending:
                    sub = CompletedSubText;
                    hint = HintCompleted;
                    break;
                default:
                    // 线索不足：整卡禁用，只表示这条还不能发，不占可发布的空位。
                    sub = IntelLackSubText;
                    hint = HintIntelLack;
                    interactive = false;
                    break;
            }
        }

        /// <summary>
        /// 列表（2.4）：一行一条，三列「名称 · 线索 · 报酬 | 状态 | 动作字」。
        /// 可发布→「发布」；线索不足→「发布」且整行禁用；进行中→「剩余」；已完成→无动作字（只能看）。
        /// 点行切右侧详情；进行中 / 已完成的行不能再发布。
        /// 未点亮（初始可见=0 且未被其他系统点亮）的条目不进列表（3.1-2）。
        /// </summary>
        private void DrawList(Rect listRect, WorldSimulation sim, BountySystem bounty)
        {
            HudSkin.DrawFrame(listRect, ListFramePath);

            GUILayout.BeginArea(Inset(listRect, 4f));
            GUILayout.Label(ListTitleText, _sectionStyle);

            _listScroll = GUILayout.BeginScrollView(_listScroll);
            var entries = bounty.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!entry.Revealed)
                {
                    continue;
                }

                DrawListRow(sim, bounty, entry);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawListRow(WorldSimulation sim, BountySystem bounty, BountyEntry entry)
        {
            BountyState state = bounty.QueryState(entry);
            bool rowDisabled = state == BountyState.IntelLack;
            string left = entry.Name + " · " + IntelLabelOf(sim, entry) + " · " + GoldOf(entry) + " G";

            GUILayout.BeginHorizontal(GUILayout.Height(ListRowHeight));
            bool rowClicked;
            if (rowDisabled)
            {
                // 线索不足：整行禁用（2.4-2）。
                bool prev = GUI.enabled;
                GUI.enabled = false;
                rowClicked = GUILayout.Button(new GUIContent(left, HintIntelLack), _rowStyle, GUILayout.ExpandWidth(true));
                GUI.enabled = prev;
            }
            else
            {
                GUI.backgroundColor = ReferenceEquals(entry, _detail) ? DetailHighlightColor : NormalColor;
                rowClicked = GUILayout.Button(new GUIContent(left, StateText(state)), _rowStyle, GUILayout.ExpandWidth(true));
                GUI.backgroundColor = NormalColor;
            }

            GUILayout.Label(StateText(state), _stateStyle, GUILayout.Width(StateColumnWidth));
            DrawActionCell(sim, bounty, entry, state);
            GUILayout.EndHorizontal();

            if (rowClicked)
            {
                // 点行：右侧详情切到该条（2.4-3）。
                _detail = entry;
                SetInteractionStatus(StateText(state) + "：《" + entry.Name + "》。");
            }
        }

        /// <summary>动作字列（2.4-2）：可发布 / 线索不足→「发布」，进行中→「剩余」，已完成→空（只能看）。</summary>
        private void DrawActionCell(WorldSimulation sim, BountySystem bounty, BountyEntry entry, BountyState state)
        {
            switch (state)
            {
                case BountyState.Available:
                    if (GUILayout.Button(PublishButtonText, _hintStyle, GUILayout.Width(ActionColumnWidth)))
                    {
                        // 先切到这条，再走与详情按钮完全相同的四校验发布路径（3.2-6：先点列表 / 先点空槽都行）。
                        _detail = entry;
                        PublishFromUi(sim, bounty, entry);
                    }

                    break;

                case BountyState.InProgress:
                    GUILayout.Label("剩余 " + DaysOf(entry) + " 天", _hintStyle, GUILayout.Width(ActionColumnWidth));
                    break;

                case BountyState.CompletedPending:
                    GUILayout.Label(string.Empty, _hintStyle, GUILayout.Width(ActionColumnWidth));
                    break;

                default:
                    bool prev = GUI.enabled;
                    GUI.enabled = false;
                    GUILayout.Button(PublishButtonText, _hintStyle, GUILayout.Width(ActionColumnWidth));
                    GUI.enabled = prev;
                    break;
            }
        }

        // ==================== 右栏：常驻详情（2.5） ====================

        private void DrawDetailColumn(Rect rightRect, WorldSimulation sim, BountySystem bounty)
        {
            HudSkin.DrawFrame(rightRect, RightFramePath);

            GUILayout.BeginArea(Inset(rightRect, ColumnInset));
            GUILayout.Label(DetailTitleText, _titleStyle);

            var entry = _detail;
            if (entry == null)
            {
                // 2.2：未选中时八个字段为空白占位。
                DrawField("名称", BlankFieldText);
                DrawField("线索", BlankFieldText);
                DrawField("费用", BlankFieldText);
                DrawField("天数", BlankFieldText);
                DrawField("报酬", BlankFieldText);
                DrawField("物资", BlankFieldText);
                DrawField("安定度", BlankFieldText);
                DrawField("状态", BlankFieldText);
                GUILayout.Space(6f);
                DrawPublishButton(sim, bounty, null);
                GUILayout.Label(ResolveStatusLine(bounty, null), _statusStyle);
                // 2.2 表里的「空态」按钮只用于演示空列表，正式界面不放，这里刻意不实现。
                GUILayout.EndArea();
                return;
            }

            BountyState state = bounty.QueryState(entry);
            bool intelHeld = IntelHeldOf(sim, entry);

            // 详情顶部：目标怪物卡图（M13 资源补全；未交付图的目标不占高）。
            DrawMonsterCard(entry);

            // 八个字段自上而下全部只读：发布前也不能在本界面改数值（2.5-1）。
            DrawField("名称", entry.Name);
            DrawField("线索", IntelLabelOf(sim, entry) + (intelHeld ? string.Empty : "（" + NotHeldText + "）"));
            DrawField("费用", entry.Fee + " G");
            DrawField("天数", DaysFieldText(entry, state));
            DrawField("报酬", "赏金 " + GoldOf(entry) + " G");
            DrawField("物资", MatLabelOf(entry));
            DrawField("安定度", SafeLabelOf(entry));
            DrawField("状态", StateText(state));

            GUILayout.Space(8f);
            DrawPublishButton(sim, bounty, entry);
            GUILayout.Label(ResolveStatusLine(bounty, entry), _statusStyle);
            GUILayout.EndArea();
        }

        /// <summary>
        /// 详情底部左侧「发布」按钮（2.2 发布区 / 3.2 发布规则）：
        /// 四项校验由 <see cref="BountySystem.CheckPublish"/> 判（用 <c>StartNewGame</c> 里 <c>Attach</c> 注入的钱包与情报系统），
        /// 全过才可点；不可点时按钮禁用，禁用原因由状态行报出。
        /// </summary>
        private void DrawPublishButton(WorldSimulation sim, BountySystem bounty, BountyEntry entry)
        {
            bool canPublish = entry != null && bounty.CheckPublish(entry) == PublishDeny.None;

            GUILayout.BeginHorizontal();
            bool prev = GUI.enabled;
            if (!canPublish)
            {
                GUI.enabled = false;
            }

            bool clicked = GUILayout.Button(PublishButtonText, GUI.skin.button,
                GUILayout.Width(PublishButtonWidth), GUILayout.Height(PublishButtonHeight));
            GUI.enabled = prev;
            GUILayout.EndHorizontal();

            if (clicked && entry != null)
            {
                PublishFromUi(sim, bounty, entry);
            }
        }

        /// <summary>详情怪物卡图高度（卡图 256×256 正方形，等比内缩）。</summary>
        private const float MonsterCardHeight = 132f;

        /// <summary>目标怪物名 → 卡图（静态缓存，含 null，避免每帧查表/告警）。群组图优先（悬赏针对一群）。</summary>
        private static readonly Dictionary<string, Texture2D> MonsterCardCache = new Dictionary<string, Texture2D>();

        private static Texture2D MonsterCardTex(BountyEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.TargetMonsterName))
            {
                return null;
            }

            if (MonsterCardCache.TryGetValue(entry.TargetMonsterName, out var cached))
            {
                return cached;
            }

            Texture2D tex = null;
            var catalog = GameConfigCatalog.Instance;
            if (catalog != null)
            {
                var defs = catalog.GetMonsterCatalog();
                for (int i = 0; i < defs.Length; i++)
                {
                    if (defs[i].Name != entry.TargetMonsterName)
                    {
                        continue;
                    }

                    var row = catalog.GetMonsterRow(defs[i].Id);
                    string res = row != null && !string.IsNullOrEmpty(row.GroupResourceName) ? row.GroupResourceName : null;
                    if (string.IsNullOrEmpty(res) && row != null)
                    {
                        res = row.ResourceName;
                    }

                    if (!string.IsNullOrEmpty(res))
                    {
                        tex = HudSkin.Tex("monsters/" + res);
                    }

                    break;
                }
            }

            MonsterCardCache[entry.TargetMonsterName] = tex;
            return tex;
        }

        /// <summary>详情栏顶部画目标怪物卡图（水平居中，等比；无图不占高）。</summary>
        private static void DrawMonsterCard(BountyEntry entry)
        {
            var tex = MonsterCardTex(entry);
            if (tex == null)
            {
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var rect = GUILayoutUtility.GetRect(MonsterCardHeight, MonsterCardHeight, GUILayout.Width(MonsterCardHeight), GUILayout.Height(MonsterCardHeight));
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, true);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        private void DrawField(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            GUILayout.Label(value, _fieldStyle, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
        }

        // ==================== 发布（3.2） ====================

        /// <summary>
        /// 发布入口（详情按钮与列表动作字共用）：四项都满足才 <see cref="BountySystem.TryPublish"/>；
        /// 任一项不满足则不扣发布费、不扣线索、槽仍为空，并只提示<b>第一项</b>不满足的原因。
        /// </summary>
        private void PublishFromUi(WorldSimulation sim, BountySystem bounty, BountyEntry entry)
        {
            PublishDeny deny = bounty.CheckPublish(entry);
            if (deny != PublishDeny.None)
            {
                SetInteractionStatus(DenyText(deny));
                return;
            }

            if (sim == null || !bounty.TryPublish(entry, sim.Wallet, sim.Intel))
            {
                // 校验通过但发布失败（同帧余额/线索被别处改掉）：扣改回滚由 BountySystem 负责，这里只提示。
                SetInteractionStatus(DenyText(bounty.CheckPublish(entry)));
                return;
            }

            // 发布成功：指定的空槽变为进行中，该指定随之失效（BountySystem 已清），列表行改进行中。
            _detail = entry;
            SetInteractionStatus("已发布到第 " + (entry.SlotIndex + 1) + " 号槽（进行中）。发布位置已消耗，请重新指定下一个空槽。");
        }

        /// <summary>状态行：优先显示最近一次交互提示，否则按四校验报第一项不满足的原因。</summary>
        private string ResolveStatusLine(BountySystem bounty, BountyEntry entry)
        {
            if (_statusIsInteraction)
            {
                return _statusLine;
            }

            if (entry == null)
            {
                return "未选中悬赏：在列表或槽位里点一条。";
            }

            PublishDeny deny = bounty.CheckPublish(entry);
            return deny == PublishDeny.None ? "四项校验都满足，可以发布。" : DenyText(deny);
        }

        private void SetInteractionStatus(string text)
        {
            _statusLine = text ?? string.Empty;
            _statusIsInteraction = true;
        }

        /// <summary><see cref="PublishDeny"/> → 需求案 3.2-3 的第一项不满足原因文案。</summary>
        private static string DenyText(PublishDeny deny)
        {
            switch (deny)
            {
                case PublishDeny.NoSlotSelected:
                    return "未指定空槽：先点一个空槽，指定下一次发布的位置。";
                case PublishDeny.NoEmptySlot:
                    return "没有空槽：槽位已全部占用，不能再发布。";
                case PublishDeny.IntelMissing:
                    return "线索未持有：先去拿到这条线索（线索不堆叠，发布时扣一条即无）。";
                case PublishDeny.GoldInsufficient:
                    return "金币不足：余额不够这条悬赏的发布费。";
                case PublishDeny.NotAvailable:
                    return "该条现在不能发布（不是可发布悬赏 / 已进行中 / 已完成待入账）。";
                default:
                    return string.Empty;
            }
        }

        // ==================== 显示文案工具 ====================

        /// <summary>状态 → 角标 / 列表状态列文案（2.3 角标列 = 2.5 状态列）。</summary>
        private static string StateText(BountyState state)
        {
            switch (state)
            {
                case BountyState.Available:
                    return BadgeAvailable;
                case BountyState.IntelLack:
                    return BadgeIntelLack;
                case BountyState.InProgress:
                    return BadgeRunning;
                case BountyState.CompletedPending:
                    return BadgeCompleted;
                default:
                    return string.Empty;
            }
        }

        private static int GoldOf(BountyEntry entry)
        {
            return entry != null ? entry.Gold : 0;
        }

        private static int DaysOf(BountyEntry entry)
        {
            return entry != null ? entry.RemainingDays : 0;
        }

        /// <summary>
        /// 线索列文案：持有时显示该线索对应的情报名，未持有时回落到「情报#编号」
        /// （是否「未持有」由详情行额外旁标，列表列宽有限不再重复标记）。
        /// </summary>
        private static string IntelLabelOf(WorldSimulation sim, BountyEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            if (entry.IntelId <= 0)
            {
                return "情报—";
            }

            var intel = sim != null ? sim.Intel : null;
            var record = intel != null ? intel.FindPlayerIntel(entry.IntelId) : null;
            if (record != null && !string.IsNullOrEmpty(record.MonsterName))
            {
                return record.MonsterName + "情报";
            }

            return "情报#" + entry.IntelId;
        }

        /// <summary>线索是否已持有（<see cref="IntelSystem.PlayerHas"/>；未接情报系统按未持有处理）。</summary>
        private static bool IntelHeldOf(WorldSimulation sim, BountyEntry entry)
        {
            if (sim == null || sim.Intel == null || entry == null || entry.IntelId <= 0)
            {
                return false;
            }

            return sim.Intel.PlayerHas(entry.IntelId);
        }

        /// <summary>详情「天数」：未发布显示限时；进行中显示剩余；已完成显示剩余并标注入账口径（2.5）。</summary>
        private static string DaysFieldText(BountyEntry entry, BountyState state)
        {
            if (entry == null)
            {
                return BlankFieldText;
            }

            if (!entry.Occupied)
            {
                return "限时 " + entry.TimeLimitDays + " 天";
            }

            return state == BountyState.CompletedPending
                ? ("剩余 " + entry.RemainingDays + " 天（" + CompletedSubText + "）")
                : ("剩余 " + entry.RemainingDays + " 天");
        }

        /// <summary>详情「物资」：物品名 × 数量；无则显示「无」（物资列可空）。</summary>
        private static string MatLabelOf(BountyEntry entry)
        {
            if (entry == null || !entry.HasMat)
            {
                return NoneText;
            }

            var def = ItemCatalog.Get(entry.MatItemId);
            string name = def != null && !string.IsNullOrEmpty(def.Name) ? def.Name : ("物品#" + entry.MatItemId);
            return name + " × " + entry.MatCount;
        }

        /// <summary>详情「安定度」：完成时的变动（可空 / 0 / 负）；失败不变动 → 显示「无」。</summary>
        private static string SafeLabelOf(BountyEntry entry)
        {
            if (entry == null || entry.SafeDelta == 0)
            {
                return NoneText;
            }

            return entry.SafeDelta > 0 ? ("+" + entry.SafeDelta) : entry.SafeDelta.ToString();
        }

        // ==================== 布局 / 样式工具 ====================

        private static Rect Inset(Rect r, float inset)
        {
            return new Rect(
                r.x + inset,
                r.y + inset,
                Mathf.Max(1f, r.width - inset * 2f),
                Mathf.Max(1f, r.height - inset * 2f));
        }

        /// <summary>GUIStyle 只能在 OnGUI 里构造（GUI.skin 此刻才可用），故首次绘制时惰性初始化。</summary>
        private static void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };

            _sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };

            _cardStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                padding = new RectOffset(4, 4, 3, 3),
            };

            _rowStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                padding = new RectOffset(4, 2, 2, 2),
            };

            _stateStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
            };

            _fieldStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
            };

            _fieldLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };

            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
            };

            _emptyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
            };

            _hintStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(2, 2, 0, 0),
            };

            // 可读性修复（ui01 美术接入后视觉走查）：IMGUI 默认皮肤在线性空间下按钮呈深灰块、
            // 文字近白，槽卡与详情文字对比过低。统一墨色文字；按钮类样式去底透出已绘制的框贴图。
            var ink = new Color(0.24f, 0.18f, 0.11f);
            foreach (var style in new[] { _titleStyle, _sectionStyle, _stateStyle, _fieldStyle, _fieldLabelStyle, _statusStyle, _emptyStyle })
            {
                style.normal.textColor = ink;
            }

            // background=null 对 GUI.Button 不生效（仍画默认深色块），用程序化羊皮纸底透出文字。
            var parchment = ParchmentTex();
            foreach (var style in new[] { _cardStyle, _rowStyle, _hintStyle })
            {
                style.normal.textColor = ink;
                style.hover.textColor = ink;
                style.active.textColor = ink;
                style.focused.textColor = ink;
                style.normal.background = parchment;
                style.hover.background = parchment;
                style.active.background = parchment;
                style.focused.background = parchment;
            }
        }

        private static Texture2D s_parchmentTex;

        /// <summary>程序化羊皮纸底（2×2 纯色）：IMGUI 按钮 background=null 仍画默认深色块，故用浅色底替代。</summary>
        private static Texture2D ParchmentTex()
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
}
