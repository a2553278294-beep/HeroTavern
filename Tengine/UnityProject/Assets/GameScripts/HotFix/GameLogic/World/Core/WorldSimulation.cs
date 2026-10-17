using System;

namespace GameLogic.World
{
    /// <summary>
    /// 地面掉落包（M7）：角色死亡时由 <see cref="WorldSimulation.Kill"/> 落到其死亡坐标，
    /// 再由 <see cref="WorldSimulation.PickupLoot"/> 拾取。
    ///
    /// 归属口径：<b>无归属</b>——任何角色走到 <see cref="Pos"/> 同一格都能拾走（行为案：死亡背包物品可被任何角色拾取）。
    /// 拾取成功后本对象会被移出 <see cref="WorldSimulation.GroundLoot"/>，物品 / 金币一并清零，防止重复拾取。
    /// </summary>
    public sealed class LootBag
    {
        /// <summary>掉落所在网格坐标（= 死亡角色死亡瞬间的 <see cref="CharacterData.Position"/>）。</summary>
        public GridPos Pos;

        /// <summary>掉落物品（<c>itemId → 数量</c>，来自 <see cref="NpcInventory.ExportAll"/>，已含身上三个装备槽）。</summary>
        public System.Collections.Generic.Dictionary<int, int> Items = new System.Collections.Generic.Dictionary<int, int>();

        /// <summary>掉落金币（来自死亡角色的 <see cref="CharacterData.Gold"/>；拾取后转入拾取者并清零）。</summary>
        public int Gold;

        /// <summary>是否已被拾空（物品与金币都没有剩余）。</summary>
        public bool IsEmpty
        {
            get { return Items.Count == 0 && Gold <= 0; }
        }

        public override string ToString()
        {
            return $"loot@{Pos} items={Items.Count} gold={Gold}";
        }
    }

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
        public const float GameMinutesPerRealSecond = 6f;

        public const int MinSpeedLevel = 1;
        public const int MaxSpeedLevel = 3;

        /// <summary>五档加速倍率（暂定等比 ×2，待策划确认）。</summary>
        private static readonly float[] SpeedMultipliers = { 1f, 2f, 4f };

        /// <summary>年龄 > 40 自然死亡判定权重占位（全局表缺失，任务文档矛盾记录 5）。</summary>
        public const int DefaultLiveWeight = 1000;
        public const int DefaultDeathWeight = 0;

        private readonly IRandomSource _random;
        private float _minuteAccumulator;
        private int _speedLevel = MinSpeedLevel;

        /// <summary>M4：城镇里有多名存活吟游诗人时，待售情报的轮流游标（保证线索不固定落在同一人身上）。</summary>
        private int _bardRoundRobin;

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

        /// <summary>
        /// 情报/线索系统（M4）：怪物刷新生成三类情报（位置 / 形象 / 属性），
        /// 交给当日在城的吟游诗人作待售清单；玩家持有条目供悬赏（M5）发布时消耗（消耗即无，不堆叠）。
        /// </summary>
        public IntelSystem Intel { get; private set; }

        /// <summary>
        /// 城镇安全分（M3）：怪物杀死居民时扣分；分数联动「全员精神 -1」与「人口隐藏/恢复」。
        /// 初值 <see cref="TownSafety.InitialSafety"/>，区间 [<see cref="TownSafety.MinSafety"/>, <see cref="TownSafety.MaxSafety"/>]。
        /// </summary>
        public TownSafety Safety { get; private set; }

        /// <summary>
        /// 悬赏系统（M5）：六槽状态机 + 四校验发布 + 0:00 入账 / 失败返还。
        /// 组合根在 <see cref="StartNewGame"/> 里创建并接好钱包 / 情报 / 怪物事件；
        /// 0:00 由 <see cref="Tavern"/> 第一顺位调用 <see cref="BountySystem.OnDaySettled"/>（顺序见 master-plan M5「修改」）。
        /// </summary>
        public BountySystem Bounty { get; private set; }

        /// <summary>
        /// 职业服务（M9）：厨师每日 1 次烹饪、铁匠每日 1 次锻造、商人到店补货、农民农产品、
        /// 牧师治疗、吟游诗人情报播报、战士接取战斗悬赏资格。
        /// 组合根在 <see cref="StartNewGame"/> 里构造注入 <c>(_random, Intel)</c>；
        /// 0:00 由 <see cref="AdvanceOneMinute"/> 调 <see cref="ProfessionService.OnDaySettled"/> 重置每日次数。
        /// </summary>
        public ProfessionService Professions { get; private set; }

        /// <summary>
        /// M6/M12 对话系统，HUD 交谈入口用。
        /// 组合根在 <see cref="StartNewGame"/> 里构造注入 <c>_random</c>；配置表来源由 <see cref="DialogueSystem"/>
        /// 自身解析（模块层 <c>WorldModule.EnsureConfigReady</c> 已先初始化 <c>GameConfigCatalog</c>），此处不加载配置。
        /// </summary>
        public DialogueSystem Dialogue { get; private set; }

        /// <summary>
        /// 角色行为决策系统（M11）：每个游戏小时为每名「存活 + 未隐藏 + 无行动锁定」的角色做一次行动决策
        /// （待机 / 点单 / 狩猎 / 接悬赏 / 对话 / 拾取 / 闲逛），并把悬赏 / 怪物 / 日结广播写进短期记忆触发动态决策。
        /// 组合根在 <see cref="StartNewGame"/> 里构造注入 <c>_random</c> 并 <c>AttachBroadcastSources(this)</c>；
        /// 每整点由 <see cref="AdvanceOneMinute"/> 调 <see cref="CharacterBehaviorSystem.TickHour"/>，
        /// 0:00 调 <see cref="CharacterBehaviorSystem.OnDaySettled"/>。
        /// </summary>
        public CharacterBehaviorSystem Behavior { get; private set; }

        /// <summary>
        /// M7：地面掉落物列表（每个死亡角色落地一个 <see cref="LootBag"/>，含背包 + 身上装备 + 金币）。
        /// 拾取入口 <see cref="PickupLoot"/>（同坐标校验）；掉落物不会自动过期，暂不做清理（TODO：行为系统补拾取 AI / 过期策略）。
        /// </summary>
        public readonly System.Collections.Generic.List<LootBag> GroundLoot = new System.Collections.Generic.List<LootBag>();

        /// <summary>M3：每 1 名居民被怪物杀死所扣的安全分（占位常量 <b>待策划定档</b>，行为案原文 -5）。</summary>
        public const int SafetyLossPerResidentDeath = 5;

        /// <summary>M3：每次 0:00 隐藏 / 恢复的人口数量（占位常量，待策划确认）。</summary>
        public const int SafetyPopulationStepPerDay = 1;

        /// <summary>
        /// M11：角色每<b>实际移动 1 格</b>扣除的体力（行为案「每移动 1 格 -1 体力」）。
        /// 与「每小时 -1」（<see cref="ApplyHourlyDrift"/>）分开结算：本常量只在 <see cref="MoveListOneStep"/> 真正挪格时扣，
        /// 被占格原地等待不扣体力。待机每小时 +1 的恢复在 <c>CharacterBehaviorSystem</c> 的待机分支里，不在此处重复。
        /// </summary>
        public const int MoveStaminaPerCell = 1;

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

        /// <summary>情报线索落定（玩家入账 / NPC 入账 / 系统池暂存）。参数：情报记录。</summary>
        public event Action<IntelRecord> IntelObtained;

        /// <summary>
        /// NPC 对玩家的好感变化（M8）。参数：npc, <b>变化后的好感值</b>（已钳进 [0,100]）。
        /// 只在 <see cref="ChangeFavor"/> 真正改变数值时触发（钳制后无变化则不触发）。
        /// </summary>
        public event Action<CharacterData, int> FavorChanged;

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

            // M4：情报系统。每只新怪物刷新时生成 3 条线索（位置 / 形象 / 属性各一），
            // 优先交给当日在城镇图的存活吟游诗人（待售清单，M9 补充货源）；没有则暂存系统池 + 占位直给玩家一条位置情报。
            Intel = new IntelSystem();
            Intel.IntelObtained += r => IntelObtained?.Invoke(r);
            Intel.SetCurrentDay(Time.DayOverall);
            Monsters.Spawned += OnMonsterSpawnedIntel;

            // M3：安全分与联动订阅（行为案「安全分每失去 1 点，精神 -1」）。
            Safety = new TownSafety();
            Safety.DeltaApplied += OnSafetyDeltaApplied;
            // M3：怪物攻击致死的安全分扣减（-5，占位待策划）。
            Monsters.Attacked += OnMonsterAttacked;

            // M5：悬赏接线（master-plan M5「修改」）。装配顺序固定：
            // ① 创建 + 读配置（配置缺失时悬赏侧内部回退占位示例，逻辑层始终可跑）；
            // ② 注入钱包与情报系统（发布四校验依赖余额与线索持有）；
            // ③ 怪物被击杀 → 回写悬赏完成进度（悬赏案 3.3-4；killer 可为 null，不影响按怪物名回写）；
            // ④ 怪物刷新 → 点亮 target 匹配的悬赏条目（悬赏案 3.1-1）；
            // ⑤ 注入 Tavern：0:00 由 Tavern 内部<b>第一顺位</b>调 Bounty.OnDaySettled，
            //    赏金 / 物资 / 安定度 / 失败返还都在营业成本与酒保工资之前入账，故都参与当次余额 &lt; 0 判定。
            Bounty = new BountySystem();
            Bounty.LoadFromConfig();
            Bounty.Attach(Wallet, Intel);
            Monsters.MonsterKilled += (m, killer) => Bounty.ReportMonsterKilled(m.Def.Name);
            Monsters.Spawned += OnMonsterSpawnedBountyReveal;
            Tavern.AttachBounty(Bounty, Warehouse, Safety);

            // M9：职业服务接线（master-plan M9「修改」）。装配顺序固定：
            // ① 构造注入随机源与情报系统（吟游诗人的货源 = M4 的情报条目，判重与刷新日都取自它）；
            // ② 登记当前在城的吟游诗人（BardAnnounceMonster 的落货对象；找人用 TownPopulation.FindByProfession）；
            // ③ 每日次数重置挂在 0:00 链路（见 AdvanceOneMinute），不塞进 Tavern 结算，避免与成本顺序耦合。
            Professions = new ProfessionService(_random, Intel);
            Professions.RegisterBards(Population.FindByProfession(ProfessionType.Bard));

            // M6/M12：对话系统接线（HUD 交谈入口用）。只构造注入 _random（与其余系统共用同一随机源，保证同 seed 可复现）；
            // 配置表由 DialogueSystem 惰性解析，模块层 WorldModule.EnsureConfigReady 已先 GameConfigCatalog.Init，此处不加载配置。
            Dialogue = new DialogueSystem(_random);

            // M11：角色行为决策系统接线。装配顺序固定：
            // ① 构造注入 _random（与其余系统共用同一随机源，保证同 seed 的决策可复现）；
            // ② AttachBroadcastSources 订阅悬赏发布 / 怪物进城 / 怪物被击杀 / 0:00 日结广播，
            //    故必须排在 Bounty 与 Monsters 建好之后（本处即 M9 之后）。
            Behavior = new CharacterBehaviorSystem(_random);
            Behavior.AttachBroadcastSources(this);
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
        /// M8：<see cref="CharacterData.FavorToPlayer"/> 的<b>唯一写入口</b>。
        /// 交易系统（<see cref="TradeSession"/>）、对话 / 事件赠送等都通过它增减好感，
        /// 以保证「钳 0-100 + 触发 <see cref="FavorChanged"/>」这条规则只有一处实现。
        ///
        /// 钳制后数值未变化（例如已是 100 还加、或已是 0 还减）时<b>不触发</b> <see cref="FavorChanged"/>，
        /// 避免 UI / M6 好感对话收到无意义刷新。返回值无意义，调用方按需读 <c>npc.FavorToPlayer</c>。
        /// </summary>
        public void ChangeFavor(CharacterData npc, int delta)
        {
            if (npc == null)
            {
                return;
            }

            int before = npc.FavorToPlayer;
            int after = TradePricing.ClampFavor(before + delta);
            if (after == before)
            {
                return;
            }

            npc.FavorToPlayer = after;
            FavorChanged?.Invoke(npc, after);
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
            // M3：隐藏居民不参与行动，不能被下达新的移动目标。
            if (character == null || !character.IsAlive || character.IsHidden || target.MapId != character.Position.MapId)
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
                ApplyHourlyDrift();       // 每小时体力 / 精神 -1（行为案；待机的 +1 由行为系统在本小时结算）
                Behavior?.TickHour(this); // M11：行为决策——下达 MoveTarget / 原地交互 / 待机恢复
                MoveCharactersOneStep(); // R2：每整点走 1 格，并按行为案「每移动 1 格体力 -1」结算
                Monsters.TickHour(this);
                Tavern.OnHourChanged(result.Hour);
                HourChanged?.Invoke(result.DayOverall, result.Hour);
            }

            if (result.CrossedMidnight)
            {
                TickActionLockDays();
                Population.ReplenishOnNewDay(result.DayOverall, WorldMap.TownCenter);
                ScatterStackedCharacters(); // 补员出生在城镇中心，散开
                UnlockSpawnAreasByPoolStage(result.DayOverall); // M3：池阶段提升先解锁场地，再刷新，保证新场地当天即可出怪
                // M4：情报 DayOverall 口径 = 刷新日：先按 DailySpawn 的入参设置，再让 Spawned 处理器生成线索。
                Intel.SetCurrentDay(result.DayOverall);
                Monsters.DailySpawn(Map, result.DayOverall);
                Monsters.OnDaySettled();
                Tavern.OnDaySettled(result.DayOverall);
                Professions.RegisterBards(Population.FindByProfession(ProfessionType.Bard)); // M9：补员后吟游诗人名单会变，先重登记
                Professions.OnDaySettled(); // M9：0:00 重置厨师 / 铁匠的每日次数
                ApplySafetyPopulationRule(); // M3：安全分 <50 隐藏 1 名非必需居民 / ≥50 恢复 1 名隐藏居民
                Behavior?.OnDaySettled(this, result.DayOverall); // M11：短期记忆当日有效 → 整池清空并重播当日在城怪物 / 可接悬赏
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
                // M3：隐藏居民不参与行动，不占格（与 MoveCharactersOneStep 的寻路障碍口径一致）。
                if (characters[i].IsAlive && !characters[i].IsHidden)
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
                // 受伤 debuff（轻伤/重伤）期间无法行动；M3：隐藏居民同样不行动。
                if (!character.IsAlive || character.IsHidden || character.ActionLockDays > 0 || !character.MoveTarget.HasValue)
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
                    // M11：行为案「每移动 1 格 -1 体力」（与每小时 -1 叠加；原地等待不扣）。
                    character.Stamina = Math.Max(0, character.Stamina - MoveStaminaPerCell);
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
                // M3：隐藏居民不参与行动，既不占用格位也不被重新散开。
                if (c.IsHidden)
                {
                    continue;
                }

                if (!c.IsAlive || c.Position.MapId != WorldMapIds.Town)
                {
                    occupied.Add(c.Position);
                    continue;
                }

                // 站到阻挡格（场景不可站立区，如圆桌/柜台）的角色同样要挪走：
                // 先到者保留格子的前提是格子本身可行走。
                if (map.IsWalkable(c.Position.X, c.Position.Y) && occupied.Add(c.Position))
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
                // M3：隐藏居民不参与每小时状态变化（与安全分精神联动口径一致：隐藏者既不加成也不损耗）。
                if (!c.IsAlive || c.IsHidden)
                {
                    continue;
                }

                c.Stamina = Math.Max(0, c.Stamina - 1);
                c.Spirit = Math.Max(0, c.Spirit - 1);
                // M7：生命百分比按「有效上限」算（含装备 hp 加成），与怪物伤害走同一口径。
                c.HealthScore += CharacterScores.HealthDriftPerHour(c.Hp, c.EffMaxHp);
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
                // M3：隐藏居民仍照常变老并参与 >40 自然死亡判定（IsHidden 只屏蔽行动/视图，不冻结时间）。
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

        /// <summary>
        /// M3：怪物池阶段提升时按顺序解锁刷新场地（<see cref="MonsterSystem.PoolStage"/> 每 <see cref="MonsterSystem.DaysPerStage"/> 天 +1）。
        /// 规则与占位说明见 <see cref="WorldMap.UnlockSpawnAreaByPoolStage"/>：阶段 1 → 场地 2 黑松林（初始已解锁），
        /// 阶段 2-5 依次解锁场地 3-6。放在 0:00 日结、<see cref="MonsterSystem.DailySpawn"/> 之前。
        /// </summary>
        private void UnlockSpawnAreasByPoolStage(int dayOverall)
        {
            Map?.UnlockSpawnAreaByPoolStage(MonsterSystem.PoolStage(dayOverall));
        }

        /// <summary>
        /// M4：怪物刷新 → 生成 3 条情报（位置 / 形象 / 属性各一，见 <see cref="IntelKind"/>），
        /// 优先交给<strong>当日在城镇图的存活吟游诗人</strong>（<see cref="IntelSystem.GrantToNpc"/> 作待售清单）。
        ///
        /// 分配口径：
        /// - 城镇里有多名吟游诗人时<strong>轮流</strong>接待：按 Id 升序固定顺序后用 <see cref="_bardRoundRobin"/> 取模轮转，
        ///   避免固定某人垄断线索（只有一人时自然固定）；排序保证同 seed 下结果可复现。
        /// - 「在城镇 + 存活」判定：<c>IsAlive</c> 且 <c>Position.MapId == <see cref="WorldMapIds.Town"/></c>；
        ///   隐藏居民（<see cref="CharacterData.IsHidden"/>，M3）不参与，与本类其余行动的隐藏口径一致。
        /// - 线索归属在<strong>刷新当日</strong>即锁定，吟游诗人当日死亡 / 迁城不回退（正式口径待策划确认）。
        /// - 城镇内<strong>没有</strong>存活吟游诗人时：形象 / 属性线索暂存系统池（<see cref="IntelSystem.ParkIntel"/>），
        ///   位置线索按占位口径直给玩家（<see cref="IntelSystem.GrantToPlayer"/>）——
        ///   <b>占位待策划确认</b>：正式口径应由吟游诗人售卖（M8 交易），
        ///   此处直给只是保证悬赏（M5）发布不会因「无情报可耗」被永久卡死。
        ///   位置线索不入池，避免同一编号同时存在于玩家与系统池两侧。
        /// </summary>
        private void OnMonsterSpawnedIntel(Monster monster)
        {
            if (Intel == null || monster == null)
            {
                return;
            }

            // 情报 DayOverall = 刷新日：取世界时间当前日，与 Monsters.DailySpawn 传入的 dayOverall 同源。
            Intel.SetCurrentDay(Time.DayOverall);

            int bardId = FindTownBardId();
            var records = Intel.CreateAllForMonster(monster);
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (bardId > 0)
                {
                    Intel.GrantToNpc(bardId, record);
                }
                else if (record.Kind == IntelKind.Location)
                {
                    Intel.GrantToPlayer(record); // 占位：无吟游诗人时直给玩家一条位置情报（待策划确认）。
                }
                else
                {
                    Intel.ParkIntel(record); // 形象 / 属性进系统池，等吟游诗人（M9）到城后取走。
                }
            }
        }

        /// <summary>
        /// M5：怪物刷新时点亮 target 匹配的悬赏条目（悬赏案 3.1-1「已被其他系统点亮」）。
        /// 等价于遍历 <see cref="BountySystem.Entries"/>、按 <c>TargetMonsterName == 怪物名</c> 逐条 <c>Reveal(id)</c>，
        /// 由悬赏侧实现（<see cref="BountySystem.RevealByTarget"/>）以复用其状态刷新；已点亮条目会被跳过，重复刷新不重复点亮。
        /// </summary>
        private void OnMonsterSpawnedBountyReveal(Monster monster)
        {
            if (Bounty == null || monster == null || monster.Def == null)
            {
                return;
            }

            Bounty.RevealByTarget(monster.Def.Name);
        }

        /// <summary>
        /// M4：找出当日「在城镇 + 存活」的吟游诗人（<see cref="ProfessionType.Bard"/>），
        /// 按 Id 升序取模轮流返回其 <see cref="CharacterData.Id"/>；没有候选人返回 -1（调用方走系统池暂存分支）。
        /// </summary>
        private int FindTownBardId()
        {
            var bards = new System.Collections.Generic.List<CharacterData>();
            CollectTownBards(Population.Residents, bards);
            CollectTownBards(Population.Visitors, bards);
            if (bards.Count == 0)
            {
                return -1;
            }

            bards.Sort((a, b) => a.Id.CompareTo(b.Id));
            int index = _bardRoundRobin % bards.Count;
            _bardRoundRobin++;
            return bards[index].Id;
        }

        private static void CollectTownBards(System.Collections.Generic.IReadOnlyList<CharacterData> characters, System.Collections.Generic.List<CharacterData> result)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                var c = characters[i];
                if (c.Profession != ProfessionType.Bard || !c.IsAlive || c.IsHidden || c.Position.MapId != WorldMapIds.Town)
                {
                    continue;
                }

                result.Add(c);
            }
        }

        /// <summary>
        /// M3：怪物攻击致死的安全分联动——<c>Safety.Change(-5)</c>。
        /// 扣减量为占位常量 <see cref="SafetyLossPerResidentDeath"/>，<b>待策划定档</b>。
        /// 随后由 <see cref="OnSafetyDeltaApplied"/> 把 -5 逐点落到全员精神（行为案）。
        /// </summary>
        private void OnMonsterAttacked(Monster monster, CharacterData target, int damage, bool targetDied)
        {
            if (!targetDied)
            {
                return;
            }

            Safety?.Change(-SafetyLossPerResidentDeath);
        }

        /// <summary>
        /// M3：安全分与精神分联动——「城镇安全分每失去 1 点，精神分 -1」（行为案）。
        /// delta 是 <see cref="TownSafety.DeltaApplied"/> 给出的<strong>钳制后实际变化量</strong>，
        /// 所以 Change(-5) 一次性落到全员精神 -5；delta ≥ 0（安全分回升）不联动精神。
        /// 隐藏居民（<see cref="CharacterData.IsHidden"/>）不参与，与 <see cref="DriftList"/> 口径一致。
        /// </summary>
        private void OnSafetyDeltaApplied(int delta)
        {
            if (delta >= 0)
            {
                return;
            }

            ApplySpiritDelta(Population.Residents, delta);
            ApplySpiritDelta(Population.Visitors, delta);
        }

        private static void ApplySpiritDelta(System.Collections.Generic.IReadOnlyList<CharacterData> characters, int delta)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                var c = characters[i];
                if (!c.IsAlive || c.IsHidden)
                {
                    continue;
                }

                c.Spirit = Math.Max(0, c.Spirit + delta);
            }
        }

        /// <summary>
        /// M3 0:00 人口隐藏/恢复：<see cref="TownSafety.Value"/> &lt; <see cref="TownSafety.HideThreshold"/>
        /// 时隐藏 <see cref="SafetyPopulationStepPerDay"/> 名非必需居民；≥ 阈值时恢复同样数量的隐藏居民。
        /// </summary>
        private void ApplySafetyPopulationRule()
        {
            if (Safety == null || Population == null)
            {
                return;
            }

            if (Safety.Value < TownSafety.HideThreshold)
            {
                HideResidents(SafetyPopulationStepPerDay);
            }
            else
            {
                RestoreResidents(SafetyPopulationStepPerDay);
            }
        }

        /// <summary>
        /// M3：随机隐藏指定数量的非必需存活居民（必需 = 开局四职业保底，见 <see cref="IsEssentialResident"/>）。
        /// 候选不足时有多少隐藏多少，没有候选则什么都不做（不抛异常、不影响日结其余流程）。
        /// </summary>
        private void HideResidents(int count)
        {
            var residents = Population.Residents;
            var candidates = new System.Collections.Generic.List<CharacterData>(residents.Count);
            for (int i = 0; i < residents.Count; i++)
            {
                var c = residents[i];
                if (c.IsAlive && !c.IsHidden && !IsEssentialResident(c))
                {
                    candidates.Add(c);
                }
            }

            for (int n = 0; n < count && candidates.Count > 0; n++)
            {
                int index = _random.Next(0, candidates.Count);
                var hidden = candidates[index];
                candidates.RemoveAt(index);
                hidden.IsHidden = true;
                hidden.MoveTarget = null; // 隐藏即停止行动：清掉进行中的移动目标。
            }
        }

        /// <summary>
        /// M3：随机恢复指定数量的隐藏存活居民（本地居民优先，暂住人口随后；同池内随机）。
        /// 没有隐藏对象时什么都不做。
        /// </summary>
        private void RestoreResidents(int count)
        {
            var hidden = CollectHidden(Population.Residents);
            if (hidden.Count == 0)
            {
                hidden = CollectHidden(Population.Visitors);
            }

            for (int n = 0; n < count && hidden.Count > 0; n++)
            {
                int index = _random.Next(0, hidden.Count);
                var character = hidden[index];
                hidden.RemoveAt(index);
                character.IsHidden = false;
                character.MoveTarget = null; // 恢复后不自动续走之前的路径，等行为系统重新下达目标。
            }
        }

        private static System.Collections.Generic.List<CharacterData> CollectHidden(System.Collections.Generic.IReadOnlyList<CharacterData> characters)
        {
            var list = new System.Collections.Generic.List<CharacterData>();
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].IsAlive && characters[i].IsHidden)
                {
                    list.Add(characters[i]);
                }
            }

            return list;
        }

        /// <summary>
        /// M3「必需居民」判定（占位）：开局四职业保底角色（厨师 / 商人 / 铁匠 / 农民）永不隐藏。
        /// <see cref="TownPopulation"/> 目前只保证「这四个职业至少各有 1 人」，没有对外标记「哪几个是开局那 4 个」，
        /// 因此按<strong>职业</strong>近似——这四个职业的所有居民都算必需（偏保守：宁可少隐藏，不可把保底职业藏起来）。
        /// 待 TownPopulation 落地「保底角色」标记后，可收紧为只保护开局那 4 个。
        /// </summary>
        private static bool IsEssentialResident(CharacterData character)
        {
            switch (character.Profession)
            {
                case ProfessionType.Chef:
                case ProfessionType.Merchant:
                case ProfessionType.Blacksmith:
                case ProfessionType.Farmer:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 杀死角色：M7 先把背包 + 身上装备 + 金币落到死亡坐标（<see cref="GroundLoot"/>），
        /// 再冻结并从对应人口统计中移除（R4 死亡补员依赖该计数）。
        /// </summary>
        public void Kill(CharacterData character)
        {
            if (character == null || !character.IsAlive)
            {
                return;
            }

            character.IsAlive = false;
            character.Hp = 0;
            character.MoveTarget = null;

            // M7：死亡掉落（必须赶在移出人口之前取坐标；行为案：死亡背包物品可被任何角色拾取）。
            DropLoot(character);

            Population.OnCharacterDied(character);
            CharacterDied?.Invoke(character);
        }

        /// <summary>
        /// M7：死亡掉落落地。先 <see cref="NpcInventory.ExportAll"/>（背包 + 三个装备槽）导出，
        /// 再 <see cref="NpcInventory.ClearAll"/> 并把 <see cref="CharacterData.Gold"/> 清零，保证掉落只发生一次。
        /// 掉落物<strong>无归属</strong>：任何角色走到同一格都能用 <see cref="PickupLoot"/> 拿走。
        /// 背包与金币都空时不产生空掉落包。
        /// </summary>
        private void DropLoot(CharacterData character)
        {
            System.Collections.Generic.Dictionary<int, int> items = character.Bag.ExportAll();
            int gold = character.Gold;

            character.Bag.ClearAll();
            character.Gold = 0;

            if (items.Count == 0 && gold <= 0)
            {
                return;
            }

            GroundLoot.Add(new LootBag
            {
                Pos = character.Position,
                Items = items,
                Gold = gold,
            });
        }

        /// <summary>
        /// M7：拾取地面掉落。校验同坐标（<c>bag.Pos == c.Position</c>）与拾取者存活，
        /// 成功后把物品并入 <c>c.Bag</c>、金币并入 <c>c.Gold</c>，并把该 <see cref="LootBag"/> 移出 <see cref="GroundLoot"/>。
        ///
        /// 返回值：true=拾取成功；false=参数非法 / 拾取者已死 / 不在同一坐标 / 该掉落已不在 <see cref="GroundLoot"/> 里。
        /// （行为案：死亡背包物品可被任何角色拾取，故不校验归属者。）
        /// </summary>
        public bool PickupLoot(CharacterData c, LootBag bag)
        {
            if (c == null || bag == null || !c.IsAlive)
            {
                return false;
            }

            if (bag.Pos != c.Position)
            {
                return false;
            }

            // 已被别人捡走（或本就不在世界里）的掉落不再结算，避免同一个包被重复入账。
            if (!GroundLoot.Contains(bag))
            {
                return false;
            }

            foreach (System.Collections.Generic.KeyValuePair<int, int> pair in bag.Items)
            {
                c.Bag.Add(pair.Key, pair.Value);
            }

            c.Gold += bag.Gold;

            bag.Items.Clear();
            bag.Gold = 0;
            GroundLoot.Remove(bag);
            return true;
        }
    }
}
