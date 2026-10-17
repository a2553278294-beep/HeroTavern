using System.Collections.Generic;

namespace GameLogic.World
{
    /// <summary>
    /// 仓库（经营案口径：只存放三类内容——售出用的酒、调酒材料、装备）。
    /// 神秘鸡尾酒不入表，按“标签完全相同”堆叠（调酒案）。
    /// </summary>
    public sealed class Inventory
    {
        private readonly Dictionary<int, int> _items = new Dictionary<int, int>();
        private readonly Dictionary<string, int> _mysteryCocktails = new Dictionary<string, int>();

        public int Count(int itemId)
        {
            return _items.TryGetValue(itemId, out int count) ? count : 0;
        }

        public void Add(int itemId, int count)
        {
            if (count <= 0)
            {
                return;
            }

            _items[itemId] = Count(itemId) + count;
        }

        public bool TryRemove(int itemId, int count)
        {
            if (count <= 0)
            {
                return true;
            }

            if (Count(itemId) < count)
            {
                return false;
            }

            _items[itemId] -= count;
            return true;
        }

        /// <summary>按标签堆叠的神秘鸡尾酒。</summary>
        public void AddMysteryCocktail(string sortedTags, int count)
        {
            _mysteryCocktails.TryGetValue(sortedTags, out int existing);
            _mysteryCocktails[sortedTags] = existing + count;
        }

        public int MysteryCocktailCount(string sortedTags)
        {
            return _mysteryCocktails.TryGetValue(sortedTags, out int count) ? count : 0;
        }

        public IReadOnlyDictionary<int, int> Items => _items;
        public IReadOnlyDictionary<string, int> MysteryCocktails => _mysteryCocktails;

        /// <summary>
        /// M8：拆分（plan M8 验收——「拆分规则只在 UI 层 M12 做，逻辑层由字典语义天然支持」）。
        /// 从本仓库取出 <paramref name="count"/> 件 <paramref name="itemId"/> 并放进<b>新仓库</b>返回；
        /// 本仓库数量不足时返回 null 且<b>不改动</b>本仓库。
        /// 数量 &lt;= 0 视为「不拆」，返回一个空仓库（等价于取消该条目）。
        /// </summary>
        public Inventory Split(int itemId, int count)
        {
            var slice = new Inventory();
            if (itemId <= 0 || count <= 0)
            {
                return slice;
            }

            if (Count(itemId) < count)
            {
                return null;
            }

            TryRemove(itemId, count);
            slice.Add(itemId, count);
            return slice;
        }

        /// <summary>
        /// M8：合并。把 <paramref name="other"/> 的物品<b>并入本仓库</b>（同名数量累加），
        /// 神秘鸡尾酒按「标签完全相同」堆叠；随后清空 <paramref name="other"/>（一次性搬空语义）。
        /// <paramref name="other"/> 为 null 时什么都不做。
        /// 交易成交时的物品转移走 <c>Inventory.TryRemove</c> / <c>Add</c>，本方法供 UI 批量操作使用。
        /// </summary>
        public void MergeFrom(Inventory other)
        {
            if (other == null)
            {
                return;
            }

            foreach (System.Collections.Generic.KeyValuePair<int, int> pair in other._items)
            {
                Add(pair.Key, pair.Value);
            }

            foreach (System.Collections.Generic.KeyValuePair<string, int> pair in other._mysteryCocktails)
            {
                AddMysteryCocktail(pair.Key, pair.Value);
            }

            other.Clear();
        }

        /// <summary>清空全部物品与神秘鸡尾酒（合并 / 重置用）。</summary>
        public void Clear()
        {
            _items.Clear();
            _mysteryCocktails.Clear();
        }
    }
}
