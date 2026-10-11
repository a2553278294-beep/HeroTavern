using System;

namespace GameLogic.World
{
    /// <summary>
    /// 网格坐标（R2：网格式地图；交互/战斗/交易要求同坐标）。
    /// </summary>
    public readonly struct GridPos : IEquatable<GridPos>
    {
        /// <summary>场地编号（1=城镇，2-6=野外，见 <see cref="WorldMapIds"/>）。</summary>
        public readonly int MapId;
        public readonly int X;
        public readonly int Y;

        public GridPos(int mapId, int x, int y)
        {
            MapId = mapId;
            X = x;
            Y = y;
        }

        /// <summary>曼哈顿距离（四方向移动下的最短步数下界）。</summary>
        public int ManhattanDistanceTo(GridPos other)
        {
            if (other.MapId != MapId)
            {
                return int.MaxValue;
            }

            return Math.Abs(other.X - X) + Math.Abs(other.Y - Y);
        }

        public bool Equals(GridPos other)
        {
            return MapId == other.MapId && X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is GridPos other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MapId;
                hash = (hash * 397) ^ X;
                hash = (hash * 397) ^ Y;
                return hash;
            }
        }

        public static bool operator ==(GridPos left, GridPos right) => left.Equals(right);

        public static bool operator !=(GridPos left, GridPos right) => !left.Equals(right);

        public override string ToString()
        {
            return $"(map:{MapId}, {X}, {Y})";
        }
    }
}
