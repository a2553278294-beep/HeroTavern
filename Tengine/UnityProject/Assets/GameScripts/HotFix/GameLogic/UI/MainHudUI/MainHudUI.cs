using GameLogic.World;
using TMPro;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// 主界面 HUD 窗口（对照 Resources/ui01/main/ui主界面.jpg / ui主界面2.jpg / ui主界面3.jpg）：
    /// 顶栏暂停+三档加速图标、日期+时间、中央钟摆、金币框（点击切换结算分栏）、NPC 交互气泡。
    ///
    /// 设计分辨率 1960×1080：窗口 Canvas 自带 CanvasScaler（ScaleWithScreenSize），与宿主根 Canvas 解耦。
    /// 图标选中态=_s1、未选中=_s2（Resources/ui01/说明.txt 约定），另一态贴图运行时从 Resources 加载互换。
    /// 结算分栏：DaySettled 后展示 5 秒自动收起（经营案口径），点击金币框可回看最近一次报告。
    /// </summary>
    [Window(UILayer.UI)]
    public sealed class MainHudUI : UIWindow
    {
        /// <summary>当前打开的实例（场景点击/气泡交互用；窗口关闭后置 null）。</summary>
        public static MainHudUI Current { get; private set; }

        // ==================== 绑定节点 ====================

        private Image _imgBtnPause;
        private Image _imgBtnSpeed1;
        private Image _imgBtnSpeed2;
        private Image _imgBtnSpeed3;
        private TextMeshProUGUI _textDate;
        private TextMeshProUGUI _textTime;
        private TextMeshProUGUI _textMoney;
        private RectTransform _pendulum;
        private Transform _settlementPanel;
        private TextMeshProUGUI _textSettleTitle;
        private TextMeshProUGUI _textSettleDay;
        private TextMeshProUGUI _textSettleNote; // 页脚：破产红字 / 默认结出提示（经营案结算分栏方案A）
        private readonly TextMeshProUGUI[] _settleLabels = new TextMeshProUGUI[4];
        private readonly TextMeshProUGUI[] _settleValues = new TextMeshProUGUI[4];
        private RectTransform _bubble;
        private Button _btnTalk;
        private Button _btnTrade;
        private Button _btnView;

        // ==================== 运行时状态 ====================

        private WorldSimulation _hookedSim;
        private SettlementReport _lastReport;
        private float _reportUntil = -1f;
        private bool _settlementPinned;
        private CharacterData _bubbleTarget;

        // ===== 结算分栏配色/文案常量（经营案结算分栏方案A）=====
        // 说明：结算分栏标题沿用 prefab m_tmpSettleTitle 默认文案「今日累计」，口径=需求案
        // 「展示刚结束的这一营业日」，故此处不覆盖标题，只固定页脚与数值行的配色。
        /// <summary>页脚默认文案，与 prefab MainHudUI.prefab 中 m_tmpSettleNote 的 m_text 逐字一致。</summary>
        private const string SettleNoteDefaultText = "每天 0:00 自动结出「本日流水」";

        /// <summary>页脚默认配色，对应 prefab m_tmpSettleNote 的 m_fontColor（154,148,112）/255。</summary>
        private static readonly Color SettleNoteDefaultColor = new Color(0.6039216f, 0.5803922f, 0.4392157f, 1f);

        /// <summary>收益绿（正数行 / 收入行）。</summary>
        private static readonly Color SettlePositiveColor = new Color(0.62f, 0.70f, 0.29f);

        /// <summary>支出红（负数行 / 破产红字）。</summary>
        private static readonly Color SettleNegativeColor = new Color(0.70f, 0.35f, 0.29f);

        /// <summary>中性色：杯数行（文本、非金额，不参与正负着色），对齐 prefab 标签色（138,130,89）/255。</summary>
        private static readonly Color SettleNeutralColor = new Color(0.5411765f, 0.50980395f, 0.34901962f, 1f);

        // 图标双态贴图（运行时加载，路径不带扩展名）。
        private Sprite _iconPauseOn, _iconPauseOff;
        private Sprite _iconSpeed1On, _iconSpeed1Off;
        private Sprite _iconSpeed2On, _iconSpeed2Off;
        private Sprite _iconSpeed3On, _iconSpeed3Off;

        // 脏检查缓存：只有值变化才改 text（TMP 文本重建有开销）。
        private int _lastMinute = -1;
        private int _lastDay = -1;
        private long _lastGold = -1;
        private int _lastSpeed = -1;
        private bool _lastPaused;

        private static WorldSimulation Sim => GameModule.World.Sim;

        #region 脚本工具生成的代码（手工按 ScriptGenerator 约定绑定）

        protected override void ScriptGenerator()
        {
            _imgBtnPause = FindChildComponent<Image>("m_imgBtnPause");
            _imgBtnSpeed1 = FindChildComponent<Image>("m_imgBtnSpeed1");
            _imgBtnSpeed2 = FindChildComponent<Image>("m_imgBtnSpeed2");
            _imgBtnSpeed3 = FindChildComponent<Image>("m_imgBtnSpeed3");
            _textDate = FindChildComponent<TextMeshProUGUI>("m_tmpDate");
            _textTime = FindChildComponent<TextMeshProUGUI>("m_tmpTime");
            _textMoney = FindChildComponent<TextMeshProUGUI>("m_tmpMoney");
            _pendulum = FindChildComponent<RectTransform>("m_imgPendulum");
            _settlementPanel = FindChild("m_goSettlementPanel");
            _textSettleTitle = FindChildComponent<TextMeshProUGUI>("m_goSettlementPanel/m_tmpSettleTitle");
            _textSettleDay = FindChildComponent<TextMeshProUGUI>("m_goSettlementPanel/m_tmpSettleDay");
            // 页脚（prefab 已有节点，此前未在代码绑定）：余额不足时显示红字，否则还原默认结出提示。
            _textSettleNote = FindChildComponent<TextMeshProUGUI>("m_goSettlementPanel/m_tmpSettleNote");
            for (int i = 0; i < 4; i++)
            {
                _settleLabels[i] = FindChildComponent<TextMeshProUGUI>($"m_goSettlementPanel/m_tmpSettleLabel{i}");
                _settleValues[i] = FindChildComponent<TextMeshProUGUI>($"m_goSettlementPanel/m_tmpSettleValue{i}");
            }

            _bubble = FindChildComponent<RectTransform>("m_goInteractBubble");
            _btnTalk = FindChildComponent<Button>("m_goInteractBubble/m_btnTalk");
            _btnTrade = FindChildComponent<Button>("m_goInteractBubble/m_btnTrade");
            _btnView = FindChildComponent<Button>("m_goInteractBubble/m_btnView");

            // 图标按钮：烘焙产物是纯 Image，运行时补 Button 组件接点击。
            BindIconButton(_imgBtnPause, OnClickPause);
            BindIconButton(_imgBtnSpeed1, () => OnClickSpeed(1));
            BindIconButton(_imgBtnSpeed2, () => OnClickSpeed(2));
            BindIconButton(_imgBtnSpeed3, () => OnClickSpeed(3));

            // 金币框整条可点击：回看/收起结算分栏。
            if (_textMoney != null)
            {
                var btn = _textMoney.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(OnClickMoney);
            }

            if (_btnTalk != null)
            {
                _btnTalk.onClick.AddListener(OnClickTalk);
            }

            if (_btnTrade != null)
            {
                _btnTrade.onClick.AddListener(OnClickTrade);
            }

            if (_btnView != null)
            {
                _btnView.onClick.AddListener(OnClickView);
            }

            // 非交互图形关掉射线接收，避免挡住世界点击（全屏藤蔓框首当其冲）。
            SetRaycast(false, _pendulum,
                _textDate != null ? _textDate.rectTransform : null,
                _textTime != null ? _textTime.rectTransform : null,
                FindChildComponent<RectTransform>("m_imgFrame"),
                FindChildComponent<RectTransform>("m_goSettlementPanel/m_imgSettleLine"));
        }

        #endregion

        private static Sprite _needleSprite;

        /// <summary>气泡按钮规整：无底图时铺浅色底；文字子节点拉满按钮矩形并居中（修烘焙错位）。</summary>
        private static void FixBubbleButton(Button btn)
        {
            if (btn == null)
            {
                return;
            }

            var img = btn.GetComponent<Image>();
            if (img != null && img.sprite == null)
            {
                img.color = new Color(0.99f, 0.97f, 0.90f, 0.96f);
            }

            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp == null)
            {
                return;
            }

            var rt = tmp.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.24f, 0.18f, 0.11f);
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 16f;
            tmp.fontSizeMax = 30f;
        }

        /// <summary>程序化钟摆针：12×58 金色圆头短针（摆轴在顶端），替代烘焙误配的整张钟面贴图。</summary>
        private static Sprite NeedleSprite()
        {
            if (_needleSprite != null)
            {
                return _needleSprite;
            }

            const int w = 12, h = 58;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var gold = new Color(0.72f, 0.55f, 0.22f);
            var edge = new Color(0.48f, 0.34f, 0.12f);
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = x - (w - 1) / 2f;
                    Color c = clear;
                    if (y >= 8 && Mathf.Abs(dx) <= 2f)
                    {
                        c = Mathf.Abs(dx) >= 1.5f ? edge : gold;
                    }

                    float dy = y - 7f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= 6f)
                    {
                        c = dist >= 4.5f ? edge : gold;
                    }

                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            _needleSprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 1f));
            return _needleSprite;
        }

        /// <summary>html-to-ugui 设计分辨率：UIWindow 据此对窗口根做居中等比适配（宿主画布为 1920×1080 横屏参考，宽高各半匹配）。</summary>
        protected override Vector2 DesignResolution => new Vector2(1960f, 1080f);

        protected override void OnCreate()
        {
            Current = this;

            // 窗口 Canvas 自带 Scaler：1960×1080 设计分辨率，与宿主根 Canvas 配置解耦。
            // 钟摆：摆轴在表盘顶部中心（960,18），改 pivot 后绕轴摆动。
            if (_pendulum != null)
            {
                _pendulum.pivot = new Vector2(0.5f, 1f);
                _pendulum.anchorMin = _pendulum.anchorMax = new Vector2(0f, 1f);
                _pendulum.anchoredPosition = new Vector2(960f, -18f);

                // 烘焙误把整张钟面贴图塞进指针节点（72×234 拉伸成贯穿顶栏的长条，视觉错位）；
                // 本批没有独立指针贴图，运行时生成一根短金针替代，长度收进表盘半径内。
                _pendulum.sizeDelta = new Vector2(12f, 58f);
                var pendulumImg = _pendulum.GetComponent<Image>();
                if (pendulumImg != null)
                {
                    pendulumImg.sprite = NeedleSprite();
                    pendulumImg.preserveAspect = false;
                    pendulumImg.color = Color.white;
                }
            }

            // 气泡：pivot 底部中心，便于贴 NPC 头顶定位。
            if (_bubble != null)
            {
                _bubble.pivot = new Vector2(0.5f, 0f);
                _bubble.gameObject.SetActive(false);
            }

            // 交互气泡按钮：烘焙 alpha=0 无底图、文字子节点与按钮错位——统一铺浅色底、文字拉满居中。
            FixBubbleButton(_btnTalk);
            FixBubbleButton(_btnTrade);
            FixBubbleButton(_btnView);

            if (_settlementPanel != null)
            {
                _settlementPanel.gameObject.SetActive(false);
            }

            LoadIconSprites();
        }

        protected override void OnDestroy()
        {
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }

            if (_hookedSim != null)
            {
                _hookedSim.SettlementReady -= OnSettlementReady;
                _hookedSim = null;
            }
        }

        // ==================== 每帧刷新 ====================

        protected override void OnUpdate()
        {
            var sim = Sim;
            if (sim == null || !sim.IsRunning)
            {
                return;
            }

            EnsureSimHooked(sim);

            // 日期/时间（分钟级脏检查）。
            var t = sim.Time;
            int minuteStamp = t.Hour * 60 + t.Minute;
            if (minuteStamp != _lastMinute)
            {
                _lastMinute = minuteStamp;
                if (_textTime != null)
                {
                    _textTime.text = $"{t.Hour}:{t.Minute:00}";
                }
            }

            if (t.DayOverall != _lastDay)
            {
                _lastDay = t.DayOverall;
                if (_textDate != null)
                {
                    // 参考图为「年/月/日」：GameTime 一年 365 天无月概念，按 30 天一月换算（12 月×30 天+5 天余量进 12 月）。
                    int doy = t.DayOfYear;
                    int month = Mathf.Min((doy - 1) / 30 + 1, 12);
                    int day = (doy - 1) % 30 + 1;
                    _textDate.text = $"{t.Year}年{month}月{day}日";
                }
            }

            // 金币。
            long gold = sim.Wallet.Currency;
            if (gold != _lastGold)
            {
                _lastGold = gold;
                if (_textMoney != null)
                {
                    _textMoney.text = $"{gold}金币";
                }
            }

            // 暂停/加速图标态。
            if (sim.SpeedLevel != _lastSpeed || sim.IsPaused != _lastPaused)
            {
                _lastSpeed = sim.SpeedLevel;
                _lastPaused = sim.IsPaused;
                RefreshSpeedIcons(sim);
            }

            // 钟摆：按当日时刻摆动（0 点最左 -60°，24 点最右 +60°，参考图 9:04 略偏左）。
            if (_pendulum != null)
            {
                float fraction = (t.Hour * 60f + t.Minute) / (24f * 60f);
                float angle = Mathf.Lerp(60f, -60f, fraction);
                _pendulum.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            // 结算分栏自动收起（钉住状态除外）。
            if (!_settlementPinned && _reportUntil > 0f && Time.realtimeSinceStartup > _reportUntil)
            {
                _reportUntil = -1f;
                if (_settlementPanel != null)
                {
                    _settlementPanel.gameObject.SetActive(false);
                }
            }

            FollowBubble();
        }

        // ==================== 顶栏交互 ====================

        private void OnClickPause()
        {
            var sim = Sim;
            if (sim != null)
            {
                GameModule.World.SetPaused(!sim.IsPaused);
            }
        }

        private void OnClickSpeed(int level)
        {
            var sim = Sim;
            if (sim == null)
            {
                return;
            }

            GameModule.World.SetSpeedLevel(level);
            if (sim.IsPaused)
            {
                GameModule.World.SetPaused(false); // 点档位顺带恢复走时
            }
        }

        private void OnClickMoney()
        {
            if (_settlementPanel == null)
            {
                return;
            }

            if (_settlementPanel.gameObject.activeSelf && !_settlementPinned)
            {
                return; // 5 秒自动窗期内不响应（经营案：期间不可收起）
            }

            bool show = !_settlementPanel.gameObject.activeSelf;
            _settlementPinned = show;
            if (show && _lastReport != null)
            {
                FillSettlement(_lastReport);
            }

            _settlementPanel.gameObject.SetActive(show);
        }

        private void RefreshSpeedIcons(WorldSimulation sim)
        {
            SetIcon(_imgBtnPause, _iconPauseOn, _iconPauseOff, sim.IsPaused);
            SetIcon(_imgBtnSpeed1, _iconSpeed1On, _iconSpeed1Off, !sim.IsPaused && sim.SpeedLevel == 1);
            SetIcon(_imgBtnSpeed2, _iconSpeed2On, _iconSpeed2Off, !sim.IsPaused && sim.SpeedLevel == 2);
            SetIcon(_imgBtnSpeed3, _iconSpeed3On, _iconSpeed3Off, !sim.IsPaused && sim.SpeedLevel == 3);
        }

        private static void SetIcon(Image img, Sprite on, Sprite off, bool selected)
        {
            if (img == null)
            {
                return;
            }

            var sprite = selected ? on : off;
            if (sprite != null)
            {
                img.sprite = sprite;
            }
        }

        private void LoadIconSprites()
        {
            _iconPauseOn = Resources.Load<Sprite>("ui01/main/ui_main_icon_04_s1");
            _iconPauseOff = Resources.Load<Sprite>("ui01/main/ui_main_icon_04_s2");
            _iconSpeed1On = Resources.Load<Sprite>("ui01/main/ui_main_icon_03_s1");
            _iconSpeed1Off = Resources.Load<Sprite>("ui01/main/ui_main_icon_03_s2");
            _iconSpeed2On = Resources.Load<Sprite>("ui01/main/ui_main_icon_02_s1");
            _iconSpeed2Off = Resources.Load<Sprite>("ui01/main/ui_main_icon_02_s2");
            _iconSpeed3On = Resources.Load<Sprite>("ui01/main/ui_main_icon_01_s1");
            _iconSpeed3Off = Resources.Load<Sprite>("ui01/main/ui_main_icon_01_s2");
        }

        private static void BindIconButton(Image img, UnityEngine.Events.UnityAction onClick)
        {
            if (img == null)
            {
                return;
            }

            var btn = img.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(onClick);
        }

        private static void SetRaycast(bool enabled, params RectTransform[] nodes)
        {
            foreach (var node in nodes)
            {
                if (node == null)
                {
                    continue;
                }

                var graphic = node.GetComponent<Graphic>();
                if (graphic != null)
                {
                    graphic.raycastTarget = enabled;
                }
            }
        }

        // ==================== 结算分栏 ====================

        private void EnsureSimHooked(WorldSimulation sim)
        {
            if (ReferenceEquals(_hookedSim, sim))
            {
                return;
            }

            if (_hookedSim != null)
            {
                _hookedSim.SettlementReady -= OnSettlementReady;
            }

            _hookedSim = sim;
            _lastMinute = -1;
            _lastDay = -1;
            _lastGold = -1;
            _lastSpeed = -1;
            _lastReport = null;
            _reportUntil = -1f;
            _settlementPinned = false;
            HideBubble();
            if (_settlementPanel != null)
            {
                _settlementPanel.gameObject.SetActive(false);
            }

            if (sim != null)
            {
                sim.SettlementReady += OnSettlementReady;
            }
        }

        private void OnSettlementReady(SettlementReport report)
        {
            _lastReport = report;
            _settlementPinned = false;
            _reportUntil = Time.realtimeSinceStartup + 5f; // 经营案：保留 5 秒自动收起
            FillSettlement(report);
            if (_settlementPanel != null)
            {
                _settlementPanel.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// 结算分栏四行填充（经营案结算分栏「方案A」，口径=需求案「展示刚结束的这一营业日」）：
        /// 第 1 行 收入 = 酒水付费 + 悬赏奖励 + 发布费返还（同属当日入账，故合并为一行）；
        /// 第 2/3 行 支出拆成营业成本、酒保工资两行（不再合并）；
        /// 第 4 行 杯数 = 售出 N 杯 / 未售 M 杯（文本行，中性色）；
        /// 扣费后余额 &lt; 0（游戏结束）时在页脚追加红字，详见 <see cref="ApplySettleNote"/>。
        /// </summary>
        private void FillSettlement(SettlementReport report)
        {
            if (_textSettleDay != null)
            {
                _textSettleDay.text = $"第 {report.DayOverall} 天";
            }

            // 标题保持 prefab 默认「今日累计」（prefab m_tmpSettleTitle 的 m_text 已核实为该文案），
            // 与「展示刚结束的这一营业日」口径一致，无需在代码里改写。

            // 第 1 行 收入：酒水付费 + 悬赏奖励 + 发布费返还（经营案：发布费返还同属入账）。
            SetSettleRow(0, "收入", report.Income + report.BountyReward + report.BountyRefund);
            // 第 2、3 行 支出：营业成本、酒保工资各占一行；支出记负值以沿用正绿负红着色。
            SetSettleRow(1, "成本", -report.Cost);
            SetSettleRow(2, "酒保工资", -report.Wage);
            // 第 4 行 杯数：文本行，不参与正负着色（走 string 重载）。
            SetSettleRow(3, "杯数", $"售出 {report.SoldCups} / 未售 {report.UnsoldCups}");

            // 页脚：破产红字 / 默认结出提示。
            ApplySettleNote(report);
        }

        /// <summary>
        /// 页脚文案（经营案结算分栏方案A）：<paramref name="report"/> 为游戏结束
        /// （扣费后余额 &lt; 0）时显示红字「余额不足，游戏结束」；否则显式还原 prefab 默认
        /// 文案与默认色，避免点金币框回看上一份报告时残留红字。
        /// </summary>
        private void ApplySettleNote(SettlementReport report)
        {
            if (_textSettleNote == null)
            {
                return;
            }

            if (report != null && report.IsGameOver)
            {
                _textSettleNote.text = "余额不足，游戏结束";
                _textSettleNote.color = SettleNegativeColor;
            }
            else
            {
                _textSettleNote.text = SettleNoteDefaultText;
                _textSettleNote.color = SettleNoteDefaultColor;
            }
        }

        /// <summary>数值行：正数显示 "+N" 并用收益绿、负数用支出红（结算分栏既有着色逻辑不变）。</summary>
        private void SetSettleRow(int index, string label, long value)
        {
            if (_settleLabels[index] != null)
            {
                _settleLabels[index].text = label;
            }

            if (_settleValues[index] != null)
            {
                _settleValues[index].text = (value >= 0 ? "+" : "") + value;
                _settleValues[index].color = value >= 0 ? SettlePositiveColor : SettleNegativeColor;
            }
        }

        /// <summary>文本行（杯数行）：值不是金额，固定用中性色，不做正负着色。</summary>
        private void SetSettleRow(int index, string label, string value)
        {
            if (_settleLabels[index] != null)
            {
                _settleLabels[index].text = label;
            }

            if (_settleValues[index] != null)
            {
                _settleValues[index].text = value;
                _settleValues[index].color = SettleNeutralColor;
            }
        }

        // ==================== NPC 交互气泡 ====================

        /// <summary>点中场景 NPC：气泡贴到 NPC 头顶（角色系统 3.2 的交谈/交易/查看）。</summary>
        public void ShowBubble(CharacterData character)
        {
            _bubbleTarget = character;
            Log.Info($"[ClickProbe] ShowBubble {character.Name}：_bubble={(_bubble != null ? "在" : "为null")}");
            if (_bubble != null)
            {
                _bubble.gameObject.SetActive(true);
            }

            FollowBubble();
            if (_bubble != null)
            {
                Log.Info($"[ClickProbe] 气泡 active={_bubble.gameObject.activeInHierarchy} anchoredPos={_bubble.anchoredPosition} scale={_bubble.lossyScale}");
            }
        }

        public void HideBubble()
        {
            _bubbleTarget = null;
            if (_bubble != null)
            {
                _bubble.gameObject.SetActive(false);
            }
        }

        /// <summary>气泡目标离场/死亡时由场景引导调用。</summary>
        public void OnCharacterRemoved(int characterId)
        {
            if (_bubbleTarget != null && _bubbleTarget.Id == characterId)
            {
                HideBubble();
            }
        }

        private void FollowBubble()
        {
            if (_bubbleTarget == null || _bubble == null || !_bubble.gameObject.activeSelf)
            {
                return;
            }

            if (!_bubbleTarget.IsAlive)
            {
                HideBubble();
                return;
            }

            var boot = WorldSceneBootstrap.Instance;
            var cam = boot != null ? boot.SceneCamera : null;
            if (cam == null)
            {
                return;
            }

            if (!boot.Views.TryGetValue(_bubbleTarget.Id, out var view) || view == null)
            {
                return;
            }

            Vector3 sp = cam.WorldToScreenPoint(view.transform.position + Vector3.up * 1.1f);
            if (sp.z <= 0f)
            {
                return;
            }

            // 两处坐标口径修正（实证「点 NPC 不出气泡」）：
            // 1) UIRoot 的 UICanvas 是 ScreenSpaceCamera（带 UICamera），不是 Overlay——转换必须传渲染相机，
            //    传 null 会按 Overlay 直读屏幕像素，游戏视图与画布不像素对齐时结果错乱；
            // 2) ScreenPointToLocalPointInRectangle 返回相对父节点 pivot（中心）的局部坐标，而烘焙 prefab
            //    的气泡锚点在左上角（0,1），直接赋值 anchoredPosition 会差半个窗口（980×540）甩出屏幕。
            var parentRect = _bubble.parent as RectTransform;
            var rootCanvas = _bubble.GetComponentInParent<Canvas>()?.rootCanvas;
            var uiCam = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? rootCanvas.worldCamera
                : null;
            if (parentRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, sp, uiCam, out var local))
            {
                var r = parentRect.rect;
                var anchor = new Vector2(
                    Mathf.Lerp(r.xMin, r.xMax, _bubble.anchorMin.x),
                    Mathf.Lerp(r.yMin, r.yMax, _bubble.anchorMin.y));
                _bubble.anchoredPosition = local - anchor;
            }
        }

        private void OnClickTalk()
        {
            var target = _bubbleTarget;
            HideBubble();
            if (target != null)
            {
                WorldSceneBootstrap.Instance?.Hud?.BeginInteractionTalk(target);
            }
        }

        private void OnClickTrade()
        {
            var target = _bubbleTarget;
            HideBubble();
            if (target != null)
            {
                WorldSceneBootstrap.Instance?.Hud?.BeginInteractionTrade(target);
            }
        }

        private void OnClickView()
        {
            var target = _bubbleTarget;
            HideBubble();
            if (target != null)
            {
                WorldSceneBootstrap.Instance?.Hud?.BeginInteractionView(target);
            }
        }
    }
}
