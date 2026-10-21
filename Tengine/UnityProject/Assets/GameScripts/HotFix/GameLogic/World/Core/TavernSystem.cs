using System;
using System.Collections.Generic;

namespace GameLogic.World
{
    /// <summary>营业日结算报告（经营案：收入=酒水付费+悬赏奖励；支出=酒保工资+营业日成本）。</summary>
    public sealed class SettlementReport
    {
        public int DayOverall;
        /// <summary>本营业日酒水收入。</summary>
        public int Income;
        /// <summary>本营业日悬赏奖励（M5：<see cref="BountySystem.OnDaySettled"/> 当次 0:00 入账的赏金合计；未接悬赏时为 0）。</summary>
        public int BountyReward;

        /// <summary>
        /// 本营业日悬赏失败的发布费返还合计（M5 悬赏案 3.3-8：失败条目<b>全额</b>返还）。
        /// 与 <see cref="BountyReward"/> 一样在 0:00 先于营业成本 / 酒保工资入账，因此两者都参与当次「余额 &lt; 0 判失败」。
        /// </summary>
        public int BountyRefund;

        /// <summary>
        /// 本营业日悬赏失败的条目名（M5；同一 0:00 多条失败时合并为一条
        /// 「悬赏名 + 发布费已返还」弹窗文案，见 <see cref="BountySettleResult.BuildFailedSummary"/>）。
        /// </summary>
        public List<string> FailedBountyNames = new List<string>();
        /// <summary>营业日成本（当日售出杯数 × 单份成本；单份成本 = 售价 × 60% 取整）。</summary>
        public int Cost;
        /// <summary>酒保工资。</summary>
        public int Wage;
        /// <summary>结算后余额（经营失败清算后按 0 呈现）。</summary>
        public int BalanceAfter;
        /// <summary>本营业日售出总杯数（点单即计入）。</summary>
        public int SoldCups;
        /// <summary>本营业日未售出总杯数（Σ max(0, 当日份数上限 − 当日售出)）。</summary>
        public int UnsoldCups;
        /// <summary>扣除后余额为负 → 游戏结束。</summary>
        public bool IsGameOver;
    }

    /// <summary>
    /// 经营核心（经营案四）：上架槽、采购份数（当日首单锁定）、酒保串行流水线、0:00 结算、失败判定。
    /// 客人进店与点单决策属角色行为系统，本类只提供下单 API（剩余份数为 0 的酒拒绝）。
    /// </summary>
    public sealed class TavernSystem
    {
        public const int BartenderWage = 20;
        public const int ShelfSlotLimit = 6;  // 上架槽上限（经营案 10.13：4 → 6）
        /// <summary>开局默认上架的每酒份数（成本按售出杯数结算，备货多无资金风险；玩家可在酒单随时改）。</summary>
        public const int DefaultPlannedCountPerWine = 10;
        public const int DrinkMinutes = 60;  // 饮用固定 1 游戏小时

        private sealed class DrinkOrder
        {
            public CharacterData Guest;
            public ItemDef Wine;
            public int RemainingMinutes;
        }

        private readonly PlayerWallet _wallet;
        private readonly BrewingSystem _brewing;
        private readonly List<int> _shelf = new List<int>();
        private readonly Dictionary<int, int> _planned = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _remaining = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _soldToday = new Dictionary<int, int>();
        private readonly Queue<DrinkOrder> _queue = new Queue<DrinkOrder>();
        private readonly List<DrinkOrder> _drinking = new List<DrinkOrder>();
        private DrinkOrder _making;
        private bool _lockedToday;
        private int _dayIncome;

        // M5：悬赏接线（由组合根调 <see cref="AttachBounty"/> 注入；未注入时悬赏完全不参与结算，报告里的三个悬赏字段为 0 / 空）。
        private BountySystem _bounty;
        private Inventory _bountyWarehouse;
        private TownSafety _bountySafety;

        /// <summary>酒馆总流水（评分口径，结算系统细化）。</summary>
        public long TotalRevenue { get; private set; }

        public bool IsGameOver { get; private set; }

        public IReadOnlyList<int> Shelf => _shelf;

        /// <summary>当天份数是否已锁定（锁定点＝当日首次成功点单，经营案 10.13）。</summary>
        public bool IsLockedToday => _lockedToday;

        /// <summary>排队等待制作的订单数。</summary>
        public int PendingOrderCount => _queue.Count + (_making != null ? 1 : 0);

        /// <summary>酒保是否正在制作（酒保三态：待命/制作中/送单中的最小暴露）。</summary>
        public bool IsMaking => _making != null;

        public event Action<SettlementReport> Settled;
        public event Action GameOver;
        /// <summary>客人点单付费（点单即入账）。参数：guest, wineId, price。</summary>
        public event Action<CharacterData, int, int> DrinkPaid;

        public TavernSystem(PlayerWallet wallet, BrewingSystem brewing)
        {
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _brewing = brewing ?? throw new ArgumentNullException(nameof(brewing));
        }

        /// <summary>
        /// M5：注入悬赏系统与其结算所需的仓库 / 安全分（悬赏案 3.3 的入账口径）。
        ///
        /// 接线口径（master-plan M5「修改」：由 Tavern 内部调用 0:00 结算，顺序为
        /// <b>悬赏结算 → 营业成本 → 酒保工资 → 判 &lt; 0</b>）：
        /// - 选 <c>AttachBounty</c> 而非构造函数加参数，是因为悬赏依赖钱包与情报（<see cref="BountySystem.Attach"/>），
        ///   组合根里它必然晚于 <see cref="TavernSystem"/> 创建；后置注入对既有构造点零侵入；
        /// - 仓库 / 安全分一并注入：<see cref="BountySystem.OnDaySettled(PlayerWallet,Inventory,TownSafety)"/>
        ///   需要它们做「物资入仓 / 安定度变动」（安定度变动由悬赏侧内部直接调 <c>safety.Change</c>，本类不重复调）。
        /// </summary>
        public void AttachBounty(BountySystem bounty, Inventory warehouse, TownSafety safety)
        {
            _bounty = bounty;
            _bountyWarehouse = warehouse;
            _bountySafety = safety;
        }

        /// <summary>单份成本 = 售价 × 60%（整数除法，经营案 10.13）。</summary>
        private static int UnitCostOf(ItemDef wine)
        {
            return wine.Price * 60 / 100;
        }

        /// <summary>设置上架酒（仅已解锁，受槽位上限约束；当日份数已锁定后不能再改上架）。</summary>
        public bool SetShelf(IReadOnlyList<int> wineIds)
        {
            if (_lockedToday || wineIds == null || wineIds.Count > ShelfSlotLimit)
            {
                return false;
            }

            foreach (int id in wineIds)
            {
                if (!_brewing.IsUnlocked(id))
                {
                    return false;
                }
            }

            _shelf.Clear();
            _shelf.AddRange(wineIds);
            return true;
        }

        /// <summary>
        /// 设定某上架酒当天准备份数（经营案 10.13：当日首次成功点单前可自由修改，锁定后不能再改）。
        /// 规划值跨天保留（次日默认份数）；当日可售份数在锁定瞬间由规划值复制生成。
        /// </summary>
        public bool SetPlannedCount(int wineId, int count)
        {
            if (_lockedToday || count < 0 || !_shelf.Contains(wineId))
            {
                return false;
            }

            _planned[wineId] = count;
            return true;
        }

        public int PlannedCountOf(int wineId)
        {
            return _planned.TryGetValue(wineId, out int count) ? count : 0;
        }

        public int RemainingCountOf(int wineId)
        {
            return _remaining.TryGetValue(wineId, out int count) ? count : 0;
        }

        /// <summary>
        /// NPC 决策可见的可售份数：当日未锁定时按规划份数预估（首单即锁定的经营案口径下，
        /// 锁定前 RemainingCountOf 恒为 0，若只读它则「第一单」永远无法产生，形成点单死锁）；
        /// 锁定后按当日实际剩余份数。
        /// </summary>
        public int AvailableCountOf(int wineId)
        {
            return _lockedToday ? RemainingCountOf(wineId) : PlannedCountOf(wineId);
        }

        /// <summary>
        /// 客人点单（玩家不能替客人点单；本 API 由角色行为系统调用）。
        /// 拒绝：游戏已结束、客人无效、未上架、当日份数已点完。
        /// 副作用：当日首次点单即锁定份数（规划值 → 可售份数），并在点单瞬间完成入账。
        /// </summary>
        public bool GuestOrder(CharacterData guest, int wineId)
        {
            // M3：隐藏居民不参与行动，不能点单。
            if (IsGameOver || guest == null || !guest.IsAlive || guest.IsHidden || !_shelf.Contains(wineId))
            {
                return false;
            }

            // 当日首单即锁定：置锁瞬间把规划份数复制为可售份数（经营案 10.13，取消 10:00 定时锁定）。
            if (!_lockedToday)
            {
                LockDayStock();
            }

            if (RemainingCountOf(wineId) <= 0)
            {
                return false; // 售罄：本营业日不再被点到。
            }

            var order = new DrinkOrder { Guest = guest, Wine = ItemCatalog.Get(wineId) };
            _remaining[wineId] = RemainingCountOf(wineId) - 1;

            // 点单即入账（经营案 10.13）：售价全额即时进钱包并计入当日流水与当日销量，饮用完毕不再入账。
            _wallet.Earn(order.Wine.Price);
            _dayIncome += order.Wine.Price;
            TotalRevenue += order.Wine.Price;
            _soldToday[wineId] = (_soldToday.TryGetValue(wineId, out int sold) ? sold : 0) + 1;
            DrinkPaid?.Invoke(guest, wineId, order.Wine.Price);

            _queue.Enqueue(order);
            return true;
        }

        /// <summary>从仍有货的上架酒中随机点一杯（角色系统点单规则落地前的占位）。</summary>
        public int GuestOrderRandom(CharacterData guest, IRandomSource random)
        {
            var available = new List<int>();
            foreach (int id in _shelf)
            {
                // 用 AvailableCountOf（未锁定按规划份数预估）：锁定前 RemainingCountOf 恒为 0，
                // 只看它会让「第一单」永远无法产生。
                if (AvailableCountOf(id) > 0)
                {
                    available.Add(id);
                }
            }

            if (available.Count == 0)
            {
                return -1;
            }

            int wineId = available[random.Next(0, available.Count)];
            return GuestOrder(guest, wineId) ? wineId : -1;
        }

        /// <summary>每游戏分钟推进酒保流水线：制作串行（同一时刻只做一单），送达后客人饮用 1 小时（付费已在点单瞬间完成）。</summary>
        internal void TickMinute()
        {
            if (IsGameOver)
            {
                return;
            }

            if (_making == null && _queue.Count > 0)
            {
                _making = _queue.Dequeue();
                _making.RemainingMinutes = _making.Wine.MakeMinutes;
            }

            if (_making != null && --_making.RemainingMinutes <= 0)
            {
                _drinking.Add(new DrinkOrder { Guest = _making.Guest, Wine = _making.Wine, RemainingMinutes = DrinkMinutes });
                _making = null;
            }

            for (int i = _drinking.Count - 1; i >= 0; i--)
            {
                if (--_drinking[i].RemainingMinutes <= 0)
                {
                    // 饮用完毕只清订单：收入与销量已在点单瞬间入账（经营案 10.13）。
                    _drinking.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 逐时钩子（WorldSimulation 每跨整点回调）。经营案 10.13 起份数不再定时锁定，
        /// 锁定点改为当日首次成功点单（见 <see cref="GuestOrder"/>），故此处不再改动任何状态。
        /// </summary>
        internal void OnHourChanged(int hour)
        {
        }

        /// <summary>锁定当日份数：规划值复制为可售份数（仅当日首次成功点单时调用）。</summary>
        private void LockDayStock()
        {
            _remaining.Clear();
            foreach (int wineId in _shelf)
            {
                _remaining[wineId] = PlannedCountOf(wineId);
            }

            _lockedToday = true;
        }

        /// <summary>
        /// 0:00 结算：<b>顺序 = 悬赏结算 → 营业成本 → 酒保工资 → 判 &lt; 0</b>（master-plan M5「修改」）。
        /// 悬赏第一顺位（<see cref="BountySystem.OnDaySettled"/>，由 <see cref="AttachBounty"/> 注入）负责
        /// 赏金 <c>wallet.Earn</c>、物资入仓、安定度变动与失败发布费全额返还；
        /// 刚性支出走 <see cref="PlayerWallet.ForceSpend"/>（允许扣成负数），
        /// 当日成本 = 售出杯数 × 单份成本（单份成本 = 售价 × 60%，整数除法），另加酒保工资；
        /// 扣后余额 &lt; 0 → 游戏结束并把余额清算为 0（= 0 不结束）。
        /// 因悬赏在支出前入账，<b>赏金与返还都参与当次 &lt; 0 判定</b>（可救当次破产）。
        /// 结算末尾清空当日可售份数并解锁，规划份数跨天保留。
        /// </summary>
        internal void OnDaySettled(int dayOverall)
        {
            if (IsGameOver)
            {
                return; // 游戏已结束，停止结算（结束后的结算口径待结算系统确认）。
            }

            // M5 第一顺位：悬赏入账 / 物资 / 安定度 / 失败返还全部先于营业成本与工资完成。
            BountySettleResult bountyResult = _bounty != null ? _bounty.OnDaySettled(_wallet, _bountyWarehouse, _bountySafety) : null;

            int soldCups = 0;
            int cost = 0;
            foreach (var pair in _soldToday)
            {
                soldCups += pair.Value;
                cost += pair.Value * UnitCostOf(ItemCatalog.Get(pair.Key));
            }

            int unsoldCups = 0;
            foreach (int wineId in _shelf)
            {
                int cap = PlannedCountOf(wineId);
                int sold = _soldToday.TryGetValue(wineId, out int s) ? s : 0;
                unsoldCups += Math.Max(0, cap - sold);
            }

            int totalExpense = cost + BartenderWage;
            int balanceAfter = _wallet.ForceSpend(totalExpense);
            IsGameOver = balanceAfter < 0;
            if (IsGameOver)
            {
                _wallet.ClearToZero();
                balanceAfter = _wallet.Currency; // 报告口径：经营失败后余额按 0 呈现。
            }

            var report = new SettlementReport
            {
                DayOverall = dayOverall,
                Income = _dayIncome,
                BountyReward = bountyResult != null ? bountyResult.RewardGold : 0,
                BountyRefund = bountyResult != null ? bountyResult.RefundGold : 0,
                Cost = cost,
                Wage = BartenderWage,
                BalanceAfter = balanceAfter,
                SoldCups = soldCups,
                UnsoldCups = unsoldCups,
                IsGameOver = IsGameOver,
            };

            // 悬赏失败条目名：同一 0:00 多条失败合并进一条弹窗文案（M5 悬赏案 3.3-8）。
            if (bountyResult != null && bountyResult.FailedNames.Count > 0)
            {
                report.FailedBountyNames.AddRange(bountyResult.FailedNames);
            }

            _dayIncome = 0;
            _soldToday.Clear();
            _remaining.Clear();
            _lockedToday = false;

            Settled?.Invoke(report);
            if (IsGameOver)
            {
                GameOver?.Invoke();
            }
        }
    }
}
