using System;

namespace GameLogic.World
{
    /// <summary>
    /// 城镇安全分（M3，纯逻辑，无 UnityEngine 依赖）。
    ///
    /// 口径：
    /// - 初值 <see cref="InitialSafety"/>=100，取值区间 [<see cref="MinSafety"/>, <see cref="MaxSafety"/>]=[0,100]；
    /// - 怪物杀死居民 / 狩猎怪物进入城镇等事件通过 <see cref="Change"/> 增减（常量为占位，待策划定档）；
    /// - 安全分与精神分联动：「城镇安全分每失去 1 点，精神分 -1」，由订阅方
    ///   （WorldSimulation）用 <see cref="DeltaApplied"/> 的 delta 直接落到全员 <c>Spirit = Max(0, Spirit + delta)</c>；
    /// - 安全分与人口隐藏联动：「Safety &lt; 50 隐藏非必需居民 / ≥50 逐日恢复」，由订阅方读 <see cref="Value"/> 判定。
    ///
    /// 事件选型说明（master-plan 只写了「delta&lt;0 时逐点触发对全员精神 -1（由 WorldSimulation 订阅）」）：
    /// 本类选择 <b>同时</b>提供 <see cref="DeltaApplied"/>（实际生效的钳制后 delta）与 <see cref="Changed"/>（新值），
    /// 二选一的替代方案是「订阅 <see cref="Changed"/> 后自行比较上一次 <see cref="Value"/> 做差」——
    /// 那样每个订阅方都要自己缓存上一次值，容易出现重复扣/漏扣，这里以 <see cref="DeltaApplied"/> 作为唯一增量入口，
    /// <see cref="Changed"/> 仅作状态展示/持久化用途；两者均在值真实变化后触发。
    /// </summary>
    public sealed class TownSafety
    {
        /// <summary>新世界初始安全分。</summary>
        public const int InitialSafety = 100;

        /// <summary>安全分下限。</summary>
        public const int MinSafety = 0;

        /// <summary>安全分上限。</summary>
        public const int MaxSafety = 100;

        /// <summary>
        /// 人口隐藏阈值（M3，占位待策划确认）：安全分 &lt; 本值时 0:00 隐藏 1 名非必需居民；
        /// 安全分 ≥ 本值时 0:00 恢复 1 名隐藏居民。判定方为 WorldSimulation（见 <see cref="Value"/>）。
        /// </summary>
        public const int HideThreshold = 50;

        /// <summary>当前安全分（0-100）。只能通过 <see cref="Change"/> 变更。</summary>
        public int Value { get; private set; } = InitialSafety;

        /// <summary>安全分变化事件。参数：变化后的新值（0-100）。仅在值真实变化后触发。</summary>
        public event Action<int> Changed;

        /// <summary>
        /// 实际生效的变化量事件。参数：钳制后真正作用的 delta（<see cref="Value"/> 变化量，可正可负，可为 0 以外的任意值）。
        /// 增量入口：精神分联动按本事件的 delta 落值，delta 为负即「每失去 1 点全员精神 -1」。
        /// </summary>
        public event Action<int> DeltaApplied;

        public TownSafety()
        {
        }

        /// <summary>
        /// 增减安全分并钳制到 [<see cref="MinSafety"/>, <see cref="MaxSafety"/>]。
        /// 值未实际变化（delta=0 或被钳制吃掉）时不触发任何事件；
        /// 值变化时先触发 <see cref="DeltaApplied"/>（钳制后实际 delta）再触发 <see cref="Changed"/>（新值）。
        /// </summary>
        public void Change(int delta)
        {
            if (delta == 0)
            {
                return;
            }

            int before = Value;
            int after = before + delta;
            if (after < MinSafety)
            {
                after = MinSafety;
            }
            else if (after > MaxSafety)
            {
                after = MaxSafety;
            }

            if (after == before)
            {
                return;
            }

            Value = after;
            DeltaApplied?.Invoke(after - before);
            Changed?.Invoke(after);
        }
    }
}
