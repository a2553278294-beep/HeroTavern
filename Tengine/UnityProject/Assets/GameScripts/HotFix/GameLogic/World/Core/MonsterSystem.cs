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
    /// M3：目录可由配置注入（<see cref="ReloadCatalog"/>）、城镇存活怪物上限 <see cref="TownMonsterLimit"/>、
    /// 击杀接口 <see cref="KillMonster"/>、迁入广播 <see cref="MonsterEnteredTown"/>。
    /// </summary>
    public sealed class MonsterSystem
    {
        public const int MaxAliveMonsters = 20;
        /// <summary>城镇图内存活怪物上限（M3）：狩猎怪物迁入城镇前检查，满 5 只则暂缓迁入/狩猎。</summary>
        public const int TownMonsterLimit = 5;
        public const int DaysPerStage = 10;
        public const int HuntingDelayHours = 24;
        public const int AttackIntervalHours = 6;
        public const int SlowedAttackIntervalHours = 12;
        public const int MaxSatiety = 100;
        public const int SatietySlowThreshold = 50;

        public static readonly MonsterDef[] PlaceholderCatalog =
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

        /// <summary>生效中的怪物目录：默认 <see cref="PlaceholderCatalog"/>，可由 <see cref="ReloadCatalog"/> 换成配置目录。</summary>
        private MonsterDef[] _catalog = PlaceholderCatalog;

        public IReadOnlyList<Monster> Monsters => _monsters;

        /// <summary>当前生效的怪物目录（占位或配置注入，见 <see cref="ReloadCatalog"/>）。</summary>
        public IReadOnlyList<MonsterDef> Catalog => _catalog;

        public event Action<Monster> Spawned;
        /// <summary>怪物攻击角色。参数：monster, target, damage, targetDied。</summary>
        public event Action<Monster, CharacterData, int, bool> Attacked;
        /// <summary>怪物被角色击杀。参数：monster, killer（killer 可为 null，表示非角色来源）。</summary>
        public event Action<Monster, CharacterData> MonsterKilled;
        /// <summary>狩猎怪物进入城镇图（迁到城镇边缘格后触发）。安全分联动 / M11 记忆池用。</summary>
        public event Action<Monster> MonsterEnteredTown;

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
                        // 城镇存活怪物已满 TownMonsterLimit 时暂缓迁入，保留 HoursSinceSpawn，下个小时继续尝试。
                        TryEnterTown(sim, monster);
                    }

                    continue;
                }

                // 狩猎态却还在城外：说明迁入被 TownMonsterLimit 挡住过，每游戏小时重试迁入，未入城前不狩猎。
                if (monster.Position.MapId != WorldMapIds.Town)
                {
                    TryEnterTown(sim, monster);
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
            // 守卫：还在城外、且城镇存活怪物已达 TownMonsterLimit 的狩猎怪物不选目标也不攻击（等迁入后再狩猎）。
            if (monster.Position.MapId != WorldMapIds.Town && CountAliveInMap(WorldMapIds.Town) >= TownMonsterLimit)
            {
                return;
            }

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

        /// <summary>
        /// 狩猎怪物迁入城镇图（跨图规则未定，占位：出现在城镇边缘的可行走格）。
        /// 守卫：城镇图内存活怪物已达 <see cref="TownMonsterLimit"/> 时拒绝迁入（返回 false，下个小时重试）。
        /// 迁入成功后广播 <see cref="MonsterEnteredTown"/>（M11 记忆池用）；已在城镇内返回 true。
        /// 注意：场景阻挡标定（WorldMap.TownSceneBlocked）后 y=0 整行为后墙/柜台，
        /// 不能再随机落 (x, 0)，改从边缘环带（x=0/x=8/y=0/y=8）挑可行走格。
        /// </summary>
        public bool TryEnterTown(WorldSimulation sim, Monster monster)
        {
            if (monster == null || !monster.IsAlive)
            {
                return false;
            }

            if (monster.Position.MapId == WorldMapIds.Town)
            {
                return true;
            }

            if (CountAliveInMap(WorldMapIds.Town) >= TownMonsterLimit)
            {
                return false;
            }

            var town = sim?.Map?.GetMap(WorldMapIds.Town);
            var border = new System.Collections.Generic.List<GridPos>();
            for (int x = 0; x < GridMap.Width; x++)
            {
                for (int y = 0; y < GridMap.Height; y++)
                {
                    bool isBorder = x == 0 || y == 0 || x == GridMap.Width - 1 || y == GridMap.Height - 1;
                    if (isBorder && town != null && town.IsWalkable(x, y))
                    {
                        border.Add(new GridPos(WorldMapIds.Town, x, y));
                    }
                }
            }

            monster.Position = border.Count > 0
                ? border[_random.Next(0, border.Count)]
                : new GridPos(WorldMapIds.Town, 0, GridMap.Height - 1); // 兜底：右下角
            MonsterEnteredTown?.Invoke(monster);
            return true;
        }

        /// <summary>怪物被角色击杀：置 IsAlive=false 并广播 <see cref="MonsterKilled"/>（M5 悬赏计数 / M10 战斗回写用）。</summary>
        public void KillMonster(Monster m, CharacterData killer)
        {
            if (m == null || !m.IsAlive)
            {
                return;
            }

            m.IsAlive = false;
            MonsterKilled?.Invoke(m, killer);
        }

        /// <summary>指定地图内存活的怪物（按 <see cref="Monster.Position"/> 的 MapId 判定；城镇上限校验与 HUD 查询用）。</summary>
        public IReadOnlyList<Monster> AliveMonstersInMap(int mapId)
        {
            var list = new List<Monster>();
            for (int i = 0; i < _monsters.Count; i++)
            {
                var m = _monsters[i];
                if (m.IsAlive && m.Position.MapId == mapId)
                {
                    list.Add(m);
                }
            }

            return list;
        }

        /// <summary>指定地图内存活怪物数（守卫热路径使用，避免每次分配列表）。</summary>
        public int CountAliveInMap(int mapId)
        {
            int count = 0;
            for (int i = 0; i < _monsters.Count; i++)
            {
                var m = _monsters[i];
                if (m.IsAlive && m.Position.MapId == mapId)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 用配置目录替换占位目录，实现怪物配置化（M3）：调用方传 <see cref="GameConfigCatalog.GetMonsterCatalog"/> 的返回值。
        /// 仅在 <paramref name="defs"/> 非空时替换；null / 空数组表示「配置未提供」，保留当前占位 <see cref="PlaceholderCatalog"/>。
        ///
        /// 权重口径（重要）：注入的是<strong>配置原值</strong>（TbMonster 千分制，如 1000/800/600），本类<strong>不做量级换算</strong>——
        /// <see cref="DailySpawn"/> 按「池内总和归一」的<strong>相对权重</strong>抽取（totalWeight = Σ 阶段≤当前阶段的 Weight，
        /// roll ∈ [0, totalWeight) 后累计命中首个 acc &gt; roll 的条目），所以量级无关：占位的 8/5 与配置的 1000/600 可直接互换。
        /// （目录内权重全为 0 时 totalWeight=0，roll 退化为 0，取阶段内第一只，不抛异常。）
        /// </summary>
        public void ReloadCatalog(MonsterDef[] defs)
        {
            if (defs == null || defs.Length == 0)
            {
                return;
            }

            _catalog = defs;
        }

        private void Attack(WorldSimulation sim, Monster monster, CharacterData target)
        {
            // M7：伤害按角色<strong>有效防御</strong>（含装备 def 加成）计算，与战斗分同一口径。
            // M10：公式统一走 CombatSystem.AttackDamageVsCharacter（怪物 Str − 目标 EffDef，下限 1），
            //     与角色在 CombatSystem.FightMonster 里交战时的数值完全一致。
            int damage = CombatSystem.AttackDamageVsCharacter(monster, target);
            target.Hp -= damage;
            bool died = target.Hp <= 0;
            if (died)
            {
                sim.Kill(target);
                monster.Satiety = Math.Min(MaxSatiety, monster.Satiety + 1);
            }
            else
            {
                // NPC 受伤处理：轻伤 1 天 / 重伤 3 天无法行动（占位阈值：单次伤害 ≥ 有效生命上限 50%，M7 用 EffMaxHp）。
                target.ActionLockDays = damage * 2 >= target.EffMaxHp ? 3 : 1;
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
                // M3：隐藏居民（CharacterData.IsHidden）不参与行动，不作为狩猎目标。
                if (!c.IsAlive || c.IsHidden || c.Position.MapId != pos.MapId)
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
