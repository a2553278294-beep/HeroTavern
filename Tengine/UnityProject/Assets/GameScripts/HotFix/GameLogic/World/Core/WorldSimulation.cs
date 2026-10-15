using System;

namespace GameLogic.World
{
    /// <summary>
    /// 世界模拟组合根（最低基座：时间 R1 / 地图 R2 / 角色生成与人口 R3·R4 / 货币 R5）。
    /// 纯 C# 实现，不依赖 Unity/TEngine；对外集成见 GameLogic.WorldModule。
    ///
    /// 时间驱动（2026-10-11 需求变更）：基准 1 真实秒 = 1 游戏分钟，五档加速
    /// （×1/×2/×4/×8/×16，档位数值待策划确认）。暂停时时刻停走。
    /// </summary>
    public sealed class WorldSimulation
    {
        /// <summary>一档（×1）下 1 真实秒对应的游戏分钟数。</summary>
        public const float GameMinutesPerRealSecond = 1f;

        public const int MinSpeedLevel = 1;
        public const int MaxSpeedLevel = 5;

        /// <summary>五档加速倍率（暂定等比 ×2，待策划确认）。</summary>
        private static readonly float[] SpeedMultipliers = { 1f, 2f, 4f, 8f, 16f };

        /// <summary>年龄 > 40 自然死亡判定权重占位（全局表缺失，任务文档矛盾记录 5）。</summary>
        public const int DefaultLiveWeight = 1000;
        public const int DefaultDeathWeight = 0;

        private readonly IRandomSource _random;
        private float _minuteAccumulator;
        private int _speedLevel = MinSpeedLevel;

        public GameTime Time { get; } = new GameTime();
        public WorldMap Map { get; private set; }
        public CharacterFactory Factory { get; private set; }
        public TownPopulation Population { get; private set; }
        public PlayerWallet Wallet { get; private set; }

        /// <summary>调酒系统（配方判定与图鉴解锁）。</summary>
        public BrewingSystem Brewing { get; private set; }

        /// <summary>仓库（售出用的酒、调酒材料、装备）。</summary>
        public Inventory Warehouse { get; private set; }

        /// <summary>经营核心（上架/采购份数/酒保流水线/结算）。</summary>
        public TavernSystem Tavern { get; private set; }

        /// <summary>怪物系统（刷新/狩猎/饱食度）。</summary>
        public MonsterSystem Monsters { get; private set; }

        /// <summary>是否暂停（R1：玩家可随时暂停；失去焦点自动暂停由驱动层调用 SetPaused）。</summary>
        public bool IsPaused { get; private set; }

        /// <summary>当前加速档位（1-5）。</summary>
        public int SpeedLevel => _speedLevel;

        /// <summary>当前加速倍率。</summary>
        public float SpeedMultiplier => SpeedMultipliers[_speedLevel - MinSpeedLevel];

        /// <summary>游戏是否已开始（StartNewGame 之后为 true）。</summary>
        public bool IsRunning { get; private set; }

        /// <summary>每游戏分钟触发（UI 时钟刷新）。参数：dayOverall, hour, minute。</summary>
        public event Action<int, int, int> MinuteChanged;

        /// <summary>每跨越整点触发。参数：dayOverall, hour。</summary>
        public event Action<int, int> HourChanged;

        /// <summary>跨越 0:00 触发（R1：结算时刻，天数 +1）。参数：newDayOverall。</summary>
        public event Action<int> DaySettled;

        /// <summary>跨越新年触发。参数：newYear。</summary>
        public event Action<int> YearChanged;

        /// <summary>角色移动一格后触发。</summary>
        public event Action<CharacterData> CharacterMoved;

        /// <summary>角色死亡后触发（人口已移除）。</summary>
        public event Action<CharacterData> CharacterDied;

        /// <summary>0:00 结算完成。参数：结算报告。</summary>
        public event Action<SettlementReport> SettlementReady;

        /// <summary>结算后余额为负，游戏结束。</summary>
        public event Action GameOver;

        /// <summary>怪物刷新。参数：新怪物。</summary>
        public event Action<Monster> MonsterSpawned;

        /// <summary>客人付费。参数：guest, wineId, price。</summary>
        public event Action<CharacterData, int, int> DrinkPaid;

        private WorldSimulation(IRandomSource random)
        {
            _random = random;
        }

        /// <summary>创建模拟实例（未开始）。seed 固定时生成结果可复现。</summary>
        public static WorldSimulation CreateNew(int seed)
        {
            return new WorldSimulation(new SystemRandomSource(seed));
        }

        /// <summary>
        /// 开始新对局：初始世界地图、初始 20 人口（四职业兜底）、初始货币 100，时钟从 Day1 00:00 起步。
        /// </summary>
        public void StartNewGame()
        {
            Map = WorldMap.CreateInitial();
            Factory = new CharacterFactory(_random);
            Population = new TownPopulation(Factory, _random);
            Population.CreateInitialPopulation(Time.DayOverall, WorldMap.TownCenter);
            ScatterStackedCharacters(); // 初始 20 人同在城镇中心，散开到空闲格
            Wallet = new PlayerWallet(PlayerWallet.InitialCurrency);
            Brewing = new BrewingSystem();
            Warehouse = new Inventory();
            Tavern = new TavernSystem(Wallet, Brewing);
            Tavern.Settled += r => SettlementReady?.Invoke(r);
            Tavern.GameOver += () => GameOver?.Invoke();
            Tavern.DrinkPaid += (g, w, p) => DrinkPaid?.Invoke(g, w, p);
            Monsters = new MonsterSystem(_random);
            Monsters.Spawned += m => MonsterSpawned?.Invoke(m);
            IsRunning = true;
        }

        public void SetPaused(bool paused)
        {
            IsPaused = paused;
        }

        /// <summary>设置加速档位（1-5），非法值钳制到范围内。</summary>
        public void SetSpeedLevel(int level)
        {
            _speedLevel = Math.Max(MinSpeedLevel, Math.Min(MaxSpeedLevel, level));
        }

        /// <summary>
        /// 以真实秒驱动模拟。一档下每真实秒推进 1 游戏分钟，按档位倍率放大。
        /// </summary>
        public void Tick(float realSeconds)
        {
            if (!IsRunning || IsPaused || realSeconds <= 0f)
            {
                return;
            }

            _minuteAccumulator += realSeconds * GameMinutesPerRealSecond * SpeedMultiplier;
            while (_minuteAccumulator >= 1f)
            {
                _minuteAccumulator -= 1f;
                AdvanceOneMinute();
            }
        }

        /// <summary>为角色设定移动目标（R2：每游戏小时沿最短路径走 1 格，仅限同图）。</summary>
        public void SetMoveTarget(CharacterData character, GridPos target)
        {
            if (character == null || !character.IsAlive || target.MapId != character.Position.MapId)
            {
                return;
            }

            // 建筑/障碍格不可作为移动目标（阻挡移动，寻路亦不可达）。
            var map = Map?.GetMap(target.MapId);
            if (map == null || !map.IsWalkable(target.X, target.Y))
            {
                return;
            }

            character.MoveTarget = target;
        }

        private void AdvanceOneMinute()
        {
            var result = Time.AdvanceMinutes(1);
            Tavern.TickMinute();
            MinuteChanged?.Invoke(result.DayOverall, result.Hour, result.Minute);

            if (result.CrossedHour)
            {
                ApplyHourlyDrift();
                MoveCharactersOneStep();
                Monsters.TickHour(this);
                Tavern.OnHourChanged(result.Hour);
                HourChanged?.Invoke(result.DayOverall, result.Hour);
            }

            if (result.CrossedMidnight)
            {
                TickActionLockDays();
                Population.ReplenishOnNewDay(result.DayOverall, WorldMap.TownCenter);
                ScatterStackedCharacters(); // 补员出生在城镇中心，散开
                Monsters.DailySpawn(Map, result.DayOverall);
                Monsters.OnDaySettled();
                Tavern.OnDaySettled(result.DayOverall);
                DaySettled?.Invoke(result.DayOverall);
            }

            if (result.CrossedYear)
            {
                ApplyYearlyAging();
                YearChanged?.Invoke(result.Year);
            }
        }

        /// <summary>
        /// R2：游戏时间每 1 小时，有移动目标的角色走 1 格。
        /// NPC 间避障：其他存活角色占用的格子作为动态障碍参与寻路（绕行）；
        /// 目标格被占用时按静态路径靠近，下一格被占则本小时等待（吧台等单格服务点自然排队）；
        /// 角色间严格不叠格。怪物不受此约束（战斗要求与角色同坐标）。
        /// </summary>
        private void MoveCharactersOneStep()
        {
            var occupied = new System.Collections.Generic.Dictionary<GridPos, int>();
            CollectOccupied(Population.Residents, occupied);
            CollectOccupied(Population.Visitors, occupied);
            MoveListOneStep(Population.Residents, occupied);
            MoveListOneStep(Population.Visitors, occupied);
        }

        private static void CollectOccupied(System.Collections.Generic.IReadOnlyList<CharacterData> characters, System.Collections.Generic.Dictionary<GridPos, int> occupied)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].IsAlive)
                {
                    occupied[characters[i].Position] = characters[i].Id;
                }
            }
        }

        private void MoveListOneStep(System.Collections.Generic.IReadOnlyList<CharacterData> characters, System.Collections.Generic.Dictionary<GridPos, int> occupied)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                var character = characters[i];
                // 受伤 debuff（轻伤/重伤）期间无法行动。
                if (!character.IsAlive || character.ActionLockDays > 0 || !character.MoveTarget.HasValue)
                {
                    continue;
                }

                var target = character.MoveTarget.Value;
                var map = Map.GetMap(character.Position.MapId);
                if (map == null)
                {
                    character.MoveTarget = null;
                    continue;
                }

                occupied.Remove(character.Position); // 自身格不算障碍
                bool moved = false;
                var path = map.FindPath(character.Position, target, OccupiedCells(occupied));
                if (path != null && path.Count > 0)
                {
                    moved = true;
                }
                else
                {
                    // 动态不可达（目标被占或被堵）：退回静态路径靠近；下一格被占则本小时等待。
                    var staticPath = map.FindPath(character.Position, target);
                    if (staticPath != null && staticPath.Count > 0 && !occupied.ContainsKey(staticPath[0]))
                    {
                        path = staticPath;
                        moved = true;
                    }
                }

                if (moved)
                {
                    character.Position = path[0];
                    occupied[character.Position] = character.Id;
                    CharacterMoved?.Invoke(character);
                }
                else
                {
                    occupied[character.Position] = character.Id; // 未移动，补回占用
                }

                if (character.Position == target)
                {
                    character.MoveTarget = null;
                }
            }
        }

        private static System.Collections.Generic.HashSet<GridPos> OccupiedCells(System.Collections.Generic.Dictionary<GridPos, int> occupied)
        {
            return new System.Collections.Generic.HashSet<GridPos>(occupied.Keys);
        }

        /// <summary>把叠在同一格的存活角色散开到城镇空闲可行走格（初始出生/补员都集中在城镇中心）。</summary>
        private void ScatterStackedCharacters()
        {
            var occupied = new System.Collections.Generic.HashSet<GridPos>();
            ScatterList(Population.Residents, occupied);
            ScatterList(Population.Visitors, occupied);
        }

        private void ScatterList(System.Collections.Generic.IReadOnlyList<CharacterData> characters, System.Collections.Generic.HashSet<GridPos> occupied)
        {
            var map = Map.GetMap(WorldMapIds.Town);
            for (int i = 0; i < characters.Count; i++)
            {
                var c = characters[i];
                if (!c.IsAlive || c.Position.MapId != WorldMapIds.Town)
                {
                    occupied.Add(c.Position);
                    continue;
                }

                if (occupied.Add(c.Position))
                {
                    continue; // 先到者保留格子
                }

                for (int tries = 0; tries < 80; tries++)
                {
                    var cell = new GridPos(WorldMapIds.Town, _random.Next(0, GridMap.Width), _random.Next(0, GridMap.Height));
                    if (map.IsWalkable(cell.X, cell.Y) && occupied.Add(cell))
                    {
                        c.Position = cell;
                        break;
                    }
                }
            }
        }

        /// <summary>每小时：体力 -1、精神 -1（行为案），健康分按生命百分比增减（分数章）。</summary>
        private void ApplyHourlyDrift()
        {
            DriftList(Population.Residents);
            DriftList(Population.Visitors);
        }

        private static void DriftList(System.Collections.Generic.IReadOnlyList<CharacterData> characters)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                var c = characters[i];
                if (!c.IsAlive)
                {
                    continue;
                }

                c.Stamina = Math.Max(0, c.Stamina - 1);
                c.Spirit = Math.Max(0, c.Spirit - 1);
                c.HealthScore += CharacterScores.HealthDriftPerHour(c.Hp, c.MaxHp);
            }
        }

        /// <summary>0:00：受伤 debuff 倒计时递减。</summary>
        private void TickActionLockDays()
        {
            TickLockList(Population.Residents);
            TickLockList(Population.Visitors);
        }

        private static void TickLockList(System.Collections.Generic.IReadOnlyList<CharacterData> characters)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].ActionLockDays > 0)
                {
                    characters[i].ActionLockDays--;
                }
            }
        }

        /// <summary>R3：跨年所有角色年龄 +1，年龄 > 40 按权重判定自然死亡（权重占位待全局表）。</summary>
        private void ApplyYearlyAging()
        {
            // 先收集再处理，避免遍历时修改人口列表。
            var all = new System.Collections.Generic.List<CharacterData>();
            all.AddRange(Population.Residents);
            all.AddRange(Population.Visitors);

            foreach (var character in all)
            {
                if (!character.IsAlive)
                {
                    continue;
                }

                character.Age++;
                if (character.Age <= 40)
                {
                    continue;
                }

                int deathWeight = DefaultDeathWeight + (character.Age - 40) * 100;
                int total = DefaultLiveWeight + deathWeight;
                if (deathWeight > 0 && _random.Next(0, total) < deathWeight)
                {
                    Kill(character);
                }
            }
        }

        /// <summary>杀死角色：冻结并从对应人口统计中移除（R4 死亡补员依赖该计数）。</summary>
        public void Kill(CharacterData character)
        {
            if (character == null || !character.IsAlive)
            {
                return;
            }

            character.IsAlive = false;
            character.Hp = 0;
            character.MoveTarget = null;
            Population.OnCharacterDied(character);
            CharacterDied?.Invoke(character);
        }
    }
}
