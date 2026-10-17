using System;
using GameLogic.World;
using TEngine;

namespace GameLogic
{
    /// <summary>
    /// 世界模拟事件 ID（逻辑层字符串事件；UI 侧监听需先 RuntimeId.ToRuntimeId 后用 int 注册）。
    /// </summary>
    public static class WorldEventIds
    {
        /// <summary>每游戏分钟。参数：dayOverall, hour, minute。</summary>
        public const string MinuteChanged = "World.MinuteChanged";

        /// <summary>每跨越整点。参数：dayOverall, hour。</summary>
        public const string HourChanged = "World.HourChanged";

        /// <summary>跨越 0:00（结算时刻，天数 +1）。参数：newDayOverall。</summary>
        public const string DaySettled = "World.DaySettled";

        /// <summary>跨越新年。参数：newYear。</summary>
        public const string YearChanged = "World.YearChanged";

        /// <summary>角色移动一格。参数：CharacterData。</summary>
        public const string CharacterMoved = "World.CharacterMoved";

        /// <summary>角色死亡。参数：CharacterData。</summary>
        public const string CharacterDied = "World.CharacterDied";

        /// <summary>货币变动。参数：currency。</summary>
        public const string CurrencyChanged = "World.CurrencyChanged";

        /// <summary>0:00 结算完成。参数：SettlementReport。</summary>
        public const string SettlementReady = "World.SettlementReady";

        /// <summary>结算后余额为负，游戏结束。</summary>
        public const string GameOver = "World.GameOver";

        /// <summary>怪物刷新。参数：Monster。</summary>
        public const string MonsterSpawned = "World.MonsterSpawned";

        /// <summary>客人付费。参数：guest, wineId, price。</summary>
        public const string DrinkPaid = "World.DrinkPaid";

        /// <summary>情报线索落定（玩家入账 / NPC 入账 / 系统池暂存）。参数：IntelRecord。</summary>
        public const string IntelObtained = "World.IntelObtained";

        /// <summary>悬赏发布成功（已扣发布费与线索、已占槽）。参数：BountyEntry。</summary>
        public const string BountyPublished = "World.BountyPublished";

        /// <summary>悬赏目标达成（已完成待入账，等下一次 0:00 入账）。参数：BountyEntry。</summary>
        public const string BountyCompleted = "World.BountyCompleted";

        /// <summary>悬赏限时到 0 未达成判失败（当次 0:00 清槽、发布费已返还）。参数：BountyEntry, refundGold。</summary>
        public const string BountyFailed = "World.BountyFailed";

        /// <summary>NPC 对玩家的好感变化（M8，由 <c>WorldSimulation.ChangeFavor</c> 触发）。参数：CharacterData, favor（变化后的好感值）。</summary>
        public const string FavorChanged = "World.FavorChanged";
    }

    /// <summary>
    /// 世界模拟桥接模块：持有纯逻辑 <see cref="WorldSimulation"/>，向 GameEvent 转发事件。
    /// 时间驱动由外层每帧调用 <see cref="Tick"/>（驱动挂接随经营主界面任务落地）；
    /// 失去焦点自动暂停（R1）由驱动层调用 <see cref="HandleApplicationFocus"/>。
    /// </summary>
    public sealed class WorldModule : Singleton<WorldModule>
    {
        /// <summary>当前世界模拟实例；StartNewGame 后可用。</summary>
        public WorldSimulation Sim { get; private set; }

        /// <summary>以随机种子开始新对局。</summary>
        public void StartNewGame()
        {
            StartNewGame(Environment.TickCount);
        }

        /// <summary>
        /// M13：重开一局（破产后的「再来一次」入口）。
        /// 口径就是 <see cref="StartNewGame(int)"/> 的两步：<b>先 <see cref="UnhookEvents"/> 解掉旧实例的全部订阅</b>
        /// （时间 / 人口 / 结算 / 悬赏 / 钱包事件都是实例上的委托，不解绑会让旧对局继续往 GameEvent 里灌数据，
        /// 而 <c>GameEvent</c> 的监听方（HUD 等）拿到的是早已作废的旧实例），
        /// 再 <see cref="StartNewGame(int)"/> 用随机种子重建世界。
        /// 配置引导（<see cref="EnsureConfigReady"/>）由 <c>StartNewGame</c> 内部完成，重开与开局走同一条路径。
        /// 注意：<c>Sim</c> 是<b>新实例</b>，持有旧实例的展示层需自行换绑
        /// （HUD 侧由 <c>WorldHud.EnsureSimHooked</c> 每帧检出并重绑）。
        /// </summary>
        public void Restart()
        {
            UnhookEvents();
            Sim = null;
            StartNewGame(Environment.TickCount);
        }

        /// <summary>以固定种子开始新对局（可复现，用于测试与回放）。</summary>
        public void StartNewGame(int seed)
        {
            UnhookEvents();

            // 配置必须先于 WorldSimulation 就绪：逻辑层的物品 / 配方 / 怪物 / 全局参数全部来自配置目录。
            EnsureConfigReady();

            Sim = WorldSimulation.CreateNew(seed);
            Sim.StartNewGame();

            // M3：注入配置里的怪物目录（TbMonster → GameConfigCatalog）。
            // null 安全：目录未 Init 或表缺失时 GetMonsterCatalog() 返回空数组，ReloadCatalog 内部保留内置占位目录。
            Sim.Monsters.ReloadCatalog(GameConfigCatalog.Instance?.GetMonsterCatalog());

            HookEvents();
            Log.Info($"WorldModule 新对局开始，seed={seed}，人口={Sim.Population.Residents.Count}，货币={Sim.Wallet.Currency}，怪物目录={Sim.Monsters.Catalog.Count}，安全分={Sim.Safety.Value}");
        }

        /// <summary>
        /// 配置引导：确保配置表已加载，并把目录注入纯逻辑层。
        ///
        /// 加载入口复用 GameProto 已有的 <see cref="ConfigSystem"/>：<c>ConfigSystem.Instance.Tables</c>
        /// 未初始化时会同步调用 <c>Load()</c> 构造 <see cref="GameConfig.Tables"/>，其内部只登记 loader，
        /// 每张表的字节流在首次访问对应属性时才惰性读取（见 GameConfig/Tables.cs）。
        /// 因此这里**不需要异步等待**：Tables 实例拿到即代表加载管线已就绪，可直接交给逻辑层。
        ///
        /// 假设（最小同步封装的前提）：
        /// 1. <c>IResourceModule</c> 已在 TEngine 启动流程中注册，ConfigSystem 的 LoadByteBuf 可用；
        /// 2. 资源以 TextAsset 形式存在于 <c>Resources/Config</c>，加载路径由 IResourceModule 解析。
        /// 若假设不成立，ConfigSystem.LoadByteBuf 会抛异常并向上传播，交由游戏启动流程中断/重试，
        /// 这里刻意不吞异常——配置缺失时逻辑层只会静默回退到占位常量，反而更难排查。
        /// </summary>
        private static void EnsureConfigReady()
        {
            // 同步获取 Tables（首次访问时触发 ConfigSystem.Load）。
            GameConfig.Tables tables = ConfigSystem.Instance.Tables;

            // 绑定运行时目录并重建物品 / 配方索引（两者可重复调用，重开对局即重建）。
            GameConfigCatalog.Init(tables);
            ItemCatalog.ReloadFromConfig();

            Log.Info($"配置目录已注入：物品={ItemCatalog.Items.Count}，配方={ItemCatalog.Recipes.Count}，目录已初始化={GameConfigCatalog.IsInitialized}");
        }

        /// <summary>每帧驱动（真实秒）。暂停或五档加速由 Sim 内部处理。</summary>
        public void Tick(float deltaTime)
        {
            Sim?.Tick(deltaTime);
        }

        public void SetPaused(bool paused)
        {
            Sim?.SetPaused(paused);
        }

        /// <summary>设置加速档位（1-5，基准 1 真实秒 = 1 游戏分钟）。</summary>
        public void SetSpeedLevel(int level)
        {
            Sim?.SetSpeedLevel(level);
        }

        /// <summary>R1：失去酒馆焦点时自动暂停。</summary>
        public void HandleApplicationFocus(bool hasFocus)
        {
            if (Sim == null || !Sim.IsRunning)
            {
                return;
            }

            if (!hasFocus)
            {
                Sim.SetPaused(true);
            }
        }

        protected override void OnRelease()
        {
            UnhookEvents();
            Sim = null;
        }

        private void HookEvents()
        {
            Sim.MinuteChanged += OnMinuteChanged;
            Sim.HourChanged += OnHourChanged;
            Sim.DaySettled += OnDaySettled;
            Sim.YearChanged += OnYearChanged;
            Sim.CharacterMoved += OnCharacterMoved;
            Sim.CharacterDied += OnCharacterDied;
            Sim.SettlementReady += OnSettlementReady;
            Sim.GameOver += OnGameOver;
            Sim.MonsterSpawned += OnMonsterSpawned;
            Sim.DrinkPaid += OnDrinkPaid;
            Sim.IntelObtained += OnIntelObtained;
            // M8：好感变化转发（交易赠予 / 索取、对话好感等统一走 WorldSimulation.ChangeFavor）。
            Sim.FavorChanged += OnFavorChanged;
            // M5：悬赏三事件（发布 / 完成 / 失败返还）转发；Bounty 未就绪时跳过（组合根在 StartNewGame 内创建）。
            if (Sim.Bounty != null)
            {
                Sim.Bounty.Published += OnBountyPublished;
                Sim.Bounty.Completed += OnBountyCompleted;
                Sim.Bounty.Failed += OnBountyFailed;
            }

            Sim.Wallet.Changed += OnCurrencyChanged;
        }

        private void UnhookEvents()
        {
            if (Sim == null)
            {
                return;
            }

            Sim.MinuteChanged -= OnMinuteChanged;
            Sim.HourChanged -= OnHourChanged;
            Sim.DaySettled -= OnDaySettled;
            Sim.YearChanged -= OnYearChanged;
            Sim.CharacterMoved -= OnCharacterMoved;
            Sim.CharacterDied -= OnCharacterDied;
            Sim.SettlementReady -= OnSettlementReady;
            Sim.GameOver -= OnGameOver;
            Sim.MonsterSpawned -= OnMonsterSpawned;
            Sim.DrinkPaid -= OnDrinkPaid;
            Sim.IntelObtained -= OnIntelObtained;
            Sim.FavorChanged -= OnFavorChanged;
            if (Sim.Bounty != null)
            {
                Sim.Bounty.Published -= OnBountyPublished;
                Sim.Bounty.Completed -= OnBountyCompleted;
                Sim.Bounty.Failed -= OnBountyFailed;
            }

            if (Sim.Wallet != null)
            {
                Sim.Wallet.Changed -= OnCurrencyChanged;
            }
        }

        private static void OnMinuteChanged(int dayOverall, int hour, int minute)
        {
            GameEvent.Send(WorldEventIds.MinuteChanged, dayOverall, hour, minute);
        }

        private static void OnHourChanged(int dayOverall, int hour)
        {
            GameEvent.Send(WorldEventIds.HourChanged, dayOverall, hour);
        }

        private static void OnDaySettled(int newDayOverall)
        {
            GameEvent.Send(WorldEventIds.DaySettled, newDayOverall);
        }

        private static void OnYearChanged(int newYear)
        {
            GameEvent.Send(WorldEventIds.YearChanged, newYear);
        }

        private static void OnCharacterMoved(CharacterData character)
        {
            GameEvent.Send(WorldEventIds.CharacterMoved, character);
        }

        private static void OnCharacterDied(CharacterData character)
        {
            GameEvent.Send(WorldEventIds.CharacterDied, character);
        }

        private static void OnCurrencyChanged(int currency)
        {
            GameEvent.Send(WorldEventIds.CurrencyChanged, currency);
        }

        private static void OnSettlementReady(SettlementReport report)
        {
            GameEvent.Send(WorldEventIds.SettlementReady, report);
        }

        private static void OnGameOver()
        {
            GameEvent.Send(WorldEventIds.GameOver);
        }

        private static void OnMonsterSpawned(Monster monster)
        {
            GameEvent.Send(WorldEventIds.MonsterSpawned, monster);
        }

        private static void OnDrinkPaid(CharacterData guest, int wineId, int price)
        {
            GameEvent.Send(WorldEventIds.DrinkPaid, guest, wineId, price);
        }

        private static void OnIntelObtained(IntelRecord record)
        {
            GameEvent.Send(WorldEventIds.IntelObtained, record);
        }

        private static void OnBountyPublished(BountyEntry entry)
        {
            GameEvent.Send(WorldEventIds.BountyPublished, entry);
        }

        private static void OnBountyCompleted(BountyEntry entry)
        {
            GameEvent.Send(WorldEventIds.BountyCompleted, entry);
        }

        private static void OnBountyFailed(BountyEntry entry, int refundGold)
        {
            GameEvent.Send(WorldEventIds.BountyFailed, entry, refundGold);
        }

        private static void OnFavorChanged(CharacterData npc, int favor)
        {
            GameEvent.Send(WorldEventIds.FavorChanged, npc, favor);
        }
    }
}
