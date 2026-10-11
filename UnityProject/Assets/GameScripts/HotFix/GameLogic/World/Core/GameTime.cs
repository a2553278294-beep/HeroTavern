using System;

namespace GameLogic.World
{
    /// <summary>
    /// 一次时间推进跨越的边界信息。
    /// </summary>
    public readonly struct TimeAdvanceResult
    {
        /// <summary>推进后的分钟（0-59）。</summary>
        public readonly int Minute;
        /// <summary>推进后的整点（0-23）。</summary>
        public readonly int Hour;
        /// <summary>推进后的年内天数（1-365）。</summary>
        public readonly int DayOfYear;
        /// <summary>推进后的总天数（从 1 计）。</summary>
        public readonly int DayOverall;
        /// <summary>推进后的年份（从 1 计）。</summary>
        public readonly int Year;
        /// <summary>是否跨越了整点（移动、饮用等按小时计时的系统在此驱动）。</summary>
        public readonly bool CrossedHour;
        /// <summary>是否跨越了 0:00（新一天开始，触发结算）。</summary>
        public readonly bool CrossedMidnight;
        /// <summary>是否跨越了新年（年龄 +1、自然死亡判定）。</summary>
        public readonly bool CrossedYear;

        public TimeAdvanceResult(int minute, int hour, int dayOfYear, int dayOverall, int year, bool crossedHour, bool crossedMidnight, bool crossedYear)
        {
            Minute = minute;
            Hour = hour;
            DayOfYear = dayOfYear;
            DayOverall = dayOverall;
            Year = year;
            CrossedHour = crossedHour;
            CrossedMidnight = crossedMidnight;
            CrossedYear = crossedYear;
        }
    }

    /// <summary>
    /// 游戏时钟（R1：即时制；每年 365 天、每天 24 小时）。
    /// 内部以分钟为最小单位（2026-10-11 需求变更：1 真实秒 = 1 游戏分钟，五档加速）；
    /// 真实秒到游戏分钟的换算与档位由 <see cref="WorldSimulation"/> 驱动。
    /// 起点为第 1 年 Day1 00:00；结算/跨年事件只在跨越边界时触发，起点不触发。
    /// </summary>
    public sealed class GameTime
    {
        public const int MinutesPerHour = 60;
        public const int HoursPerDay = 24;
        public const int DaysPerYear = 365;
        public const int MinutesPerDay = MinutesPerHour * HoursPerDay;
        public const long MinutesPerYear = (long)MinutesPerDay * DaysPerYear;

        /// <summary>从开局累计的游戏分钟数。0 = 第 1 年 Day1 00:00。</summary>
        public long TotalMinutes { get; private set; }

        /// <summary>当前年份（从 1 计）。</summary>
        public int Year => (int)(TotalMinutes / MinutesPerYear) + 1;

        /// <summary>当前年内天数（1-365）。</summary>
        public int DayOfYear => (int)((TotalMinutes / MinutesPerDay) % DaysPerYear) + 1;

        /// <summary>当前总天数（从 1 计），供属性权重等“游戏天数”口径使用。</summary>
        public int DayOverall => (int)(TotalMinutes / MinutesPerDay) + 1;

        /// <summary>当前整点（0-23）。</summary>
        public int Hour => (int)((TotalMinutes / MinutesPerHour) % HoursPerDay);

        /// <summary>当前分钟（0-59）。</summary>
        public int Minute => (int)(TotalMinutes % MinutesPerHour);

        /// <summary>
        /// 推进指定游戏分钟数，返回跨越的边界。
        /// </summary>
        public TimeAdvanceResult AdvanceMinutes(int minutes)
        {
            if (minutes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minutes));
            }

            long before = TotalMinutes;
            TotalMinutes += minutes;

            bool crossedHour = TotalMinutes / MinutesPerHour != before / MinutesPerHour;
            bool crossedMidnight = TotalMinutes / MinutesPerDay != before / MinutesPerDay;
            bool crossedYear = TotalMinutes / MinutesPerYear != before / MinutesPerYear;
            return new TimeAdvanceResult(Minute, Hour, DayOfYear, DayOverall, Year, crossedHour, crossedMidnight, crossedYear);
        }

        public override string ToString()
        {
            return $"Year {Year} Day {DayOfYear} {Hour:00}:{Minute:00}";
        }
    }
}
