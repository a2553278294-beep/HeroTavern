using System;
using System.Collections.Generic;

namespace GameLogic.World
{
    /// <summary>调酒合成结果三态（调酒案二）。</summary>
    public enum BrewOutcome
    {
        /// <summary>首次命中新酒：扣材料、unlock 置 1、成品酒 ×1 入仓。</summary>
        NewDiscovery,
        /// <summary>命中已研发：材料全额返还，不产出。</summary>
        AlreadyKnown,
        /// <summary>未命中（或命中新酒但材料不足）：不扣任何材料，生成神秘鸡尾酒 ×1。</summary>
        Failed,
    }

    public readonly struct BrewResult
    {
        public readonly BrewOutcome Outcome;
        /// <summary>命中时为配方；未命中为 null。</summary>
        public readonly RecipeDef Recipe;
        /// <summary>神秘鸡尾酒标签（未命中时）。</summary>
        public readonly string MysteryTags;

        public BrewResult(BrewOutcome outcome, RecipeDef recipe, string mysteryTags)
        {
            Outcome = outcome;
            Recipe = recipe;
            MysteryTags = mysteryTags;
        }
    }

    /// <summary>
    /// 调酒系统（调酒案三）：1 基酒 + 2 配料无序比对 + 加冰校验；一次只判一条配方，命中即止。
    /// 配方本身不消耗，判定永远只读配方表。
    /// </summary>
    public sealed class BrewingSystem
    {
        /// <summary>开局已解锁的可售酒（酒水产品表 unlock 预填的占位口径）。</summary>
        private static readonly int[] InitiallyUnlocked = { ItemCatalog.WineWhisky, ItemCatalog.WineAle };

        private readonly HashSet<int> _unlockedWines = new HashSet<int>();

        public BrewingSystem()
        {
            foreach (int id in InitiallyUnlocked)
            {
                _unlockedWines.Add(id);
            }
        }

        /// <summary>是否已解锁（图鉴）。</summary>
        public bool IsUnlocked(int wineId)
        {
            return _unlockedWines.Contains(wineId);
        }

        public IReadOnlyCollection<int> UnlockedWines => _unlockedWines;

        /// <summary>
        /// 合成判定。槽位内容：1 基酒 + 2 配料（允许同款两份，不区分槽位顺序）+ 加冰开关。
        /// 调用前需保证仓库中材料充足（界面层按钮置灰）；不足时按未命中处理并返回 Failed（不扣料）。
        /// 扣料为原子操作：先按基酒 ×1 / 配料 ×2（同款合并计数）/ 冰块 ×1 整体校验，任一项不足则一份都不扣。
        /// </summary>
        public BrewResult Brew(Inventory inventory, int baseWineId, int ingredientA, int ingredientB, bool useIce)
        {
            var baseDef = ItemCatalog.Get(baseWineId);
            var defA = ItemCatalog.Get(ingredientA);
            var defB = ItemCatalog.Get(ingredientB);
            if (baseDef == null || defA == null || defB == null
                || baseDef.Kind != ItemKind.BaseWine
                || defA.Kind != ItemKind.Ingredient || defB.Kind != ItemKind.Ingredient)
            {
                throw new ArgumentException("槽位内容不合法：需要 1 基酒 + 2 配料。");
            }

            var recipe = FindRecipe(baseWineId, ingredientA, ingredientB, useIce);
            if (recipe == null)
            {
                // 未命中：不扣任何材料，神秘鸡尾酒 ×1（标签 = 所放素材与基酒的所有标签）。
                string tags = MergeTags(baseDef, defA, defB);
                inventory.AddMysteryCocktail(tags, 1);
                return new BrewResult(BrewOutcome.Failed, null, tags);
            }

            if (IsUnlocked(recipe.ProductWineId))
            {
                // 已研发：材料全额返还（即不扣），不产出。
                return new BrewResult(BrewOutcome.AlreadyKnown, recipe, null);
            }

            // 首次命中：扣基酒 1 + 配料 2（加冰另扣冰块 1），unlock 置 1，成品酒 ×1 入仓。
            // FindRecipe 已保证 recipe.NeedIce == useIce，这里取二者与运算作为最终是否扣冰的口径。
            bool needIce = recipe.NeedIce && useIce;

            // 扣料前先用 Inventory.Count 整体校验（配料同款需 2 份，按 id 合并计数）：
            // 任一项不足则不执行任何 TryRemove，直接按 Failed（未命中）语义返回并给神秘鸡尾酒，
            // 避免逐个 TryRemove 扣到一半失败留下半扣库存。
            if (!HasAllMaterials(inventory, baseWineId, ingredientA, ingredientB, needIce))
            {
                string tags = MergeTags(baseDef, defA, defB);
                inventory.AddMysteryCocktail(tags, 1);
                return new BrewResult(BrewOutcome.Failed, null, tags);
            }

            // 校验已通过，此处连续扣料必定成功。
            inventory.TryRemove(baseWineId, 1);
            inventory.TryRemove(ingredientA, 1);
            inventory.TryRemove(ingredientB, 1);
            if (needIce)
            {
                inventory.TryRemove(ItemCatalog.IceCube, 1);
            }

            _unlockedWines.Add(recipe.ProductWineId);
            inventory.Add(recipe.ProductWineId, 1);
            return new BrewResult(BrewOutcome.NewDiscovery, recipe, null);
        }

        /// <summary>
        /// 扣料前置校验：基酒 ×1、配料 ×2（配料 A 与 B 同款时需同一种库存 ≥2，按 id 合并计数）、
        /// needIce 时冰块 ×1。全部充足返回 true；任一不足返回 false，调用方不得执行任何扣料。
        /// </summary>
        private static bool HasAllMaterials(Inventory inventory, int baseWineId, int ingredientA, int ingredientB, bool needIce)
        {
            if (inventory.Count(baseWineId) < 1)
            {
                return false;
            }

            if (ingredientA == ingredientB)
            {
                if (inventory.Count(ingredientA) < 2)
                {
                    return false;
                }
            }
            else if (inventory.Count(ingredientA) < 1 || inventory.Count(ingredientB) < 1)
            {
                return false;
            }

            return !needIce || inventory.Count(ItemCatalog.IceCube) >= 1;
        }

        private static RecipeDef FindRecipe(int baseWineId, int ingredientA, int ingredientB, bool useIce)
        {
            foreach (var recipe in ItemCatalog.Recipes)
            {
                if (recipe.BaseWineId != baseWineId || recipe.NeedIce != useIce)
                {
                    continue;
                }

                // 两份配料无序比对。
                bool match = (recipe.IngredientIdA == ingredientA && recipe.IngredientIdB == ingredientB)
                             || (recipe.IngredientIdA == ingredientB && recipe.IngredientIdB == ingredientA);
                if (match)
                {
                    return recipe; // 一次只判一条，命中即止。
                }
            }

            return null;
        }

        private static string MergeTags(ItemDef baseDef, ItemDef defA, ItemDef defB)
        {
            var tags = new List<string>();
            tags.AddRange(baseDef.Tags);
            tags.AddRange(defA.Tags);
            tags.AddRange(defB.Tags);
            tags.Sort(StringComparer.Ordinal);
            return string.Join(",", tags);
        }
    }
}
