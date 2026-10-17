using System;
using System.Collections.Generic;

namespace GameLogic.World
{
    /// <summary>
    /// 角色随机生成（R3）。姓名优先取 GameConfigCatalog 的 NAME 配置词表，
    /// 配置缺失时回退代码占位词表；职业权重为代码占位（任务文档决策 4）。
    /// </summary>
    public sealed class CharacterFactory
    {
        public const int MinInitialAge = 18;
        public const int MaxInitialAge = 40;
        public const int AttributeMin = 0;
        public const int AttributeMax = 100;
        public const int BaseAttributeWeight = 1000;
        public const int MinAttributeWeight = 1;
        public const int DefaultProfessionWeight = 1000;

        /// <summary>初始金币下界（含）。<b>占位</b>：文档未定（矛盾记录 14），待策划定档。</summary>
        public const int MinInitialGold = 20;

        /// <summary>初始金币上界（不含）。实际取值区间 [<see cref="MinInitialGold"/>, 80]。<b>占位</b>，待策划定档。</summary>
        public const int MaxInitialGoldExclusive = 81;

        /// <summary>商人 / 农民初始材料件数下界（含），type=4（<b>占位规则，待配置表定档</b>）。</summary>
        public const int TraderMaterialMinItems = 2;

        /// <summary>商人 / 农民初始材料件数上界（含），type=4（<b>占位规则，待配置表定档</b>）。</summary>
        public const int TraderMaterialMaxItems = 4;

        /// <summary>铁匠初始武器件数（type=1，<b>占位规则，待配置表定档</b>）。</summary>
        public const int BlacksmithWeaponItems = 1;

        /// <summary>其他职业初始物品件数上界（含），<b>占位规则，待配置表定档</b>。</summary>
        public const int OtherStarterMaxItems = 1;

        /// <summary>姓名占位词表（GameConfigCatalog 未提供 NAME 词表时回退使用）。</summary>
        private static readonly string[] Surnames =
        {
            "李", "王", "张", "刘", "陈", "杨", "赵", "黄", "周", "吴", "徐", "孙",
        };

        private static readonly string[] GivenNames =
        {
            "明", "华", "强", "芳", "娜", "敏", "静", "丽", "军", "磊",
            "勇", "艳", "杰", "娟", "涛", "超", "秀", "兰", "霞", "平",
        };

        private readonly IRandomSource _random;
        private readonly HashSet<string> _usedNames = new HashSet<string>();
        private int _nextId = 1;

        public CharacterFactory(IRandomSource random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>
        /// 属性抽取权重（R3：属性权重 = 1000 −（属性值 − 1）×（10 − 游戏天数 ÷ 10），最低 1）。
        /// “游戏天数”采用总天数口径（GameTime.DayOverall）。
        /// </summary>
        public static int AttributeWeight(int attributeValue, int dayOverall)
        {
            int factor = 10 - dayOverall / 10;
            long weight = BaseAttributeWeight - (long)(attributeValue - 1) * factor;
            return (int)Math.Max(MinAttributeWeight, weight);
        }

        /// <summary>
        /// 生成一名角色。固定职业优先（R4 开局指定职业），否则按权重抽取。
        /// </summary>
        public CharacterData CreateCharacter(int dayOverall, GridPos spawnPos, ProfessionType? fixedProfession = null, bool isResident = true)
        {
            var data = new CharacterData
            {
                Id = _nextId++,
                Name = RollUniqueName(),
                Age = _random.Next(MinInitialAge, MaxInitialAge + 1),
                Personality = RollPersonality(),
                Profession = fixedProfession ?? RollProfession(),
                MaxHp = RollAttribute(dayOverall),
                Str = RollAttribute(dayOverall),
                Int = RollAttribute(dayOverall),
                Def = 0,
                // M7：初始金币占位 20-80（矛盾记录 14：文档未定，待策划定档）。
                Gold = _random.Next(MinInitialGold, MaxInitialGoldExclusive),
                FavorToPlayer = 0,
                IsAlive = true,
                IsResident = isResident,
                Position = spawnPos,
                MoveTarget = null,
            };
            data.Hp = data.MaxHp;
            GrantStarterItems(data);
            return data;
        }

        /// <summary>
        /// M7：按职业发放初始物品（<b>占位规则，待配置表定档</b>）：
        /// - 商人 / 农民：2-4 件 type=4 材料，从 <see cref="GameConfigCatalog.AllItems"/> 里 <see cref="ItemKind.Ingredient"/> 的物品随机取；
        /// - 铁匠：1 件 type=1 武器，取 <see cref="ItemKind.Equipment"/>；
        /// - 其他职业：0-1 件材料（同上 Ingredient 池）。
        ///
        /// 配置缺失（<see cref="GameConfigCatalog.Instance"/> 为 null、<c>AllItems</c> 为空或该类别没有物品）时
        /// <b>静默跳过</b>——不抛异常、不告警、不阻塞角色生成，逻辑层始终可跑。
        /// </summary>
        private void GrantStarterItems(CharacterData data)
        {
            switch (data.Profession)
            {
                case ProfessionType.Merchant:
                case ProfessionType.Farmer:
                    AddStarterItems(data, _random.Next(TraderMaterialMinItems, TraderMaterialMaxItems + 1), ItemKind.Ingredient);
                    break;
                case ProfessionType.Blacksmith:
                    AddStarterItems(data, BlacksmithWeaponItems, ItemKind.Equipment);
                    break;
                default:
                    AddStarterItems(data, _random.Next(0, OtherStarterMaxItems + 1), ItemKind.Ingredient);
                    break;
            }
        }

        /// <summary>
        /// 从配置目录的指定类别里随机取 <paramref name="count"/> 件物品放进 <paramref name="data"/> 的背包（每件 1 个）。
        /// 类别池为空（配置缺失 / 没有该类物品）时直接返回，一个都不发。
        /// </summary>
        private void AddStarterItems(CharacterData data, int count, ItemKind kind)
        {
            if (count <= 0)
            {
                return;
            }

            var catalog = GameConfigCatalog.Instance;
            if (catalog == null)
            {
                return; // 配置目录未 Init：跳过发放，不报错。
            }

            IReadOnlyList<ItemDef> all = catalog.AllItems;
            if (all == null || all.Count == 0)
            {
                return;
            }

            var pool = new List<ItemDef>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].Kind == kind)
                {
                    pool.Add(all[i]);
                }
            }

            if (pool.Count == 0)
            {
                return;
            }

            for (int n = 0; n < count; n++)
            {
                data.Bag.Add(pool[_random.Next(0, pool.Count)].Id, 1);
            }
        }

        /// <summary>
        /// 职业权重抽取（R3：抽中概率 = 职业权重 ÷ 可选职业权重总和，默认 1000）。
        /// “无职业”概率文档未定，开局生成暂不参与 None（任务文档矛盾记录 4）。
        /// </summary>
        public ProfessionType RollProfession()
        {
            var pool = (ProfessionType[])Enum.GetValues(typeof(ProfessionType));
            int total = 0;
            int count = 0;
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i] != ProfessionType.None)
                {
                    total += DefaultProfessionWeight;
                    count++;
                }
            }

            int roll = _random.Next(0, total);
            int acc = 0;
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i] == ProfessionType.None)
                {
                    continue;
                }

                acc += DefaultProfessionWeight;
                if (roll < acc)
                {
                    return pool[i];
                }
            }

            return ProfessionType.None;
        }

        private PersonalityType RollPersonality()
        {
            var pool = (PersonalityType[])Enum.GetValues(typeof(PersonalityType));
            PersonalityType value;
            do
            {
                value = pool[_random.Next(0, pool.Length)];
            }
            while (value == PersonalityType.None);

            return value;
        }

        private int RollAttribute(int dayOverall)
        {
            int total = 0;
            for (int v = AttributeMin; v <= AttributeMax; v++)
            {
                total += AttributeWeight(v, dayOverall);
            }

            int roll = _random.Next(0, total);
            int acc = 0;
            for (int v = AttributeMin; v <= AttributeMax; v++)
            {
                acc += AttributeWeight(v, dayOverall);
                if (roll < acc)
                {
                    return v;
                }
            }

            return AttributeMax;
        }

        private int _fallbackNameSeq;

        /// <summary>
        /// 生效的姓名词表：优先 GameConfigCatalog 配置词表（NAME 表），
        /// 未 Init / 任一词表为空时回退代码占位词表。
        /// </summary>
        private static void ResolveNamePools(out string[] surnames, out string[] given)
        {
            string[] configuredSurnames = null;
            string[] configuredGiven = null;

            var catalog = GameConfigCatalog.Instance;
            if (catalog != null)
            {
                var pools = catalog.GetNamePools();
                configuredSurnames = pools.surnames;
                configuredGiven = pools.given;
            }

            if (configuredSurnames != null && configuredSurnames.Length > 0
                && configuredGiven != null && configuredGiven.Length > 0)
            {
                surnames = configuredSurnames;
                given = configuredGiven;
                return;
            }

            surnames = Surnames;
            given = GivenNames;
        }

        private string RollUniqueName()
        {
            string[] surnames;
            string[] given;
            ResolveNamePools(out surnames, out given);

            // 词表耗尽时回退编号名（不阻断模拟，长期运行由补员循环消耗词表）。
            int capacity = surnames.Length * given.Length;
            if (_usedNames.Count >= capacity)
            {
                string fallback;
                do
                {
                    fallback = "旅者" + (++_fallbackNameSeq);
                }
                while (!_usedNames.Add(fallback));

                return fallback;
            }

            string name;
            do
            {
                name = surnames[_random.Next(0, surnames.Length)] + given[_random.Next(0, given.Length)];
            }
            while (!_usedNames.Add(name));

            return name;
        }
    }
}
