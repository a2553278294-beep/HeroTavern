using System;
using System.Collections.Generic;

using ConfigBountyRow = GameConfig.ht.Bounty;

namespace GameLogic.World
{
    /// <summary>
    /// 悬赏状态（《英雄酒馆-悬赏系统需求案》3.1 状态规则）。
    ///
    /// 需求案状态机（<b>未点亮</b>的条目不在其中：初始可见=0 且未被其他系统点亮时不进列表，
    /// 本类以 <see cref="BountyEntry.Revealed"/> 表达「进不进列表」）：
    /// - <see cref="Available"/>    可发布：未占槽 + 已点亮 + 线索已持有；发布成功后进入 <see cref="InProgress"/>；
    /// - <see cref="IntelLack"/>     线索不足：未占槽 + 已点亮 + 线索未持有；获得线索后回到 <see cref="Available"/>；
    /// - <see cref="InProgress"/>    进行中：发布成功，占一个槽；目标达成进 <see cref="CompletedPending"/>，限时到 0 未达成判失败（当次 0:00 清槽）；
    /// - <see cref="CompletedPending"/> 已完成：目标达成但尚未 0:00 入账，仍占槽，辅文案「下一天入账」；本次 0:00 入账后清槽、编号回到列表。
    ///
    /// 「失败」不是状态：需求案 3.1/3.3 明确失败不停留，限时到 0 的那次 0:00 直接清槽 + 返还发布费。
    /// </summary>
    public enum BountyState
    {
        /// <summary>可发布（列表可点，发布成功后进 <see cref="InProgress"/>）。</summary>
        Available = 1,

        /// <summary>线索不足（列表可见但禁用，获得线索后回到 <see cref="Available"/>）。</summary>
        IntelLack = 2,

        /// <summary>进行中（占槽，不可撤下）。</summary>
        InProgress = 3,

        /// <summary>已完成待入账（仍占槽，下一次 0:00 入账后清槽）。</summary>
        CompletedPending = 4,
    }

    /// <summary>
    /// 发布拒绝原因（《悬赏系统需求案》3.2 发布规则）。
    /// 四项校验只提示<b>第一项</b>不满足的原因；任一项不满足都不扣发布费、不扣线索、槽仍为空。
    /// </summary>
    public enum PublishDeny
    {
        /// <summary>四项校验全部通过，可以发布。</summary>
        None = 0,

        /// <summary>该条现在不是「可发布」（未点亮 / 已进行中 / 已完成待入账 / 目标为空）。</summary>
        NotAvailable = 1,

        /// <summary>还没有指定空槽（或原指定的空槽已被占用，需重新指定）。</summary>
        NoSlotSelected = 2,

        /// <summary>槽位已全部占用（6 槽占满不能再发）。</summary>
        NoEmptySlot = 3,

        /// <summary>线索未持有（线索不堆叠，发布时扣一条即无）。</summary>
        IntelMissing = 4,

        /// <summary>金币余额不少于发布费这一项不满足。</summary>
        GoldInsufficient = 5,
    }

    /// <summary>
    /// 一条悬赏（纯逻辑值对象，无 UnityEngine 依赖；字段口径见 master-plan M5 与悬赏案 2.5 详情表）。
    ///
    /// 运行时字段（不属于配置、随发布/结算变化）：
    /// - <see cref="SlotIndex"/>：-1 = 未占槽；≥0 = 占用 <see cref="BountySystem.Slots"/> 中该下标（需求案 2.3：真正占槽的只有进行中和已完成）；
    /// - <see cref="RemainingDays"/>：还要经过几次 0:00 到期（发布时取 <see cref="TimeLimitDays"/>；不足一日仍显示「剩 1 天」）；
    /// - <see cref="KilledCount"/>：冒险侧按怪物名回写的已击杀数（只增不减到 <see cref="TargetCount"/>）；
    /// - <see cref="Revealed"/>：初始可见=1 或被其他系统点亮（怪物首次刷新 / 购买情报）；
    /// - <see cref="State"/>：状态缓存，权威值以 <see cref="BountySystem.QueryState"/> 为准（缓存只用于不接情报系统的展示层）。
    /// </summary>
    public sealed class BountyEntry
    {
        /// <summary>悬赏编号（配置主键，同一编号可在清槽后重复发布）。</summary>
        public int Id;

        /// <summary>悬赏名。</summary>
        public string Name;

        /// <summary>目标怪物名（配置「目标」列的怪物名部分；冒险侧按名字匹配，需求案 3.3-4）。</summary>
        public string TargetMonsterName;

        /// <summary>目标击杀数量（配置「目标」列的数量部分）。</summary>
        public int TargetCount;

        /// <summary>赏金（金币，完成后当次 0:00 入账，进结算分栏收入第一行）。</summary>
        public int Gold;

        /// <summary>发布费（金币，发布时刻从余额扣除，不进结算分栏；失败时全额返还）。</summary>
        public int Fee;

        /// <summary>物资物品 id（无物资为 0）。</summary>
        public int MatItemId;

        /// <summary>物资数量（无物资为 0）。</summary>
        public int MatCount;

        /// <summary>限时（游戏日；限时为 1 表示发布后再经过 1 次 0:00 到期）。</summary>
        public int TimeLimitDays;

        /// <summary>所需线索编号（<see cref="IntelSystem.IntelRecord.Id"/>；本期限时填非 0）。</summary>
        public int IntelId;

        /// <summary>完成时的安定度变动（可空/0/负；失败不变动）。</summary>
        public int SafeDelta;

        /// <summary>状态缓存（发布 / 结算时同步；判定以 <see cref="BountySystem.QueryState"/> 为准）。</summary>
        public BountyState State = BountyState.Available;

        /// <summary>占用槽位下标；-1 = 未占槽。</summary>
        public int SlotIndex = -1;

        /// <summary>剩余天数（还要经过几次 0:00 到期）。</summary>
        public int RemainingDays;

        /// <summary>已击杀数（回写完成用；只回写完成，不回写失败）。</summary>
        public int KilledCount;

        /// <summary>是否已点亮（unlock=1 初始可见，或被其他系统点亮）。未点亮不进列表。</summary>
        public bool Revealed;

        /// <summary>是否占用槽位。</summary>
        public bool Occupied
        {
            get { return SlotIndex >= 0; }
        }

        /// <summary>是否有物资（详情「物资」列，无则显示「无」）。</summary>
        public bool HasMat
        {
            get { return MatItemId > 0 && MatCount > 0; }
        }

        /// <summary>清槽复位：回到列表，等待重新收集线索 + 重新付一次发布费。</summary>
        public void ResetRunProgress()
        {
            SlotIndex = -1;
            RemainingDays = 0;
            KilledCount = 0;
        }

        public override string ToString()
        {
            return "#" + Id + " " + Name + " 目标:" + TargetMonsterName + "x" + TargetCount
                   + " 赏金:" + Gold + " 费:" + Fee + " 限时:" + TimeLimitDays + "天"
                   + " 状态:" + State + (SlotIndex >= 0 ? (" 槽:" + SlotIndex) : " 未占槽");
        }
    }

    /// <summary>
    /// 0:00 悬赏结算结果（《悬赏系统需求案》3.3；由 Tavern 结算第一顺位消费，见 master-plan M5「修改」）。
    /// 顺序口径（3.3-7）：悬赏赏金入账、物资入库、完成槽清空、失败槽清空、发布费返还，
    /// 都参与当次「余额 &lt; 0 判失败」的计算，允许救当次破产。
    /// </summary>
    public sealed class BountySettleResult
    {
        /// <summary>本次入账的悬赏赏金合计（进结算分栏收入第一行；物资不进金币合计）。</summary>
        public int RewardGold;

        /// <summary>本次返还的发布费合计（失败条目全额返还）。</summary>
        public int RefundGold;

        /// <summary>本次失败条目的悬赏名（多条同时失败时合并成一条弹窗文案）。</summary>
        public readonly List<string> FailedNames = new List<string>();

        /// <summary>本次安定度净变动（完成条目的 <see cref="BountyEntry.SafeDelta"/> 合计；失败不变动）。</summary>
        public int SafetyDelta;

        /// <summary>本次是否有悬赏失败（决定是否弹合并失败文案）。</summary>
        public bool HasFailed
        {
            get { return FailedNames.Count > 0; }
        }

        /// <summary>当次 0:00 由悬赏侧带来的金币净变化（赏金 + 返还）。</summary>
        public int TotalGold
        {
            get { return RewardGold + RefundGold; }
        }

        /// <summary>失败合并弹窗文案（需求案 3.3-8：含悬赏名和「发布费已返还」，同一次 0:00 多条合并为一条）。</summary>
        public string BuildFailedSummary()
        {
            if (FailedNames.Count == 0)
            {
                return string.Empty;
            }

            return string.Join("、", FailedNames.ToArray()) + "：悬赏失败，发布费已返还 " + RefundGold + " G。";
        }

        public override string ToString()
        {
            return "悬赏结算 赏金:" + RewardGold + " 返还:" + RefundGold + " 安定度:" + SafetyDelta
                   + (FailedNames.Count > 0 ? (" 失败:" + string.Join("、", FailedNames.ToArray())) : " 无失败");
        }
    }

    /// <summary>
    /// 悬赏系统（M5，纯逻辑，无 UnityEngine 依赖）。出处《英雄酒馆-悬赏系统需求案》全篇
    /// （2.3 槽态 / 3.1 状态机 / 3.2 发布 / 3.3 结算 / 5 配置表）。
    ///
    /// 口径速查：
    /// - 槽位固定 <see cref="DefaultSlotCount"/>=6（可由配置 <c>ht.TbBountyConst.SlotCount</c> 或 <see cref="Configure"/> 注入）；
    ///   「可发布 / 线索不足」<b>不占槽</b>，真正占槽的只有进行中和已完成；
    /// - 发布 = 四项校验（可发布 / 已指定空槽 / 线索已持有 / 金币不少于发布费）<b>同时</b>满足才扣费扣线索占槽，
    ///   只报第一项不满足的原因（<see cref="CheckPublish"/>）；
    /// - 冒险侧只回写完成（<see cref="ReportMonsterKilled"/>），不回写失败：没等到完成又过了限时就是失败；
    /// - 0:00（<see cref="OnDaySettled"/>）：已完成当次入账并清槽；未达成先减 1 天，减到 0 判失败（不入账、安定度不变、全额返还发布费、清槽）；
    ///   同一编号清槽后回到列表，重新收集线索 + 重新付一次发布费即可再次发布。
    ///
    /// 组合根接线（master-plan M5「修改」）：
    /// <list type="bullet">
    /// <item>构造后调一次 <see cref="LoadFromConfig"/>（配置未就绪则回退内置 2 条占位示例）；</item>
    /// <item>调 <see cref="Attach"/> 注入 <see cref="PlayerWallet"/> / <see cref="IntelSystem"/>（单参 <see cref="CheckPublish(BountyEntry)"/> 依赖它）；</item>
    /// <item><c>Monsters.MonsterKilled += m =&gt; Bounty.ReportMonsterKilled(m.Def.Name)</c>；</item>
    /// <item>0:00 在 Tavern 结算<b>之前</b>调 <see cref="OnDaySettled"/>，结果填 <c>SettlementReport.BountyReward / BountyRefund / FailedBountyNames</c>。</item>
    /// </list>
    /// </summary>
    public sealed class BountySystem
    {
        /// <summary>槽位数缺省值（悬赏案 5.2：本期填 6，最小 1）。</summary>
        public const int DefaultSlotCount = 6;

        /// <summary>目标 / 物资列的数量分隔记号（配置形如「怪物名*数量」「物品id*数量」）。</summary>
        private static readonly char[] CountSeparators = { '*', '\u00d7', 'x', 'X' };

        /// <summary>多项目标 / 多项物资的列表分隔记号（本期一项一条，解析时取第一项并告警）。</summary>
        private static readonly char[] ListSeparators = { '\u3001', ',', '\uff0c', ';', '\uff1b', '|', '/', '+' };

        private readonly List<BountyEntry> _entries = new List<BountyEntry>();
        private readonly List<BountyEntry> _slots = new List<BountyEntry>();
        private readonly List<string> _loadWarnings = new List<string>();

        private int _slotCount = DefaultSlotCount;
        private int _selectedEmptySlot = -1;
        private PlayerWallet _wallet;
        private IntelSystem _intel;

        /// <summary>配置表来源（模块层拿到 <c>GameConfig.Tables</c> 时可显式注入，优先于 <see cref="GameConfigCatalog"/> 单例）。</summary>
        private static GameConfig.Tables _tablesSource;

        public BountySystem()
            : this(DefaultSlotCount)
        {
        }

        public BountySystem(int slotCount)
        {
            Configure(slotCount);
        }

        // ------------------------------------------------------------------
        // 只读视图
        // ------------------------------------------------------------------

        /// <summary>槽位数（配置 <c>TbBountyConst.SlotCount</c> 或 <see cref="Configure"/> 注入，默认 <see cref="DefaultSlotCount"/>）。</summary>
        public int SlotCount
        {
            get { return _slotCount; }
        }

        /// <summary>全部悬赏（按配置表行序；含未点亮条目，UI 需用 <see cref="BountyEntry.Revealed"/> 过滤）。</summary>
        public IReadOnlyList<BountyEntry> Entries
        {
            get { return _entries; }
        }

        /// <summary>槽位数组，长度恒等于 <see cref="SlotCount"/>；空位为 null（需求案 2.3 的空槽卡片）。</summary>
        public IReadOnlyList<BountyEntry> Slots
        {
            get { return _slots; }
        }

        /// <summary>已指定的下一次发布位置；-1 = 未指定（同一时间只指定一个空槽）。</summary>
        public int SelectedEmptySlot
        {
            get { return _selectedEmptySlot; }
        }

        /// <summary>空槽数量。</summary>
        public int EmptySlotCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (_slots[i] == null)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>已占用槽数量（进行中 + 已完成待入账）。</summary>
        public int OccupiedSlotCount
        {
            get { return _slotCount - EmptySlotCount; }
        }

        /// <summary>槽位是否已满（占满后不能再发布，对应验收 5）。</summary>
        public bool AllSlotsOccupied
        {
            get { return EmptySlotCount == 0; }
        }

        /// <summary>加载 / 解析告警（占位回退、配置列解析失败等；供 HUD / 日志排查）。</summary>
        public IReadOnlyList<string> LoadWarnings
        {
            get { return _loadWarnings; }
        }

        // ------------------------------------------------------------------
        // 事件
        // ------------------------------------------------------------------

        /// <summary>发布成功（已扣发布费与线索、已占槽）。参数：刚发布的条目。</summary>
        public event Action<BountyEntry> Published;

        /// <summary>目标达成（进行中 → 已完成待入账，等下一次 0:00 入账）。参数：刚完成的条目。</summary>
        public event Action<BountyEntry> Completed;

        /// <summary>
        /// M13：本局累计达成的悬赏数（每次进入 <see cref="BountyState.CompletedPending"/> 记 1）。
        /// 用于结算评分的「累计悬赏完成数」展示；<c>BountySystem</c> 每局新建，故随新对局自然归零，无需手动重置。
        /// </summary>
        public int CompletedCount { get; private set; }

        /// <summary>限时到 0 未达成判失败（当次 0:00 清槽、发布费全额返还）。参数：失败条目、返还金额（= 该条发布费）。</summary>
        public event Action<BountyEntry, int> Failed;

        /// <summary>0:00 悬赏赏金入账合计（结算分栏收入第一行的一部分；仅在有入账时触发）。参数：当日悬赏赏金合计。</summary>
        public event Action<int> SettlementContribution;

        // ------------------------------------------------------------------
        // 接线
        // ------------------------------------------------------------------

        /// <summary>注入钱包与情报系统（单参 <see cref="CheckPublish(BountyEntry)"/> / <see cref="QueryState(BountyEntry)"/> 依赖它们）。</summary>
        public void Attach(PlayerWallet wallet, IntelSystem intel)
        {
            _wallet = wallet;
            _intel = intel;
        }

        /// <summary>只注入情报系统（<see cref="IntelSystem"/> 可在情报初始化后单独接线）。</summary>
        public void AttachIntel(IntelSystem intel)
        {
            _intel = intel;
        }

        /// <summary>
        /// 注入槽位数（悬赏案 5.2；合法值 ≥1，非法值忽略并保留当前槽数）。
        /// 缩容时超出新槽数的占用条目按「清槽」处理（回到列表，不入账、不返还），
        /// 原已指定的空槽若越界则清空指定。
        /// </summary>
        public void Configure(int slotCount)
        {
            if (slotCount <= 0)
            {
                return;
            }

            int oldCount = _slots.Count;
            if (slotCount != oldCount)
            {
                var kept = new BountyEntry[slotCount];
                int keep = oldCount < slotCount ? oldCount : slotCount;
                for (int i = 0; i < keep; i++)
                {
                    kept[i] = _slots[i];
                }

                _slots.Clear();
                for (int i = 0; i < slotCount; i++)
                {
                    _slots.Add(kept[i]);
                }

                for (int i = 0; i < _entries.Count; i++)
                {
                    var e = _entries[i];
                    if (e.SlotIndex >= slotCount)
                    {
                        ReleaseSlot(e);
                    }
                }

                _slotCount = slotCount;
            }

            if (_selectedEmptySlot >= _slotCount)
            {
                _selectedEmptySlot = -1;
            }
        }

        // ------------------------------------------------------------------
        // 查询
        // ------------------------------------------------------------------

        /// <summary>取槽位条目（越界或空位返回 null）。</summary>
        public BountyEntry GetSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count)
            {
                return null;
            }

            return _slots[slotIndex];
        }

        /// <summary>指定槽位是否为未张贴的空槽。</summary>
        public bool IsSlotEmpty(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < _slots.Count && _slots[slotIndex] == null;
        }

        /// <summary>第一个空槽下标；全满返回 -1。</summary>
        public int FirstEmptySlot()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i] == null)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>按编号取条目（找不到返回 null）。</summary>
        public BountyEntry FindById(int id)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Id == id)
                {
                    return _entries[i];
                }
            }

            return null;
        }

        /// <summary>点空槽：指定下一次发布的位置。同一时间只指定一个空槽，再点另一个空槽则改指定；点已占用槽无效（2.3-2）。</summary>
        public void SelectEmptySlot(int slotIndex)
        {
            if (!IsSlotEmpty(slotIndex))
            {
                // 越界 / 已占用：忽略（点已占用槽只看详情，不能覆盖）。
                return;
            }

            _selectedEmptySlot = slotIndex;
        }

        /// <summary>取消空槽指定（<see cref="SelectedEmptySlot"/> 回到 -1）。</summary>
        public void ClearSlotSelection()
        {
            _selectedEmptySlot = -1;
        }

        /// <summary>是否已点亮（列表是否展示该条；未点亮不进列表，需求案 3.1-2）。</summary>
        public bool IsRevealed(int id)
        {
            var e = FindById(id);
            return e != null && e.Revealed;
        }

        // ------------------------------------------------------------------
        // 状态判定（3.1）
        // ------------------------------------------------------------------

        /// <summary>按需求案 3.1 推导状态（用 <see cref="Attach"/> 注入的 <see cref="IntelSystem"/> 判线索是否持有）。</summary>
        public BountyState QueryState(BountyEntry e)
        {
            return QueryState(e, _intel);
        }

        /// <summary>
        /// 按需求案 3.1 推导状态：
        /// - 已占槽：进行中 / 已完成待入账（缓存与占槽矛盾时以「已占槽」为准）；
        /// - 未点亮：按「不可发布」处理（未点亮条目不进列表，UI 先用 <see cref="BountyEntry.Revealed"/> 过滤）；
        /// - 未占槽 + 已点亮 + 线索已持有 → <see cref="BountyState.Available"/>；无线索 → <see cref="BountyState.IntelLack"/>。
        /// 未接情报系统（<paramref name="intel"/> 为 null）时保守判 <see cref="BountyState.IntelLack"/>。
        /// </summary>
        public BountyState QueryState(BountyEntry e, IntelSystem intel)
        {
            if (e == null)
            {
                // null 没有状态可言，按「不可发布」保守返回；CheckPublish 对 null 另判 NotAvailable。
                return BountyState.IntelLack;
            }

            if (e.SlotIndex >= 0)
            {
                if (e.State != BountyState.InProgress && e.State != BountyState.CompletedPending)
                {
                    // 缓存陈旧（例如缩容/外部改动），以「已占槽」为准。
                    return BountyState.InProgress;
                }

                return e.State;
            }

            if (!e.Revealed)
            {
                return BountyState.IntelLack;
            }

            return intel != null && intel.PlayerHas(e.IntelId) ? BountyState.Available : BountyState.IntelLack;
        }

        /// <summary>刷新全部条目的状态缓存（不接情报系统的展示层用；权威判定始终是 <see cref="QueryState"/>）。</summary>
        public void RefreshStates(IntelSystem intel = null)
        {
            var source = intel ?? _intel;
            for (int i = 0; i < _entries.Count; i++)
            {
                _entries[i].State = QueryState(_entries[i], source);
            }
        }

        // ------------------------------------------------------------------
        // 发布（3.2）
        // ------------------------------------------------------------------

        /// <summary>四项校验（用 <see cref="Attach"/> 注入的钱包与情报系统）。</summary>
        public PublishDeny CheckPublish(BountyEntry e)
        {
            return CheckPublish(e, _wallet, _intel);
        }

        /// <summary>四项校验（显式钱包，情报系统用 <see cref="Attach"/> 注入的）。</summary>
        public PublishDeny CheckPublish(BountyEntry e, PlayerWallet wallet)
        {
            return CheckPublish(e, wallet, _intel);
        }

        /// <summary>
        /// 四项校验，<b>只报第一项</b>不满足的原因（需求案 3.2）：
        /// ① 该条是可发布悬赏 → <see cref="PublishDeny.NotAvailable"/>；② 已指定空槽 → <see cref="PublishDeny.NoSlotSelected"/>；
        /// ③ 还有空槽 → <see cref="PublishDeny.NoEmptySlot"/>；④ 线索已持有 → <see cref="PublishDeny.IntelMissing"/>；
        /// ⑤ 金币不少于发布费 → <see cref="PublishDeny.GoldInsufficient"/>。
        /// 未注入钱包时无法核实第 ⑤ 项，返回 <see cref="PublishDeny.GoldInsufficient"/>（保守，不允许发布）。
        /// </summary>
        public PublishDeny CheckPublish(BountyEntry e, PlayerWallet wallet, IntelSystem intel)
        {
            // ① 是一条「可发布」的悬赏
            if (e == null || !e.Revealed || e.SlotIndex >= 0 || string.IsNullOrEmpty(e.TargetMonsterName))
            {
                return PublishDeny.NotAvailable;
            }

            var state = QueryState(e, intel);
            if (state != BountyState.Available)
            {
                return state == BountyState.IntelLack ? PublishDeny.IntelMissing : PublishDeny.NotAvailable;
            }

            // ② 已经指定一个空槽
            if (!IsSlotEmpty(_selectedEmptySlot))
            {
                return PublishDeny.NoSlotSelected;
            }

            // ③ 还有空槽
            if (EmptySlotCount <= 0)
            {
                return PublishDeny.NoEmptySlot;
            }

            // ④ 线索已持有
            if (intel == null || !intel.PlayerHas(e.IntelId))
            {
                return PublishDeny.IntelMissing;
            }

            // ⑤ 金币余额不少于发布费
            if (wallet == null || !wallet.CanAfford(e.Fee))
            {
                return PublishDeny.GoldInsufficient;
            }

            return PublishDeny.None;
        }

        /// <summary>
        /// 发布（3.2-2）：四项都满足才扣发布费、扣线索、指定的空槽变为进行中。
        /// 任一项不满足：不扣发布费、不扣线索、槽仍为空（返回 false，调用方用 <see cref="CheckPublish"/> 取原因）。
        /// 发布费在发布时刻从余额扣除（不进 0:00 结算分栏），扣后余额参与当日 0:00 的经营失败判定。
        /// 限时按「还要经过几次 0:00」起算：<see cref="BountyEntry.RemainingDays"/> = <see cref="BountyEntry.TimeLimitDays"/>。
        /// </summary>
        public bool TryPublish(BountyEntry e, PlayerWallet wallet, IntelSystem intel)
        {
            if (CheckPublish(e, wallet, intel) != PublishDeny.None)
            {
                return false;
            }

            // 扣发布费（预付给接单 NPC 的赏金）
            if (!wallet.TrySpend(e.Fee))
            {
                return false;
            }

            // 扣线索（不堆叠，扣完即无）；极端情况下被抢走则把发布费回滚，保证「要么都扣要么都不扣」
            if (intel == null || !intel.TryConsumePlayerIntel(e.IntelId))
            {
                wallet.Earn(e.Fee);
                return false;
            }

            _wallet = wallet;
            _intel = intel;

            OccupySlot(e, _selectedEmptySlot);

            // 该槽已被占用，发布位置指定随之失效（UI 需重新指定下一个空槽）。
            _selectedEmptySlot = -1;

            Published?.Invoke(e);
            return true;
        }

        // ------------------------------------------------------------------
        // 冒险侧回写（3.3-4：只回写完成，不回写失败）
        // ------------------------------------------------------------------

        /// <summary>
        /// 按怪物名回写完成进度：<b>进行中</b>且目标怪物名匹配的条目 <see cref="BountyEntry.KilledCount"/>+1，
        /// 达到 <see cref="BountyEntry.TargetCount"/> → <see cref="BountyState.CompletedPending"/>（仍占槽，等下一次 0:00 入账）。
        /// 本方法<b>不</b>判失败：不回写失败、也不减剩余天数（那是 0:00 的事）。
        /// </summary>
        public void ReportMonsterKilled(string monsterName)
        {
            if (string.IsNullOrEmpty(monsterName))
            {
                return;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.SlotIndex < 0 || e.State != BountyState.InProgress)
                {
                    continue;
                }

                if (!string.Equals(e.TargetMonsterName, monsterName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (e.KilledCount < e.TargetCount)
                {
                    e.KilledCount++;
                }

                if (e.KilledCount >= e.TargetCount)
                {
                    e.State = BountyState.CompletedPending;
                    CompletedCount++; // M13：本局累计达成数（破产遮罩展示）。
                    Completed?.Invoke(e);
                }
            }
        }

        // ------------------------------------------------------------------
        // 0:00 结算（3.3）
        // ------------------------------------------------------------------

        /// <summary>
        /// 0:00 结算（需求案 3.3；必须在 Tavern 成本/工资之前调用，使赏金与返还参与当次失败判定）：
        /// 1. <b>已完成待入账</b>：当次入账——赏金 <c>wallet.Earn</c>、物资入库 <c>warehouse.Add</c>、安定度 <c>safety.Change</c>、清槽回列表；
        /// 2. <b>进行中未达成</b>：<see cref="BountyEntry.RemainingDays"/>-1；
        ///    减到 0 判失败——不入账、安定度不变、发布费<b>全额</b>返还 <c>wallet.Earn</c>、清槽、名字记进 <see cref="BountySettleResult.FailedNames"/> 供合并弹窗；
        ///    未减到 0 保持进行中（辅文案剩余天数减 1）。
        /// 同一编号清槽后回到列表，重新收集线索 + 重新付一次发布费即可再次发布。
        /// </summary>
        public BountySettleResult OnDaySettled(PlayerWallet wallet, Inventory warehouse, TownSafety safety)
        {
            var result = new BountySettleResult();

            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.SlotIndex < 0)
                {
                    // 未占槽：可发布 / 线索不足的条目不进结算。
                    continue;
                }

                if (e.State == BountyState.CompletedPending)
                {
                    // 目标已达成（无论发生在本次 0:00 前还是「减到 0 的那次 0:00」）→ 同一次入账，不重复发奖。
                    if (wallet != null && e.Gold > 0)
                    {
                        wallet.Earn(e.Gold);
                        result.RewardGold += e.Gold;
                    }

                    if (warehouse != null && e.HasMat)
                    {
                        warehouse.Add(e.MatItemId, e.MatCount);
                    }

                    if (safety != null && e.SafeDelta != 0)
                    {
                        safety.Change(e.SafeDelta);
                        result.SafetyDelta += e.SafeDelta;
                    }

                    ReleaseSlot(e);
                    continue;
                }

                if (e.State != BountyState.InProgress)
                {
                    continue;
                }

                // 未达成：剩余天数减 1
                e.RemainingDays = e.RemainingDays > 0 ? e.RemainingDays - 1 : 0;
                if (e.RemainingDays > 0)
                {
                    continue;
                }

                // 减到 0 且未达成 → 失败：不入账、安定度不变、发布费全额返还、清槽
                int refund = e.Fee;
                if (wallet != null && refund > 0)
                {
                    wallet.Earn(refund);
                    result.RefundGold += refund;
                }

                string name = e.Name;
                ReleaseSlot(e);
                result.FailedNames.Add(name);
                Failed?.Invoke(e, refund);
            }

            if (result.RewardGold > 0)
            {
                SettlementContribution?.Invoke(result.RewardGold);
            }

            return result;
        }

        // ------------------------------------------------------------------
        // 点亮（unlock=0 的条目由其他系统点亮：怪物首次刷新 / 购买情报）
        // ------------------------------------------------------------------

        /// <summary>按编号点亮（需求案 3.1-1「已被其他系统点亮」）；编号不存在或已点亮时静默返回。</summary>
        public void Reveal(int id)
        {
            var e = FindById(id);
            if (e == null || e.Revealed)
            {
                return;
            }

            e.Revealed = true;
            if (e.SlotIndex < 0)
            {
                e.State = _intel != null && _intel.PlayerHas(e.IntelId) ? BountyState.Available : BountyState.IntelLack;
            }
        }

        /// <summary>怪物首次刷新时点亮 target 匹配的条目；返回本次新点亮的条数。</summary>
        public int RevealByTarget(string monsterName)
        {
            if (string.IsNullOrEmpty(monsterName))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.Revealed || !string.Equals(e.TargetMonsterName, monsterName, StringComparison.Ordinal))
                {
                    continue;
                }

                Reveal(e.Id);
                count++;
            }

            return count;
        }

        // ------------------------------------------------------------------
        // 配置加载
        // ------------------------------------------------------------------

        /// <summary>注入配置表来源（模块层已持有 <c>GameConfig.Tables</c> 时可直接注入，优先于 <see cref="GameConfigCatalog"/> 单例）。</summary>
        public static void SetTablesSource(GameConfig.Tables tables)
        {
            _tablesSource = tables;
        }

        /// <summary>已注入的配置表来源（未注入为 null）。</summary>
        public static GameConfig.Tables TablesSource
        {
            get { return _tablesSource; }
        }

        /// <summary>
        /// 从配置加载悬赏与槽位数：表来源优先 <see cref="SetTablesSource"/> 注入值，其次 <see cref="GameConfigCatalog.Instance"/> 的
        /// <c>Tables</c>（读 <c>ht.TbBounty</c> / <c>ht.TbBountyConst</c>，懒加载表，读取异常被吞进 <see cref="LoadWarnings"/>）。
        /// 配置缺失（目录未 Init / 表读不到 / 无可用行）时回退<b>内置 2 条占位示例</b>，保证逻辑层始终可跑。
        /// </summary>
        public void LoadFromConfig()
        {
            LoadFromConfig(ResolveTables());
        }

        /// <summary>从指定配置表加载（显式传入，优先于 <see cref="LoadFromConfig()"/> 的自动解析）。</summary>
        public void LoadFromConfig(GameConfig.Tables tables)
        {
            _entries.Clear();
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i] = null;
            }

            _selectedEmptySlot = -1;
            _loadWarnings.Clear();

            if (tables != null)
            {
                LoadSlotCount(tables);
                LoadBountyRows(tables);
            }

            if (_entries.Count == 0)
            {
                _loadWarnings.Add("TbBounty 无可用行，回退内置占位示例 2 条（占位数据，非正式数值）。");
                BuildPlaceholderEntries();
            }
        }

        private static GameConfig.Tables ResolveTables()
        {
            if (_tablesSource != null)
            {
                return _tablesSource;
            }

            var catalog = GameConfigCatalog.Instance;
            return catalog != null ? catalog.Tables : null;
        }

        private void LoadSlotCount(GameConfig.Tables tables)
        {
            try
            {
                var constTable = tables.TbBountyConst;
                int slotCount = constTable != null ? constTable.SlotCount : 0;
                if (slotCount > 0)
                {
                    Configure(slotCount);
                }
                else if (slotCount < 0)
                {
                    _loadWarnings.Add("TbBountyConst.SlotCount 非法（" + slotCount + "），保留默认 " + _slotCount + "。");
                }
            }
            catch (Exception e)
            {
                _loadWarnings.Add("读取 TbBountyConst 失败，槽位数保留 " + _slotCount + "：" + e.Message);
            }
        }

        private void LoadBountyRows(GameConfig.Tables tables)
        {
            try
            {
                var rows = tables.TbBounty.DataList;
                for (int i = 0; i < rows.Count; i++)
                {
                    var entry = FromRow(rows[i]);
                    if (entry != null)
                    {
                        _entries.Add(entry);
                    }
                }
            }
            catch (Exception e)
            {
                _loadWarnings.Add("读取 TbBounty 失败：" + e.Message);
            }
        }

        private BountyEntry FromRow(ConfigBountyRow row)
        {
            if (row == null)
            {
                return null;
            }

            var entry = new BountyEntry
            {
                Id = row.Id,
                Name = string.IsNullOrEmpty(row.Name) ? ("悬赏" + row.Id) : row.Name,
                Gold = row.Gold < 0 ? 0 : row.Gold,
                Fee = row.Fee < 0 ? 0 : row.Fee,
                // 限时最小 1（悬赏案 5.1-1）
                TimeLimitDays = row.Time < 1 ? 1 : row.Time,
                IntelId = row.Intel,
                SafeDelta = row.Safe,
                SlotIndex = -1,
            };

            ParseTarget(row.Target, entry);
            ParseMat(row.Mat, entry);

            // 初始可见只取 0/1：1 开局出现在列表；0 被其他系统点亮后才出现
            entry.Revealed = row.Unlock == 1;
            entry.State = entry.Revealed ? BountyState.Available : BountyState.IntelLack;
            return entry;
        }

        /// <summary>解析「怪物名*数量」（本期一项一条，多项取第一项并告警）。</summary>
        private void ParseTarget(string target, BountyEntry entry)
        {
            string text = TakeFirstItem(target);
            if (text.Length == 0)
            {
                _loadWarnings.Add("悬赏 " + entry.Id + "《" + entry.Name + "》目标为空，该条无法按名回写完成。");
                entry.TargetMonsterName = string.Empty;
                entry.TargetCount = 0;
                return;
            }

            string name;
            int count;
            if (!TrySplitCount(text, out name, out count))
            {
                name = text;
                count = 1;
                _loadWarnings.Add("悬赏 " + entry.Id + "《" + entry.Name + "》目标「" + text + "」没写数量，按 1 只处理。");
            }

            entry.TargetMonsterName = name;
            entry.TargetCount = count < 1 ? 1 : count;
        }

        /// <summary>解析「物品id*数量」（可空；无物资 → MatItemId/MatCount = 0，详情显示「无」）。</summary>
        private void ParseMat(string mat, BountyEntry entry)
        {
            entry.MatItemId = 0;
            entry.MatCount = 0;

            string text = TakeFirstItem(mat);
            if (text.Length == 0)
            {
                return;
            }

            string idText;
            int count;
            if (!TrySplitCount(text, out idText, out count))
            {
                _loadWarnings.Add("悬赏 " + entry.Id + "《" + entry.Name + "》物资「" + text + "」没写数量，已忽略物资。");
                return;
            }

            int itemId;
            if (!int.TryParse(idText, out itemId) || itemId <= 0)
            {
                _loadWarnings.Add("悬赏 " + entry.Id + "《" + entry.Name + "》物资 id 非法（" + idText + "），已忽略物资。");
                return;
            }

            entry.MatItemId = itemId;
            entry.MatCount = count < 1 ? 1 : count;
        }

        private static string TakeFirstItem(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string[] parts = text.Split(ListSeparators);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length > 0)
                {
                    return p;
                }
            }

            return string.Empty;
        }

        /// <summary>按<b>最后一个</b>数量记号切分（怪物名本身可能含字母/记号），数量取右侧整数。</summary>
        private static bool TrySplitCount(string text, out string head, out int count)
        {
            head = string.Empty;
            count = 0;

            int idx = -1;
            for (int i = text.Length - 1; i >= 0; i--)
            {
                char c = text[i];
                for (int k = 0; k < CountSeparators.Length; k++)
                {
                    if (c == CountSeparators[k])
                    {
                        idx = i;
                        break;
                    }
                }

                if (idx >= 0)
                {
                    break;
                }
            }

            if (idx < 0)
            {
                return false;
            }

            head = text.Substring(0, idx).Trim();
            string tail = text.Substring(idx + 1).Trim();
            if (head.Length == 0 || tail.Length == 0)
            {
                return false;
            }

            return int.TryParse(tail, out count);
        }

        /// <summary>
        /// 内置占位示例（<b>占位数据，非正式数值</b>）：配置缺失时的兜底，保证逻辑层可跑、可验收。
        /// 占位口径：线索编号取 <see cref="IntelSystem.IntelItemBaseId"/> 段（9000 起，与 <see cref="IntelSystem"/> 占位编号段一致）。
        /// </summary>
        private void BuildPlaceholderEntries()
        {
            _entries.Add(new BountyEntry
            {
                Id = 9001,
                Name = "清剿野狼（占位）",
                TargetMonsterName = "野狼",
                TargetCount = 3,
                Gold = 120,
                Fee = 40,
                MatItemId = 0,
                MatCount = 0,
                TimeLimitDays = 2,
                IntelId = IntelSystem.IntelItemBaseId,
                SafeDelta = 2,
                Revealed = true,
                State = BountyState.Available,
                SlotIndex = -1,
            });

            _entries.Add(new BountyEntry
            {
                Id = 9002,
                Name = "巨魔讨伐（占位）",
                TargetMonsterName = "巨魔",
                TargetCount = 1,
                Gold = 260,
                Fee = 90,
                // 占位物资：物品 id 40 = 伏特加（见 GameConfigCatalog 类注释冲突 3），待策划补录正式物资 id
                MatItemId = 40,
                MatCount = 2,
                TimeLimitDays = 3,
                IntelId = IntelSystem.IntelItemBaseId + 1,
                SafeDelta = -1,
                Revealed = true,
                State = BountyState.Available,
                SlotIndex = -1,
            });
        }

        // ------------------------------------------------------------------
        // 内部
        // ------------------------------------------------------------------

        /// <summary>占槽：槽位数组写入条目，<see cref="BountyEntry.RemainingDays"/> 按限时起算并进入进行中。</summary>
        private void OccupySlot(BountyEntry e, int slotIndex)
        {
            _slots[slotIndex] = e;
            e.SlotIndex = slotIndex;
            e.State = BountyState.InProgress;
            e.RemainingDays = e.TimeLimitDays < 1 ? 1 : e.TimeLimitDays;
            e.KilledCount = 0;
        }

        /// <summary>清槽：同一编号回到列表，等待重新收集线索 + 重新付一次发布费。</summary>
        private void ReleaseSlot(BountyEntry e)
        {
            int index = e.SlotIndex;
            if (index >= 0 && index < _slots.Count && ReferenceEquals(_slots[index], e))
            {
                _slots[index] = null;
            }

            if (index == _selectedEmptySlot)
            {
                _selectedEmptySlot = -1;
            }

            e.ResetRunProgress();

            // 清槽后线索已在发布时扣掉：默认按「线索不足」缓存，权威值仍以 QueryState 为准
            // （若玩家在别处重新拿到同一编号线索，QueryState 会自动回到 Available）。
            e.State = _intel != null && _intel.PlayerHas(e.IntelId) ? BountyState.Available : BountyState.IntelLack;
        }
    }
}
