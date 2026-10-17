using System;
using System.Collections.Generic;

namespace GameLogic.World
{
    /// <summary>
    /// 战斗结果（M10：回合制战斗，行为案「战斗」节）。
    ///
    /// 字段口径：
    /// - <see cref="WinnerId"/> / <see cref="LoserId"/>：角色用 <see cref="CharacterData.Id"/>，怪物用 <see cref="MonsterDef.Id"/>；
    ///   <b>校验失败（不同坐标 / 目标非法 / 已死亡）时两者都为 <see cref="InvalidId"/></b>。
    /// - <see cref="RoundLog"/>：逐回合文本日志，供 HUD / M11 记忆池转述。
    /// - <see cref="LoserDied"/>：败方是否死亡（角色走 <see cref="WorldSimulation.Kill"/> 落 <see cref="WorldSimulation.GroundLoot"/>；
    ///   怪物走 <see cref="MonsterSystem.KillMonster"/> 广播 <see cref="MonsterSystem.MonsterKilled"/> 触发悬赏回写）。
    /// </summary>
    public sealed class CombatResult
    {
        /// <summary>「无胜者 / 无败者」占位 Id（校验失败时使用；角色 Id 与怪物 <see cref="MonsterDef.Id"/> 均从 1 开始）。</summary>
        public const int InvalidId = -1;

        public int WinnerId = InvalidId;
        public int LoserId = InvalidId;
        public readonly List<string> RoundLog = new List<string>();
        public bool LoserDied;

        /// <summary>是否打完了完整一场（true=有胜者；false=校验失败）。</summary>
        public bool HasResult
        {
            get { return WinnerId != InvalidId && LoserId != InvalidId; }
        }

        /// <summary>校验失败的结果（附失败原因日志）。</summary>
        public static CombatResult Invalid(string reason)
        {
            var result = new CombatResult();
            result.RoundLog.Add(reason);
            return result;
        }

        public override string ToString()
        {
            return HasResult
                ? $"winner:#{WinnerId} loser:#{LoserId} died:{LoserDied} rounds:{RoundLog.Count}"
                : $"invalid({(RoundLog.Count > 0 ? RoundLog[0] : string.Empty)})";
        }
    }

    /// <summary>
    /// 回合制战斗系统（M10）。出处：《英雄酒馆·角色行为系统策划案》「战斗」节 + master-plan M10；纯逻辑、<b>不依赖 UnityEngine</b>。
    ///
    /// 规则（行为案原文对应）：
    /// ①目标必须与发起者处于同一地图坐标，否则本类直接返回失败结果（<see cref="CombatResult.WinnerId"/> = <see cref="CombatResult.InvalidId"/>）；
    /// ②回合制轮流普攻，<b>发起者先出手</b>；
    /// ③普攻伤害取「力量与智慧中较高的一项」减去对方防御，最终伤害<b>不低于 1</b>（<see cref="AttackDamage(int,int)"/>）；
    /// ④血量归 0 者战斗失败并死亡：角色 <see cref="WorldSimulation.Kill"/>（背包 / 装备 / 金币落 <see cref="WorldSimulation.GroundLoot"/>，可被任何人拾取），
    ///   怪物 <see cref="MonsterSystem.KillMonster"/>；
    /// ⑤伤害公式与 <see cref="MonsterSystem"/> 的主动攻击共用同一实现（<see cref="AttackDamageVsCharacter"/>），保证怪物打人与角色打怪口径一致。
    ///
    /// 属性口径（M7）：伤害一律读<strong>有效属性</strong> <see cref="CharacterData.EffStr"/> / <see cref="CharacterData.EffInt"/> /
    /// <see cref="CharacterData.EffDef"/> / <see cref="CharacterData.EffMaxHp"/>（含装备加成），基础字段保持「裸装值」语义。
    ///
    /// 荣誉分：击杀怪物 → <see cref="CharacterScores.HonorForKill"/>(怪物四维) ；击杀<strong>敌对</strong>角色 → 同公式（角色有效四维）。
    /// 「敌对」判定用 <see cref="TownPopulation.GetRelation"/>（NPC 间好感 -100~100，&lt;0 为敌对，双向同值）；
    /// <see cref="CharacterData.FavorToPlayer"/> 是「角色对玩家」的好感，不参与 NPC 之间互杀的荣誉判定（口径说明见 <see cref="IsHostile"/>）。
    ///
    /// 受伤 debuff：单次伤害 ≥ 有效生命上限 50% 判<strong>重伤</strong>（<see cref="CharacterData.ActionLockDays"/> = 3），
    /// 否则<strong>轻伤</strong> = 1（矛盾 #17；出处《立项规划》「NPC 受伤处理」）。
    /// 倒计时由 <see cref="WorldSimulation"/> 的 0:00 日结链路 <c>TickActionLockDays</c> 统一递减（每天 -1，归 0 后恢复行动），
    /// 本类只负责「挂 debuff」，不自行计时。
    /// </summary>
    public sealed class CombatSystem
    {
        /// <summary>单次普攻的伤害下限（行为案：最终伤害不会低于 1）。</summary>
        public const int MinDamage = 1;

        /// <summary>重伤判定：单次伤害 ≥ 有效生命上限 × 该百分比 / 100（矛盾 #17 占位阈值）。</summary>
        public const int HeavyWoundPercent = 50;

        /// <summary>轻伤停战天数（矛盾 #17）。</summary>
        public const int LightWoundDays = 1;

        /// <summary>重伤停战天数（矛盾 #17）。</summary>
        public const int HeavyWoundDays = 3;

        /// <summary>
        /// 回合数上限（安全阀）：每回合双方至少各挨 <see cref="MinDamage"/> 点，血量归零必分胜负，理论上不会触发；
        /// 保留上限防止数值异常（例如生命上限为 0 的脏数据）导致死循环。
        /// </summary>
        public const int MaxRounds = 256;

        /// <summary>
        /// 角色 vs 角色（同图同格）。
        ///
        /// 校验：双方非 null、均存活、且 <c>attacker.Position == defender.Position</c>（同坐标）。
        /// 结算：发起者先手轮流普攻；血量归零 → <see cref="WorldSimulation.Kill"/>（含 <see cref="WorldSimulation.GroundLoot"/> 掉落链）；
        /// 胜者击杀敌对角色（<see cref="TownPopulation.GetRelation"/> &lt; 0）时荣誉 += 四维和 ÷ 4。
        /// </summary>
        public CombatResult Fight(WorldSimulation sim, CharacterData attacker, CharacterData defender)
        {
            var result = new CombatResult();
            if (!Validate(sim, attacker, defender != null && defender.IsAlive, defender == null ? default(GridPos) : defender.Position, result))
            {
                return result;
            }

            result.RoundLog.Add($"[战斗] {attacker.Name} 向 {defender.Name} 发起战斗（同坐标 {attacker.Position}）。");

            CharacterData loser = null;
            for (int round = 1; round <= MaxRounds && loser == null; round++)
            {
                // 发起者先出手。
                if (!Melee(attacker, defender, round, result))
                {
                    loser = defender;
                }

                if (loser == null && !Melee(defender, attacker, round, result))
                {
                    loser = attacker;
                }
            }

            if (loser == null)
            {
                return CombatResult.Invalid($"回合数超过上限 {MaxRounds}，判定无效。");
            }

            CharacterData winner = loser == defender ? attacker : defender;

            // 血量归 0 → 死亡：走既有 Kill 链路（M7 掉落 + 人口移除 + CharacterDied 事件）。
            sim.Kill(loser);
            result.LoserDied = true;
            result.LoserId = loser.Id;

            if (IsHostile(sim, winner, loser))
            {
                int honor = CharacterScores.HonorForKill(loser.EffMaxHp, loser.EffStr, loser.EffInt, loser.EffDef);
                winner.HonorScore += honor;
                result.RoundLog.Add($"{winner.Name} 击杀敌对角色 {loser.Name}，荣誉 +{honor}（现 {winner.HonorScore}）。");
            }
            else
            {
                result.RoundLog.Add($"{winner.Name} 击杀 {loser.Name}（双方非敌对，无荣誉入账）。");
            }

            result.WinnerId = winner.Id;
            return result;
        }

        /// <summary>
        /// 角色 vs 怪物（同图同格）。发起者先手。
        ///
        /// 角色胜 → <see cref="MonsterSystem.KillMonster"/>（广播 <see cref="MonsterSystem.MonsterKilled"/>，悬赏回写链路已由
        /// <see cref="WorldSimulation.StartNewGame"/> 接好），并荣誉 += 怪物四维和 ÷ 4；
        /// 角色败 → <see cref="WorldSimulation.Kill"/>（地面掉落）。
        /// </summary>
        public CombatResult FightMonster(WorldSimulation sim, CharacterData attacker, Monster target)
        {
            var result = new CombatResult();
            if (!Validate(sim, attacker, target != null && target.IsAlive, target == null ? default(GridPos) : target.Position, result))
            {
                return result;
            }

            result.RoundLog.Add($"[战斗] {attacker.Name} 与 {target.Def.Name} 交战（同坐标 {attacker.Position}）。");

            bool monsterDead = false;
            for (int round = 1; round <= MaxRounds && !monsterDead; round++)
            {
                // 角色先出手。
                monsterDead = !MeleeVsMonster(attacker, target, round, result);
                if (monsterDead)
                {
                    break;
                }

                if (!MeleeMonsterVsCharacter(target, attacker, round, result))
                {
                    // 角色血量归零 → 死亡。
                    sim.Kill(attacker);
                    result.LoserDied = true;
                    result.LoserId = attacker.Id;
                    result.WinnerId = target.Def.Id;
                    result.RoundLog.Add($"{attacker.Name} 被 {target.Def.Name} 击倒。");
                    return result;
                }
            }

            sim.Monsters.KillMonster(target, attacker);
            result.LoserDied = true;
            result.LoserId = target.Def.Id;
            result.WinnerId = attacker.Id;

            int honor = CharacterScores.HonorForKill(target.Def.Hp, target.Def.Str, target.Def.Int, target.Def.Def);
            attacker.HonorScore += honor;
            result.RoundLog.Add($"{attacker.Name} 击杀 {target.Def.Name}，荣誉 +{honor}（现 {attacker.HonorScore}）。");
            return result;
        }

        /// <summary>
        /// 通用伤害公式：<c>max(攻方力量, 攻方智力) − 目标防御</c>，下限 <see cref="MinDamage"/>（行为案③④）。
        /// <paramref name="atkOffense"/> 传 <c>Math.Max(character.EffStr, character.EffInt)</c>，<paramref name="targetDef"/> 传目标<strong>有效防御</strong>。
        /// </summary>
        public static int AttackDamage(int atkOffense, int targetDef)
        {
            int raw = atkOffense - targetDef;
            return raw < MinDamage ? MinDamage : raw;
        }

        /// <summary>角色普攻伤害便捷重载：取 <c>max(EffStr, EffInt)</c> 作攻方伤害，<paramref name="targetDef"/> 通常为目标 <see cref="CharacterData.EffDef"/>。</summary>
        public static int AttackDamage(CharacterData atk, int targetDef)
        {
            if (atk == null)
            {
                throw new ArgumentNullException(nameof(atk));
            }

            return AttackDamage(Math.Max(atk.EffStr, atk.EffInt), targetDef);
        }

        /// <summary>
        /// 怪物打角色：<c>m.Def.Str − 目标 EffDef</c>，下限 <see cref="MinDamage"/>（与行为案同一口径）。
        /// <see cref="MonsterSystem"/> 的主动攻击调用本方法，保证「怪物偷袭」与「角色与怪物交战」数值一致（M10「修改」）。
        /// </summary>
        public static int AttackDamageVsCharacter(Monster m, CharacterData def)
        {
            if (m == null)
            {
                throw new ArgumentNullException(nameof(m));
            }

            if (def == null)
            {
                throw new ArgumentNullException(nameof(def));
            }

            return AttackDamage(m.Def.Str, def.EffDef);
        }

        /// <summary>
        /// 挂受伤 debuff（矛盾 #17）：单次伤害 ≥ 有效生命上限 50% → 重伤 <see cref="HeavyWoundDays"/> 天，否则轻伤 <see cref="LightWoundDays"/> 天。
        /// 角色已死亡时不挂（死亡即冻结，见行为案⑥）。倒计时由 <c>WorldSimulation</c> 的 0:00 日结递减。
        /// </summary>
        public static int ApplyInjuryDays(CharacterData victim, int damage)
        {
            if (victim == null || !victim.IsAlive)
            {
                return 0;
            }

            int maxHp = Math.Max(1, victim.EffMaxHp);
            victim.ActionLockDays = damage * 100 >= maxHp * HeavyWoundPercent ? HeavyWoundDays : LightWoundDays;
            return victim.ActionLockDays;
        }

        /// <summary>
        /// 敌对判定（荣誉入账门槛）：用 <see cref="TownPopulation.GetRelation"/> 读 <b>NPC 之间</b>的双向好感（-100~100，&lt;0 敌对）。
        /// <see cref="CharacterData.FavorToPlayer"/> 是角色对<strong>玩家</strong>的好感，NPC 互杀不适用，故不参与本判定。
        /// </summary>
        private static bool IsHostile(WorldSimulation sim, CharacterData winner, CharacterData loser)
        {
            return sim?.Population != null && sim.Population.GetRelation(winner.Id, loser.Id) < 0;
        }

        /// <summary>同坐标与存活校验。返回 false 时 <paramref name="result"/> 已带失败原因，调用方直接返回。</summary>
        private static bool Validate(WorldSimulation sim, CharacterData attacker, bool targetAlive, GridPos targetPos, CombatResult result)
        {
            if (sim == null)
            {
                result.RoundLog.Add("战斗失败：模拟未初始化。");
                return false;
            }

            if (attacker == null || !attacker.IsAlive)
            {
                result.RoundLog.Add("战斗失败：发起者不存在或已死亡。");
                return false;
            }

            if (!targetAlive)
            {
                result.RoundLog.Add("战斗失败：目标不存在或已死亡。");
                return false;
            }

            if (!attacker.Position.Equals(targetPos))
            {
                result.RoundLog.Add($"战斗失败：发起者 {attacker.Name} 与目标不在同一坐标（{attacker.Position} vs {targetPos}）。");
                return false;
            }

            return true;
        }

        /// <summary>角色普攻角色。返回 false = 目标血量归零。</summary>
        private static bool Melee(CharacterData atk, CharacterData target, int round, CombatResult result)
        {
            int damage = AttackDamage(atk, target.EffDef);
            target.Hp -= damage;
            result.RoundLog.Add($"R{round} {atk.Name} 普攻 {target.Name}，伤害 {damage}（余 {Math.Max(0, target.Hp)}/{target.EffMaxHp}）。");

            if (target.Hp <= 0)
            {
                return false;
            }

            ApplyInjuryDays(target, damage);
            return true;
        }

        /// <summary>角色普攻怪物。返回 false = 怪物血量归零。</summary>
        private static bool MeleeVsMonster(CharacterData atk, Monster target, int round, CombatResult result)
        {
            int damage = AttackDamage(atk, target.Def.Def);
            target.Hp -= damage;
            result.RoundLog.Add($"R{round} {atk.Name} 普攻 {target.Def.Name}，伤害 {damage}（余 {Math.Max(0, target.Hp)}/{target.Def.Hp}）。");

            if (target.Hp <= 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>怪物攻击角色（与 <see cref="MonsterSystem"/> 同一公式）。返回 false = 角色血量归零。</summary>
        private static bool MeleeMonsterVsCharacter(Monster monster, CharacterData target, int round, CombatResult result)
        {
            int damage = AttackDamageVsCharacter(monster, target);
            target.Hp -= damage;
            result.RoundLog.Add($"R{round} {monster.Def.Name} 攻击 {target.Name}，伤害 {damage}（余 {Math.Max(0, target.Hp)}/{target.EffMaxHp}）。");

            if (target.Hp <= 0)
            {
                return false;
            }

            ApplyInjuryDays(target, damage);
            return true;
        }
    }
}
