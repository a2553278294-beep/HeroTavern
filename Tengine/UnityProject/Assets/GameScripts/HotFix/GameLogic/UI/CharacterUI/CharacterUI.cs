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
    /// 角色查看窗口（《角色系统 v3.1》3.5「查看」）：标题页签 = 角色名；属性 / 背包两页签。
    /// 属性页：立绘占位 + 好感度 + 角色信息（姓名/年龄/职业/性格）+ 基础战斗属性 + 分数摘要；
    /// 背包页：小人占位 + 金币 + 装备三槽（武器/防具/饰品）+ 物品网格（堆叠数角标）。
    /// 只读展示，不修改任何角色状态；不夺走走时（经营案「暂停与否由玩家决定」）。
    /// 立绘/小人美术未接入：沿用羊皮纸槽 + lv 角花占位。
    /// </summary>
    [Window(UILayer.UI)]
    public sealed class CharacterUI : UIWindow
    {
        /// <summary>当前打开的实例。</summary>
        public static CharacterUI Current { get; private set; }

        /// <summary>待查看角色（ShowUIAsync 为异步，先寄存、OnCreate 自取）。</summary>
        public static CharacterData PendingNpc;

        private const int BagSlotCount = 24;

        private static readonly Color TabOnColor = new Color(0.62f, 0.70f, 0.29f);
        private static readonly Color TabOffColor = new Color(0.79f, 0.77f, 0.68f);

        // ==================== 绑定节点 ====================

        private TextMeshProUGUI _textTitle;
        private Button _btnClose;
        private Button _btnTabAttr;
        private Button _btnTabBag;
        private Transform _pageAttr;
        private Transform _pageBag;
        private TextMeshProUGUI _textFavor;
        private TextMeshProUGUI _textInfo;
        private TextMeshProUGUI _textScores;
        private TextMeshProUGUI _textBagGold;
        private TextMeshProUGUI _textEquip;
        private readonly TextMeshProUGUI[] _bagNames = new TextMeshProUGUI[BagSlotCount];
        private readonly TextMeshProUGUI[] _bagCounts = new TextMeshProUGUI[BagSlotCount];
        private readonly Image[] _bagFrames = new Image[BagSlotCount];
        private readonly Image[] _bagBgs = new Image[BagSlotCount];

        // ==================== 运行时状态 ====================

        private CharacterData _npc;
        private int _tab;
        private string _lastSignature;
        private Sprite _lv1;

        #region 脚本工具生成的代码（手工按 ScriptGenerator 约定绑定）

        protected override void ScriptGenerator()
        {
            _textTitle = FindChildComponent<TextMeshProUGUI>("m_goTitleTab/m_tmpTitle");
            _btnClose = FindChildComponent<Button>("m_btnClose");
            _btnTabAttr = FindChildComponent<Button>("m_goPanel/m_btnTabAttr");
            _btnTabBag = FindChildComponent<Button>("m_goPanel/m_btnTabBag");
            // 烘焙按钮自带占位 "Text (TMP)" 子节点，与 m_tmpTabAttr/m_tmpTabBag 标签叠字：隐藏占位。
            HideBakedPlaceholderText(_btnTabAttr);
            HideBakedPlaceholderText(_btnTabBag);
            _pageAttr = FindChild("m_goPanel/m_goPageAttr");
            _pageBag = FindChild("m_goPanel/m_goPageBag");
            _textFavor = FindChildComponent<TextMeshProUGUI>("m_goPanel/m_goPageAttr/m_goFavorBlock/m_tmpFavor");
            _textInfo = FindChildComponent<TextMeshProUGUI>("m_goPanel/m_goPageAttr/m_tmpInfo");
            _textScores = FindChildComponent<TextMeshProUGUI>("m_goPanel/m_goPageAttr/m_goScoreBlock/m_tmpScores");
            _textBagGold = FindChildComponent<TextMeshProUGUI>("m_goPanel/m_goPageBag/m_tmpBagGold");
            _textEquip = FindChildComponent<TextMeshProUGUI>("m_goPanel/m_goPageBag/m_tmpEquip");
            for (int i = 0; i < BagSlotCount; i++)
            {
                _bagNames[i] = FindChildComponent<TextMeshProUGUI>($"m_goPanel/m_goPageBag/m_goBagSlot{i}/m_tmpBagSlotName{i}");
                _bagCounts[i] = FindChildComponent<TextMeshProUGUI>($"m_goPanel/m_goPageBag/m_goBagSlot{i}/m_tmpBagSlotCount{i}");
                _bagFrames[i] = FindChildComponent<Image>($"m_goPanel/m_goPageBag/m_goBagSlot{i}/m_imgBagSlotFrame{i}");
                _bagBgs[i] = FindChildComponent<Image>($"m_goPanel/m_goPageBag/m_goBagSlot{i}");
            }

            if (_btnClose != null)
            {
                _btnClose.onClick.AddListener(() => GameModule.UI.CloseUI<CharacterUI>());
            }

            if (_btnTabAttr != null)
            {
                _btnTabAttr.onClick.AddListener(() => SetTab(0));
            }

            if (_btnTabBag != null)
            {
                _btnTabBag.onClick.AddListener(() => SetTab(1));
            }
        }

        /// <summary>隐藏按钮下烘焙占位文本（名为 "Text (TMP)" 的子节点）。</summary>
        private static void HideBakedPlaceholderText(Button btn)
        {
            if (btn == null)
            {
                return;
            }
            var placeholder = btn.transform.Find("Text (TMP)");
            if (placeholder != null)
            {
                placeholder.gameObject.SetActive(false);
            }
        }

        #endregion

        /// <summary>html-to-ugui 设计分辨率：UIWindow 据此对窗口根做居中等比适配（宿主画布为 1920×1080 横屏参考，宽高各半匹配）。</summary>
        protected override Vector2 DesignResolution => new Vector2(1960f, 1080f);

        protected override void OnCreate()
        {
            Current = this;

            _lv1 = Resources.Load<Sprite>("ui01/common/ui_common_frame_lv1");

            if (PendingNpc != null)
            {
                var npc = PendingNpc;
                PendingNpc = null;
                Open(npc);
            }
            else
            {
                SetTab(0);
            }
        }

        protected override void OnDestroy()
        {
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }

            WorldSceneBootstrap.Instance?.Hud?.OnCharacterPanelClosed();
        }

        /// <summary>打开并查看角色；每次打开回到「属性」页。</summary>
        public void Open(CharacterData npc)
        {
            _npc = npc;
            _lastSignature = null;
            SetTab(0);
            RefreshIfChanged();
        }

        private void SetTab(int tab)
        {
            _tab = tab;
            if (_pageAttr != null)
            {
                _pageAttr.gameObject.SetActive(tab == 0);
            }

            if (_pageBag != null)
            {
                _pageBag.gameObject.SetActive(tab == 1);
            }

            TintTab(_btnTabAttr, tab == 0);
            TintTab(_btnTabBag, tab == 1);
        }

        private static void TintTab(Button btn, bool selected)
        {
            if (btn == null)
            {
                return;
            }

            var img = btn.GetComponent<Image>();
            if (img != null)
            {
                img.color = selected ? TabOnColor : TabOffColor;
            }
        }

        protected override void OnUpdate()
        {
            RefreshIfChanged();
        }

        // ==================== 刷新 ====================

        private void RefreshIfChanged()
        {
            if (_npc == null)
            {
                return;
            }

            // 签名：属性页字段 + 背包内容；角色死亡/属性变动时重刷。
            var sb = new StringBuilder(128);
            sb.Append(_npc.Name).Append(_npc.Age).Append(_npc.FavorToPlayer).Append(_npc.Gold)
              .Append(_npc.Hp).Append(_npc.EffMaxHp).Append(_npc.EffStr).Append(_npc.EffDef).Append(_npc.EffInt)
              .Append(_npc.Stamina).Append(_npc.Spirit).Append(_npc.HealthScore).Append(_npc.HonorScore).Append(_npc.InfoScore);
            var bag = _npc.Bag;
            if (bag != null)
            {
                sb.Append(bag.EquippedWeaponId).Append(bag.EquippedArmorId).Append(bag.EquippedAccessoryId);
                foreach (var kv in bag.Items.OrderBy(kv => kv.Key))
                {
                    sb.Append(kv.Key).Append(':').Append(kv.Value).Append(',');
                }
            }

            string signature = sb.ToString();
            if (signature == _lastSignature)
            {
                return;
            }

            _lastSignature = signature;
            RefreshAll();
        }

        private void RefreshAll()
        {
            if (_textTitle != null)
            {
                _textTitle.text = string.IsNullOrEmpty(_npc.Name) ? "角色" : _npc.Name;
            }

            // ---- 属性页 ----
            if (_textFavor != null)
            {
                _textFavor.text = string.Format("{0} / 100   （{1:0.00}）", _npc.FavorToPlayer, _npc.FavorToPlayer / 100f);
            }

            if (_textInfo != null)
            {
                _textInfo.text =
                    $"姓名：{(_npc.Name ?? "（无名）")}\n" +
                    $"年龄：{_npc.Age} 岁\n" +
                    $"职业：{_npc.Profession}\n" +
                    $"性格：{_npc.Personality}\n" +
                    $"生命：{_npc.Hp} / {_npc.EffMaxHp}\n" +
                    $"力量：{_npc.EffStr}　防御：{_npc.EffDef}　智力：{_npc.EffInt}";
            }

            if (_textScores != null)
            {
                // CharacterScores.Social 需要 TownPopulation，本窗口不持有城镇引用，故不显示社交分。
                _textScores.text =
                    $"财富 {CharacterScores.Wealth(_npc)}　　战斗 {CharacterScores.Combat(_npc)}　　体力 {CharacterScores.Stamina(_npc)}\n" +
                    $"精神 {CharacterScores.Spirit(_npc)}　　荣誉 {CharacterScores.Honor(_npc)}　　信息 {CharacterScores.Info(_npc)}";
            }

            // ---- 背包页 ----
            var bagInv = _npc.Bag;
            if (_textBagGold != null)
            {
                _textBagGold.text = $"金币：{_npc.Gold}";
            }

            if (_textEquip != null)
            {
                _textEquip.text =
                    $"武器：{EquipName(bagInv != null ? bagInv.EquippedWeaponId : 0)}\n" +
                    $"防具：{EquipName(bagInv != null ? bagInv.EquippedArmorId : 0)}\n" +
                    $"饰品：{EquipName(bagInv != null ? bagInv.EquippedAccessoryId : 0)}";
            }

            var ids = bagInv != null
                ? bagInv.Items.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(id => id).ToList()
                : new List<int>();
            for (int i = 0; i < BagSlotCount; i++)
            {
                bool occupied = i < ids.Count;
                int itemId = occupied ? ids[i] : 0;
                var def = occupied ? ItemCatalog.Get(itemId) : null;

                if (_bagNames[i] != null)
                {
                    _bagNames[i].text = occupied && def != null ? def.Name : string.Empty;
                }

                if (_bagCounts[i] != null)
                {
                    _bagCounts[i].text = occupied ? bagInv.Items[itemId].ToString() : string.Empty;
                }

                if (_bagFrames[i] != null)
                {
                    _bagFrames[i].color = occupied ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                }

                if (_bagBgs[i] != null)
                {
                    _bagBgs[i].color = occupied ? new Color(0.97f, 0.95f, 0.88f) : new Color(0.97f, 0.95f, 0.88f, 0.5f);
                }
            }
        }

        private static string EquipName(int itemId)
        {
            if (itemId <= 0)
            {
                return "空";
            }

            var def = ItemCatalog.Get(itemId);
            return def != null ? def.Name : "空";
        }
    }
}
