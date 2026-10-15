using System;
using System.Collections.Generic;

namespace GameLogic.World
{
    /// <summary>
    /// 小镇人口（R4：初始 20 本地 NPC，四职业兜底；本地≤30、暂住≤6；死亡累计≥4 次日补员至 20）。
    /// </summary>
    public sealed class TownPopulation
    {
        public const int InitialResidentCount = 20;
        public const int MaxResidents = 30;
        public const int MaxVisitors = 6;
        public const int ReplenishDeathThreshold = 4;
        public const int ReplenishTarget = 20;

        /// <summary>开局必须出现的职业（各至少 1 名）。</summary>
        private static readonly ProfessionType[] OpeningProfessions =
        {
            ProfessionType.Chef, ProfessionType.Merchant, ProfessionType.Blacksmith, ProfessionType.Farmer,
        };

        private readonly IRandomSource _random;
        private readonly Dictionary<(int, int), int> _relations = new Dictionary<(int, int), int>();
        private readonly CharacterFactory _factory;
        private readonly List<CharacterData> _residents = new List<CharacterData>();
        private readonly List<CharacterData> _visitors = new List<CharacterData>();

        /// <summary>本地 NPC 累计死亡人数（同一 NPC 只计 1 次）。</summary>
        public int CumulativeDeaths { get; private set; }

        public IReadOnlyList<CharacterData> Residents => _residents;
        public IReadOnlyList<CharacterData> Visitors => _visitors;

        public TownPopulation(CharacterFactory factory, IRandomSource random)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>
        /// NPC 间好感（R3：初始 -100~100 抽取；-100~0 敌对，0~70 普通，70~100 亲近；增减后不越界）。
        /// 无序对存储，双向同值。
        /// </summary>
        public int GetRelation(int characterIdA, int characterIdB)
        {
            return _relations.TryGetValue(NormalizePair(characterIdA, characterIdB), out int favor) ? favor : 0;
        }

        public void ChangeRelation(int characterIdA, int characterIdB, int delta)
        {
            var key = NormalizePair(characterIdA, characterIdB);
            int value = (_relations.TryGetValue(key, out int favor) ? favor : 0) + delta;
            _relations[key] = Math.Max(-100, Math.Min(100, value));
        }

        /// <summary>社交分：其他角色对该角色的好感度之和。</summary>
        public int GetSocialScore(int characterId)
        {
            int sum = 0;
            foreach (var pair in _relations)
            {
                if (pair.Key.Item1 == characterId || pair.Key.Item2 == characterId)
                {
                    sum += pair.Value;
                }
            }

            return sum;
        }

        private static (int, int) NormalizePair(int a, int b)
        {
            return a < b ? (a, b) : (b, a);
        }

        /// <summary>
        /// 生成开局人口（R4：先指定厨师/商人/铁匠/农民各 1 名，再随机补满 20 人）。
        /// </summary>
        public void CreateInitialPopulation(int dayOverall, GridPos spawnPos)
        {
            foreach (var profession in OpeningProfessions)
            {
                AddResident(_factory.CreateCharacter(dayOverall, spawnPos, profession));
            }

            while (_residents.Count < InitialResidentCount)
            {
                AddResident(_factory.CreateCharacter(dayOverall, spawnPos));
            }

            RollInitialRelations();
        }

        /// <summary>开局抽取 NPC 间初始好感。</summary>
        private void RollInitialRelations()
        {
            for (int i = 0; i < _residents.Count; i++)
            {
                for (int j = i + 1; j < _residents.Count; j++)
                {
                    _relations[NormalizePair(_residents[i].Id, _residents[j].Id)] = _random.Next(-100, 101);
                }
            }
        }

        public bool AddResident(CharacterData character)
        {
            if (_residents.Count >= MaxResidents)
            {
                return false;
            }

            character.IsResident = true;
            _residents.Add(character);
            return true;
        }

        public bool AddVisitor(CharacterData character)
        {
            if (_visitors.Count >= MaxVisitors)
            {
                return false;
            }

            character.IsResident = false;
            _visitors.Add(character);
            return true;
        }

        /// <summary>
        /// 本地 NPC 死亡：移除并累计死亡人数（R4）。暂住 NPC 死亡不计入。
        /// </summary>
        public void OnCharacterDied(CharacterData character)
        {
            if (_residents.Remove(character))
            {
                CumulativeDeaths++;
            }
            else
            {
                _visitors.Remove(character);
            }
        }

        /// <summary>累计死亡达到补员触发人数。</summary>
        public bool NeedsReplenish => CumulativeDeaths >= ReplenishDeathThreshold;

        /// <summary>
        /// 次日补员（R4：补入人数 = 20 − 当前本地人口，先补缺失职业，再随机；
        /// 补足到 20 后清零累计死亡；生成失败保留任务次日再补）。
        /// </summary>
        public void ReplenishOnNewDay(int dayOverall, GridPos spawnPos)
        {
            if (!NeedsReplenish)
            {
                return;
            }

            foreach (var profession in OpeningProfessions)
            {
                if (_residents.Count >= ReplenishTarget)
                {
                    break;
                }

                if (!HasProfession(profession) && _residents.Count < MaxResidents)
                {
                    AddResident(_factory.CreateCharacter(dayOverall, spawnPos, profession));
                }
            }

            while (_residents.Count < ReplenishTarget && _residents.Count < MaxResidents)
            {
                AddResident(_factory.CreateCharacter(dayOverall, spawnPos));
            }

            if (_residents.Count >= ReplenishTarget)
            {
                CumulativeDeaths = 0;
            }
        }

        private bool HasProfession(ProfessionType profession)
        {
            for (int i = 0; i < _residents.Count; i++)
            {
                if (_residents[i].Profession == profession)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
