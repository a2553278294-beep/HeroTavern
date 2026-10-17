using System;

namespace GameLogic.World
{
    /// <summary>
    /// 玩家货币（R5：整数、初始 500）。
    /// 日常消费走 <see cref="TrySpend"/>（余额不足即失败，不透支）；
    /// 0:00 结算的「营业成本 + 酒保工资」属刚性支出，走 <see cref="ForceSpend"/>（允许扣成负数），
    /// 扣后余额 &lt; 0 判经营失败并由 <see cref="ClearToZero"/> 归零（master-plan §2 #16 / M1 结算口径）。
    /// </summary>
    public sealed class PlayerWallet
    {
        /// <summary>开局金币（经营案 10.13：100 → 500；master-plan §2 #3 取经营案最新修订）。</summary>
        public const int InitialCurrency = 500;

        /// <summary>当前货币。变动时触发 <see cref="Changed"/>（UI 数字滚动由表现层负责）。</summary>
        public int Currency { get; private set; }

        public event Action<int> Changed;

        public PlayerWallet(int initialCurrency = InitialCurrency)
        {
            if (initialCurrency < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCurrency));
            }

            Currency = initialCurrency;
        }

        /// <summary>余额是否足够（R5：不足时按钮置灰，由 UI 层读取）。</summary>
        public bool CanAfford(int amount)
        {
            return amount >= 0 && Currency >= amount;
        }

        /// <summary>尝试消费；余额不足返回 false 且不变动。</summary>
        public bool TrySpend(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            if (Currency < amount)
            {
                return false;
            }

            Currency -= amount;
            Changed?.Invoke(Currency);
            return true;
        }

        /// <summary>
        /// 强制扣减：不做余额校验，允许扣成负数（0:00 结算的成本与工资）。
        /// 返回扣减后的余额，由调用方按 &lt; 0 判破产。
        /// </summary>
        public int ForceSpend(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            Currency -= amount;
            Changed?.Invoke(Currency);
            return Currency;
        }

        /// <summary>破产清算：余额直接记 0（结算扣成负数时调用，报告口径以 0 呈现）。</summary>
        public void ClearToZero()
        {
            if (Currency == 0)
            {
                return;
            }

            Currency = 0;
            Changed?.Invoke(Currency);
        }

        /// <summary>入账（酒水付费、悬赏奖励等）。付费时点＝点单瞬间（master-plan §2 #16）。</summary>
        public void Earn(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            Currency += amount;
            Changed?.Invoke(Currency);
        }
    }
}
