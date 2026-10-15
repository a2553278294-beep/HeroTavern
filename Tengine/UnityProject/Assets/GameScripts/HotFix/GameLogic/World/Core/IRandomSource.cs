namespace GameLogic.World
{
    /// <summary>
    /// 随机源抽象：注入种子化实现以保证生成结果可复现（测试与回放）。
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>返回 [minInclusive, maxExclusive) 的整数。</summary>
        int Next(int minInclusive, int maxExclusive);
    }

    /// <summary>
    /// 基于 System.Random 的种子化随机源。
    /// </summary>
    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly System.Random _random;

        public SystemRandomSource(int seed)
        {
            _random = new System.Random(seed);
        }

        public int Next(int minInclusive, int maxExclusive)
        {
            return _random.Next(minInclusive, maxExclusive);
        }
    }
}
