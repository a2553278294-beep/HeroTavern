using System.Collections.Generic;
using System.Linq;
using System.Text;
using GameLogic.World;
using TMPro;
using TEngine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// NPC 交易窗口（对照 Resources/ui01/shop/ui交易1/2/3.png；角色系统 3.4 + 3.5.2 拆分）：
    /// 左玩家栏 / 右 NPC 栏各 6×3 格，底部左右出价条（价值 + 5 出价槽，右侧含好感与增减预览），中下确认交易。
    ///
    /// 交互（沿用 OnGUI 白模的「点击代替拖拽」口径）：左键点物品 = 整叠摆出/撤出；右键点可拆分物品 = 拆分数量弹窗；
    /// 悬停 0.5s 出详情浮层（交易2）。好感不足（<see cref="TradeSession.CanBuy"/> 为 false）的 NPC 物品整格置灰不可点。
    ///
    /// 物品图标：<see cref="ItemIconLibrary"/> 按 Item 表 icon 列加载 ui01/icons，运行时注入槽位
    /// （<see cref="UiArtInjector"/>，不改烘焙 prefab）；有图标的格子不叠名称（对照 ui交易1.png），无图标物品保持文字名。
    /// 交易不夺走走时（经营案「暂停与否由玩家决定」）；世界点击由 EventSystem 射线拦截，面板区不穿透。
    /// 拆分实现说明：OnGUI 版把拆出物品搬进独立批次仓库，本版直接以「摆出数量」表达（语义等价，物品不出仓库，
    /// 关窗无需并回批次），规则 3.5.2 的「部分摆出」能力不变。
    /// </summary>
    [Window(UILayer.UI)]
    public sealed class TradeUI : UIWindow
    {
        /// <summary>当前打开的实例。</summary>
        public static TradeUI Current { get; private set; }

        /// <summary>待交易 NPC（ShowUIAsync 为异步，先寄存、OnCreate 自取）。</summary>
        public static CharacterData PendingNpc;

        private const int GridSlotCount = 18;
        private const int OfferSlotCount = 5;
        private const float HoverDelaySeconds = 0.5f;

        // ==================== 绑定节点 ====================

        private TextMeshProUGUI _textTitle;
        private TextMeshProUGUI _textPlayerGold;
        private TextMeshProUGUI _textNpcGold;
        private TextMeshProUGUI _textLeftValue;
        private TextMeshProUGUI _textRightValue;
        private TextMeshProUGUI _textFavor;
        private TextMeshProUGUI _textFavorDelta;
        private Transform _favorDeltaBox;
        private TextMeshProUGUI _textResult;
        private Button _btnConfirm;
        private Button _btnClose;

        private readonly Button[] _pSlots = new Button[GridSlotCount];
        private readonly Button[] _nSlots = new Button[GridSlotCount];
        private readonly Button[] _poSlots = new Button[OfferSlotCount];
        private readonly Button[] _noSlots = new Button[OfferSlotCount];
        private readonly Image[] _pFrames = new Image[GridSlotCount];
        private readonly Image[] _nFrames = new Image[GridSlotCount];
        private readonly Image[] _poFrames = new Image[OfferSlotCount];
        private readonly Image[] _noFrames = new Image[OfferSlotCount];
        private readonly TextMeshProUGUI[] _pNames = new TextMeshProUGUI[GridSlotCount];
        private readonly TextMeshProUGUI[] _nNames = new TextMeshProUGUI[GridSlotCount];
        private readonly TextMeshProUGUI[] _poNames = new TextMeshProUGUI[OfferSlotCount];
        private readonly TextMeshProUGUI[] _noNames = new TextMeshProUGUI[OfferSlotCount];
        private readonly TextMeshProUGUI[] _pCounts = new TextMeshProUGUI[GridSlotCount];
        private readonly TextMeshProUGUI[] _nCounts = new TextMeshProUGUI[GridSlotCount];
        private readonly TextMeshProUGUI[] _poCounts = new TextMeshProUGUI[OfferSlotCount];
        private readonly TextMeshProUGUI[] _noCounts = new TextMeshProUGUI[OfferSlotCount];
        private readonly Image[] _pIcons = new Image[GridSlotCount];
        private readonly Image[] _nIcons = new Image[GridSlotCount];
        private readonly Image[] _poIcons = new Image[OfferSlotCount];
        private readonly Image[] _noIcons = new Image[OfferSlotCount];

        // 详情浮层
        private RectTransform _tooltip;
        private TextMeshProUGUI _textTipName;
        private TextMeshProUGUI _textTipKind;
        private TextMeshProUGUI _textTipValue;
        private TextMeshProUGUI _textTipDesc;
        private TextMeshProUGUI _textTipTags;

        // 拆分弹窗
        private Transform _splitPopup;
        private TextMeshProUGUI _textSplitTitle;
        private TextMeshProUGUI _textSplitCount;
        private Button _btnSplitMinus;
        private Button _btnSplitPlus;
        private Button _btnSplitOk;
        private Button _splitDimBtn;

        // ==================== 运行时状态 ====================

        private CharacterData _npc;
        private TradeSession _session;
        private readonly List<int> _playerItems = new List<int>();
        private readonly List<int> _npcItems = new List<int>();
        private string _resultLine = string.Empty;
        private bool _resultSuccess;
        private string _lastSignature;

        // 悬停浮层
        private int _hoverItemId;
        private float _hoverSince;
        private bool _tooltipShown;

        // 拆分弹窗
        private bool _splitOpen;
        private int _splitItemId;
        private bool _splitIsPlayer;
        private int _splitCount;
        private int _splitMax;

        // 稀有度角花（按物品类别映射：材料/冰块=lv1，基酒=lv2，酒水=lv3，装备=lv4）
        private Sprite _lv1, _lv2, _lv3, _lv4;

        private static WorldSimulation Sim => GameModule.World.Sim;

        #region 脚本工具生成的代码（手工按 ScriptGenerator 约定绑定）

        protected override void ScriptGenerator()
        {
            _textTitle = FindChildComponent<TextMeshProUGUI>("m_goTitleTab/m_tmpTitle");
            _textPlayerGold = FindChildComponent<TextMeshProUGUI>("m_goPanel/m_tmpPlayerGold");
            _textNpcGold = FindChildComponent<TextMeshProUGUI>("m_goPanel/m_tmpNpcGold");
            _textLeftValue = FindChildComponent<TextMeshProUGUI>("m_goPanel/m_goLeftStrip/m_tmpLeftValue");
            _textRightValue = FindChildComponent<TextMeshProUGUI>("m_goPanel/m_goRightStrip/m_tmpRightValue");
            _textFavor = FindChildComponent<TextMeshProUGUI>("m_goPanel/m_goRightStrip/m_tmpFavor");
            _textFavorDelta = FindChildComponent<TextMeshProUGUI>("m_goPanel/m_goRightStrip/m_goFavorDeltaBox/m_tmpFavorDelta");
            _favorDeltaBox = FindChild("m_goPanel/m_goRightStrip/m_goFavorDeltaBox");
            _textResult = FindChildComponent<TextMeshProUGUI>("m_goPanel/m_tmpResult");
            _btnConfirm = FindChildComponent<Button>("m_goPanel/m_btnConfirm");
            _btnClose = FindChildComponent<Button>("m_btnClose");

            BindSlotGrid("m_goPanel/m_btnPSlot", "m_goPanel/m_imgPSlotFrame", "m_goPanel/m_tmpPSlotName", "m_goPanel/m_tmpPSlotCount",
                GridSlotCount, _pSlots, _pFrames, _pNames, _pCounts, _pIcons, i => OnClickGridSlot(i, true), (i, right) => OnGridSlotClick(i, true, right));
            BindSlotGrid("m_goPanel/m_btnNSlot", "m_goPanel/m_imgNSlotFrame", "m_goPanel/m_tmpNSlotName", "m_goPanel/m_tmpNSlotCount",
                GridSlotCount, _nSlots, _nFrames, _nNames, _nCounts, _nIcons, i => OnClickGridSlot(i, false), (i, right) => OnGridSlotClick(i, false, right));
            BindSlotGrid("m_goPanel/m_goLeftStrip/m_btnPoSlot", "m_goPanel/m_goLeftStrip/m_imgPoSlotFrame", "m_goPanel/m_goLeftStrip/m_tmpPoSlotName", "m_goPanel/m_goLeftStrip/m_tmpPoSlotCount",
                OfferSlotCount, _poSlots, _poFrames, _poNames, _poCounts, _poIcons, i => OnClickOfferSlot(i, true), null);
            BindSlotGrid("m_goPanel/m_goRightStrip/m_btnNoSlot", "m_goPanel/m_goRightStrip/m_imgNoSlotFrame", "m_goPanel/m_goRightStrip/m_tmpNoSlotName", "m_goPanel/m_goRightStrip/m_tmpNoSlotCount",
                OfferSlotCount, _noSlots, _noFrames, _noNames, _noCounts, _noIcons, i => OnClickOfferSlot(i, false), null);

            _tooltip = FindChildComponent<RectTransform>("m_goTooltip");
            _textTipName = FindChildComponent<TextMeshProUGUI>("m_goTooltip/m_tmpTipName");
            _textTipKind = FindChildComponent<TextMeshProUGUI>("m_goTooltip/m_tmpTipKind");
            _textTipValue = FindChildComponent<TextMeshProUGUI>("m_goTooltip/m_tmpTipValue");
            _textTipDesc = FindChildComponent<TextMeshProUGUI>("m_goTooltip/m_tmpTipDesc");
            _textTipTags = FindChildComponent<TextMeshProUGUI>("m_goTooltip/m_tmpTipTags");

            _splitPopup = FindChild("m_goSplitPopup");
            _textSplitTitle = FindChildComponent<TextMeshProUGUI>("m_goSplitPopup/m_goSplitFrame/m_tmpSplitTitle");
            _textSplitCount = FindChildComponent<TextMeshProUGUI>("m_goSplitPopup/m_goSplitFrame/m_tmpSplitCount");
            _btnSplitMinus = FindChildComponent<Button>("m_goSplitPopup/m_goSplitFrame/m_btnSplitMinus");
            _btnSplitPlus = FindChildComponent<Button>("m_goSplitPopup/m_goSplitFrame/m_btnSplitPlus");
            _btnSplitOk = FindChildComponent<Button>("m_goSplitPopup/m_goSplitFrame/m_btnSplitOk");

            if (_btnConfirm != null)
            {
                _btnConfirm.onClick.AddListener(ExecuteTrade);
            }

            // 烘焙按钮 alpha=0/无贴图（确认交易整体隐形）：运行时补美术与文案；关闭按钮补通用关闭图。
            UiArtInjector.ApplyButtonArt(_btnConfirm != null ? _btnConfirm.GetComponent<Image>() : null, "ui01/shop/ui_shop_btn_01");
            var confirmText = _btnConfirm != null ? _btnConfirm.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            if (confirmText != null)
            {
                confirmText.text = "确认交易";
                confirmText.color = new Color(0.24f, 0.18f, 0.11f);
            }

            UiArtInjector.ApplyButtonArt(_btnClose != null ? _btnClose.GetComponent<Image>() : null, "ui01/common/ui_common_btn_close");

            if (_btnClose != null)
            {
                _btnClose.onClick.AddListener(() => GameModule.UI.CloseUI<TradeUI>());
            }

            if (_btnSplitMinus != null)
            {
                _btnSplitMinus.onClick.AddListener(() => StepSplit(-1));
            }

            if (_btnSplitPlus != null)
            {
                _btnSplitPlus.onClick.AddListener(() => StepSplit(+1));
            }

            if (_btnSplitOk != null)
            {
                _btnSplitOk.onClick.AddListener(ConfirmSplit);
            }
        }

        private void BindSlotGrid(string btnPath, string framePath, string namePath, string countPath,
            int count, Button[] buttons, Image[] frames, TextMeshProUGUI[] names, TextMeshProUGUI[] counts, Image[] icons,
            System.Action<int> onLeft, System.Action<int, bool> onClick)
        {
            for (int i = 0; i < count; i++)
            {
                buttons[i] = FindChildComponent<Button>($"{btnPath}{i}");
                frames[i] = FindChildComponent<Image>($"{framePath}{i}");
                names[i] = FindChildComponent<TextMeshProUGUI>($"{namePath}{i}");
                counts[i] = FindChildComponent<TextMeshProUGUI>($"{countPath}{i}");
                // 物品图标注入槽位按钮（置于按钮底图之上；边框/名称/计数是面板层兄弟节点，天然压在图标上）。
                icons[i] = buttons[i] != null
                    ? UiArtInjector.EnsureChildImage(buttons[i].transform, "m_imgItemIcon", new Vector2(0.08f, 0.22f), new Vector2(0.92f, 0.92f), true)
                    : null;

                int slot = i;
                if (buttons[i] == null)
                {
                    continue;
                }

                var trigger = buttons[i].gameObject.AddComponent<EventTrigger>();

                var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                click.callback.AddListener(data =>
                {
                    var ped = (PointerEventData)data;
                    if (onClick != null)
                    {
                        onClick(slot, ped.button == PointerEventData.InputButton.Right);
                    }
                    else if (ped.button == PointerEventData.InputButton.Left)
                    {
                        onLeft?.Invoke(slot);
                    }
                });
                trigger.triggers.Add(click);

                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => OnSlotHoverEnter(slot, buttons[i]));
                trigger.triggers.Add(enter);

                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(_ => ClearHover());
                trigger.triggers.Add(exit);
            }
        }

        #endregion

        /// <summary>html-to-ugui 设计分辨率：UIWindow 据此对窗口根做居中等比适配（宿主画布为 1920×1080 横屏参考，宽高各半匹配）。</summary>
        protected override Vector2 DesignResolution => new Vector2(1960f, 1080f);

        protected override void OnCreate()
        {
            Current = this;

            _lv1 = Resources.Load<Sprite>("ui01/common/ui_common_frame_lv1");
            _lv2 = Resources.Load<Sprite>("ui01/common/ui_common_frame_lv2");
            _lv3 = Resources.Load<Sprite>("ui01/common/ui_common_frame_lv3");
            _lv4 = Resources.Load<Sprite>("ui01/common/ui_common_frame_lv4");

            if (_tooltip != null)
            {
                _tooltip.pivot = new Vector2(0f, 1f);
                _tooltip.gameObject.SetActive(false);
            }

            if (_splitPopup != null)
            {
                _splitPopup.gameObject.SetActive(false);
            }

            // 压暗层点击 = 取消拆分。
            var dimImg = FindChildComponent<Image>("m_goSplitPopup/m_goSplitDim");
            if (dimImg != null)
            {
                _splitDimBtn = dimImg.gameObject.AddComponent<Button>();
                _splitDimBtn.transition = Selectable.Transition.None;
                _splitDimBtn.onClick.AddListener(CloseSplitPopup);
            }

            if (PendingNpc != null)
            {
                var npc = PendingNpc;
                PendingNpc = null;
                Open(npc);
            }
        }

        protected override void OnDestroy()
        {
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
        }

        // ==================== 打开 ====================

        /// <summary>打开交易窗口：新建 <see cref="TradeSession"/>，关闭即丢弃。</summary>
        public void Open(CharacterData npc)
        {
            _npc = npc;
            _session = npc != null ? new TradeSession(npc) : null;
            _resultLine = string.Empty;
            _resultSuccess = false;
            _splitOpen = false;
            ClearHover();
            _lastSignature = null;
            RefreshIfChanged();
        }

        protected override void OnUpdate()
        {
            UpdateHover();
            RefreshIfChanged();
        }

        // ==================== 刷新 ====================

        private void RefreshIfChanged()
        {
            var sim = Sim;
            if (sim == null || !sim.IsRunning || _session == null || _npc == null)
            {
                return;
            }

            var sb = new StringBuilder(256);
            sb.Append(sim.Wallet.Currency).Append('|').Append(_npc.Gold).Append('|').Append(_session.Favor);
            AppendOfferHash(sb, _session.PlayerOffer);
            AppendOfferHash(sb, _session.NpcOffer);
            AppendBagHash(sb, sim.Warehouse.Items);
            AppendBagHash(sb, _npc.Bag.Items);
            sb.Append(_resultLine);

            string signature = sb.ToString();
            if (signature == _lastSignature)
            {
                return;
            }

            _lastSignature = signature;
            RefreshAll(sim);
        }

        private static void AppendOfferHash(StringBuilder sb, Dictionary<int, int> offer)
        {
            sb.Append('|');
            foreach (var kv in offer.OrderBy(kv => kv.Key))
            {
                sb.Append(kv.Key).Append(':').Append(kv.Value).Append(',');
            }
        }

        private static void AppendBagHash(StringBuilder sb, IReadOnlyDictionary<int, int> items)
        {
            sb.Append('|');
            if (items == null)
            {
                return;
            }

            foreach (var kv in items.OrderBy(kv => kv.Key))
            {
                sb.Append(kv.Key).Append(':').Append(kv.Value).Append(',');
            }
        }

        private void RefreshAll(WorldSimulation sim)
        {
            // 顶栏
            if (_textTitle != null)
            {
                _textTitle.text = _npc.Name + " · 交易";
            }

            if (_textPlayerGold != null)
            {
                _textPlayerGold.text = $"玩家金币数 {sim.Wallet.Currency}";
            }

            if (_textNpcGold != null)
            {
                _textNpcGold.text = $"{_npc.Name}金币数 {_npc.Gold}";
            }

            // 物品栏
            _playerItems.Clear();
            _playerItems.AddRange(sim.Warehouse.Items.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(id => id));
            _npcItems.Clear();
            _npcItems.AddRange(_npc.Bag.Items.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(id => id));

            FillGrid(sim, true);
            FillGrid(sim, false);
            FillOffers(true);
            FillOffers(false);

            // 价值与好感预览
            if (_textLeftValue != null)
            {
                _textLeftValue.text = $"价值 {_session.PlayerOfferValue}";
            }

            if (_textRightValue != null)
            {
                _textRightValue.text = $"价值 {_session.NpcOfferValue}";
            }

            if (_textFavor != null)
            {
                _textFavor.text = $"好感 {_session.Favor}";
            }

            PreviewFavor(out int delta, out _, out _, out _);
            if (_textFavorDelta != null)
            {
                _textFavorDelta.text = delta == 0 ? string.Empty : (delta > 0 ? "+" + delta : delta.ToString());
                _textFavorDelta.color = delta >= 0 ? new Color(0.55f, 0.65f, 0.29f) : new Color(0.70f, 0.35f, 0.29f);
            }

            // 好感预览框空态隐藏（无增减时残留一块空白框）。
            if (_favorDeltaBox != null)
            {
                _favorDeltaBox.gameObject.SetActive(delta != 0);
            }

            if (_textResult != null)
            {
                _textResult.text = _resultLine;
                _textResult.color = _resultSuccess ? new Color(0.55f, 0.65f, 0.29f) : new Color(0.70f, 0.35f, 0.29f);
            }
        }

        private void FillGrid(WorldSimulation sim, bool playerSide)
        {
            var items = playerSide ? _playerItems : _npcItems;
            var buttons = playerSide ? _pSlots : _nSlots;
            var frames = playerSide ? _pFrames : _nFrames;
            var names = playerSide ? _pNames : _nNames;
            var counts = playerSide ? _pCounts : _nCounts;
            var icons = playerSide ? _pIcons : _nIcons;
            var bagItems = playerSide ? sim.Warehouse.Items : _npc.Bag.Items;
            var offer = playerSide ? _session.PlayerOffer : _session.NpcOffer;

            for (int i = 0; i < GridSlotCount; i++)
            {
                bool hasItem = i < items.Count;
                int itemId = hasItem ? items[i] : 0;
                int held = hasItem && bagItems.TryGetValue(itemId, out int h) ? h : 0;
                int remaining = hasItem ? held - (offer.TryGetValue(itemId, out int o) ? o : 0) : 0;
                bool occupied = hasItem && remaining > 0;

                var def = occupied ? ItemCatalog.Get(itemId) : null;
                var icon = occupied ? ItemIconLibrary.Get(itemId) : null;
                if (names[i] != null)
                {
                    // 有图标的格子不叠名称（对照 ui交易1.png）；无图标物品保持文字名。
                    names[i].text = occupied && def != null && icon == null ? def.Name : string.Empty;
                }

                if (counts[i] != null)
                {
                    counts[i].text = occupied ? remaining.ToString() : string.Empty;
                }

                if (frames[i] != null)
                {
                    frames[i].sprite = occupied && def != null ? LvSpriteOf(def.Kind) : _lv1;
                    frames[i].color = occupied ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                }

                if (buttons[i] != null)
                {
                    bool usable = occupied;
                    if (usable && !playerSide && !_session.CanBuy(itemId))
                    {
                        usable = false; // 好感不足：整格置灰不可点（需求 3.4-06）
                    }

                    buttons[i].interactable = usable;
                    var img = buttons[i].GetComponent<Image>();
                    if (img != null)
                    {
                        img.color = usable ? new Color(0.97f, 0.95f, 0.88f) : new Color(0.75f, 0.74f, 0.68f);
                    }

                    if (icons[i] != null)
                    {
                        icons[i].sprite = icon;
                        icons[i].gameObject.SetActive(icon != null);
                        // 置灰槽位的图标同步降透明度（好感不足不可买的视觉反馈）。
                        icons[i].color = usable ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                    }
                }
                else if (icons[i] != null)
                {
                    icons[i].gameObject.SetActive(false);
                }
            }
        }

        private void FillOffers(bool playerSide)
        {
            var offer = playerSide ? _session.PlayerOffer : _session.NpcOffer;
            var buttons = playerSide ? _poSlots : _noSlots;
            var names = playerSide ? _poNames : _noNames;
            var counts = playerSide ? _poCounts : _noCounts;
            var icons = playerSide ? _poIcons : _noIcons;
            var ids = offer.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(id => id).ToList();

            for (int i = 0; i < OfferSlotCount; i++)
            {
                bool occupied = i < ids.Count;
                int itemId = occupied ? ids[i] : 0;
                var def = occupied ? ItemCatalog.Get(itemId) : null;
                var icon = occupied ? ItemIconLibrary.Get(itemId) : null;

                if (names[i] != null)
                {
                    // 有图标的出价槽不叠名称；无图标物品保持文字名。
                    names[i].text = occupied && def != null && icon == null ? def.Name : string.Empty;
                }

                if (counts[i] != null)
                {
                    counts[i].text = occupied ? offer[itemId].ToString() : string.Empty;
                }

                if (icons[i] != null)
                {
                    icons[i].sprite = icon;
                    icons[i].gameObject.SetActive(icon != null);
                    icons[i].color = Color.white;
                }

                if (buttons[i] != null)
                {
                    buttons[i].interactable = occupied;
                    var img = buttons[i].GetComponent<Image>();
                    if (img != null)
                    {
                        img.color = occupied ? new Color(0.90f, 0.95f, 0.80f) : new Color(0.97f, 0.95f, 0.88f);
                    }
                }
            }
        }

        private Sprite LvSpriteOf(ItemKind kind)
        {
            switch (kind)
            {
                case ItemKind.BaseWine: return _lv2 != null ? _lv2 : _lv1;
                case ItemKind.Wine: return _lv3 != null ? _lv3 : _lv1;
                case ItemKind.Equipment: return _lv4 != null ? _lv4 : _lv1;
                default: return _lv1;
            }
        }

        // ==================== 点击交互 ====================

        private void OnGridSlotClick(int slot, bool playerSide, bool rightClick)
        {
            if (!rightClick)
            {
                OnClickGridSlot(slot, playerSide);
                return;
            }

            // 右键：拆分数量弹窗（仅当可摆出数量 > 1）。
            var items = playerSide ? _playerItems : _npcItems;
            if (slot >= items.Count || _session == null)
            {
                return;
            }

            int itemId = items[slot];
            if (!playerSide && !_session.CanBuy(itemId))
            {
                return;
            }

            int available = AvailableOf(itemId, playerSide);
            if (available > 1)
            {
                OpenSplitPopup(itemId, playerSide, available);
            }
        }

        private void OnClickGridSlot(int slot, bool playerSide)
        {
            if (_session == null)
            {
                return;
            }

            var items = playerSide ? _playerItems : _npcItems;
            if (slot >= items.Count)
            {
                return;
            }

            int itemId = items[slot];
            if (!playerSide && !_session.CanBuy(itemId))
            {
                return;
            }

            int available = AvailableOf(itemId, playerSide);
            if (available <= 0)
            {
                return;
            }

            Offer(itemId, playerSide, CurrentOfferOf(itemId, playerSide) + available);
            ClearHover();
        }

        private void OnClickOfferSlot(int slot, bool playerSide)
        {
            if (_session == null)
            {
                return;
            }

            var offer = playerSide ? _session.PlayerOffer : _session.NpcOffer;
            var ids = offer.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(id => id).ToList();
            if (slot >= ids.Count)
            {
                return;
            }

            Offer(ids[slot], playerSide, 0); // 摆出数清零 = 撤出
            ClearHover();
        }

        private int AvailableOf(int itemId, bool playerSide)
        {
            var sim = Sim;
            if (sim == null || _npc == null)
            {
                return 0;
            }

            int held = playerSide ? sim.Warehouse.Count(itemId) : _npc.Bag.Count(itemId);
            return held - CurrentOfferOf(itemId, playerSide);
        }

        private int CurrentOfferOf(int itemId, bool playerSide)
        {
            var offer = playerSide ? _session.PlayerOffer : _session.NpcOffer;
            return offer.TryGetValue(itemId, out int n) ? n : 0;
        }

        private void Offer(int itemId, bool playerSide, int count)
        {
            if (playerSide)
            {
                _session.OfferPlayerItem(itemId, count);
            }
            else
            {
                _session.OfferNpcItem(itemId, count);
            }

            _resultLine = string.Empty;
            _lastSignature = null;
        }

        // ==================== 确认交易 ====================

        /// <summary>执行交易（钱包 + 玩家仓库 + <c>WorldSimulation.ChangeFavor</c> 回调；先校验后转移，失败不动数据）。</summary>
        private void ExecuteTrade()
        {
            var sim = Sim;
            if (_session == null || sim == null)
            {
                _resultLine = "交易失败：" + TradeResult.FailArgument;
                _resultSuccess = false;
                _lastSignature = null;
                return;
            }

            TradeResult result = _session.Execute(sim.Wallet, sim.Warehouse, sim.ChangeFavor);
            if (!result.Success)
            {
                _resultLine = "交易失败：" + result.FailReason;
                _resultSuccess = false;
            }
            else if (result.IsGift)
            {
                _resultLine = "交易成功（赠予）：好感 +" + result.FavorDelta;
                _resultSuccess = true;
            }
            else if (result.IsBeg)
            {
                _resultLine = "交易成功（索取）：好感 " + result.FavorDelta;
                _resultSuccess = true;
            }
            else
            {
                _resultLine = "交易成功（公平交换）：好感不变";
                _resultSuccess = true;
            }

            _lastSignature = null;
        }

        /// <summary>
        /// 好感增减预览（与 <see cref="TradeSession.Execute"/> 同口径）：公平 → 0；玩家方高出 → 赠予；NPC 方高出 → 索取。
        /// 警告：出售价 0 / 索取被拒 / 任一方金币不足。
        /// </summary>
        private void PreviewFavor(out int delta, out bool isGift, out bool isBeg, out string warning)
        {
            delta = 0;
            isGift = false;
            isBeg = false;
            warning = string.Empty;

            if (_session == null)
            {
                return;
            }

            int playerValue = _session.PlayerOfferValue;
            int npcValue = _session.NpcOfferValue;
            var notes = new List<string>();

            if (_session.PlayerOffer.Count > 0 && playerValue <= 0)
            {
                notes.Add("出售价为 0，不可出售");
            }

            if (!TradePricing.IsFairTrade(playerValue, npcValue))
            {
                if (playerValue > npcValue)
                {
                    isGift = true;
                    delta = TradePricing.GiftFavorDelta(playerValue);
                }
                else
                {
                    isBeg = true;
                    delta = -TradePricing.BegFavorPenalty(npcValue - playerValue);
                    if (_session.Favor <= TradePricing.MinFavor)
                    {
                        notes.Add("对方好感已归零，索取会被拒绝");
                    }
                }
            }

            // 金币校验：差额正=玩家收、负=玩家付。
            var sim = Sim;
            int goldDelta = playerValue - npcValue;
            if (goldDelta > 0 && _npc != null && _npc.Gold < goldDelta)
            {
                notes.Add($"对方金币不足（差 {goldDelta} G）");
            }
            else if (goldDelta < 0 && sim != null && sim.Wallet != null && !sim.Wallet.CanAfford(-goldDelta))
            {
                notes.Add($"玩家金币不足（差 {-goldDelta} G）");
            }

            warning = string.Join("；", notes);
        }

        // ==================== 悬停详情浮层（交易2） ====================

        private void OnSlotHoverEnter(int slot, Button source)
        {
            int itemId = ItemIdOfSlot(source);
            if (itemId <= 0)
            {
                ClearHover();
                return;
            }

            if (itemId != _hoverItemId)
            {
                _hoverItemId = itemId;
                _hoverSince = Time.realtimeSinceStartup;
                _tooltipShown = false;
            }
        }

        private int ItemIdOfSlot(Button source)
        {
            // 由按钮名反查当前槽位物品（刷新后名字与列表对应；空槽返回 0）。
            for (int i = 0; i < GridSlotCount; i++)
            {
                if (ReferenceEquals(source, _pSlots[i]))
                {
                    return i < _playerItems.Count && AvailableOf(_playerItems[i], true) > 0 ? _playerItems[i] : 0;
                }

                if (ReferenceEquals(source, _nSlots[i]))
                {
                    return i < _npcItems.Count && AvailableOf(_npcItems[i], false) > 0 ? _npcItems[i] : 0;
                }
            }

            for (int i = 0; i < OfferSlotCount; i++)
            {
                if (ReferenceEquals(source, _poSlots[i]))
                {
                    var ids = _session.PlayerOffer.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(id => id).ToList();
                    return i < ids.Count ? ids[i] : 0;
                }

                if (ReferenceEquals(source, _noSlots[i]))
                {
                    var ids = _session.NpcOffer.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(id => id).ToList();
                    return i < ids.Count ? ids[i] : 0;
                }
            }

            return 0;
        }

        private void UpdateHover()
        {
            if (_hoverItemId <= 0 || _tooltipShown || _tooltip == null)
            {
                return;
            }

            if (Time.realtimeSinceStartup - _hoverSince < HoverDelaySeconds)
            {
                return;
            }

            _tooltipShown = true;
            ShowTooltip(_hoverItemId);
        }

        private void ShowTooltip(int itemId)
        {
            var def = ItemCatalog.Get(itemId);
            if (def == null || _tooltip == null)
            {
                return;
            }

            if (_textTipName != null)
            {
                _textTipName.text = def.Name;
            }

            if (_textTipKind != null)
            {
                _textTipKind.text = KindText(def.Kind);
            }

            if (_textTipValue != null)
            {
                _textTipValue.text = $"价值：{TradeSession.UnitValue(itemId)}";
            }

            if (_textTipDesc != null)
            {
                _textTipDesc.text = def.MakeMinutes > 0 ? $"制作时长 {def.MakeMinutes / 60} 小时。" : string.Empty;
            }

            if (_textTipTags != null)
            {
                _textTipTags.text = def.Tags != null && def.Tags.Length > 0 ? string.Join("　", def.Tags) : string.Empty;
            }

            _tooltip.gameObject.SetActive(true);
            PositionTooltip();
        }

        private void PositionTooltip()
        {
            if (_tooltip == null)
            {
                return;
            }

            Vector2 sp = Input.mousePosition;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, sp, null, out var local))
            {
                float x = Mathf.Clamp(local.x + 24f, -960f, 960f - 300f);
                float y = Mathf.Clamp(local.y - 12f, -540f + 330f, 540f);
                _tooltip.anchoredPosition = new Vector2(x, y);
            }
        }

        private void ClearHover()
        {
            _hoverItemId = 0;
            _tooltipShown = false;
            if (_tooltip != null)
            {
                _tooltip.gameObject.SetActive(false);
            }
        }

        private static string KindText(ItemKind kind)
        {
            switch (kind)
            {
                case ItemKind.BaseWine: return "基酒";
                case ItemKind.Ingredient: return "配料";
                case ItemKind.Ice: return "冰块";
                case ItemKind.Wine: return "酒水";
                case ItemKind.Equipment: return "装备";
                default: return string.Empty;
            }
        }

        // ==================== 拆分弹窗（交易3） ====================

        private void OpenSplitPopup(int itemId, bool playerSide, int max)
        {
            _splitOpen = true;
            _splitItemId = itemId;
            _splitIsPlayer = playerSide;
            _splitMax = max;
            _splitCount = 1;

            var def = ItemCatalog.Get(itemId);
            if (_textSplitTitle != null)
            {
                _textSplitTitle.text = (def != null ? def.Name : "物品") + " · 拆分数量";
            }

            UpdateSplitView();
            if (_splitPopup != null)
            {
                _splitPopup.gameObject.SetActive(true);
            }
        }

        private void StepSplit(int delta)
        {
            _splitCount = Mathf.Clamp(_splitCount + delta, 1, _splitMax);
            UpdateSplitView();
        }

        private void UpdateSplitView()
        {
            if (_textSplitCount != null)
            {
                _textSplitCount.text = $"{_splitCount} / {_splitMax}";
            }
        }

        private void ConfirmSplit()
        {
            if (_splitOpen && _session != null)
            {
                Offer(_splitItemId, _splitIsPlayer, CurrentOfferOf(_splitItemId, _splitIsPlayer) + _splitCount);
            }

            CloseSplitPopup();
        }

        private void CloseSplitPopup()
        {
            _splitOpen = false;
            if (_splitPopup != null)
            {
                _splitPopup.gameObject.SetActive(false);
            }
        }
    }
}
