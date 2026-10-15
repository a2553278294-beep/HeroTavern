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

        private readonly Dictionary<int, GridMap> _maps = new Dictionary<int, GridMap>();
        private readonly List<int> _activeSpawnAreas = new List<int>();

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

        private void Register(GridMap map)
        {
            _maps.Add(map.MapId, map);
        }
    }
}
