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
    }
}
