using System;
using System.Collections.Generic;

namespace GameLogic.World
{
    /// <summary>怪物定义（怪物参数表本地缺失的占位条目；阶段=怪物池解锁阶段）。</summary>
    public sealed class MonsterDef
    {
        public readonly int Id;
        public readonly string Name;
        public readonly int Stage;
        public readonly int Weight;
        public readonly int Hp;
        public readonly int Str;
        public readonly int Int;
        public readonly int Def;

        public MonsterDef(int id, string name, int stage, int weight, int hp, int str, int intel, int def)
        {
            Id = id;
            Name = name;
            Stage = stage;
            Weight = weight;
            Hp = hp;
            Str = str;
            Int = intel;
            Def = def;
        }
    }

    /// <summary>场上怪物实例。</summary>
    public sealed class Monster
    {
        public MonsterDef Def;
        public int Hp;
        /// <summary>饱食度 0-100：每击杀 1 名角色 +1；每天自然 -1；超过 50 减缓攻击频率；满 100 停止攻击。</summary>
        public int Satiety;
        public GridPos Position;
        public bool IsHunting;
        public int HoursSinceSpawn;
        public int AttackCooldown;
        public bool IsAlive = true;
    }

    /// <summary>
    /// 怪物系统（边缘系统 + 立项·怪物刷新区域）。
    /// 占位口径（待确认）：每天刷新 1 只；狩猎延迟 24 小时；攻击间隔 6 小时（饱食度＞50 后 12 小时）；
    /// 单次伤害 ≥ 生命上限 50% 判重伤（3 天），否则轻伤（1 天）。
    /// </summary>
    public sealed class MonsterSystem
    {
        public const int MaxAliveMonsters = 20;
        public const int DaysPerStage = 10;
        public const int HuntingDelayHours = 24;
        public const int AttackIntervalHours = 6;
        public const int SlowedAttackIntervalHours = 12;
        public const int MaxSatiety = 100;
        public const int SatietySlowThreshold = 50;

        public static readonly MonsterDef[] Catalog =
        {
            new MonsterDef(1, "野狼", 1, 8, 60, 12, 4, 2),
            new MonsterDef(2, "毒蛛", 1, 5, 45, 10, 6, 1),
            new MonsterDef(3, "石傀儡", 2, 5, 120, 16, 2, 10),
            new MonsterDef(4, "沼妖", 3, 5, 90, 14, 12, 4),
            new MonsterDef(5, "骨骑士", 4, 5, 150, 20, 8, 12),
            new MonsterDef(6, "黑曜魔像", 5, 5, 220, 26, 6, 18),
        };

        private readonly IRandomSource _random;
        private readonly List<Monster> _monsters = new List<Monster>();

        public IReadOnlyList<Monster> Monsters => _monsters;

        public event Action<Monster> Spawned;
        /// <summary>怪物攻击角色。参数：monster, target, damage, targetDied。</summary>
        public event Action<Monster, CharacterData, int, bool> Attacked;

        public MonsterSystem(IRandomSource random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>当前怪物池阶段：每 10 个经营日解锁下一阶段，累计抽取。</summary>
        public static int PoolStage(int dayOverall)
        {
            return 1 + (dayOverall - 1) / DaysPerStage;
        }

        /// <summary>每日刷新（占位：每天 1 只，活跃刷新区域随机落格；场上存活数上限 20）。</summary>
        public Monster DailySpawn(WorldMap map, int dayOverall)
        {
            _monsters.RemoveAll(m => !m.IsAlive);
            if (_monsters.Count >= MaxAliveMonsters || map.ActiveSpawnAreas.Count == 0)
            {
                return null;
            }

            int stage = PoolStage(dayOverall);
            int totalWeight = 0;
            foreach (var def in Catalog)
            {
                if (def.Stage <= stage)
                {
                    totalWeight += def.Weight;
                }
            }

            int roll = _random.Next(0, totalWeight);
            MonsterDef picked = null;
            int acc = 0;
            foreach (var def in Catalog)
            {
                if (def.Stage > stage)
                {
                    continue;
                }

                acc += def.Weight;
                if (roll < acc)
                {
                    picked = def;
                    break;
                }
            }

            int areaId = map.ActiveSpawnAreas[_random.Next(0, map.ActiveSpawnAreas.Count)];
            var monster = new Monster
            {
                Def = picked,
                Hp = picked.Hp,
                Satiety = 0,
                Position = new GridPos(areaId, _random.Next(0, GridMap.Width), _random.Next(0, GridMap.Height)),
            };
            _monsters.Add(monster);
            Spawned?.Invoke(monster);
            return monster;
        }

        /// <summary>每游戏小时推进：狩猎倒计时、接近城镇、攻击。</summary>
        internal void TickHour(WorldSimulation sim)
        {
            for (int i = 0; i < _monsters.Count; i++)
            {
                var monster = _monsters[i];
                if (!monster.IsAlive)
                {
                    continue;
                }

                monster.HoursSinceSpawn++;
                if (!monster.IsHunting)
                {
                    // 刷新一段时间后进入狩猎阶段，向城镇靠近（跨图规则未定，占位：出现在城镇边缘格）。
                    if (monster.HoursSinceSpawn >= HuntingDelayHours)
                    {
                        monster.IsHunting = true;
                        monster.Position = new GridPos(WorldMapIds.Town, _random.Next(0, GridMap.Width), 0);
                    }

                    continue;
                }

                if (monster.Satiety >= MaxSatiety)
                {
                    continue; // 饱食度满时停止攻击。
                }

                if (--monster.AttackCooldown > 0)
                {
                    continue;
                }

                monster.AttackCooldown = monster.Satiety > SatietySlowThreshold ? SlowedAttackIntervalHours : AttackIntervalHours;
                HuntOnce(sim, monster);
            }
        }

        /// <summary>每日（0:00）：饱食度自然 -1。</summary>
        internal void OnDaySettled()
        {
            foreach (var monster in _monsters)
            {
                if (monster.IsAlive)
                {
                    monster.Satiety = Math.Max(0, monster.Satiety - 1);
                }
            }
        }

        private void HuntOnce(WorldSimulation sim, Monster monster)
        {
            var target = FindNearestCharacter(sim.Population, monster.Position);
            if (target == null)
            {
                return;
            }

            if (target.Position == monster.Position)
            {
                Attack(sim, monster, target);
                return;
            }

            // 逐步靠近目标（每小时 1 格，沿用角色移动规则）。
            var map = sim.Map.GetMap(monster.Position.MapId);
            if (map != null && map.TryStepTowards(monster.Position, target.Position, out var next))
            {
                monster.Position = next;
            }
        }

        private void Attack(WorldSimulation sim, Monster monster, CharacterData target)
        {
            int damage = Math.Max(1, monster.Def.Str - target.Def);
            target.Hp -= damage;
            bool died = target.Hp <= 0;
            if (died)
            {
                sim.Kill(target);
                monster.Satiety = Math.Min(MaxSatiety, monster.Satiety + 1);
            }
            else
            {
                // NPC 受伤处理：轻伤 1 天 / 重伤 3 天无法行动（占位阈值：单次伤害 ≥ 上限 50%）。
                target.ActionLockDays = damage * 2 >= target.MaxHp ? 3 : 1;
            }

            Attacked?.Invoke(monster, target, damage, died);
        }

        private static CharacterData FindNearestCharacter(TownPopulation population, GridPos pos)
        {
            CharacterData best = null;
            int bestDist = int.MaxValue;
            Search(population.Residents, pos, ref best, ref bestDist);
            Search(population.Visitors, pos, ref best, ref bestDist);
            return best;
        }

        private static void Search(IReadOnlyList<CharacterData> list, GridPos pos, ref CharacterData best, ref int bestDist)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (!c.IsAlive || c.Position.MapId != pos.MapId)
                {
                    continue;
                }

                int dist = c.Position.ManhattanDistanceTo(pos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = c;
                }
            }
        }
    }
}
