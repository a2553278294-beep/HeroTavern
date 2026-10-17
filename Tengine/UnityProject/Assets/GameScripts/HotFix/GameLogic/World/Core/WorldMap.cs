using System.Collections.Generic;

namespace GameLogic.World
{
    /// <summary>
    /// 场地编号（R2/边缘系统：1=城镇，2-6=野外）。
    /// </summary>
    public static class WorldMapIds
    {
        public const int Town = 1;
        public const int BlackPineForest = 2;
        public const int StoneFangMine = 3;
        public const int MistSwamp = 4;
        public const int BoneTomb = 5;
        public const int ObsidianCastle = 6;
    }

    /// <summary>
    /// 世界地图集合（R2：6 个 9×9 场地；建筑坐标表落地前由代码登记）。
    /// 跨图移动规则文档未定义，本期仅支持图内移动（任务文档决策 3）。
    /// </summary>
    public sealed class WorldMap
    {
        /// <summary>酒馆在城镇图中的坐标（map 表落地前的占位值）。</summary>
        public static readonly GridPos TavernPos = new GridPos(WorldMapIds.Town, 4, 4);

        /// <summary>城镇中心（初始人口出生点）。</summary>
        public static readonly GridPos TownCenter = new GridPos(WorldMapIds.Town, 4, 5);

        /// <summary>厨房坐标（经营案：点击厨房生成采购/调酒两个小型 UI；map 表落地前占位）。</summary>
        public static readonly GridPos KitchenPos = new GridPos(WorldMapIds.Town, 2, 4);

        /// <summary>悬赏布告栏坐标（立项：门外悬赏布告栏；map 表落地前占位）。</summary>
        public static readonly GridPos BountyBoardPos = new GridPos(WorldMapIds.Town, 6, 4);

        /// <summary>吧台位（建筑阻挡移动后，客人走到此格点单/交互；占位待 map 表）。</summary>
        public static readonly GridPos TavernBarPos = new GridPos(WorldMapIds.Town, 5, 4);

        /// <summary>6 张桌子占位坐标（立项：6 桌 12 座；桌椅位置读全局参数，表缺失前占位）。</summary>
        public static readonly GridPos[] TablePositions =
        {
            new GridPos(WorldMapIds.Town, 1, 2), new GridPos(WorldMapIds.Town, 3, 2), new GridPos(WorldMapIds.Town, 5, 2),
            new GridPos(WorldMapIds.Town, 1, 6), new GridPos(WorldMapIds.Town, 3, 6), new GridPos(WorldMapIds.Town, 5, 6),
        };

        /// <summary>
        /// 城镇场景不可站立格（按底图 sence_base_01 的 9×9 网格叠加分析标定，建筑格之外补充）：
        /// - y=0/1 整行：后墙、吧台后柜台、厨房台面、门楣与木桶堆；
        /// - y=2/3 整行：吧台正面与桌台带（门口本期不开放，门外悬赏板交互走相邻服务格）；
        /// - (4,5)/(4,6)：大堂圆桌；x=8 列 y=4~8：门外花坛/角落木桶（户外装饰区）。
        /// 全图（. 可行走 / # 阻挡；K=厨房 T=吧台 B=悬赏板 S=吧凳为建筑格，由 AddBuilding 标记）：
        ///   x→  0 1 2 3 4 5 6 7 8
        ///  y=0  # # # # # # # # #
        ///  y=1  # # # # # # # # #
        ///  y=2  # T # T # T # # #
        ///  y=3  # # # # # # # # #
        ///  y=4  # . K . T . B . #
        ///  y=5  . . . . # . . . #
        ///  y=6  . S . S # S . . #
        ///  y=7  . . . . . . . . #
        ///  y=8  . . . . . . . . #
        /// </summary>
        private static readonly (int X, int Y)[] TownSceneBlocked =
        {
            (0, 0), (1, 0), (2, 0), (3, 0), (4, 0), (5, 0), (6, 0), (7, 0), (8, 0),
            (0, 1), (1, 1), (2, 1), (3, 1), (4, 1), (5, 1), (6, 1), (7, 1), (8, 1),
            (0, 2), (2, 2), (4, 2), (6, 2), (7, 2), (8, 2),
            (0, 3), (1, 3), (2, 3), (3, 3), (4, 3), (5, 3), (6, 3), (7, 3), (8, 3),
            (0, 4), (8, 4),
            (4, 5), (8, 5),
            (4, 6), (8, 6),
            (8, 7),
            (8, 8),
        };

        private readonly Dictionary<int, GridMap> _maps = new Dictionary<int, GridMap>();
        private readonly List<int> _activeSpawnAreas = new List<int>();

        /// <summary>M3：池阶段 1（黑松林已随初始世界解锁）；阶段 N 依次解锁场地 N。</summary>
        public const int FirstPoolStage = 1;

        /// <summary>
        /// M3：池阶段 <see cref="FirstPoolStage"/> 对应的场地号（= <see cref="WorldMapIds.BlackPineForest"/>）。
        /// 场地号换算：<c>场地号 = FirstPoolStageSpawnArea + (阶段 - FirstPoolStage)</c>。
        /// </summary>
        public const int FirstPoolStageSpawnArea = WorldMapIds.BlackPineForest;

        /// <summary>当前已解锁的怪物刷新区域（R2：初始 1 个，随进程增加）。</summary>
        public IReadOnlyList<int> ActiveSpawnAreas => _activeSpawnAreas;

        private WorldMap()
        {
        }

        /// <summary>
        /// 创建初始世界：6 张 9×9 地图，酒馆登记在城镇，初始怪物刷新区域为黑松林。
        /// </summary>
        public static WorldMap CreateInitial()
        {
            var world = new WorldMap();
            world.Register(new GridMap(WorldMapIds.Town, "城镇"));
            world.Register(new GridMap(WorldMapIds.BlackPineForest, "黑松林"));
            world.Register(new GridMap(WorldMapIds.StoneFangMine, "石牙矿洞"));
            world.Register(new GridMap(WorldMapIds.MistSwamp, "沉雾沼泽"));
            world.Register(new GridMap(WorldMapIds.BoneTomb, "白骨古墓"));
            world.Register(new GridMap(WorldMapIds.ObsidianCastle, "黑曜城堡"));

            var town = world.GetMap(WorldMapIds.Town);
            // 建筑/桌子均为障碍物：阻挡寻路与移动；交互在相邻服务格（如吧台位）进行。
            town.AddBuilding(new MapBuilding("tavern", "酒馆", TavernPos, true));
            town.AddBuilding(new MapBuilding("kitchen", "厨房", KitchenPos, true));
            town.AddBuilding(new MapBuilding("bounty_board", "悬赏布告栏", BountyBoardPos, true));
            for (int i = 0; i < TablePositions.Length; i++)
            {
                town.AddBuilding(new MapBuilding($"table_{i}", "桌子", TablePositions[i], true));
            }

            // 场景不可站立区（墙面/柜台/圆桌/户外装饰，见 TownSceneBlocked 标定表）：
            // 阻挡寻路与移动，也避免出生散开/闲逛落点选到这些格子。
            for (int i = 0; i < TownSceneBlocked.Length; i++)
            {
                town.SetBlocked(TownSceneBlocked[i].X, TownSceneBlocked[i].Y, true);
            }

            world._activeSpawnAreas.Add(WorldMapIds.BlackPineForest);
            return world;
        }

        public GridMap GetMap(int mapId)
        {
            return _maps.TryGetValue(mapId, out var map) ? map : null;
        }

        /// <summary>解锁新的怪物刷新区域。</summary>
        public void UnlockSpawnArea(int mapId)
        {
            if (_maps.ContainsKey(mapId) && !_activeSpawnAreas.Contains(mapId))
            {
                _activeSpawnAreas.Add(mapId);
            }
        }

        /// <summary>
        /// M3：按怪物池阶段（<see cref="MonsterSystem.PoolStage"/>，每 <see cref="MonsterSystem.DaysPerStage"/> 个经营日 +1）
        /// 顺序解锁刷新场地。由 WorldSimulation 在 0:00 日结、<see cref="MonsterSystem.DailySpawn"/> 之前调用。
        ///
        /// 占位规则（待策划确认，场地号见 <see cref="WorldMapIds"/>）：
        /// 阶段 1 → 场地 2 黑松林（随 <see cref="CreateInitial"/> 解锁，调用本方法为幂等空转）；
        /// 阶段 2 → 场地 3 石牙矿洞；阶段 3 → 场地 4 沉雾沼泽；
        /// 阶段 4 → 场地 5 白骨古墓；阶段 5 → 场地 6 黑曜城堡。即「阶段 N → 场地号 N+1」。
        /// 阶段继续提升（&gt;5）时无更多场地，<see cref="UnlockSpawnArea"/> 内部按「未注册场地」忽略。
        /// 本方法可每天重复调用：已解锁场地不会重复加入。
        /// </summary>
        public void UnlockSpawnAreaByPoolStage(int poolStage)
        {
            for (int stage = FirstPoolStage; stage <= poolStage; stage++)
            {
                UnlockSpawnArea(FirstPoolStageSpawnArea + (stage - FirstPoolStage));
            }
        }

        private void Register(GridMap map)
        {
            _maps.Add(map.MapId, map);
        }
    }
}
