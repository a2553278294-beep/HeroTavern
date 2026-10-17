using System;
using System.Collections.Generic;

namespace GameLogic.World
{
    /// <summary>
    /// 角色行为决策引擎（M11）：<b>记忆池 + 权重抽取 + 动态决策</b>。纯 C#，无 UnityEngine 依赖。
    ///
    /// 出处与口径：
    /// - 《英雄酒馆角色行为系统策划案》「基本行为」7 种（战斗/使用物品/移动/对话/交易/交互/待机）
    ///   → <see cref="BehaviorKind"/>；本系统每游戏小时为每名「存活 + 未隐藏 + 无行动锁定」的角色
    ///   穷举一条<b>交互链</b>作为候选行动（移动 → 交互），共覆盖
    ///   待机 / 点单饮酒 / 狩猎在城怪物 / 接悬赏 / 同坐标对话 / 拾取掉落 / 闲逛兜底 7 类。
    /// - 「权重计算」：各行动带来的 <b>8 项总分变化之和</b>即行动权重（财富/健康/战斗/体力/荣誉/精神/社交/信息）；
    ///   <b>分数有上限的（体力 100、精神 100、好感 100），超出上限部分不计入</b>。
    ///   公式一律复用 <see cref="CharacterScores"/>，分数变化口径见本类各 ScoreXxx 方法注释。
    /// - 「动态决策」：短期记忆更新（广播）时重新穷举候选，与<b>原行动剩余步骤</b>的权重比较，
    ///   新候选权重更高才换行动；<b>原行动已执行的部分不再计分</b>
    ///   （<see cref="BehaviorAction.ExecutedCount"/> 之前的步在 <see cref="ScoreAction"/> 里被跳过）。
    /// - 「待机」：体力为 0 时强制回城执行待机，每小时 +1 生命 / +1 体力（见 <see cref="ApplyRestRecovery"/>）。
    ///
    /// 接线路径（组合根在 <see cref="WorldSimulation"/> 的整点分支调用，调用方负责创建本类）：
    /// <list type="number">
    /// <item><c>TickHour(sim)</c>：每个游戏小时推进一次；</item>
    /// <item><c>AttachBroadcastSources(sim)</c>：订阅悬赏发布 / 怪物进城 / 怪物被击杀 / 0:00 日结，
    ///   自动写入短期记忆并触发动态决策（也可由外部直接调 <see cref="OnBroadcast"/>）；</item>
    /// <item>本类只新增逻辑、不修改任何既有文件：移动执行仍由 <see cref="WorldSimulation.SetMoveTarget"/>
    ///   + 整点 <c>MoveCharactersOneStep</c> 驱动；每小时体力/精神 -1 仍由 <c>ApplyHourlyDrift</c> 统一结算。</item>
    /// </list>
    /// </summary>
    public sealed class CharacterBehaviorSystem
    {
        // ------------------------------------------------------------------
        // 占位常量（策划案未给数值者一律标「占位」，待策划定档）
        // ------------------------------------------------------------------

        /// <summary>体力上限（策划案「角色初始拥有 100 体力值」；体力分 = 体力值，超出部分不计）。</summary>
        public const int MaxStamina = 100;

        /// <summary>精神上限（策划案「角色初始拥有 100 点精神分」；饮酒回精神超出部分不计）。</summary>
        public const int MaxSpirit = 100;

        /// <summary>好感上限（好感度 0-100，矛盾 #6；社交分的「好感」超出上限部分不计）。</summary>
        public const int MaxFavor = 100;

        /// <summary>待机每小时恢复生命值（策划案「待机」：每小时回复 1 点生命值）。</summary>
        public const int RestHpPerHour = 1;

        /// <summary>待机每小时恢复体力值（策划案「待机」：每小时回复 1 点体力值）。</summary>
        public const int RestStaminaPerHour = 1;

        /// <summary>饮用一杯酒提供的精神分（<b>占位常量</b>，矛盾 #19：首版 NPC 饮用付费酒 精神 +10）。</summary>
        public const int DrinkSpiritGain = 10;

        /// <summary>每获取 1 条信息的信息分（策划案「信息分」：每次获取信息 +1）。</summary>
        public const int InfoGainPerInfo = 1;

        /// <summary>一次成功对话带来的好感增量（<b>占位</b>：策划案只说「社交分 = 好感之和」，未给对话增量）。</summary>
        public const int TalkFavorGain = 1;

        /// <summary>闲逛换来的精神增量（<b>占位</b>：策划案未定义「无目的地移动」的收益）。</summary>
        public const int WanderSpiritGain = 1;

        /// <summary>待机候选的基础权重（<b>占位</b>：保证「满血满体力」时待机仍是一个非空候选）。</summary>
        public const int BaseRestWeight = 1;

        /// <summary>闲逛兜底候选的最小权重（<b>占位</b>：保证任意场景候选集永不为空，角色不会卡死不动）。</summary>
        public const int BaseWanderWeight = 1;

        /// <summary>酒标签与角色性格匹配的加分（<b>占位</b>：master-plan M11「客人点单偏好」，待配置表落地）。</summary>
        public const int PersonalityMatchBonus = 3;

        /// <summary>战士接战斗悬赏的权重百分比（策划案「战士接取战斗悬赏后进行战斗」，战士优先）。</summary>
        public const int WarriorBountyWeightPercent = 100;

        /// <summary>非战士接战斗悬赏的权重百分比（<b>占位</b>：非战士只能浏览悬赏信息，收益大幅打折）。</summary>
        public const int NonWarriorBountyWeightPercent = 20;

        /// <summary>接悬赏时按「无目标怪物实例」的荣誉命中率折扣（<b>占位</b>，战斗悬赏还没确定具体目标怪物时用）。</summary>
        public const int BountyHonorRatePercent = 50;

        /// <summary>开战所需的最低战斗分差胜率（百分比，<b>占位</b>：低于此胜率不主动接战，避免必死送命）。</summary>
        public const int MinWinRatePercentToHunt = 25;

        /// <summary>单条行动权重上限（安全阀，防止脏数据把加权轮盘撑爆）。</summary>
        public const int MaxWeightPerAction = 100000;

        /// <summary>随机闲逛时最多尝试的落点次数。</summary>
        public const int WanderTryCount = 24;

        /// <summary>性格 → 酒标签的占位匹配词表（Cheerful 喜甜、Calm 喜清淡、Cautious 喜烈/苦；<b>待配置</b>）。</summary>
        private static readonly string[] CheerfulTagWords = { "甜", "蜜", "果", "花" };

        private static readonly string[] CalmTagWords = { "清", "淡", "茶", "素" };

        private static readonly string[] CautiousTagWords = { "烈", "苦", "辛", "辣" };

        private readonly IRandomSource _random;
        private readonly CombatSystem _combat = new CombatSystem();

        /// <summary>每名角色一份记忆池（角色 id → 池；首次访问时创建并接长期记忆）。</summary>
        private readonly Dictionary<int, MemoryPool> _pools = new Dictionary<int, MemoryPool>();

        /// <summary>每名角色当前正在执行的行动（动态决策时与新候选比较；已执行步保留为执行痕迹）。</summary>
        private readonly Dictionary<int, BehaviorAction> _current = new Dictionary<int, BehaviorAction>();

        /// <summary>每名角色最近一次穷举出的候选清单（供 HUD / 调试观察权重分布，按角色 id 复用同一 List）。</summary>
        private readonly Dictionary<int, List<BehaviorAction>> _candidateCache = new Dictionary<int, List<BehaviorAction>>();

        // 三个独立缓冲区：避免「正在遍历快照时又调用需要缓冲区的辅助方法」造成别名覆盖。
        private readonly List<CharacterData> _poolBuffer = new List<CharacterData>();
        private readonly List<CharacterData> _tickBuffer = new List<CharacterData>();
        private readonly List<CharacterData> _broadcastBuffer = new List<CharacterData>();
        private readonly List<CharacterData> _scanBuffer = new List<CharacterData>();

        private WorldSimulation _attached;
        private bool _sourcesAttached;

        /// <summary>最近一次日结的天数：<see cref="OnDaySettled"/> 对同一天幂等，避免「组合根显式调用 + DaySettled 广播」双入口重复结算。</summary>
        private int _lastSettledDay;

        /// <summary>累计做出的行动决策次数（每次加权抽取 +1）。</summary>
        public int DecisionsMade { get; private set; }

        /// <summary>动态决策实际换行动的次数（新候选权重高于原行动剩余权重）。</summary>
        public int ReconsiderCount { get; private set; }

        /// <summary>累计处理短期记忆广播的次数。</summary>
        public int BroadcastCount { get; private set; }

        /// <summary>最近一次决策的文字摘要（HUD / 日志用）。</summary>
        public string LastDecisionSummary { get; private set; } = string.Empty;

        /// <summary>构造注入随机源（保证同 seed 决策可复现）。传 null 时退回 <see cref="SystemRandomSource"/>（seed=0）。</summary>
        public CharacterBehaviorSystem(IRandomSource random)
        {
            _random = random ?? new SystemRandomSource(0);
        }

        // ------------------------------------------------------------------
        // 记忆池
        // ------------------------------------------------------------------

        /// <summary>
        /// 取（并在首次访问时创建）该角色的记忆池：
        /// 长期记忆 = 城镇/酒馆坐标（<c>LoadFromConfig</c> 读 map 表，缺失回退 <see cref="WorldMap"/> 常量）
        /// + 城镇安全值（<c>AttachSafety(sim.Safety)</c> 订阅 <c>TownSafety.Changed</c> 广播更新）；
        /// 短期记忆当日有效，0:00 由 <see cref="OnDaySettled"/> 整池清空。
        /// </summary>
        public MemoryPool MemoryPoolOf(WorldSimulation sim, int characterId)
        {
            if (_pools.TryGetValue(characterId, out var pool) && pool != null)
            {
                int day = DayOf(sim);
                if (day > 0 && pool.CurrentDayOverall != day)
                {
                    pool.CurrentDayOverall = day;
                }

                return pool;
            }

            pool = new MemoryPool();
            if (sim != null)
            {
                pool.CurrentDayOverall = DayOf(sim);
                pool.AttachSafety(sim.Safety);
                pool.LoadFromConfig();
            }

            _pools[characterId] = pool;
            return pool;
        }

        /// <summary>为世界里的全部存活角色补齐记忆池（新生成的角色首小时就有长期记忆）。</summary>
        public void EnsurePools(WorldSimulation sim)
        {
            if (sim == null || sim.Population == null)
            {
                return;
            }

            CollectAll(sim, _poolBuffer);
            for (int i = 0; i < _poolBuffer.Count; i++)
            {
                var c = _poolBuffer[i];
                if (c != null)
                {
                    MemoryPoolOf(sim, c.Id);
                }
            }
        }

        /// <summary>当前角色正在执行的行动（null = 本小时尚未决策 / 行动已结束）。</summary>
        public BehaviorAction CurrentActionOf(int characterId)
        {
            return _current.TryGetValue(characterId, out var action) ? action : null;
        }

        /// <summary>该角色最近一次穷举的候选清单（只读；用于观察权重分布）。</summary>
        public IReadOnlyList<BehaviorAction> CandidatesOf(int characterId)
        {
            return _candidateCache.TryGetValue(characterId, out var list) ? list : (IReadOnlyList<BehaviorAction>)new List<BehaviorAction>();
        }

        /// <summary>调试摘要：角色当前行动 + 候选权重。</summary>
        public string BuildDebugLine(int characterId)
        {
            var action = CurrentActionOf(characterId);
            var head = "#" + characterId + " 当前: " + (action != null ? action.ToString() : "无");
            var candidates = CandidatesOf(characterId);
            if (candidates.Count == 0)
            {
                return head + " | 候选: 无";
            }

            var parts = new List<string>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                parts.Add(candidates[i].Kind + "#" + candidates[i].TargetId + "(" + candidates[i].Weight + ")");
            }

            return head + " | 候选: " + string.Join("，", parts);
        }

        /// <summary>清空全部状态（重开对局时调用；记忆池里的安全值订阅也会一并解绑）。</summary>
        public void Reset()
        {
            foreach (var pair in _pools)
            {
                pair.Value?.DetachSafety();
            }

            _pools.Clear();
            _current.Clear();
            _candidateCache.Clear();
            DecisionsMade = 0;
            ReconsiderCount = 0;
            BroadcastCount = 0;
            _lastSettledDay = 0;
            LastDecisionSummary = string.Empty;
        }

        // ------------------------------------------------------------------
        // 广播接线（悬赏发布 / 怪物进城 / 怪物死亡 / 0:00 日结）
        // ------------------------------------------------------------------

        /// <summary>
        /// 订阅组合根的广播源（可重复调用，内部幂等）：
        /// - 悬赏发布 → <see cref="MemoryKind.BountyAvailable"/>（策划案：悬赏栏持续向城镇在场角色广播）；
        /// - 怪物进城 → <see cref="MemoryKind.MonsterInTown"/>（策划案：怪物在城镇时持续向城镇角色广播）；
        /// - 怪物被击杀 → 遗忘该条在城怪物记忆，并作废正在追它的行动；
        /// - 0:00 日结 → 短期记忆整池清空（短期记忆<b>当日有效</b>）并重新播一遍当日在城怪物 / 可接悬赏。
        /// </summary>
        public void AttachBroadcastSources(WorldSimulation sim)
        {
            if (sim == null || _sourcesAttached)
            {
                return;
            }

            _attached = sim;
            if (sim.Bounty != null)
            {
                sim.Bounty.Published += OnBountyPublished;
            }

            if (sim.Monsters != null)
            {
                sim.Monsters.MonsterEnteredTown += OnMonsterEnteredTown;
                sim.Monsters.MonsterKilled += OnMonsterKilled;
            }

            sim.DaySettled += OnDaySettledFromEvent;
            _sourcesAttached = true;
        }

        /// <summary>解除广播订阅（重开对局 / 世界销毁时调用）。</summary>
        public void DetachBroadcastSources(WorldSimulation sim)
        {
            if (!_sourcesAttached || sim == null)
            {
                return;
            }

            if (sim.Bounty != null)
            {
                sim.Bounty.Published -= OnBountyPublished;
            }

            if (sim.Monsters != null)
            {
                sim.Monsters.MonsterEnteredTown -= OnMonsterEnteredTown;
                sim.Monsters.MonsterKilled -= OnMonsterKilled;
            }

            sim.DaySettled -= OnDaySettledFromEvent;
            _sourcesAttached = false;
            _attached = null;
        }

        /// <summary>
        /// <see cref="WorldSimulation.DaySettled"/> 广播入口（委托签名是 <c>Action&lt;int&gt;</c>，
        /// 故与组合根显式调用的双参 <see cref="OnDaySettled(WorldSimulation,int)"/> 分开命名，避免重载混淆）。
        /// </summary>
        private void OnDaySettledFromEvent(int dayOverall)
        {
            if (_attached != null)
            {
                OnDaySettled(_attached, dayOverall);
            }
        }

        private void OnBountyPublished(BountyEntry entry)
        {
            if (_attached == null || entry == null)
            {
                return;
            }

            OnBroadcast(_attached, new MemoryItem(MemoryKind.BountyAvailable, entry.Id, entry.Name, DayOf(_attached)));
        }

        private void OnMonsterEnteredTown(Monster monster)
        {
            if (_attached == null || monster == null || monster.Def == null)
            {
                return;
            }

            OnBroadcast(_attached, new MemoryItem(MemoryKind.MonsterInTown, monster.Def.Id, monster.Def.Name, DayOf(_attached)));
        }

        private void OnMonsterKilled(Monster monster, CharacterData killer)
        {
            if (_attached == null || monster == null || monster.Def == null)
            {
                return;
            }

            var sim = _attached;
            int day = DayOf(sim);
            CollectEligible(sim, _broadcastBuffer);
            for (int i = 0; i < _broadcastBuffer.Count; i++)
            {
                var actor = _broadcastBuffer[i];
                if (actor == null)
                {
                    continue;
                }

                MemoryPoolOf(sim, actor.Id).Forget(MemoryKind.MonsterInTown, monster.Def.Id);

                var action = CurrentActionOf(actor.Id);
                if (action != null && action.Kind == BehaviorKind.HuntMonster && action.TargetId == monster.Def.Id)
                {
                    action.MarkAllStepsDone(); // 目标已消失：作废行动痕迹，下一小时重新决策
                    ForgetAction(actor);
                }
            }

            LastDecisionSummary = "怪物 " + monster.Def.Name + " 被击杀 @D" + day;
        }

        // ------------------------------------------------------------------
        // 主循环：每个游戏小时
        // ------------------------------------------------------------------

        /// <summary>
        /// 每游戏小时推进一次（由组合根在整点分支调用）。对每名
        /// <see cref="IsEligible"/>（存活 + 未隐藏 + 无行动锁定）的角色依次：
        /// <list type="number">
        /// <item>体力 ≤ 0 → <b>强制回城待机</b>（目标 = 城镇中心，行为 <see cref="BehaviorKind.RestAtTown"/>），
        ///   并结算待机恢复（每小时 +1 生命 / +1 体力，出处见 <see cref="ApplyRestRecovery"/>）；</item>
        /// <item>仍在执行且目标有效的交互链 → 继续执行下一步（不重新抽取）；</item>
        /// <item>否则：感知当前位置 + 记忆池穷举可执行交互 → 计算各候选 <b>Weight = Σ 8 项分数变化</b>
        ///   → <b>按权重随机抽取</b> → 执行第一步（移动只设 MoveTarget，原地交互当场生效）。</item>
        /// </list>
        /// </summary>
        public void TickHour(WorldSimulation sim)
        {
            if (sim == null || sim.Population == null)
            {
                return;
            }

            EnsurePools(sim);
            int day = DayOf(sim);

            CollectEligible(sim, _tickBuffer);
            for (int i = 0; i < _tickBuffer.Count; i++)
            {
                var actor = _tickBuffer[i];
                // 战斗可能在本次循环里让角色死亡 / 上锁（人口列表同时被改动），因此每轮重新判定。
                if (!IsEligible(actor))
                {
                    ForgetAction(actor);
                    continue;
                }

                var pool = MemoryPoolOf(sim, actor.Id);
                pool.CurrentDayOverall = day;

                if (actor.Stamina <= 0)
                {
                    ForceRest(sim, actor, pool);
                    continue;
                }

                if (AdvanceCurrent(sim, actor, pool))
                {
                    continue;
                }

                DecideAndStart(sim, actor, pool);
            }
        }

        /// <summary>
        /// ① 体力为 0 → 强制回城待机（《策划案》「体力为 0 时，角色将强制回到城镇中执行待机，直至体力回满」）。
        /// 回城途中（跨图不可达时按矛盾 #15 只能原地）同样按待机口径恢复，等待体力回满后自动恢复决策。
        /// </summary>
        private void ForceRest(WorldSimulation sim, CharacterData actor, MemoryPool pool)
        {
            var restCell = TownRestCellOf(sim, pool);
            var action = new BehaviorAction(actor, BehaviorKind.RestAtTown, restCell, 0);
            if (actor.Position != restCell)
            {
                action.AddStep(BehaviorKind.MoveTo, restCell, 0);
            }

            action.AddStep(BehaviorKind.RestAtTown, restCell, 0);
            action.Weight = ScoreAction(sim, actor, pool, action);
            // 「出发」即视为移动步已执行；随后按待机口径恢复 1 小时。
            action.MarkStepDone();
            ApplyRestRecovery(actor);
            action.MarkStepDone();
            SetCurrent(actor, action);
            LastDecisionSummary = "#" + actor.Id + " 体力 0 → 强制回城待机 " + restCell;
        }

        /// <summary>
        /// 继续执行当前交互链的下一步；无行动 / 行动已结束 / 目标失效时返回 false（由调用方重新决策）。
        /// 行动权重按<b>剩余步骤</b>重算（已执行步不计分，策划案「动态决策」）。
        /// </summary>
        private bool AdvanceCurrent(WorldSimulation sim, CharacterData actor, MemoryPool pool)
        {
            if (!_current.TryGetValue(actor.Id, out var action) || action == null || action.IsFinished || !action.HasPendingSteps)
            {
                return false;
            }

            if (!IsStillExecutable(sim, actor, action))
            {
                action.MarkAllStepsDone();
                ForgetAction(actor);
                return false;
            }

            action.Weight = ScoreAction(sim, actor, pool, action);
            ExecuteNextStep(sim, actor, pool, action);
            return true;
        }

        /// <summary>②③④ 穷举候选 → 计算权重 → 加权轮盘抽取 → 执行第一步。</summary>
        private void DecideAndStart(WorldSimulation sim, CharacterData actor, MemoryPool pool)
        {
            var candidates = CandidateCache(actor.Id);
            EnumerateCandidates(sim, actor, candidates);

            var chosen = DrawWeighted(candidates);
            DecisionsMade++;

            if (chosen == null)
            {
                // 全部候选 0 权重（极端脏数据）：兜底待机一小时，保证角色不会卡死。
                RestOneHour(sim, actor, pool);
                return;
            }

            AdoptAction(sim, actor, pool, chosen);
        }

        /// <summary>采纳候选行动：记录为当前行动（权重按剩余步重算）并执行第一步。</summary>
        private void AdoptAction(WorldSimulation sim, CharacterData actor, MemoryPool pool, BehaviorAction action)
        {
            SetCurrent(actor, action);
            action.Weight = ScoreAction(sim, actor, pool, action);
            ExecuteNextStep(sim, actor, pool, action);
            LastDecisionSummary = "#" + actor.Id + " " + action;
        }

        /// <summary>执行当前行动的第一步：移动只下达 MoveTarget，原地交互当场结算。</summary>
        private void ExecuteNextStep(WorldSimulation sim, CharacterData actor, MemoryPool pool, BehaviorAction action)
        {
            if (action == null)
            {
                return;
            }

            if (!action.HasPendingSteps)
            {
                action.MarkAllStepsDone();
                return;
            }

            var step = action.CurrentStep;

            if (step.Kind == BehaviorKind.MoveTo)
            {
                var dest = ResolveMoveDestination(sim, actor, pool, action, step);
                if (actor.Position != dest)
                {
                    // 移动执行：只设目标，实际每游戏小时走 1 格由 WorldSimulation.MoveCharactersOneStep 驱动。
                    sim.SetMoveTarget(actor, dest);
                }

                action.MarkStepDone();
                return;
            }

            // 交互步必须与目标同坐标（策划案：对话 / 交易 / 交互要求同一坐标）。
            var targetPos = ResolveInteractionPos(sim, actor, pool, step);
            if (actor.Position != targetPos)
            {
                sim.SetMoveTarget(actor, targetPos);
                return; // 还在路上：本小时不结算交互，也不推进步骤
            }

            bool ok;
            switch (step.Kind)
            {
                case BehaviorKind.RestAtTown:
                    ApplyRestRecovery(actor);
                    ok = true;
                    break;
                case BehaviorKind.OrderDrink:
                    ok = ExecuteOrderDrink(sim, actor, pool);
                    break;
                case BehaviorKind.TalkTo:
                    ok = ExecuteTalk(sim, actor, step);
                    break;
                case BehaviorKind.AcceptBounty:
                    ok = ExecuteAcceptBounty(sim, actor, step);
                    break;
                case BehaviorKind.HuntMonster:
                    ok = ExecuteHunt(sim, actor, step);
                    break;
                case BehaviorKind.PickupLoot:
                    ok = ExecutePickupLoot(sim, actor, step);
                    break;
                default:
                    ok = true;
                    break;
            }

            if (!ok)
            {
                // 目标已失效（酒售罄 / 怪物跑了 / 掉落被别人捡走 / 悬赏被接）：作废本行动，下一小时重新决策。
                action.MarkAllStepsDone();
                ForgetAction(actor);
                return;
            }

            if (!actor.IsAlive)
            {
                action.MarkAllStepsDone();
                return;
            }

            action.MarkStepDone();
        }

        // ------------------------------------------------------------------
        // 动态决策
        // ------------------------------------------------------------------

        /// <summary>
        /// 短期记忆广播：写入<b>在场（同图）</b>角色的短期记忆，并触发该角色的动态决策
        /// （重新穷举候选 → 与原行动<b>剩余步骤</b>的权重比较 → 更高才换行动）。
        /// </summary>
        public void OnBroadcast(WorldSimulation sim, MemoryItem item)
        {
            if (sim == null || sim.Population == null || item == null)
            {
                return;
            }

            BroadcastCount++;
            int day = DayOf(sim);
            item.DayOverall = day;

            CollectEligible(sim, _broadcastBuffer);
            for (int i = 0; i < _broadcastBuffer.Count; i++)
            {
                var actor = _broadcastBuffer[i];
                if (!IsEligible(actor))
                {
                    continue;
                }

                if (!IsInAudience(actor, item))
                {
                    continue;
                }

                var pool = MemoryPoolOf(sim, actor.Id);
                pool.CurrentDayOverall = day;
                pool.Broadcast(item);
                Reconsider(sim, actor, pool);
            }
        }

        /// <summary>
        /// 同坐标广播（交互 / 交易 / 对话类事件）：只写入<b>与 <paramref name="pos"/> 同一格</b>的角色短期记忆并触发重抽。
        /// </summary>
        public void OnBroadcastAt(WorldSimulation sim, MemoryItem item, GridPos pos)
        {
            if (sim == null || sim.Population == null || item == null)
            {
                return;
            }

            BroadcastCount++;
            int day = DayOf(sim);
            item.DayOverall = day;

            CollectEligible(sim, _broadcastBuffer);
            for (int i = 0; i < _broadcastBuffer.Count; i++)
            {
                var actor = _broadcastBuffer[i];
                if (!IsEligible(actor) || actor.Position != pos)
                {
                    continue;
                }

                var pool = MemoryPoolOf(sim, actor.Id);
                pool.CurrentDayOverall = day;
                pool.Broadcast(item);
                Reconsider(sim, actor, pool);
            }
        }

        /// <summary>
        /// 动态决策（策划案「动态决策」）：角色执行行动时若短期记忆有更新，
        /// 重新穷举交互链并计算权重，与<b>原行动权重（只算未执行的剩余步骤）</b>比较，
        /// 新候选更高就改执行新行动。返回 true 表示确实换了行动。
        /// </summary>
        public bool Reconsider(WorldSimulation sim, CharacterData actor)
        {
            if (sim == null || !IsEligible(actor))
            {
                return false;
            }

            return Reconsider(sim, actor, MemoryPoolOf(sim, actor.Id));
        }

        private bool Reconsider(WorldSimulation sim, CharacterData actor, MemoryPool pool)
        {
            var current = CurrentActionOf(actor.Id);
            int currentWeight = current != null && !current.IsFinished
                ? ScoreAction(sim, actor, pool, current)
                : 0;

            var candidates = CandidateCache(actor.Id);
            EnumerateCandidates(sim, actor, candidates);

            var best = BestCandidate(candidates, current);
            if (best == null || best.Weight <= currentWeight)
            {
                if (current != null)
                {
                    current.Weight = currentWeight;
                }

                return false;
            }

            if (current != null)
            {
                // 原行动保留已执行步作为执行痕迹，剩余步骤不再计分、不再执行。
                current.MarkAllStepsDone();
            }

            ReconsiderCount++;
            AdoptAction(sim, actor, pool, best);
            LastDecisionSummary = "#" + actor.Id + " 动态改判: 原权重 " + currentWeight + " → " + best.Weight + "（" + best.Kind + "）";
            return true;
        }

        // ------------------------------------------------------------------
        // 候选穷举（感知当前位置 + 记忆池）
        // ------------------------------------------------------------------

        /// <summary>
        /// 穷举该角色此刻可执行的全部交互链（一条链 = 一个行动），逐条算权重后只保留 <b>Weight &gt; 0</b> 的候选。
        /// 覆盖 7 类行为：待机 / 点单饮酒 / 狩猎在城怪物 / 接悬赏 / 同坐标对话 / 拾取掉落 / 闲逛兜底。
        /// </summary>
        public int EnumerateCandidates(WorldSimulation sim, CharacterData actor, List<BehaviorAction> results)
        {
            if (results == null)
            {
                return 0;
            }

            results.Clear();
            if (sim == null || !IsEligible(actor))
            {
                return 0;
            }

            var pool = MemoryPoolOf(sim, actor.Id);

            // ⑦ 待机（城镇内恢复体力 / 生命）：随时的低风险去处，也是体力见底时的落点。
            TryAddCandidate(sim, actor, pool, results, BehaviorKind.RestAtTown, TownRestCellOf(sim, pool), 0, true);

            // ⑥ 点单饮酒：到吧台点一杯（精神 +10 占位、按售价折算财富负分）。
            TryAddCandidate(sim, actor, pool, results, BehaviorKind.OrderDrink, BarCellOf(sim, pool), 0, true);

            // ① 狩猎在城怪物（交互对象来自记忆池 MemoryKind.MonsterInTown）。
            AddHuntCandidates(sim, actor, pool, results);

            // ⑥ 接悬赏（交互对象来自记忆池 MemoryKind.BountyAvailable；战士优先）。
            AddBountyCandidates(sim, actor, pool, results);

            // ④ 对话：必须与目标同坐标（好感 → 社交分；对话获取信息 → 信息分 +1）。
            AddTalkCandidates(sim, actor, pool, results);

            // ⑥ 拾取掉落（同图任意角色都能拾取，行为案：死亡背包物品可被任何人拾取）。
            AddLootCandidates(sim, actor, pool, results);

            // ③ 移动 / 闲逛兜底：保证候选集永不为空。
            TryAddCandidate(sim, actor, pool, results, BehaviorKind.MoveTo, RandomWalkableCell(sim, actor), 0, true);

            return results.Count;
        }

        private void AddHuntCandidates(WorldSimulation sim, CharacterData actor, MemoryPool pool, List<BehaviorAction> results)
        {
            var remembered = pool.MonsterDefIdsInTown; // 短期记忆：在城怪物（当日有效）
            for (int i = 0; i < remembered.Count; i++)
            {
                int defId = remembered[i];
                var monster = FindMonsterInMap(sim, actor.Position.MapId, defId);
                if (monster == null)
                {
                    pool.Forget(MemoryKind.MonsterInTown, defId); // 怪物已离开 / 死亡：记忆过期即遗忘
                    continue;
                }

                TryAddCandidate(sim, actor, pool, results, BehaviorKind.HuntMonster, monster.Position, defId, true);
            }
        }

        private void AddBountyCandidates(WorldSimulation sim, CharacterData actor, MemoryPool pool, List<BehaviorAction> results)
        {
            var remembered = pool.AvailableBountyIds; // 短期记忆：可接悬赏（当日有效）
            for (int i = 0; i < remembered.Count; i++)
            {
                int bountyId = remembered[i];
                if (sim.Bounty == null || !IsBountyOpenForNpc(sim.Bounty.FindById(bountyId)))
                {
                    pool.Forget(MemoryKind.BountyAvailable, bountyId);
                    continue;
                }

                TryAddCandidate(sim, actor, pool, results, BehaviorKind.AcceptBounty, BountyCellOf(sim, pool), bountyId, true);
            }
        }

        private void AddTalkCandidates(WorldSimulation sim, CharacterData actor, MemoryPool pool, List<BehaviorAction> results)
        {
            CollectAll(sim, _scanBuffer);
            for (int i = 0; i < _scanBuffer.Count; i++)
            {
                var other = _scanBuffer[i];
                if (other == null || other.Id == actor.Id || !other.IsAlive || other.IsHidden)
                {
                    continue;
                }

                if (other.Position != actor.Position)
                {
                    continue; // 对话必须同一坐标（策划案「对话」）
                }

                TryAddCandidate(sim, actor, pool, results, BehaviorKind.TalkTo, other.Position, other.Id, false);
            }
        }

        private void AddLootCandidates(WorldSimulation sim, CharacterData actor, MemoryPool pool, List<BehaviorAction> results)
        {
            if (sim.GroundLoot == null)
            {
                return;
            }

            for (int i = 0; i < sim.GroundLoot.Count; i++)
            {
                var bag = sim.GroundLoot[i];
                if (bag == null || bag.IsEmpty || bag.Pos.MapId != actor.Position.MapId)
                {
                    continue;
                }

                // TargetId 用 GroundLoot 的下标标识掉落包（LootBag 本身没有 id）。
                TryAddCandidate(sim, actor, pool, results, BehaviorKind.PickupLoot, bag.Pos, i, true);
            }
        }

        /// <summary>
        /// 构造一条候选行动（必要时自动补一步移动）并按剩余步计算权重；<b>Weight ≤ 0 的候选直接丢弃</b>
        /// （策划案：角色始终以提高分数为目标，负收益行动不会被抽中）。
        /// </summary>
        private void TryAddCandidate(WorldSimulation sim, CharacterData actor, MemoryPool pool, List<BehaviorAction> results,
            BehaviorKind kind, GridPos destination, int targetId, bool needMove)
        {
            var action = new BehaviorAction(actor, kind, destination, targetId);
            if (kind == BehaviorKind.MoveTo)
            {
                // 纯移动（闲逛兜底）：移动步即行动本身，只保留一步。若按通用模式再追加一次 kind 步，
                // 行动会变成「MoveTo + MoveTo」两步，<see cref="ScoreAction"/> 的「纯闲逛单步」特判
                // （Steps.Count == 1）永远不命中，按通用口径计成双倍体力负分 → 闲逛候选恒被过滤。
                // 实证（任务：角色不移动）：满属性开局所有候选权重 ≤0，全员原地兜底待机，20 游戏小时 0 移动。
                action.AddStep(BehaviorKind.MoveTo, destination, targetId);
            }
            else
            {
                if (needMove && actor.Position != destination)
                {
                    action.AddStep(BehaviorKind.MoveTo, destination, targetId);
                }

                action.AddStep(kind, destination, targetId);
            }

            int weight = ScoreAction(sim, actor, pool, action);
            if (weight <= 0)
            {
                return;
            }

            action.Weight = weight;
            results.Add(action);
        }

        /// <summary>
        /// 行动权重 = 剩余步骤的分数变化之和（<b>已执行步不计分</b>：从 <see cref="BehaviorAction.ExecutedCount"/> 开始累加）。
        /// 无步骤的行动按主类目计分。
        /// </summary>
        private int ScoreAction(WorldSimulation sim, CharacterData actor, MemoryPool pool, BehaviorAction action)
        {
            if (action == null)
            {
                return 0;
            }

            // ③ 纯闲逛（只有一步 MoveTo）：按「闲逛净收益 + 兜底最小权重」计分，
            // 否则纯移动只有负的体力成本、永远被过滤掉，候选集会在远离城镇中心时为空。
            if (action.Kind == BehaviorKind.MoveTo && action.Steps.Count == 1 && action.Steps[0].Kind == BehaviorKind.MoveTo)
            {
                return ScoreWander(actor, action.Steps[0].Destination);
            }

            int total = 0;
            for (int i = Math.Max(0, action.ExecutedCount); i < action.Steps.Count; i++)
            {
                total += ScoreStep(sim, actor, pool, action.Steps[i]);
                if (total >= MaxWeightPerAction)
                {
                    return MaxWeightPerAction;
                }
            }

            if (action.Steps.Count == 0)
            {
                return 0;
            }

            return total;
        }

        /// <summary>
        /// 单步的分数变化。分数口径（master-plan M11「分数变化口径」）：
        /// 体力 = −移动格数；健康 = 预计 hp 变化映射的档位差；战斗 = 装备差距（当前行为不直接改装备，记 0）；
        /// 荣誉 = 可击杀目标四维和 ÷ 4；精神 = 饮酒 +10（占位）；社交 = 对话对象好感变化；信息 = +1/条。
        /// <b>统一说明</b>：每小时体力/精神 -1 由 <c>WorldSimulation.ApplyHourlyDrift</c> 对所有角色统一结算，
        /// 对每个候选都是同一个常数，故不在单个候选里重复计分（否则会抵消掉行动之间的真实差异）。
        /// </summary>
        private int ScoreStep(WorldSimulation sim, CharacterData actor, MemoryPool pool, BehaviorStep step)
        {
            switch (step.Kind)
            {
                case BehaviorKind.MoveTo:
                    return -MoveStaminaCost(actor, step.Destination);
                case BehaviorKind.RestAtTown:
                    return ScoreRest(actor);
                case BehaviorKind.OrderDrink:
                    return ScoreOrderDrink(sim, actor);
                case BehaviorKind.TalkTo:
                    return ScoreTalk(sim, actor, step);
                case BehaviorKind.AcceptBounty:
                    return ScoreBounty(sim, actor, pool, step);
                case BehaviorKind.HuntMonster:
                    return ScoreHunt(sim, actor, pool, step);
                case BehaviorKind.PickupLoot:
                    return ScoreLoot(sim, actor, step);
                default:
                    return 0;
            }
        }

        /// <summary>
        /// ③ 闲逛兜底的权重：<b>精神 +1</b>（占位：换心情）− 移动消耗的体力，再加最小权重保证候选集非空。
        /// 移动本身的体力成本是负收益，所以「净收益 ≤ 0」时也保留 <see cref="BaseWanderWeight"/>（兜底口径，待策划给出探索收益）。
        /// </summary>
        private static int ScoreWander(CharacterData actor, GridPos destination)
        {
            int spirit = CappedGain(actor.Spirit, WanderSpiritGain, MaxSpirit);
            int net = spirit - MoveStaminaCost(actor, destination);
            return net > 0 ? net + BaseWanderWeight : BaseWanderWeight;
        }

        /// <summary>⑦ 待机：+1 生命 / +1 体力（计入体力上限 100 以内的部分）+ 对应健康分档位差 + 基础权重。</summary>
        private static int ScoreRest(CharacterData actor)
        {
            int hpGain = 0;
            int maxHp = Math.Max(1, actor.EffMaxHp);
            if (actor.Hp < maxHp)
            {
                hpGain = Math.Min(RestHpPerHour, maxHp - actor.Hp);
            }

            int staminaGain = CappedGain(actor.Stamina, RestStaminaPerHour, MaxStamina);
            int health = ProjectedHealthDelta(actor, hpGain);
            return hpGain + staminaGain + health + BaseRestWeight;
        }

        /// <summary>
        /// ⑥ 点单饮酒：<b>精神 +10</b>（占位，矛盾 #19；精神上限 100，超出部分不计）
        /// + <b>财富 −售价</b>（占位：点单由酒馆钱包当场入账，此处按售价折算角色的财富损失，压制无脑点单）
        /// + 信息 +1（与酒保交互获得酒馆商品信息）+ 性格×酒标签匹配加分（占位）。
        /// 无在售酒 / 买不起（占位判定：金币 ≥ 最低售价）时不生成候选。
        /// </summary>
        private int ScoreOrderDrink(WorldSimulation sim, CharacterData actor)
        {
            var tavern = sim.Tavern;
            if (tavern == null)
            {
                return 0;
            }

            int cheapest = int.MaxValue;
            bool personalityMatch = false;
            var shelf = tavern.Shelf;
            for (int i = 0; i < shelf.Count; i++)
            {
                int wineId = shelf[i];
                if (tavern.RemainingCountOf(wineId) <= 0)
                {
                    continue; // 当日售罄
                }

                var def = ItemCatalog.Get(wineId);
                int price = def != null ? def.Price : 0;
                if (price < cheapest)
                {
                    cheapest = price;
                }

                if (def != null && MatchesPersonality(actor.Personality, def))
                {
                    personalityMatch = true;
                }
            }

            if (cheapest == int.MaxValue || actor.Gold < cheapest)
            {
                return 0;
            }

            int spirit = CappedGain(actor.Spirit, DrinkSpiritGain, MaxSpirit);
            int info = InfoGainPerInfo;
            int bonus = personalityMatch ? PersonalityMatchBonus : 0;
            return spirit - cheapest + info + bonus;
        }

        /// <summary>
        /// ① 狩猎在城怪物：<b>荣誉 += 怪物四维和 ÷ 4</b>（<see cref="CharacterScores.HonorForKill"/>）
        /// 并<b>按战斗分差估算的成功率打折</b>；再减去按预计掉血映射的健康分档位差。
        /// 战斗分本身不变（狩猎不直接改变力量/智力/防御，记 0）。
        /// </summary>
        private int ScoreHunt(WorldSimulation sim, CharacterData actor, MemoryPool pool, BehaviorStep step)
        {
            var monster = FindMonsterInMap(sim, actor.Position.MapId, step.TargetId);
            if (monster == null || monster.Def == null || !pool.HasMonsterInTown(monster.Def.Id))
            {
                return 0;
            }

            double winRate = EstimateWinRate01(actor, monster);
            if (winRate * 100 < MinWinRatePercentToHunt)
            {
                return 0; // 占位阈值：胜率过低不主动接战
            }

            var def = monster.Def;
            int honor = CharacterScores.HonorForKill(def.Hp, def.Str, def.Int, def.Def);
            int honorTerm = (int)(honor * winRate);
            int health = ProjectedHealthDelta(actor, -ExpectedDamageVs(actor, monster));
            return honorTerm + health;
        }

        /// <summary>
        /// ⑥ 接悬赏：<b>荣誉</b>按目标怪物四维和 ÷ 4 预估并按占位命中率与职业权重打折（战士优先）
        /// + <b>信息 +1</b>（与悬赏栏交互可获得其中的悬赏信息）。财富不加分：赏金在 0:00 由 M5 结算给发布者（玩家）。
        /// </summary>
        private int ScoreBounty(WorldSimulation sim, CharacterData actor, MemoryPool pool, BehaviorStep step)
        {
            if (sim.Bounty == null || !pool.HasBounty(step.TargetId))
            {
                return 0;
            }

            var entry = sim.Bounty.FindById(step.TargetId);
            if (!IsBountyOpenForNpc(entry))
            {
                return 0;
            }

            var target = FindMonsterDefByName(sim, entry.TargetMonsterName);
            int honor = target != null
                ? CharacterScores.HonorForKill(target.Hp, target.Str, target.Int, target.Def)
                : 0;

            int honorTerm = honor * BountyHonorRatePercent / 100 * BountyProfessionPercent(actor) / 100;
            return honorTerm + InfoGainPerInfo;
        }

        /// <summary>④ 对话：<b>社交分</b> = 其他角色对本角色的好感之和 → 折算成本次对话的好感增量（好感上限 100，超出部分不计）+ 信息 +1。</summary>
        private int ScoreTalk(WorldSimulation sim, CharacterData actor, BehaviorStep step)
        {
            if (!TryFindCharacter(sim, step.TargetId, out var other) || other == null)
            {
                return 0;
            }

            if (other.Id == actor.Id || !other.IsAlive || other.IsHidden || other.Position != actor.Position)
            {
                return 0;
            }

            int relation = sim.Population != null ? sim.Population.GetRelation(actor.Id, other.Id) : 0;
            int favorGain = CappedGain(Math.Max(0, relation), TalkFavorGain, MaxFavor);
            return favorGain + InfoGainPerInfo;
        }

        /// <summary>⑥ 拾取掉落：<b>财富分 = 金币数量</b> → 掉落包内金币 + 物品基础价值（占位：按 <see cref="TradeSession.UnitValue"/> 全额计入）。</summary>
        private int ScoreLoot(WorldSimulation sim, CharacterData actor, BehaviorStep step)
        {
            var bag = FindLootBag(sim, step.TargetId);
            if (bag == null || bag.IsEmpty || bag.Pos.MapId != actor.Position.MapId)
            {
                return 0;
            }

            int wealth = bag.Gold;
            foreach (var pair in bag.Items)
            {
                wealth += TradeSession.UnitValue(pair.Key) * pair.Value;
            }

            return wealth;
        }

        // ------------------------------------------------------------------
        // 各行为的执行
        // ------------------------------------------------------------------

        /// <summary>
        /// 客人点单闭环：到店后调 <see cref="TavernSystem.GuestOrderRandom"/>（售价当场入账，经营案 10.13），
        /// 成功则 <b>精神 +10</b>（占位常量，矛盾 #19；精神上限 100，超出部分不计）并把该酒写进自己的短期记忆。
        /// </summary>
        private bool ExecuteOrderDrink(WorldSimulation sim, CharacterData actor, MemoryPool pool)
        {
            var tavern = sim.Tavern;
            if (tavern == null || !actor.IsAlive || actor.IsHidden)
            {
                return false;
            }

            int wineId = tavern.GuestOrderRandom(actor, _random);
            if (wineId < 0)
            {
                return false;
            }

            int gain = CappedGain(actor.Spirit, DrinkSpiritGain, MaxSpirit);
            if (gain > 0)
            {
                actor.Spirit = Math.Min(MaxSpirit, actor.Spirit + gain);
            }

            actor.InfoScore += InfoGainPerInfo; // 与酒保交互获得酒馆商品信息

            var def = ItemCatalog.Get(wineId);
            pool.Broadcast(MemoryKind.ShopGoods, wineId, def != null ? def.Name : ("酒#" + wineId), DayOf(sim));
            return true;
        }

        /// <summary>④ 对话执行：好感 +1（钳到上限 100）与信息分 +1。</summary>
        private bool ExecuteTalk(WorldSimulation sim, CharacterData actor, BehaviorStep step)
        {
            if (sim.Population == null || !TryFindCharacter(sim, step.TargetId, out var other) || other == null)
            {
                return false;
            }

            if (!other.IsAlive || other.IsHidden || other.Position != actor.Position)
            {
                return false;
            }

            int relation = sim.Population.GetRelation(actor.Id, other.Id);
            int gain = CappedGain(Math.Max(0, relation), TalkFavorGain, MaxFavor);
            if (gain > 0)
            {
                sim.Population.ChangeRelation(actor.Id, other.Id, gain);
            }

            actor.InfoScore += InfoGainPerInfo; // 对话能够使角色获取信息
            return true;
        }

        /// <summary>
        /// ⑥ 接悬赏执行：走到布告栏 → 交互获得悬赏信息（<b>信息分 +1</b>）；
        /// 战士额外按 <see cref="ProfessionService.WarriorAccept(CharacterData, BountyEntry)"/> 判定接取资格。
        /// <b>占位说明</b>：M5 未提供 NPC「接取 / 占槽」写入口，槽位与 0:00 入账仍归悬赏系统，
        /// 本里程碑只做「资格判定 + 信息获取」，战斗悬赏的实际闭环由 M5（占槽/结算）+ M10（战斗）承担。
        /// </summary>
        private bool ExecuteAcceptBounty(WorldSimulation sim, CharacterData actor, BehaviorStep step)
        {
            if (sim.Bounty == null)
            {
                return false;
            }

            var entry = sim.Bounty.FindById(step.TargetId);
            if (!IsBountyOpenForNpc(entry))
            {
                return false;
            }

            if (actor.Profession == ProfessionType.Warrior && sim.Professions != null)
            {
                bool eligible = sim.Professions.WarriorAccept(actor, entry);
                if (eligible)
                {
                    LastDecisionSummary = "#" + actor.Id + " 战士可接悬赏 #" + entry.Id + " " + entry.Name;
                }
            }

            actor.InfoScore += InfoGainPerInfo; // 与悬赏栏交互 → 获得其中的悬赏信息
            return true;
        }

        /// <summary>
        /// ① 狩猎执行：走到怪物同格后交给 <see cref="CombatSystem.FightMonster"/>（M10 回合制战斗）：
        /// 胜 → <see cref="MonsterSystem.KillMonster"/> + 荣誉 += 四维和 ÷ 4（由战斗系统入账）；败 → 死亡并掉落。
        /// </summary>
        private bool ExecuteHunt(WorldSimulation sim, CharacterData actor, BehaviorStep step)
        {
            var monster = FindMonsterInMap(sim, actor.Position.MapId, step.TargetId);
            if (monster == null || !monster.IsAlive)
            {
                return false;
            }

            if (actor.Position != monster.Position)
            {
                sim.SetMoveTarget(actor, monster.Position);
                return true; // 还在赶路
            }

            var result = _combat.FightMonster(sim, actor, monster);
            if (result == null || !result.HasResult)
            {
                return false;
            }

            LastDecisionSummary = "#" + actor.Id + " 与 " + monster.Def.Name + " 交战：" + result.ToString();
            return true;
        }

        /// <summary>⑥ 拾取掉落执行：<b>财富 +金币 / +物品</b>（<see cref="WorldSimulation.PickupLoot"/> 要求同坐标）。</summary>
        private bool ExecutePickupLoot(WorldSimulation sim, CharacterData actor, BehaviorStep step)
        {
            var bag = FindLootBag(sim, step.TargetId);
            if (bag == null || bag.IsEmpty)
            {
                return false;
            }

            return sim.PickupLoot(actor, bag);
        }

        /// <summary>
        /// 待机恢复（策划案「待机」：每小时回复 1 点生命值、1 点体力值）。
        /// 注意：每小时 -1 体力由 <c>WorldSimulation.ApplyHourlyDrift</c> 统一结算，
        /// 因此「待机 +1」在净效果上表现为体力不继续下降 + 生命每小时回 1 点，直到体力回满后恢复正常决策。
        /// </summary>
        private static void ApplyRestRecovery(CharacterData actor)
        {
            int maxHp = Math.Max(0, actor.EffMaxHp);
            if (actor.Hp < maxHp)
            {
                actor.Hp = Math.Min(maxHp, actor.Hp + RestHpPerHour);
            }

            if (actor.Stamina < MaxStamina)
            {
                actor.Stamina = Math.Min(MaxStamina, actor.Stamina + RestStaminaPerHour);
            }
        }

        /// <summary>兜底：原地待机一小时（候选全被过滤时的最后保底）。</summary>
        private void RestOneHour(WorldSimulation sim, CharacterData actor, MemoryPool pool)
        {
            var action = new BehaviorAction(actor, BehaviorKind.RestAtTown, actor.Position, 0);
            action.AddStep(BehaviorKind.RestAtTown, actor.Position, 0);
            action.Weight = ScoreAction(sim, actor, pool, action);
            ApplyRestRecovery(actor);
            SetCurrent(actor, action);
            action.MarkStepDone();
            DecisionsMade++;
            LastDecisionSummary = "#" + actor.Id + " 无正收益候选 → 兜底待机";
        }

        // ------------------------------------------------------------------
        // 广播 / 日结
        // ------------------------------------------------------------------

        /// <summary>0:00 日结：短期记忆<b>当日有效</b>，整池清空后重新播一遍当日在城怪物与可接悬赏。</summary>
        public void OnDaySettled(WorldSimulation sim, int dayOverall)
        {
            if (sim == null || dayOverall == _lastSettledDay)
            {
                return; // 同一天只结算一次：组合根 AdvanceOneMinute 显式调用与 DaySettled 广播是两个入口
            }

            _lastSettledDay = dayOverall;

            CollectAll(sim, _poolBuffer);
            for (int i = 0; i < _poolBuffer.Count; i++)
            {
                var c = _poolBuffer[i];
                if (c != null)
                {
                    MemoryPoolOf(sim, c.Id).ClearDaily(dayOverall);
                }
            }

            SeedDailyMemories(sim, dayOverall);
        }

        /// <summary>当日重播：把当下仍在城里的怪物与仍可接的悬赏写进各角色短期记忆（「持续广播」的等价实现）。</summary>
        private void SeedDailyMemories(WorldSimulation sim, int dayOverall)
        {
            if (sim.Monsters == null)
            {
                return;
            }

            var monsters = sim.Monsters.AliveMonstersInMap(WorldMapIds.Town);
            for (int i = 0; i < monsters.Count; i++)
            {
                var m = monsters[i];
                if (m != null && m.Def != null)
                {
                    OnBroadcast(sim, new MemoryItem(MemoryKind.MonsterInTown, m.Def.Id, m.Def.Name, dayOverall));
                }
            }

            if (sim.Bounty == null)
            {
                return;
            }

            var entries = sim.Bounty.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (IsBountyOpenForNpc(entry))
                {
                    OnBroadcast(sim, new MemoryItem(MemoryKind.BountyAvailable, entry.Id, entry.Name, dayOverall));
                }
            }
        }

        /// <summary>广播受众：同图即可感知（悬赏 / 怪物 / 特价 / 商品都在城镇侧；安全分通知面向全员）。</summary>
        private static bool IsInAudience(CharacterData actor, MemoryItem item)
        {
            if (item.Kind == MemoryKind.SafetyChanged)
            {
                return true;
            }

            return actor.Position.MapId == WorldMapIds.Town;
        }

        // ------------------------------------------------------------------
        // 目标解析
        // ------------------------------------------------------------------

        /// <summary>目标是否仍然存在（交互链是否还能继续）；失效则作废。</summary>
        private bool IsStillExecutable(WorldSimulation sim, CharacterData actor, BehaviorAction action)
        {
            for (int i = action.ExecutedCount; i < action.Steps.Count; i++)
            {
                var step = action.Steps[i];
                switch (step.Kind)
                {
                    case BehaviorKind.HuntMonster:
                        if (FindMonsterInMap(sim, actor.Position.MapId, step.TargetId) == null)
                        {
                            return false;
                        }

                        break;
                    case BehaviorKind.AcceptBounty:
                        if (sim.Bounty == null || !IsBountyOpenForNpc(sim.Bounty.FindById(step.TargetId)))
                        {
                            return false;
                        }

                        break;
                    case BehaviorKind.PickupLoot:
                    {
                        var bag = FindLootBag(sim, step.TargetId);
                        if (bag == null || bag.IsEmpty)
                        {
                            return false;
                        }

                        break;
                    }
                    case BehaviorKind.TalkTo:
                        if (!TryFindCharacter(sim, step.TargetId, out _))
                        {
                            return false;
                        }

                        break;
                    case BehaviorKind.OrderDrink:
                        if (sim.Tavern == null)
                        {
                            return false;
                        }

                        break;
                }
            }

            return true;
        }

        /// <summary>移动步的目的地：优先跟随交互目标的实时位置（怪物会走、对话对象会动），否则用步里记录的坐标。</summary>
        private GridPos ResolveMoveDestination(WorldSimulation sim, CharacterData actor, MemoryPool pool, BehaviorAction action, BehaviorStep step)
        {
            var next = action.GetStep(action.ExecutedCount + 1);
            if (next != null && next.Kind != BehaviorKind.MoveTo)
            {
                return ResolveInteractionPos(sim, actor, pool, next);
            }

            return step.Destination;
        }

        /// <summary>交互步的目标坐标（对话 / 交互要求与对象同坐标；目标丢失时原地不动）。</summary>
        private static GridPos ResolveInteractionPos(WorldSimulation sim, CharacterData actor, MemoryPool pool, BehaviorStep step)
        {
            switch (step.Kind)
            {
                case BehaviorKind.OrderDrink:
                    return BarCellOf(sim, pool);
                case BehaviorKind.AcceptBounty:
                    return BountyCellOf(sim, pool);
                case BehaviorKind.RestAtTown:
                    return actor.Position.MapId == pool.TownPos.MapId ? TownRestCellOf(sim, pool) : actor.Position;
                case BehaviorKind.TalkTo:
                    return TryFindCharacter(sim, step.TargetId, out var other) && other != null ? other.Position : actor.Position;
                case BehaviorKind.HuntMonster:
                {
                    var monster = FindMonsterInMap(sim, actor.Position.MapId, step.TargetId);
                    return monster != null ? monster.Position : actor.Position;
                }

                case BehaviorKind.PickupLoot:
                {
                    var bag = FindLootBag(sim, step.TargetId);
                    return bag != null ? bag.Pos : actor.Position;
                }

                default:
                    return step.Destination;
            }
        }

        /// <summary>吧台服务格：酒馆坐标所在的图上，优先 <see cref="WorldMap.TavernBarPos"/>（建筑阻挡移动时客人在此格点单）。</summary>
        private static GridPos BarCellOf(WorldSimulation sim, MemoryPool pool)
        {
            var bar = new GridPos(pool.TavernPos.MapId, WorldMap.TavernBarPos.X, WorldMap.TavernBarPos.Y);
            return NearestWalkable(sim, bar, pool.TavernPos);
        }

        /// <summary>悬赏布告栏服务格：<see cref="WorldMap.BountyBoardPos"/> 本身是障碍物，故取其相邻可行走格。</summary>
        private static GridPos BountyCellOf(WorldSimulation sim, MemoryPool pool)
        {
            var board = new GridPos(pool.TownPos.MapId, WorldMap.BountyBoardPos.X, WorldMap.BountyBoardPos.Y);
            return NearestWalkable(sim, board, pool.TownPos);
        }

        /// <summary>城镇待机格：城镇中心的可行走邻格（待机恢复不要求精确站位，取最近可行走格即可）。</summary>
        private static GridPos TownRestCellOf(WorldSimulation sim, MemoryPool pool)
        {
            var center = new GridPos(pool.TownPos.MapId, WorldMap.TownCenter.X, WorldMap.TownCenter.Y);
            return NearestWalkable(sim, center, pool.TownPos);
        }

        /// <summary>取目标格自身或最近（曼哈顿半径 ≤ 2）的可行走格；都不可走时回退给定兜底格。</summary>
        private static GridPos NearestWalkable(WorldSimulation sim, GridPos target, GridPos fallback)
        {
            var map = sim != null && sim.Map != null ? sim.Map.GetMap(target.MapId) : null;
            if (map == null)
            {
                return fallback;
            }

            if (map.IsWalkable(target.X, target.Y))
            {
                return target;
            }

            for (int radius = 1; radius <= 2; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (Math.Abs(dx) + Math.Abs(dy) != radius)
                        {
                            continue;
                        }

                        int x = target.X + dx;
                        int y = target.Y + dy;
                        if (map.IsWalkable(x, y))
                        {
                            return new GridPos(target.MapId, x, y);
                        }
                    }
                }
            }

            return fallback;
        }

        /// <summary>随机闲逛落点（同图内随机可行走格）。</summary>
        private GridPos RandomWalkableCell(WorldSimulation sim, CharacterData actor)
        {
            var map = sim != null && sim.Map != null ? sim.Map.GetMap(actor.Position.MapId) : null;
            if (map == null)
            {
                return actor.Position;
            }

            for (int i = 0; i < WanderTryCount; i++)
            {
                int x = _random.Next(0, GridMap.Width);
                int y = _random.Next(0, GridMap.Height);
                if (map.IsWalkable(x, y))
                {
                    return new GridPos(actor.Position.MapId, x, y);
                }
            }

            return actor.Position;
        }

        private static Monster FindMonsterInMap(WorldSimulation sim, int mapId, int monsterDefId)
        {
            var monsters = sim != null && sim.Monsters != null ? sim.Monsters.Monsters : null;
            if (monsters == null)
            {
                return null;
            }

            for (int i = 0; i < monsters.Count; i++)
            {
                var m = monsters[i];
                if (m != null && m.IsAlive && m.Def != null && m.Def.Id == monsterDefId && m.Position.MapId == mapId)
                {
                    return m;
                }
            }

            return null;
        }

        private static MonsterDef FindMonsterDefByName(WorldSimulation sim, string monsterName)
        {
            var catalog = sim != null && sim.Monsters != null ? sim.Monsters.Catalog : null;
            if (catalog == null || string.IsNullOrEmpty(monsterName))
            {
                return null;
            }

            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog[i] != null && string.Equals(catalog[i].Name, monsterName, StringComparison.Ordinal))
                {
                    return catalog[i];
                }
            }

            return null;
        }

        private static LootBag FindLootBag(WorldSimulation sim, int index)
        {
            if (sim == null || sim.GroundLoot == null || index < 0 || index >= sim.GroundLoot.Count)
            {
                return null;
            }

            return sim.GroundLoot[index];
        }

        private static bool TryFindCharacter(WorldSimulation sim, int characterId, out CharacterData found)
        {
            found = null;
            var population = sim != null ? sim.Population : null;
            if (population == null)
            {
                return false;
            }

            for (int i = 0; i < population.Residents.Count; i++)
            {
                if (population.Residents[i].Id == characterId)
                {
                    found = population.Residents[i];
                    return true;
                }
            }

            for (int i = 0; i < population.Visitors.Count; i++)
            {
                if (population.Visitors[i].Id == characterId)
                {
                    found = population.Visitors[i];
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------
        // 加权轮盘
        // ------------------------------------------------------------------

        /// <summary>
        /// 按权重随机抽取（<see cref="IRandomSource"/> 加权轮盘）：候选里 Weight ≤ 0 的不参与；
        /// 全部为 0 时返回 null（由调用方走待机兜底）。
        /// </summary>
        private BehaviorAction DrawWeighted(List<BehaviorAction> candidates)
        {
            int total = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Weight > 0)
                {
                    total += candidates[i].Weight;
                }
            }

            if (total <= 0)
            {
                return null;
            }

            int roll = _random.Next(0, total);
            BehaviorAction last = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Weight <= 0)
                {
                    continue;
                }

                last = candidates[i];
                roll -= candidates[i].Weight;
                if (roll < 0)
                {
                    return candidates[i];
                }
            }

            return last;
        }

        /// <summary>动态决策用：候选里权重最高的一条（跳过当前行动本身）。</summary>
        private static BehaviorAction BestCandidate(List<BehaviorAction> candidates, BehaviorAction current)
        {
            BehaviorAction best = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate == null || candidate.Weight <= 0 || ReferenceEquals(candidate, current))
                {
                    continue;
                }

                if (best == null || candidate.Weight > best.Weight)
                {
                    best = candidate;
                }
            }

            return best;
        }

        // ------------------------------------------------------------------
        // 小工具
        // ------------------------------------------------------------------

        /// <summary>有上限的分数增量：<b>超出上限部分不计</b>（策划案「权重计算」）。</summary>
        private static int CappedGain(int current, int gain, int cap)
        {
            if (gain <= 0 || current >= cap)
            {
                return 0;
            }

            return Math.Min(gain, cap - current);
        }

        /// <summary>移动的体力成本：<b>每移动 1 格 -1 体力</b>；跨图不可达时按 0 计（本期仅图内移动，矛盾 #15）。</summary>
        private static int MoveStaminaCost(CharacterData actor, GridPos destination)
        {
            int distance = actor.Position.ManhattanDistanceTo(destination);
            if (distance <= 0 || distance == int.MaxValue)
            {
                return 0;
            }

            return distance;
        }

        /// <summary>健康分变化：按「预计 hp 变化」重新映射档位后的差值（<see cref="CharacterScores.HealthDriftPerHour"/>）。</summary>
        private static int ProjectedHealthDelta(CharacterData actor, int hpDelta)
        {
            int maxHp = Math.Max(1, actor.EffMaxHp);
            int before = CharacterScores.HealthDriftPerHour(actor.Hp, maxHp);
            int projected = Math.Max(0, Math.Min(maxHp, actor.Hp + hpDelta));
            int after = CharacterScores.HealthDriftPerHour(projected, maxHp);
            return after - before;
        }

        /// <summary>战斗分差估算的胜率（0-1）：<c>打倒对方所需回合 / 双方所需回合之和</c>，与 M10 同伤害公式。</summary>
        private static double EstimateWinRate01(CharacterData actor, Monster monster)
        {
            int turnsToKill = CeilDiv(Math.Max(1, monster.Hp), CombatSystem.AttackDamage(actor, monster.Def.Def));
            int turnsToDie = CeilDiv(Math.Max(1, actor.Hp), CombatSystem.AttackDamageVsCharacter(monster, actor));
            return (double)turnsToDie / (turnsToKill + turnsToDie);
        }

        /// <summary>预计承受的伤害（按击杀怪物所需回合 × 怪物单回合伤害，封顶不致死）。</summary>
        private static int ExpectedDamageVs(CharacterData actor, Monster monster)
        {
            int turnsToKill = CeilDiv(Math.Max(1, monster.Hp), CombatSystem.AttackDamage(actor, monster.Def.Def));
            int damage = CombatSystem.AttackDamageVsCharacter(monster, actor) * turnsToKill;
            return damage < actor.Hp ? damage : Math.Max(0, actor.Hp - 1);
        }

        private static int CeilDiv(int value, int divisor)
        {
            if (divisor <= 0)
            {
                return value;
            }

            return (value + divisor - 1) / divisor;
        }

        /// <summary>接战斗悬赏的职业权重（战士优先）。</summary>
        private static int BountyProfessionPercent(CharacterData actor)
        {
            return actor.Profession == ProfessionType.Warrior ? WarriorBountyWeightPercent : NonWarriorBountyWeightPercent;
        }

        /// <summary>NPC 侧的「悬赏可接」判定：已点亮 + 未占槽 + 有怪物目标（不套用玩家的线索持有条件）。</summary>
        private static bool IsBountyOpenForNpc(BountyEntry entry)
        {
            return entry != null
                   && entry.Revealed
                   && !entry.Occupied
                   && !string.IsNullOrEmpty(entry.TargetMonsterName);
        }

        /// <summary>性格 × 酒标签匹配（<b>占位映射</b>，待配置表落地）。</summary>
        private static bool MatchesPersonality(PersonalityType personality, ItemDef wine)
        {
            if (wine == null || wine.Tags == null)
            {
                return false;
            }

            string[] words;
            switch (personality)
            {
                case PersonalityType.Cheerful:
                    words = CheerfulTagWords;
                    break;
                case PersonalityType.Calm:
                    words = CalmTagWords;
                    break;
                case PersonalityType.Cautious:
                    words = CautiousTagWords;
                    break;
                default:
                    return false;
            }

            for (int i = 0; i < wine.Tags.Length; i++)
            {
                var tag = wine.Tags[i];
                if (string.IsNullOrEmpty(tag))
                {
                    continue;
                }

                for (int w = 0; w < words.Length; w++)
                {
                    if (tag.IndexOf(words[w], StringComparison.Ordinal) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>是否参与行为决策：存活 + 未隐藏 + 无行动锁定（M3 隐藏口径 / M7 受伤 debuff 口径）。</summary>
        private static bool IsEligible(CharacterData c)
        {
            return c != null && c.IsAlive && !c.IsHidden && c.ActionLockDays <= 0;
        }

        private static int DayOf(WorldSimulation sim)
        {
            return sim != null && sim.Time != null && sim.Time.DayOverall > 0 ? sim.Time.DayOverall : 1;
        }

        private void CollectAll(WorldSimulation sim, List<CharacterData> into)
        {
            into.Clear();
            if (sim == null || sim.Population == null)
            {
                return;
            }

            var residents = sim.Population.Residents;
            for (int i = 0; i < residents.Count; i++)
            {
                into.Add(residents[i]);
            }

            var visitors = sim.Population.Visitors;
            for (int i = 0; i < visitors.Count; i++)
            {
                into.Add(visitors[i]);
            }
        }

        private void CollectEligible(WorldSimulation sim, List<CharacterData> into)
        {
            into.Clear();
            if (sim == null || sim.Population == null)
            {
                return;
            }

            for (int pass = 0; pass < 2; pass++)
            {
                var source = pass == 0 ? sim.Population.Residents : sim.Population.Visitors;
                for (int i = 0; i < source.Count; i++)
                {
                    if (IsEligible(source[i]))
                    {
                        into.Add(source[i]);
                    }
                }
            }
        }

        private List<BehaviorAction> CandidateCache(int characterId)
        {
            if (!_candidateCache.TryGetValue(characterId, out var list) || list == null)
            {
                list = new List<BehaviorAction>();
                _candidateCache[characterId] = list;
            }

            return list;
        }

        private void SetCurrent(CharacterData actor, BehaviorAction action)
        {
            if (actor == null)
            {
                return;
            }

            _current[actor.Id] = action;
        }

        private void ForgetAction(CharacterData actor)
        {
            if (actor != null)
            {
                _current.Remove(actor.Id);
            }
        }
    }
}
