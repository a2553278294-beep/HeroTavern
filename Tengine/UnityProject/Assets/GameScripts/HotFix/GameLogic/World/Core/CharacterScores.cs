using System;

namespace GameLogic.World
{
    /// <summary>
    /// 角色分数计算器（行为系统策划案·分数章，纯公式；决策循环属行为系统后续任务）。
    /// </summary>
    public static class CharacterScores
    {
        /// <summary>财富分 = 金币数量。</summary>
        public static int Wealth(CharacterData c)
        {
            return c.Gold;
        }

        /// <summary>
        /// 战斗分 = 力量² + 智力² + 防御。M7：三项都取<strong>有效属性</strong>
        /// （<see cref="CharacterData.EffStr"/> / <see cref="CharacterData.EffInt"/> / <see cref="CharacterData.EffDef"/>，
        /// 含装备加成），装备变化会即时反映到战斗分。
        /// </summary>
        public static int Combat(CharacterData c)
        {
            return c.EffStr * c.EffStr + c.EffInt * c.EffInt + c.EffDef;
        }

        /// <summary>体力分 = 体力值。</summary>
        public static int Stamina(CharacterData c)
        {
            return c.Stamina;
        }

        /// <summary>精神分（初始 100，每小时 -1；城镇安全分联动属安全系统）。</summary>
        public static int Spirit(CharacterData c)
        {
            return c.Spirit;
        }

        /// <summary>荣誉分。</summary>
        public static int Honor(CharacterData c)
        {
            return c.HonorScore;
        }

        /// <summary>信息分（每次获取信息 +1）。</summary>
        public static int Info(CharacterData c)
        {
            return c.InfoScore;
        }

        /// <summary>
        /// 健康分每小时增减：100% +1；80-99% 0；50-79% -1；20-49% -2；1-19% -3。
        /// </summary>
        public static int HealthDriftPerHour(int hp, int maxHp)
        {
            if (maxHp <= 0 || hp <= 0)
            {
                return 0;
            }

            int percent = hp * 100 / maxHp;
            if (percent >= 100) return 1;
            if (percent >= 80) return 0;
            if (percent >= 50) return -1;
            if (percent >= 20) return -2;
            return -3;
        }

        /// <summary>荣誉分：击杀怪物 = 怪物四维之和 ÷ 4；击杀好感 &lt; 0 的角色同理（角色四维）。</summary>
        public static int HonorForKill(int hp, int str, int intel, int def)
        {
            return (hp + str + intel + def) / 4;
        }

        /// <summary>社交分 = 其他角色（含玩家）对该角色好感度之和；NPC 间部分由 TownPopulation.GetSocialScore 提供。</summary>
        public static int Social(TownPopulation population, CharacterData c)
        {
            return population.GetSocialScore(c.Id) + c.FavorToPlayer;
        }
    }
}
