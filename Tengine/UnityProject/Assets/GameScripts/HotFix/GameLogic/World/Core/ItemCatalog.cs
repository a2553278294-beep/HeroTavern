namespace GameLogic.World
{
    /// <summary>物品类别（仓库三类口径：售出用的酒、调酒材料、装备；调酒材料再分基酒/配料/冰块）。</summary>
    public enum ItemKind
    {
        BaseWine = 1,
        Ingredient = 2,
        Ice = 3,
        Wine = 4,
        Equipment = 5,
    }

    /// <summary>
    /// 物品定义（在线 Item 表本地缺失的代码占位；标签词表以 Item 表标签对照页为准，待配置表落地）。
    /// </summary>
    public sealed class ItemDef
    {
        public readonly int Id;
        public readonly string Name;
        public readonly ItemKind Kind;
        /// <summary>售价（酒水产品表 PRICE）。</summary>
        public readonly int Price;
        /// <summary>制作时长（游戏分钟；酒水产品表 TIME，文档以游戏小时计，此处已换算）。</summary>
        public readonly int MakeMinutes;
        /// <summary>标签（基酒类型/材料类型）。</summary>
        public readonly string[] Tags;

        public ItemDef(int id, string name, ItemKind kind, int price, int makeMinutes, params string[] tags)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Price = price;
            MakeMinutes = makeMinutes;
            Tags = tags;
        }
    }

    /// <summary>调酒配方（配方表占位；配料两份无序，use1=加冰要求）。</summary>
    public sealed class RecipeDef
    {
        public readonly int Id;
        public readonly string Name;
        public readonly int BaseWineId;
        public readonly int IngredientIdA;
        public readonly int IngredientIdB;
        public readonly bool NeedIce;
        public readonly int ProductWineId;

        public RecipeDef(int id, string name, int baseWineId, int ingredientIdA, int ingredientIdB, bool needIce, int productWineId)
        {
            Id = id;
            Name = name;
            BaseWineId = baseWineId;
            IngredientIdA = ingredientIdA;
            IngredientIdB = ingredientIdB;
            NeedIce = needIce;
            ProductWineId = productWineId;
        }
    }

    /// <summary>物品与配方目录（占位条目，Item/配方配置表落地后替换）。</summary>
    public static class ItemCatalog
    {
        // 基酒
        public const int BaseVodka = 1;
        public const int BaseRum = 2;
        public const int BaseTequila = 3;
        // 配料
        public const int IngLemon = 11;
        public const int IngMint = 12;
        public const int IngHoney = 13;
        // 冰块
        public const int IceCube = 20;
        // 成品酒（酒水产品表占位）
        public const int WineWhisky = 101;
        public const int WineAle = 102;
        public const int WineTequilaSunrise = 103;
        public const int WineHoneyVodka = 104;

        public static readonly ItemDef[] Items =
        {
            new ItemDef(BaseVodka, "伏特加", ItemKind.BaseWine, 10, 0, "伏特加"),
            new ItemDef(BaseRum, "朗姆酒", ItemKind.BaseWine, 10, 0, "朗姆"),
            new ItemDef(BaseTequila, "龙舌兰", ItemKind.BaseWine, 12, 0, "龙舌兰"),
            new ItemDef(IngLemon, "柠檬", ItemKind.Ingredient, 2, 0, "柠檬"),
            new ItemDef(IngMint, "薄荷", ItemKind.Ingredient, 2, 0, "薄荷"),
            new ItemDef(IngHoney, "蜂蜜", ItemKind.Ingredient, 3, 0, "蜂蜜"),
            new ItemDef(IceCube, "冰块", ItemKind.Ice, 1, 0),
            new ItemDef(WineWhisky, "威士忌", ItemKind.Wine, 20, 60, "威士忌"),
            new ItemDef(WineAle, "麦酒", ItemKind.Wine, 8, 60, "麦酒"),
            new ItemDef(WineTequilaSunrise, "龙舌兰日出", ItemKind.Wine, 30, 120, "龙舌兰", "柠檬", "薄荷"),
            new ItemDef(WineHoneyVodka, "蜜糖伏特加", ItemKind.Wine, 25, 120, "伏特加", "蜂蜜"),
        };

        public static readonly RecipeDef[] Recipes =
        {
            new RecipeDef(1, "龙舌兰日出配方", BaseTequila, IngLemon, IngMint, false, WineTequilaSunrise),
            new RecipeDef(2, "蜜糖伏特加配方", BaseVodka, IngHoney, IngHoney, true, WineHoneyVodka),
        };

        public static ItemDef Get(int id)
        {
            for (int i = 0; i < Items.Length; i++)
            {
                if (Items[i].Id == id)
                {
                    return Items[i];
                }
            }

            return null;
        }
    }
}
