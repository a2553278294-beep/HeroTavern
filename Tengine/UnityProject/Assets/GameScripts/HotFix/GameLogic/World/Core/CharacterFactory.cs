using System;
using System.Collections.Generic;

namespace GameLogic.World
{
    /// <summary>
    /// 角色随机生成（R3）。NAME / Profession 配置表本地缺失，词表与权重为代码占位（任务文档决策 4）。
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

        /// <summary>姓名占位词表（NAME 表落地后替换）。</summary>
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
                FavorToPlayer = 0,
                IsAlive = true,
                IsResident = isResident,
                Position = spawnPos,
                MoveTarget = null,
            };
            data.Hp = data.MaxHp;
            return data;
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

        private string RollUniqueName()
        {
            // 占位词表耗尽时回退编号名（NAME 配置表落地前不阻断模拟，长期运行由补员循环消耗词表）。
            int capacity = Surnames.Length * GivenNames.Length;
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
                name = Surnames[_random.Next(0, Surnames.Length)] + GivenNames[_random.Next(0, GivenNames.Length)];
            }
            while (!_usedNames.Add(name));

            return name;
        }
    }
}
