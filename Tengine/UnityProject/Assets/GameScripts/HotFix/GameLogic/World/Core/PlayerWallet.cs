using System;

namespace GameLogic.World
{
    /// <summary>
    /// 玩家货币（R5：整数、初始 100、不可透支）。
    /// </summary>
    public sealed class PlayerWallet
    {
        public const int InitialCurrency = 100;

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

        /// <summary>入账（酒水付费、悬赏奖励等）。</summary>
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
