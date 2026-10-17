using System.Collections.Generic;
using System.Linq;
using System.Text;
using GameLogic.World;
using TMPro;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// 酒单窗口（对照 Resources/ui01/mixology/ui酒单.png；经营案「酒单」节）：
    /// 非全屏面板（不遮挡顶部 UI），6 上架槽 3 列×2 行卡片，每张卡片展示
    /// 名称 / 标签 / 售价 / 上架状态 / 上架份数步进器，页脚显示当日成本预估。
    ///
    /// 经营案口径：
    /// - 准备阶段（当日首单前）可改上架开关与份数；营业开始（<see cref="TavernSystem.IsLockedToday"/>）后全部只读，仍可打开查看。
    /// - 上架数量为 0 = 不上架，无独立「关架」逻辑；✓ 按钮负责上架/下架（槽位上限 <see cref="TavernSystem.ShelfSlotLimit"/>）。
    /// - 份数不在设定时扣钱，成本按当日实际售出杯数在 0:00 结算；页脚只是预估。
    /// </summary>
    [Window(UILayer.UI)]
    public sealed class DrinkMenuUI : UIWindow
    {
        /// <summary>当前打开的实例（视觉验收/外部判定用）。</summary>
        public static DrinkMenuUI Current { get; private set; }

        private const int CardCount = TavernSystem.ShelfSlotLimit;

        // ==================== 绑定节点 ====================

        private Button _btnClose;
        private TextMeshProUGUI _textFooter;
        private readonly Transform[] _cards = new Transform[CardCount];
        private readonly TextMeshProUGUI[] _textNames = new TextMeshProUGUI[CardCount];
        private readonly TextMeshProUGUI[] _textTags = new TextMeshProUGUI[CardCount];
        private readonly TextMeshProUGUI[] _textStatus = new TextMeshProUGUI[CardCount];
        private readonly TextMeshProUGUI[] _textPrices = new TextMeshProUGUI[CardCount];
        private readonly Button[] _btnPluses = new Button[CardCount];
        private readonly TextMeshProUGUI[] _textCounts = new TextMeshProUGUI[CardCount];
        private readonly Button[] _btnMinuses = new Button[CardCount];
        private readonly Button[] _btnShelves = new Button[CardCount];
        private readonly Image[] _imgShelves = new Image[CardCount];

        // ==================== 运行时状态 ====================

        private readonly List<int> _wines = new List<int>(); // 当前展示的已解锁酒（与卡片一一对应）
        private Sprite _shelfOnSprite;   // ui_mixology_btn_01_s1（绿✓：可点上架）
        private Sprite _shelfOffSprite;  // ui_mixology_btn_01_s2（灰：锁定/不可用）
        private string _lastSignature;

        private static WorldSimulation Sim => GameModule.World.Sim;

        #region 脚本工具生成的代码（手工按 ScriptGenerator 约定绑定）

        protected override void ScriptGenerator()
        {
            _btnClose = FindChildComponent<Button>("m_btnClose");
            _textFooter = FindChildComponent<TextMeshProUGUI>("m_goPanel/m_tmpFooter");
            for (int i = 0; i < CardCount; i++)
            {
                string root = $"m_goPanel/m_goCard{i}";
                _cards[i] = FindChild(root);
                _textNames[i] = FindChildComponent<TextMeshProUGUI>($"{root}/m_tmpName{i}");
                _textTags[i] = FindChildComponent<TextMeshProUGUI>($"{root}/m_goTagBg{i}/m_tmpTag{i}");
                _textStatus[i] = FindChildComponent<TextMeshProUGUI>($"{root}/m_tmpStatus{i}");
                _textPrices[i] = FindChildComponent<TextMeshProUGUI>($"{root}/m_tmpPrice{i}");
                _btnPluses[i] = FindChildComponent<Button>($"{root}/m_btnPlus{i}");
                _textCounts[i] = FindChildComponent<TextMeshProUGUI>($"{root}/m_tmpCount{i}");
                _btnMinuses[i] = FindChildComponent<Button>($"{root}/m_btnMinus{i}");
                _btnShelves[i] = FindChildComponent<Button>($"{root}/m_btnShelf{i}");
                _imgShelves[i] = FindChildComponent<Image>($"{root}/m_btnShelf{i}");

                int slot = i; // 闭包捕获
                if (_btnPluses[i] != null)
                {
                    _btnPluses[i].onClick.AddListener(() => OnClickStep(slot, +1));
                }

                if (_btnMinuses[i] != null)
                {
                    _btnMinuses[i].onClick.AddListener(() => OnClickStep(slot, -1));
                }

                if (_btnShelves[i] != null)
                {
                    _btnShelves[i].onClick.AddListener(() => OnClickToggleShelf(slot));
                }
            }

            if (_btnClose != null)
            {
                _btnClose.onClick.AddListener(() => GameModule.UI.CloseUI<DrinkMenuUI>());
            }
        }

        #endregion

        /// <summary>html-to-ugui 设计分辨率：UIWindow 据此对窗口根做居中等比适配（宿主画布为 1920×1080 横屏参考，宽高各半匹配）。</summary>
        protected override Vector2 DesignResolution => new Vector2(1960f, 1080f);

        protected override void OnCreate()
        {
            Current = this;

            // 窗口 Canvas 自带 Scaler：1960×1080 设计分辨率，与宿主根 Canvas 配置解耦。
            _shelfOnSprite = Resources.Load<Sprite>("ui01/mixology/ui_mixology_btn_01_s1");
            _shelfOffSprite = Resources.Load<Sprite>("ui01/mixology/ui_mixology_btn_01_s2");
        }

        protected override void OnDestroy()
        {
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
        }

        protected override void OnRefresh()
        {
            _lastSignature = null; // 强制全量重刷
            RefreshIfChanged();
        }

        protected override void OnUpdate()
        {
            RefreshIfChanged();
        }

        // ==================== 刷新 ====================

        private void RefreshIfChanged()
        {
            var sim = Sim;
            if (sim == null || !sim.IsRunning)
            {
                return;
            }

            var sb = new StringBuilder(128);
            sb.Append(sim.Tavern.IsLockedToday ? 'L' : 'U');
            foreach (int id in sim.Brewing.UnlockedWines)
            {
                sb.Append(id).Append(':');
                sb.Append(sim.Tavern.PlannedCountOf(id)).Append(',');
            }

            sb.Append('|');
            foreach (int id in sim.Tavern.Shelf)
            {
                sb.Append(id).Append(',');
            }

            string signature = sb.ToString();
            if (signature == _lastSignature)
            {
                return;
            }

            _lastSignature = signature;
            RefreshCards(sim);
        }

        private void RefreshCards(WorldSimulation sim)
        {
            bool locked = sim.Tavern.IsLockedToday;
            _wines.Clear();
            _wines.AddRange(sim.Brewing.UnlockedWines);

            for (int i = 0; i < CardCount; i++)
            {
                bool hasWine = i < _wines.Count;
                if (_cards[i] != null)
                {
                    _cards[i].gameObject.SetActive(hasWine);
                }

                if (!hasWine)
                {
                    continue;
                }

                int wineId = _wines[i];
                var def = ItemCatalog.Get(wineId);
                bool onShelf = sim.Tavern.Shelf.Contains(wineId);
                int planned = sim.Tavern.PlannedCountOf(wineId);

                if (_textNames[i] != null && def != null)
                {
                    _textNames[i].text = def.Name;
                }

                if (_textTags[i] != null && def != null)
                {
                    _textTags[i].text = def.Tags != null && def.Tags.Length > 0 ? def.Tags[0] : string.Empty;
                }

                if (_textStatus[i] != null)
                {
                    _textStatus[i].text = onShelf ? "已上架" : "未上架";
                    _textStatus[i].color = onShelf ? new Color(0.54f, 0.51f, 0.35f) : new Color(0.72f, 0.70f, 0.60f);
                }

                if (_textPrices[i] != null && def != null)
                {
                    _textPrices[i].text = $"售价：{def.Price}G";
                }

                if (_textCounts[i] != null)
                {
                    _textCounts[i].text = planned.ToString();
                }

                // 锁定期（营业中）全部只读；✓ 按钮换灰色态。
                SetInteractable(_btnPluses[i], !locked);
                SetInteractable(_btnMinuses[i], !locked);
                SetInteractable(_btnShelves[i], !locked);
                if (_imgShelves[i] != null)
                {
                    var sprite = locked ? _shelfOffSprite : _shelfOnSprite;
                    if (sprite != null)
                    {
                        _imgShelves[i].sprite = sprite;
                    }
                }
            }

            // 页脚：当日成本预估 = Σ 上架酒 份数 × 单份成本（售价 × 60% 取整，经营案口径）。
            long estimate = 0;
            foreach (int wineId in sim.Tavern.Shelf)
            {
                var def = ItemCatalog.Get(wineId);
                if (def != null)
                {
                    estimate += (long)sim.Tavern.PlannedCountOf(wineId) * def.Price * 60 / 100;
                }
            }

            if (_textFooter != null)
            {
                _textFooter.text = locked
                    ? $"当日成本预估：{estimate}G　（营业中，酒单已锁定）"
                    : $"当日成本预估：{estimate}G";
            }
        }

        private static void SetInteractable(Button btn, bool interactable)
        {
            if (btn != null)
            {
                btn.interactable = interactable;
            }
        }

        // ==================== 交互 ====================

        private void OnClickStep(int slot, int delta)
        {
            var sim = Sim;
            if (sim == null || slot >= _wines.Count)
            {
                return;
            }

            int wineId = _wines[slot];
            int next = Mathf.Max(0, sim.Tavern.PlannedCountOf(wineId) + delta);
            sim.Tavern.SetPlannedCount(wineId, next); // 锁定期返回 false，经营案：营业中不可改
        }

        private void OnClickToggleShelf(int slot)
        {
            var sim = Sim;
            if (sim == null || slot >= _wines.Count)
            {
                return;
            }

            int wineId = _wines[slot];
            var shelf = new List<int>(sim.Tavern.Shelf);
            if (shelf.Contains(wineId))
            {
                shelf.Remove(wineId);
                if (sim.Tavern.SetShelf(shelf))
                {
                    sim.Tavern.SetPlannedCount(wineId, 0); // 下架顺带清零份数（份数 0 = 不上架，经营案口径）
                }
            }
            else
            {
                shelf.Add(wineId);
                if (sim.Tavern.SetShelf(shelf) && sim.Tavern.PlannedCountOf(wineId) <= 0)
                {
                    sim.Tavern.SetPlannedCount(wineId, 10); // 新上架默认 10 份（参考图占位数值口径）
                }
            }
        }
    }
}
