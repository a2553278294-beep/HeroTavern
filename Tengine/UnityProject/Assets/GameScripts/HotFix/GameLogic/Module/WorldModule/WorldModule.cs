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

        /// <summary>以固定种子开始新对局（可复现，用于测试与回放）。</summary>
        public void StartNewGame(int seed)
        {
            UnhookEvents();
            Sim = WorldSimulation.CreateNew(seed);
            Sim.StartNewGame();
            HookEvents();
            Log.Info($"WorldModule 新对局开始，seed={seed}，人口={Sim.Population.Residents.Count}，货币={Sim.Wallet.Currency}");
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
    }
}
