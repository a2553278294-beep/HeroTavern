using System.Collections.Generic;

namespace GameLogic.World
{
    /// <summary>
    /// 基本行为（《英雄酒馆角色行为系统策划案》「基本行为」）。
    ///
    /// 策划案 7 种基本行为：①战斗 ②使用物品 ③移动 ④对话 ⑤交易 ⑥交互（建筑/场景物品）⑦待机。
    /// 本枚举在这 7 类之上把「与怪物的战斗」「领取悬赏」「拾取战利品」单列，便于权重计算按类分档：
    /// - <see cref="Idle"/>        ⑦ 待机（城镇中恢复体力/生命，每小时 +1hp/+1 体力）；
    /// - <see cref="MoveTo"/>      ③ 移动（最短路径前往目标坐标，每小时 1 格）；
    /// - <see cref="OrderDrink"/>  ⑥ 与吧台/酒保交互点单并饮酒；
    /// - <see cref="TalkTo"/>      ④ 与同坐标其他角色对话（获取信息 → 信息分 +1/条）；
    /// - <see cref="TradeWith"/>   ⑤ 与同坐标其他角色（含玩家）交易；
    /// - <see cref="AcceptBounty"/>⑥ 与悬赏布告栏交互领取悬赏；
    /// - <see cref="HuntMonster"/> ① 战斗（与怪物交战；PvP 走 <see cref="CombatSystem"/>）；
    /// - <see cref="RestAtTown"/>  ⑦ 城镇内待机（体力为 0 时强制回城执行）；
    /// - <see cref="PickupLoot"/>  ⑥ 交互：拾取死亡角色背包中的物品；
    /// - <see cref="UseItem"/>     ② 使用 / 装备 / 卸下背包物品。
    /// </summary>
    public enum BehaviorKind
    {
        /// <summary>无动作（占位，权重计算的兜底候选）。</summary>
        Idle = 0,

        /// <summary>移动到目标坐标。</summary>
        MoveTo = 1,

        /// <summary>到吧台点单并饮酒。</summary>
        OrderDrink = 2,

        /// <summary>与同坐标的其他角色对话。</summary>
        TalkTo = 3,

        /// <summary>与同坐标的其他角色（或玩家）交易。</summary>
        TradeWith = 4,

        /// <summary>与悬赏布告栏交互领取悬赏。</summary>
        AcceptBounty = 5,

        /// <summary>狩猎同坐标怪物。</summary>
        HuntMonster = 6,

        /// <summary>城镇内待机回体力/生命。</summary>
        RestAtTown = 7,

        /// <summary>拾取战利品（死亡角色背包）。</summary>
        PickupLoot = 8,

        /// <summary>使用 / 装备 / 卸下背包物品。</summary>
        UseItem = 9,
    }

    /// <summary>
    /// 一条交互链中的一步（纯数据值对象，无 UnityEngine 依赖）。
    ///
    /// 动态决策口径（策划案「动态决策」）：
    /// 行动 = 一条交互链（移动→交互→使用/购买/接悬赏…），执行过程中短期记忆更新会触发重抽，
    /// <b>原行动中已执行的部分不再计算分数</b>——已完成的步骤以 <see cref="Done"/>=true 标记，
    /// 行为系统在重算权重时跳过它们。
    /// </summary>
    public sealed class BehaviorStep
    {
        /// <summary>本步行为类型。</summary>
        public BehaviorKind Kind;

        /// <summary>本步目标坐标；无坐标语义的行为（对话/交易/使用物品）填角色当前坐标。</summary>
        public GridPos Destination;

        /// <summary>本步目标 id：物品 id / 角色 id / <see cref="BountyEntry.Id"/> / <see cref="MonsterDef.Id"/>；无目标填 0。</summary>
        public int TargetId;

        /// <summary>是否已执行（已执行部分不再计分）。</summary>
        public bool Done;

        public BehaviorStep()
        {
        }

        public BehaviorStep(BehaviorKind kind, GridPos destination, int targetId)
        {
            Kind = kind;
            Destination = destination;
            TargetId = targetId;
        }

        /// <summary>是否已执行（<see cref="Done"/> 的语义别名）。</summary>
        public bool IsDone => Done;

        public override string ToString()
        {
            return Kind + "->" + Destination + "#" + TargetId + (Done ? " (已执行)" : string.Empty);
        }
    }

    /// <summary>
    /// 一条行动 = 一条交互链（纯数据 + 小逻辑，无 UnityEngine 依赖）。
    ///
    /// 权重口径（策划案「权重计算」）：角色感知当前位置 → 经记忆池穷举可交互对象 → 穷举可执行交互 →
    /// 每个交互链算一个候选行动 → <b>各行动带来的 8 项总分变化之和即该行动权重</b>
    /// （财富/健康/战斗/体力/荣誉/精神/社交/信息）；<b>分数有上限的（体力 100、精神 100、好感 100），超出上限的部分不计入</b>，
    /// 所以 <see cref="Weight"/> 是「实际计入的总分变化」，不是各项毛变化之和。
    ///
    /// 动态决策口径：行动执行到一半若短期记忆更新（悬赏广播 / 怪物进城 / 特价刷新），
    /// 行为系统重新生成候选并重抽，<b>原行动已执行的部分（<see cref="Steps"/> 中 <see cref="BehaviorStep.Done"/>=true 的步）不再计分</b>；
    /// 重抽后的新行动是一个新的 <see cref="BehaviorAction"/> 实例，本实例保留已完成步作为执行痕迹。
    ///
    /// 使用顺序：<c>new BehaviorAction(actor, kind, dest, targetId)</c> → <c>AddStep(...)</c> 若干 →
    /// 行为系统填 <see cref="Weight"/> → 抽取后逐步 <see cref="MarkStepDone"/>，全部完成时 <see cref="IsFinished"/> 置 true。
    /// </summary>
    public sealed class BehaviorAction
    {
        /// <summary>行动发起者（Actor 为 null 的候选在决策时应跳过）。</summary>
        public CharacterData Actor;

        /// <summary>行动类型（整条交互链的主类目：到店点单 = <see cref="BehaviorKind.OrderDrink"/>；领悬赏出门打猎 = <see cref="BehaviorKind.AcceptBounty"/>）。</summary>
        public BehaviorKind Kind;

        /// <summary>行动目标坐标（交互链终点，通常是最后一个交互对象所在格）。</summary>
        public GridPos Destination;

        /// <summary>行动目标 id：物品 / 角色 / <see cref="BountyEntry.Id"/> / <see cref="MonsterDef.Id"/>；无目标填 0。</summary>
        public int TargetId;

        /// <summary>
        /// 行动权重 = 8 项总分（财富/健康/战斗/体力/荣誉/精神/社交/信息）的<b>计入变化</b>之和；
        /// 有上限的分数（体力 100、精神 100、好感 100）超出上限部分不计入。重算权重时由行为系统覆盖写回。
        /// </summary>
        public int Weight;

        /// <summary>交互链的步骤序列（按执行顺序）。</summary>
        public List<BehaviorStep> Steps = new List<BehaviorStep>();

        /// <summary>已执行步数（= 前面连续 Done 的步数；重抽计分时已执行部分从权重中剔除）。</summary>
        public int ExecutedCount;

        /// <summary>是否已全部执行完毕（步骤全部 Done 后置 true；无步骤的行动被 <see cref="MarkStepDone"/> 一次即视为完成）。</summary>
        public bool IsFinished;

        public BehaviorAction()
        {
        }

        public BehaviorAction(CharacterData actor, BehaviorKind kind, GridPos destination, int targetId)
            : this(actor, kind, destination, targetId, 0)
        {
        }

        public BehaviorAction(CharacterData actor, BehaviorKind kind, GridPos destination, int targetId, int weight)
        {
            Actor = actor;
            Kind = kind;
            Destination = destination;
            TargetId = targetId;
            Weight = weight;
        }

        /// <summary>追加一步并返回自身（便于链式构建）。</summary>
        public BehaviorAction AddStep(BehaviorKind kind, GridPos destination, int targetId)
        {
            Steps.Add(new BehaviorStep(kind, destination, targetId));
            return this;
        }

        /// <summary>追加一步并返回自身（无坐标语义时 destination 传 <c>default(GridPos)</c>）。</summary>
        public BehaviorAction AddStep(BehaviorKind kind, int targetId)
        {
            return AddStep(kind, default(GridPos), targetId);
        }

        /// <summary>是否还有未执行的步骤。</summary>
        public bool HasPendingSteps => ExecutedCount >= 0 && ExecutedCount < Steps.Count;

        /// <summary>剩余未执行步数。</summary>
        public int PendingStepCount => Steps.Count - ExecutedCount;

        /// <summary>当前待执行步骤；已全部执行或从未添加步骤时为 null（<see cref="CurrentStep"/>）。</summary>
        public BehaviorStep CurrentStep => HasPendingSteps ? Steps[ExecutedCount] : null;

        /// <summary>取第 index 步（越界返回 null）。</summary>
        public BehaviorStep GetStep(int index)
        {
            return index >= 0 && index < Steps.Count ? Steps[index] : null;
        }

        /// <summary>
        /// 标记当前一步已完成：<see cref="CurrentStep"/>.<c>Done</c> 置 true、<see cref="ExecutedCount"/> +1；
        /// 全部步骤完成（或本就没有步骤）时 <see cref="IsFinished"/> 置 true。
        /// 已执行部分在动态决策重抽时不再计分——行为系统重算权重应只基于未执行的剩余步骤。
        /// </summary>
        public void MarkStepDone()
        {
            if (!HasPendingSteps)
            {
                IsFinished = true;
                return;
            }

            Steps[ExecutedCount].Done = true;
            ExecutedCount++;
            if (ExecutedCount >= Steps.Count)
            {
                ExecutedCount = Steps.Count;
                IsFinished = true;
            }
        }

        /// <summary>把剩余步骤一次性标记完成（行动被外部取消 / 目标消失时使用，执行痕迹仍然保留）。</summary>
        public void MarkAllStepsDone()
        {
            for (int i = 0; i < Steps.Count; i++)
            {
                Steps[i].Done = true;
            }

            ExecutedCount = Steps.Count;
            IsFinished = true;
        }

        public override string ToString()
        {
            return Kind + " 目标" + Destination + "#" + TargetId + " 权重" + Weight
                   + " 进度" + ExecutedCount + "/" + Steps.Count + (IsFinished ? " 已完成" : string.Empty);
        }
    }
}
