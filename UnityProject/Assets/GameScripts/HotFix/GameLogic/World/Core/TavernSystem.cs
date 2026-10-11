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
        /// <summary>本营业日悬赏奖励（悬赏系统落地前恒为 0）。</summary>
        public int BountyReward;
        /// <summary>营业日成本（当日实际销售量 × 单份成本）。</summary>
        public int Cost;
        /// <summary>酒保工资。</summary>
        public int Wage;
        /// <summary>结算后余额。</summary>
        public int BalanceAfter;
        /// <summary>扣除后余额为负 → 游戏结束。</summary>
        public bool IsGameOver;
    }

    /// <summary>
    /// 经营核心（经营案四）：上架槽、采购份数（10:00 锁定）、酒保串行流水线、0:00 结算、失败判定。
    /// 客人进店与点单决策属角色行为系统，本类只提供下单 API（剩余份数为 0 的酒拒绝）。
    /// </summary>
    public sealed class TavernSystem
    {
        public const int BartenderWage = 20;
        public const int LockHour = 10;
        public const int ShelfSlotLimit = 4; // 上架槽上限（全局参数表缺失，占位待配置）
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
        private int _dayBounty;

        /// <summary>酒馆总流水（评分口径，结算系统细化）。</summary>
        public long TotalRevenue { get; private set; }

        public bool IsGameOver { get; private set; }

        public IReadOnlyList<int> Shelf => _shelf;

        /// <summary>当天份数是否已在采购时刻锁定。</summary>
        public bool IsLockedToday => _lockedToday;

        /// <summary>排队等待制作的订单数。</summary>
        public int PendingOrderCount => _queue.Count + (_making != null ? 1 : 0);

        /// <summary>酒保是否正在制作（酒保三态：待命/制作中/送单中的最小暴露）。</summary>
        public bool IsMaking => _making != null;

        public event Action<SettlementReport> Settled;
        public event Action GameOver;
        /// <summary>客人完成付费。参数：guest, wineId, price。</summary>
        public event Action<CharacterData, int, int> DrinkPaid;

        public TavernSystem(PlayerWallet wallet, BrewingSystem brewing)
        {
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _brewing = brewing ?? throw new ArgumentNullException(nameof(brewing));
        }

        /// <summary>设置上架酒（仅已解锁，受槽位上限约束）。</summary>
        public bool SetShelf(IReadOnlyList<int> wineIds)
        {
            if (wineIds == null || wineIds.Count > ShelfSlotLimit)
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
        /// 设定某上架酒当天准备份数（经营案：采购时刻 10:00 前可自由修改，锁定后不能再改）。
        /// 规划值跨天保留作为默认值（待确认口径）。
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
        /// 客人点单（玩家不能替客人点单；本 API 由角色行为系统调用）。
        /// 拒绝：未上架、售罄（剩余份数 0）、游戏已结束。
        /// </summary>
        public bool GuestOrder(CharacterData guest, int wineId)
        {
            if (IsGameOver || guest == null || !guest.IsAlive || !_shelf.Contains(wineId))
            {
                return false;
            }

            if (RemainingCountOf(wineId) <= 0)
            {
                return false; // 售罄：本营业日不再被点到。
            }

            _remaining[wineId] = RemainingCountOf(wineId) - 1;
            _queue.Enqueue(new DrinkOrder { Guest = guest, Wine = ItemCatalog.Get(wineId) });
            return true;
        }

        /// <summary>从仍有货的上架酒中随机点一杯（角色系统点单规则落地前的占位）。</summary>
        public int GuestOrderRandom(CharacterData guest, IRandomSource random)
        {
            var available = new List<int>();
            foreach (int id in _shelf)
            {
                if (RemainingCountOf(id) > 0)
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

        /// <summary>每游戏分钟推进酒保流水线：制作串行（同一时刻只做一单），送达后客人饮用 1 小时再付费。</summary>
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
                var order = _drinking[i];
                if (--order.RemainingMinutes <= 0)
                {
                    _drinking.RemoveAt(i);
                    // 送达饮用完毕付费：售价全额进入收入；销量计入付费发生的营业日。
                    _wallet.Earn(order.Wine.Price);
                    _dayIncome += order.Wine.Price;
                    TotalRevenue += order.Wine.Price;
                    _soldToday[order.Wine.Id] = (_soldToday.TryGetValue(order.Wine.Id, out int s) ? s : 0) + 1;
                    DrinkPaid?.Invoke(order.Guest, order.Wine.Id, order.Wine.Price);
                }
            }
        }

        /// <summary>采购时刻（10:00）锁定当天份数：规划值复制为可售份数；营业中不能追加。</summary>
        internal void OnHourChanged(int hour)
        {
            if (hour != LockHour || _lockedToday)
            {
                return;
            }

            _remaining.Clear();
            foreach (int wineId in _shelf)
            {
                _remaining[wineId] = PlannedCountOf(wineId);
            }

            _lockedToday = true;
        }

        /// <summary>
        /// 0:00 结算：扣除酒保工资与当日成本（实际销量 × 售价 × 40%，取整待确认）；
        /// 扣除后余额为负 → 游戏结束。份数周期与 10:00 锁定对齐，0:00 不清空剩余份数（待确认）。
        /// </summary>
        internal void OnDaySettled(int dayOverall)
        {
            if (IsGameOver)
            {
                return; // 游戏已结束，停止结算（结束后的结算口径待结算系统确认）。
            }

            int cost = 0;
            foreach (var pair in _soldToday)
            {
                cost += pair.Value * (ItemCatalog.Get(pair.Key).Price * 2 / 5);
            }

            int totalExpense = cost + BartenderWage;
            bool paid = _wallet.TrySpend(totalExpense);
            IsGameOver = !paid;

            var report = new SettlementReport
            {
                DayOverall = dayOverall,
                Income = _dayIncome,
                BountyReward = _dayBounty,
                Cost = cost,
                Wage = BartenderWage,
                BalanceAfter = _wallet.Currency,
                IsGameOver = IsGameOver,
            };

            _dayIncome = 0;
            _dayBounty = 0;
            _soldToday.Clear();
            _lockedToday = false;

            Settled?.Invoke(report);
            if (IsGameOver)
            {
                GameOver?.Invoke();
            }
        }
    }
}
