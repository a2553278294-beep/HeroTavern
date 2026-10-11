using System.Collections.Generic;

namespace GameLogic.World
{
    /// <summary>
    /// 网格地图中的建筑（R2：建筑坐标；坐标表落地前由代码登记）。
    /// 默认不阻挡移动：交互规则要求角色与建筑同坐标，角色需能站上建筑格。
    /// </summary>
    public sealed class MapBuilding
    {
        public string Id;
        public string Name;
        public GridPos Pos;
        public bool BlocksMovement;

        public MapBuilding(string id, string name, GridPos pos, bool blocksMovement = false)
        {
            Id = id;
            Name = name;
            Pos = pos;
            BlocksMovement = blocksMovement;
        }
    }

    /// <summary>
    /// 单张 9×9 网格地图（R2）。四方向移动，BFS 最短路径。
    /// </summary>
    public sealed class GridMap
    {
        public const int Width = 9;
        public const int Height = 9;

        private static readonly (int dx, int dy)[] Directions = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        private readonly bool[,] _blocked = new bool[Width, Height];
        private readonly List<MapBuilding> _buildings = new List<MapBuilding>();

        public int MapId { get; }
        public string Name { get; }
        public IReadOnlyList<MapBuilding> Buildings => _buildings;

        public GridMap(int mapId, string name)
        {
            MapId = mapId;
            Name = name;
        }

        public bool IsInside(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        public bool IsWalkable(int x, int y)
        {
            return IsInside(x, y) && !_blocked[x, y];
        }

        public void SetBlocked(int x, int y, bool blocked)
        {
            if (IsInside(x, y))
            {
                _blocked[x, y] = blocked;
            }
        }

        public void AddBuilding(MapBuilding building)
        {
            _buildings.Add(building);
            if (building.BlocksMovement)
            {
                SetBlocked(building.Pos.X, building.Pos.Y, true);
            }
        }

        public MapBuilding FindBuilding(string id)
        {
            for (int i = 0; i < _buildings.Count; i++)
            {
                if (_buildings[i].Id == id)
                {
                    return _buildings[i];
                }
            }

            return null;
        }

        /// <summary>
        /// BFS 最短路径。返回值不含起点、含终点；起点即终点返回空表；不可达返回 null。
        /// 终点允许是不可行走格（例如阻挡建筑的格子）时按不可达处理。
        /// extraBlocked：动态障碍（如其他角色占用的格子），与静态阻挡一并参与寻路。
        /// </summary>
        public List<GridPos> FindPath(GridPos from, GridPos to, ISet<GridPos> extraBlocked = null)
        {
            if (from.MapId != MapId || to.MapId != MapId)
            {
                return null;
            }

            if (from == to)
            {
                return new List<GridPos>();
            }

            if (!IsWalkable(to.X, to.Y) || !IsWalkable(from.X, from.Y)
                || (extraBlocked != null && (extraBlocked.Contains(to) || extraBlocked.Contains(from))))
            {
                return null;
            }

            var visited = new bool[Width, Height];
            var parent = new GridPos[Width, Height];
            var queue = new Queue<GridPos>();
            visited[from.X, from.Y] = true;
            queue.Enqueue(from);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                for (int i = 0; i < Directions.Length; i++)
                {
                    int nx = cur.X + Directions[i].dx;
                    int ny = cur.Y + Directions[i].dy;
                    if (!IsWalkable(nx, ny) || visited[nx, ny]
                        || (extraBlocked != null && extraBlocked.Contains(new GridPos(MapId, nx, ny))))
                    {
                        continue;
                    }

                    visited[nx, ny] = true;
                    parent[nx, ny] = cur;
                    var next = new GridPos(MapId, nx, ny);
                    if (nx == to.X && ny == to.Y)
                    {
                        return Reconstruct(parent, from, next);
                    }

                    queue.Enqueue(next);
                }
            }

            return null;
        }

        /// <summary>
        /// 朝目标走一格（R2：角色每小时移动 1 格，自动使用最短路径）。
        /// </summary>
        public bool TryStepTowards(GridPos from, GridPos to, out GridPos next, ISet<GridPos> extraBlocked = null)
        {
            var path = FindPath(from, to, extraBlocked);
            if (path == null || path.Count == 0)
            {
                next = from;
                return false;
            }

            next = path[0];
            return true;
        }

        private static List<GridPos> Reconstruct(GridPos[,] parent, GridPos from, GridPos to)
        {
            var path = new List<GridPos>();
            var cur = to;
            while (cur != from)
            {
                path.Add(cur);
                cur = parent[cur.X, cur.Y];
            }

            path.Reverse();
            return path;
        }
    }
}
