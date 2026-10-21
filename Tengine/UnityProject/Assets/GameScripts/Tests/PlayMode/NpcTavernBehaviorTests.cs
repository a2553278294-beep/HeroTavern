using System.Collections.Generic;
using GameLogic.World;
using NUnit.Framework;

namespace GameLogic.Tests
{
    /// <summary>
    /// 酒馆交互行为回归（「角色仅在待机、无交互行为」修复实证）：
    /// 纯逻辑层驱动 WorldSimulation（不开场景），覆盖三条修复口径：
    /// 1. 上架有货时 NPC 应能点单（首单死锁：锁定前 RemainingCountOf 恒 0）；
    /// 2. 体力耗尽回城待机后体力应能持续恢复（待机 +1 与每小时 -1 抵消的旧口径下永远锁死）；
    /// 3. 待机应在城镇中心格执行，而不是原地（兜底路径）。
    /// </summary>
    public sealed class NpcTavernBehaviorTests
    {
        private static WorldSimulation CreateSim()
        {
            var sim = WorldSimulation.CreateNew(20261020);
            sim.StartNewGame();
            return sim;
        }

        /// <summary>推进 gameMinutes 个游戏分钟（一档 6 分钟/秒，直接喂分钟粒度）。</summary>
        private static void AdvanceMinutes(WorldSimulation sim, int gameMinutes)
        {
            sim.SetSpeedLevel(1); // 1x：Tick(1/6f) ≈ 1 游戏分钟，分钟级推进最直观
            for (int i = 0; i < gameMinutes; i++)
            {
                sim.Tick(1f / 6f);
            }
        }

        private static void StockShelf(WorldSimulation sim, int aleCount)
        {
            Assert.IsTrue(sim.Tavern.SetShelf(new List<int> { ItemCatalog.WineAle, ItemCatalog.WineWhisky }),
                "上架初始已解锁的麦酒/威士忌应成功");
            Assert.IsTrue(sim.Tavern.SetPlannedCount(ItemCatalog.WineAle, aleCount), "设定麦酒规划份数应成功");
            Assert.IsTrue(sim.Tavern.SetPlannedCount(ItemCatalog.WineWhisky, 3), "设定威士忌规划份数应成功");
        }

        [Test]
        public void NpcOrdersDrinkWhenShelfStocked()
        {
            var sim = CreateSim();
            StockShelf(sim, 10);

            int paidCount = 0;
            sim.DrinkPaid += (guest, wineId, price) => paidCount++;

            // 精神降到 50：点单收益 = 精神 +10 − 售价 + 信息 +1 为正值（行为案权重口径）。
            foreach (var c in All(sim))
            {
                c.Spirit = 50;
            }

            AdvanceMinutes(sim, 24 * 60); // 一个完整游戏日

            Assert.Greater(paidCount, 0,
                "上架有货且点单收益为正时，NPC 应能完成至少一次点单（首单不应被「锁定前剩余份数恒 0」卡死）");
        }

        [Test]
        public void ExhaustedNpcRecoversStaminaWhileResting()
        {
            var sim = CreateSim();
            var npc = FirstEligible(sim);
            npc.Stamina = 0; // 体力耗尽 → 强制回城待机

            AdvanceMinutes(sim, 12 * 60); // 12 个游戏小时

            Assert.GreaterOrEqual(npc.Stamina, 6,
                "待机每小时 +1 体力，12 小时后体力应显著恢复（旧口径被每小时 -1 抵消，体力永远卡在 0/1）");
        }

        [Test]
        public void RestingNpcRecoversToFullAndResumesActivity()
        {
            var sim = CreateSim();
            var npc = FirstEligible(sim);
            npc.Stamina = 0;

            // 判据：体力曾回满（+1/小时持续待机）且回满后脱离待机恢复日常决策
            // （回满后角色会复工、体力自然回落，不能断言终点时刻仍为满值）。
            int peak = 0;
            bool resumedAfterFull = false;
            bool reachedFull = false;
            for (int h = 0; h < 120; h++) // 5 个游戏日，足够 +1/小时回满 100
            {
                AdvanceMinutes(sim, 60);
                if (npc.Stamina > peak)
                {
                    peak = npc.Stamina;
                }

                var action = sim.Behavior.CurrentActionOf(npc.Id);
                bool resting = action != null && action.Kind == BehaviorKind.RestAtTown;
                if (npc.Stamina >= CharacterBehaviorSystem.MaxStamina)
                {
                    reachedFull = true;
                }
                else if (reachedFull && !resting)
                {
                    resumedAfterFull = true;
                }
            }

            Assert.AreEqual(CharacterBehaviorSystem.MaxStamina, peak,
                "待机应能持续到体力回满（策划案「直至体力回满」）");
            Assert.IsTrue(resumedAfterFull, "体力回满后应恢复日常决策，不再保持待机");
        }

        [Test]
        public void ForcedRestMovesNpcToTownCell()
        {
            var sim = CreateSim();
            var npc = FirstEligible(sim);
            var map = sim.Map.GetMap(WorldMapIds.Town);
            var townCell = WorldMap.TownCenter;

            // 待机落点 ≈ 城镇中心的可行走格（TownCenter 自身可能不可走，FindPath 以它为终点恒失败）。
            GridPos restTarget = townCell;
            for (int x = 0; x < GridMap.Width; x++)
            {
                for (int y = 0; y < GridMap.Height; y++)
                {
                    var cell = new GridPos(WorldMapIds.Town, x, y);
                    if (map.IsWalkable(x, y) && townCell.ManhattanDistanceTo(cell) < townCell.ManhattanDistanceTo(restTarget))
                    {
                        restTarget = cell;
                    }
                }
            }

            // 放到「能真正寻路回城」的远处可行走格（排除不可达口袋区域），再耗尽体力。
            GridPos far = npc.Position;
            for (int x = GridMap.Width - 1; x >= 0; x--)
            {
                for (int y = GridMap.Height - 1; y >= 0; y--)
                {
                    var cell = new GridPos(WorldMapIds.Town, x, y);
                    if (map.IsWalkable(x, y) && townCell.ManhattanDistanceTo(cell) >= 5)
                    {
                        var path = map.FindPath(cell, restTarget);
                        if (path != null && path.Count > 0)
                        {
                            far = cell;
                        }
                    }
                }
            }

            npc.Position = far;
            npc.Stamina = 0;

            AdvanceMinutes(sim, 12 * 60); // 城镇中心拥挤时可能整点等待，放宽到 12 小时看位移趋势

            Assert.Less(npc.Position.ManhattanDistanceTo(townCell), far.ManhattanDistanceTo(townCell),
                "强制待机应把角色带回城镇中心方向（待机应在城镇中执行，而不是原地）");
        }

        [Test]
        public void NpcsMoveOverHours()
        {
            var sim = CreateSim();
            var before = new Dictionary<int, GridPos>();
            foreach (var c in All(sim))
            {
                before[c.Id] = c.Position;
            }

            AdvanceMinutes(sim, 6 * 60); // 6 个游戏小时

            int moved = 0;
            foreach (var c in All(sim))
            {
                if (before.TryGetValue(c.Id, out var pos) && !pos.Equals(c.Position))
                {
                    moved++;
                }
            }

            Assert.Greater(moved, 0, "数小时内至少一名 NPC 应发生移动（闲逛兜底保证候选集非空）");
        }

        private static CharacterData FirstEligible(WorldSimulation sim)
        {
            foreach (var c in All(sim))
            {
                if (c.IsAlive && !c.IsHidden)
                {
                    return c;
                }
            }

            Assert.Fail("初始人口应有可行动角色");
            return null;
        }

        private static List<CharacterData> All(WorldSimulation sim)
        {
            var list = new List<CharacterData>();
            list.AddRange(sim.Population.Residents);
            list.AddRange(sim.Population.Visitors);
            return list;
        }
    }
}
