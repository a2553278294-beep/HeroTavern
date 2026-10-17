using System;
using System.Collections.Generic;

using ConfigDialogue01Row = GameConfig.ht.Dialogue01Row;
using ConfigDialogueRow = GameConfig.ht.DialogueRow;
using ConfigEventRow = GameConfig.ht.EventRow;

namespace GameLogic.World
{
    /// <summary>
    /// 一句对话（纯逻辑值对象；无 UnityEngine 依赖）。
    /// <see cref="Speaker"/> 是配置表里的中文说话者词（事件角色 / 客人 / 玩家 / 系统 / 相关角色 / 酒保），
    /// 由 View 层（M12 <c>DialoguePanel</c>）负责映射到头像与立绘。
    /// </summary>
    public sealed class DialogueLine
    {
        /// <summary>文案编号（Dialogue 表 id / Dialogue01 表 dialogue_id 派生）。</summary>
        public string Id;

        /// <summary>说话者（中文词，见类注释）。</summary>
        public string Speaker;

        /// <summary>对话正文。</summary>
        public string Text;

        public DialogueLine()
        {
        }

        public DialogueLine(string id, string speaker, string text)
        {
            Id = id;
            Speaker = speaker;
            Text = text;
        }

        public override string ToString()
        {
            return (string.IsNullOrEmpty(Speaker) ? "" : Speaker + "：") + Text;
        }
    }

    /// <summary>
    /// 一次会话（一组 stage 的全部文案行按 order 顺序播放）。
    /// - <see cref="EventId"/>：事件编号（Event 表 event_id；旧口径 Dialogue01 为 "legacy01"）；
    /// - <see cref="GroupId"/>：阶段编号（Event 表 id，即对话行 group_id）；
    /// - <see cref="Cursor"/>：下一句的下标，<see cref="Next"/> 前进，播完 <see cref="IsFinished"/> 为 true。
    /// 会话本身是无状态值对象，重复播放请重新调用 <c>StartGroup</c> / <c>StartChat</c> 拿新实例。
    /// </summary>
    public sealed class DialogueSession
    {
        /// <summary>事件编号。</summary>
        public string EventId;

        /// <summary>阶段编号（= 对话行 group_id）。</summary>
        public string GroupId;

        /// <summary>本段全部句子（已按 Dialogue.order 升序）。</summary>
        public List<DialogueLine> Lines = new List<DialogueLine>();

        /// <summary>下一句的下标。</summary>
        public int Cursor;

        /// <summary>是否已播完。</summary>
        public bool IsFinished
        {
            get { return Cursor >= Lines.Count; }
        }

        /// <summary>还剩几句。</summary>
        public int Remaining
        {
            get { return Lines.Count - Cursor; }
        }

        /// <summary>取下一句并前进游标；已播完返回 null。</summary>
        public DialogueLine Next()
        {
            if (Cursor >= Lines.Count)
            {
                return null;
            }

            var line = Lines[Cursor];
            Cursor++;
            return line;
        }

        /// <summary>从头复读（不改变会话归属的 group/event）。</summary>
        public void Reset()
        {
            Cursor = 0;
        }

        public override string ToString()
        {
            return "group:" + GroupId + " event:" + EventId + " " + Cursor + "/" + Lines.Count;
        }
    }

    /// <summary>
    /// 对话条件查询接口（只读；由 <c>WorldSimulation</c> 或上层 UI 适配器实现）。
    ///
    /// 存在的理由：<c>DialogueSystem</c> 需要世界状态（背包 / 好感 / 悬赏 / 怪物 / 职业服务 / NPC 关系）
    /// 才能判断 Event 表 trigger，但直接引用 <c>WorldSimulation</c> 会形成反向依赖。
    /// 本接口只暴露「问一句」的能力，全部为只读，无副作用。
    ///
    /// 粒度说明（按 Event 表 23 个 trigger 词的实际需要收敛，缺哪条会导致条件恒为 false）：
    /// - 物品类 trigger（持有物品时交谈 / 向本人购买后交谈 / 交付物品后交谈）都带
    ///   <c>item_id + required_count</c>，故 <c>PlayerHolds</c> / <c>PlayerBoughtFrom</c> / <c>PlayerDeliveredTo</c>
    ///   都带 <c>(itemId, count)</c> 参数；count<=0 时由调用方按 1 处理。
    /// - 悬赏类 trigger（关联悬赏发布 / 完成 / 终止 / 战斗死亡）都跟「本次绑定的怪物」有关，
    ///   故 <c>Bounty*</c> 系列都带 <c>(npcId, monsterId)</c>：npcId 是该阶段绑定的 NPC
    ///   （主线首次播放时绑定，特殊事件沿用同一怪物实例），monsterId 取 Event.monster_id（<=0 表示不限怪物）。
    /// - 职业服务类 trigger（本人烹饪完成后交谈 / 本人锻造完成后交谈 / 本人战斗完成并返馆 / 本人治疗完成后交谈）
    ///   统一收敛为 <see cref="NpcFinishedServiceForPlayer"/>：itemId>0 表示「结算过该物品的服务」
    ///   （烹饪→面包、锻造→长剑），itemId<=0 表示任意一次服务（如战斗返馆、治疗回血）。
    /// - 情报类 trigger（购得新怪物情报）需要 <see cref="MonsterSpawned"/>（目标确实已刷新）与
    ///   <see cref="BoughtMonsterIntel"/>（玩家确实向该 NPC 买到了这次情报）两件事。
    /// - 双 NPC 事件（Ex02「不该说的笑话」）的 <c>related</c> 是 speaker=相关角色 的占位，
    ///   首版按单 NPC 播放，另一方 id 由 <c>DialogueSystem.BindEventNpcs</c> 绑定后经
    ///   <see cref="GetFavorBetween"/> / <see cref="IsHostileWith"/> 查询。
    /// </summary>
    public interface IWorldQuery
    {
        /// <summary>NPC 对玩家的好感度（0-100）。</summary>
        int GetFavorToPlayer(int npcId);

        /// <summary>NPC A 对 NPC B 的好感（正=友好，负=敌对）。</summary>
        int GetFavorBetween(int npcId, int otherNpcId);

        /// <summary>玩家背包是否持有某物品（count&gt;0）。trigger「持有物品时交谈」。</summary>
        bool PlayerHolds(int itemId, int count);

        /// <summary>玩家是否已向该 NPC 成功买入 itemId 累计 count 件（赠与/索取/与其他 NPC 交易不计）。trigger「向本人购买后交谈」。</summary>
        bool PlayerBoughtFrom(int npcId, int itemId, int count);

        /// <summary>玩家是否已向该 NPC 交付（赠与或卖出）itemId 累计 count 件。trigger「交付物品后交谈」。</summary>
        bool PlayerDeliveredTo(int npcId, int itemId, int count);

        /// <summary>该 NPC 是否已为玩家完成过一次职业服务（itemId&gt;0 指定物品结算，&lt;=0 任意服务）。trigger「本人烹饪/锻造/战斗/治疗完成后交谈」。</summary>
        bool NpcFinishedServiceForPlayer(int npcId, int itemId);

        /// <summary>怪物是否已实际刷新到场上（用于情报类 trigger 的前置）。</summary>
        bool MonsterSpawned(int monsterId);

        /// <summary>怪物当前是否仍存活（trigger「目标提前清除」取反）。</summary>
        bool MonsterAlive(int monsterId);

        /// <summary>玩家是否已向该 NPC 成功购买过该怪物的情报（购买时目标存活且无玩家针对它的悬赏）。trigger「购得新怪物情报」。</summary>
        bool BoughtMonsterIntel(int sellerNpcId, int monsterId);

        /// <summary>该 NPC 关联的怪物是否已有玩家发布成功的悬赏。trigger「关联悬赏发布」「关联悬赏发布后交谈」。</summary>
        bool BountyPublished(int npcId, int monsterId);

        /// <summary>该 NPC 关联的悬赏是否已成功且报酬已结算。trigger「关联悬赏完成」「关联悬赏完成后交谈」。</summary>
        bool BountyCompleted(int npcId, int monsterId);

        /// <summary>该 NPC 关联的悬赏是否因接赏者阵亡以外的原因失败/失效/取消。trigger「关联悬赏终止」。</summary>
        bool BountyFailed(int npcId, int monsterId);

        /// <summary>该 NPC 关联的悬赏中接赏者是否战斗中阵亡且悬赏失败。trigger「关联悬赏战斗死亡」「关联悬赏阵亡后交谈」「本人悬赏战斗死亡」。</summary>
        bool BountyHunterDied(int npcId, int monsterId);

        /// <summary>两个 NPC 之间当前是否敌对（好感为负）。trigger「NPC关系转为敌对」。</summary>
        bool IsHostileWith(int npcId, int otherNpcId);
    }

    /// <summary>
    /// 对话 / 文案系统（master-plan M6）。
    ///
    /// 数据源：<c>ht.TbEvent</c>（阶段表：category / profession / previous_id / trigger / item_id /
    /// monster_id / required_count / favor_threshold / value）+ <c>ht.TbDialogue</c>（文案行：
    /// 按 group_id + order 组成会话）+ <c>ht.TbDialogue01</c>（旧口径闲聊备用）。
    ///
    /// ===== 与 master-plan M6 规格的冲突（以生成代码 / 实际表数据为准）=====
    /// 1) 计划写 <c>category=CHAT / profession=0 池</c> 枚举口径，但 Event 表的 category / profession /
    ///    trigger 源值是中文词（未匹配 __enums__ 占位词表），生成代码按 string 读取，
    ///    所以本类用 <see cref="CategoryChat"/> / <see cref="AnyProfession"/> 等中文常量匹配。
    /// 2) 计划写「无职业 NPC 用 profession=0 池」；实际表里 profession=0 的别名写作「不限」，
    ///    且备注限定「仅无职业且身份为客人/酒保的 NPC 在酒馆交谈时可选，按说话者身份筛选」，
    ///    所以 <see cref="StartChat(CharacterData,int)"/> 对「不限」池额外做说话者身份过滤。
    /// 3) 计划只列 9 个 trigger 词，实际 Event 表有 23 个中文 trigger 词
    ///    （含「本人烹饪完成后交谈」「关联悬赏终止」「目标提前清除」「NPC关系转为敌对」等），
    ///    <see cref="IsConditionMet"/> 全词表分派，未收录的新词默认满足并写入 <see cref="Warnings"/>。
    /// 4) Event 表的 favor_threshold 取值只有 5 / 10；<c>GetPlayableGroups</c> 用「当前好感 &gt;= 门槛」判定。
    /// 5) 「双 NPC 事件」首版只支持单 NPC 播放：Ex02 系列经 <see cref="BindEventNpcs"/> 绑定另一方后
    ///    才有意义，未绑定时相关 trigger 返回 false（不会误播），TODO 注释见对应分支。
    ///
    /// 线程模型：纯逻辑、无 UnityEngine 依赖，按主循环单线程调用（与 BountySystem 等模块一致）。
    /// </summary>
    public sealed class DialogueSystem
    {
        // ------------------------------------------------------------------
        // 配置词表常量（Event / Dialogue 表里实际出现的中文枚举词）
        // ------------------------------------------------------------------

        /// <summary>文案类别：普通闲聊。</summary>
        public const string CategoryChat = "普通闲聊";
        /// <summary>文案类别：主线事件。</summary>
        public const string CategoryMain = "主线事件";
        /// <summary>文案类别：特殊事件。</summary>
        public const string CategoryEx = "特殊事件";
        /// <summary>文案类别：好感度对话。</summary>
        public const string CategoryFavor = "好感度对话";

        /// <summary>说话者：事件角色（占位，首版单 NPC 播放时即该 NPC）。</summary>
        public const string SpeakerEventNpc = "事件角色";
        /// <summary>说话者：客人。</summary>
        public const string SpeakerGuest = "客人";
        /// <summary>说话者：玩家。</summary>
        public const string SpeakerPlayer = "玩家";
        /// <summary>说话者：系统（系统提示型 stage）。</summary>
        public const string SpeakerSystem = "系统";
        /// <summary>说话者：相关角色（双 NPC 事件的另一方占位）。</summary>
        public const string SpeakerRelatedNpc = "相关角色";
        /// <summary>说话者：酒保。</summary>
        public const string SpeakerBartender = "酒保";

        /// <summary>职业列的「不限」别名（无职业客人 / 酒保 / 系统提示用）。</summary>
        public const string AnyProfession = "不限";

        // Event 表 trigger 词全表（23 个）。
        /// <summary>日常交谈。</summary>
        public const string TriggerTalk = "日常交谈";
        /// <summary>首次交谈。</summary>
        public const string TriggerFirstTalk = "首次交谈";
        /// <summary>持有物品时交谈。</summary>
        public const string TriggerHoldItem = "持有物品时交谈";
        /// <summary>向本人购买后交谈。</summary>
        public const string TriggerBoughtFromNpc = "向本人购买后交谈";
        /// <summary>交付物品后交谈。</summary>
        public const string TriggerDeliveredToNpc = "交付物品后交谈";
        /// <summary>本人烹饪完成后交谈。</summary>
        public const string TriggerNpcCooked = "本人烹饪完成后交谈";
        /// <summary>本人锻造完成后交谈。</summary>
        public const string TriggerNpcForged = "本人锻造完成后交谈";
        /// <summary>本人战斗完成并返馆。</summary>
        public const string TriggerNpcBattleDone = "本人战斗完成并返馆";
        /// <summary>本人悬赏战斗死亡。</summary>
        public const string TriggerNpcBountyDied = "本人悬赏战斗死亡";
        /// <summary>本人治疗完成后交谈。</summary>
        public const string TriggerNpcHealed = "本人治疗完成后交谈";
        /// <summary>购得新怪物情报。</summary>
        public const string TriggerIntelBought = "购得新怪物情报";
        /// <summary>关联悬赏发布后交谈。</summary>
        public const string TriggerBountyPublishedTalk = "关联悬赏发布后交谈";
        /// <summary>关联悬赏发布。</summary>
        public const string TriggerBountyPublished = "关联悬赏发布";
        /// <summary>关联悬赏完成。</summary>
        public const string TriggerBountyCompleted = "关联悬赏完成";
        /// <summary>关联悬赏完成后交谈。</summary>
        public const string TriggerBountyCompletedTalk = "关联悬赏完成后交谈";
        /// <summary>关联悬赏战斗死亡。</summary>
        public const string TriggerBountyDied = "关联悬赏战斗死亡";
        /// <summary>关联悬赏阵亡后交谈。</summary>
        public const string TriggerBountyDiedTalk = "关联悬赏阵亡后交谈";
        /// <summary>关联悬赏终止。</summary>
        public const string TriggerBountyTerminated = "关联悬赏终止";
        /// <summary>目标提前清除。</summary>
        public const string TriggerTargetCleared = "目标提前清除";
        /// <summary>NPC关系转为敌对。</summary>
        public const string TriggerRelationHostile = "NPC关系转为敌对";
        /// <summary>与关系另一方交谈。</summary>
        public const string TriggerTalkOtherParty = "与关系另一方交谈";
        /// <summary>返回事件角色交谈。</summary>
        public const string TriggerTalkEventNpc = "返回事件角色交谈";
        /// <summary>好感达到门槛（favor_threshold 当前只有 5 / 10）。</summary>
        public const string TriggerFavorReached = "好感达到门槛";

        /// <summary>旧口径 Dialogue01 会话的 <see cref="DialogueSession.EventId"/> 固定值。</summary>
        public const string LegacyEventId = "legacy01";

        /// <summary>某次抽取的结果（供「同一天沿用」缓存）。</summary>
        private sealed class ChatPick
        {
            public int Day;
            public string GroupId;
        }

        /// <summary>职业枚举 → Event 表 profession 列中文词（Event 表用中文词，不用 ht.Profession 枚举）。</summary>
        private static readonly Dictionary<ProfessionType, string> ProfessionNameByType = new Dictionary<ProfessionType, string>
        {
            { ProfessionType.Chef, "厨师" },
            { ProfessionType.Merchant, "商人" },
            { ProfessionType.Blacksmith, "铁匠" },
            { ProfessionType.Farmer, "农民" },
            { ProfessionType.Warrior, "战士" },
            { ProfessionType.Priest, "牧师" },
            { ProfessionType.Bard, "吟游诗人" },
        };

        /// <summary>可播类别（<see cref="GetPlayableGroups"/> 只返回这三类；普通闲聊走 <see cref="StartChat"/>）。</summary>
        private static readonly string[] PlayableCategories = { CategoryMain, CategoryEx, CategoryFavor };

        /// <summary>闲聊类别（<see cref="StartChat"/> 的抽取池）。</summary>
        private const string ChatCategory = CategoryChat;

        // ------------------------------------------------------------------
        // 状态
        // ------------------------------------------------------------------

        private static GameConfig.Tables _tablesSource;

        private readonly IRandomSource _random;
        private readonly List<string> _warnings = new List<string>();

        private GameConfig.Tables _indexedTables;
        private bool _indexed;

        private readonly List<ConfigEventRow> _eventRows = new List<ConfigEventRow>();
        private readonly Dictionary<string, ConfigEventRow> _eventByGroup = new Dictionary<string, ConfigEventRow>();
        private readonly Dictionary<string, ConfigDialogueRow[]> _linesByGroup = new Dictionary<string, ConfigDialogueRow[]>();
        private readonly Dictionary<string, List<string>> _groupsByEvent = new Dictionary<string, List<string>>();

        private readonly HashSet<string> _playedGroups = new HashSet<string>();
        private readonly Dictionary<string, int> _groupNpcByGroup = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _eventNpcByEvent = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _eventRelatedNpcByEvent = new Dictionary<string, int>();

        private readonly Dictionary<int, ChatPick> _chatByNpc = new Dictionary<int, ChatPick>();
        private readonly Dictionary<int, ChatPick> _legacyByNpc = new Dictionary<int, ChatPick>();

        // ------------------------------------------------------------------
        // 构造与配置来源
        // ------------------------------------------------------------------

        /// <summary>随机源必填（注入以保证抽取结果可复现）。</summary>
        public DialogueSystem(IRandomSource random)
        {
            _random = random;
        }

        /// <summary>注入配置表来源（模块层已持有 <c>GameConfig.Tables</c> 时优先用注入值）。</summary>
        public static void SetTablesSource(GameConfig.Tables tables)
        {
            _tablesSource = tables;
        }

        /// <summary>当前配置表来源（未注入时为 null）。</summary>
        public static GameConfig.Tables TablesSource
        {
            get { return _tablesSource; }
        }

        private static GameConfig.Tables ResolveTables()
        {
            if (_tablesSource != null)
            {
                return _tablesSource;
            }

            var catalog = GameConfigCatalog.Instance;
            return catalog != null ? catalog.Tables : null;
        }

        /// <summary>解析/配置告警（未知 trigger 词、缺表、缺行等）。</summary>
        public List<string> Warnings
        {
            get { return _warnings; }
        }

        /// <summary>丢弃索引与全部运行态（换表 / 重开局用）。</summary>
        public void Reset()
        {
            _indexed = false;
            _indexedTables = null;
            _eventRows.Clear();
            _eventByGroup.Clear();
            _linesByGroup.Clear();
            _groupsByEvent.Clear();
            _playedGroups.Clear();
            _groupNpcByGroup.Clear();
            _eventNpcByEvent.Clear();
            _eventRelatedNpcByEvent.Clear();
            _chatByNpc.Clear();
            _legacyByNpc.Clear();
        }

        /// <summary>清空「已播 / 绑定 / 当天抽取」记录但保留索引（重开局一日用）。</summary>
        public void ClearProgress()
        {
            _playedGroups.Clear();
            _groupNpcByGroup.Clear();
            _eventNpcByEvent.Clear();
            _eventRelatedNpcByEvent.Clear();
            _chatByNpc.Clear();
            _legacyByNpc.Clear();
        }

        // ------------------------------------------------------------------
        // 索引
        // ------------------------------------------------------------------

        private void EnsureIndex()
        {
            GameConfig.Tables tables = ResolveTables();
            if (_indexed && ReferenceEquals(tables, _indexedTables))
            {
                return;
            }

            _eventRows.Clear();
            _eventByGroup.Clear();
            _linesByGroup.Clear();
            _groupsByEvent.Clear();

            _indexed = true;
            _indexedTables = tables;

            if (tables == null)
            {
                Warn("未拿到 GameConfig.Tables（GameConfigCatalog 未 Init 且未 SetTablesSource）：对话抽取全部返回空。");
                return;
            }

            try
            {
                var rows = tables.TbEvent.DataList;
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row == null || string.IsNullOrEmpty(row.Id))
                    {
                        continue;
                    }

                    _eventRows.Add(row);
                    _eventByGroup[row.Id] = row;

                    List<string> groups;
                    if (!_groupsByEvent.TryGetValue(row.EventId, out groups))
                    {
                        groups = new List<string>();
                        _groupsByEvent[row.EventId] = groups;
                    }

                    groups.Add(row.Id);
                }
            }
            catch (Exception e)
            {
                Warn("读取 TbEvent 失败：" + e.Message);
            }

            try
            {
                var lines = tables.TbDialogue.DataList;
                var buckets = new Dictionary<string, List<ConfigDialogueRow>>();
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (line == null || string.IsNullOrEmpty(line.GroupId))
                    {
                        continue;
                    }

                    List<ConfigDialogueRow> bucket;
                    if (!buckets.TryGetValue(line.GroupId, out bucket))
                    {
                        bucket = new List<ConfigDialogueRow>();
                        buckets[line.GroupId] = bucket;
                    }

                    bucket.Add(line);
                }

                foreach (var kv in buckets)
                {
                    bucket_sort(kv.Value);
                    _linesByGroup[kv.Key] = kv.Value.ToArray();
                }
            }
            catch (Exception e)
            {
                Warn("读取 TbDialogue 失败：" + e.Message);
            }
        }

        /// <summary>稳定排序：order 升序，order 相同时保持表行序（用原始下标兜底）。</summary>
        private static void bucket_sort(List<ConfigDialogueRow> bucket)
        {
            // 不用 LINQ（热路径 + 保持无外部依赖）：插入排序在单组 2-5 行时足够。
            for (int i = 1; i < bucket.Count; i++)
            {
                var cur = bucket[i];
                int j = i - 1;
                while (j >= 0 && bucket[j].Order > cur.Order)
                {
                    bucket[j + 1] = bucket[j];
                    j--;
                }

                bucket[j + 1] = cur;
            }
        }

        private ConfigEventRow GetEventRow(string groupId)
        {
            if (string.IsNullOrEmpty(groupId))
            {
                return null;
            }

            EnsureIndex();
            ConfigEventRow row;
            return _eventByGroup.TryGetValue(groupId, out row) ? row : null;
        }

        /// <summary>该阶段的首个说话者（用于「不限」闲聊池的身份筛选）。</summary>
        public string GroupSpeaker(string groupId)
        {
            EnsureIndex();
            ConfigDialogueRow[] lines;
            if (!_linesByGroup.TryGetValue(groupId, out lines) || lines.Length == 0)
            {
                return string.Empty;
            }

            return lines[0].Speaker ?? string.Empty;
        }

        // ------------------------------------------------------------------
        // 职业 / 类别匹配
        // ------------------------------------------------------------------

        /// <summary>职业枚举 → Event 表 profession 中文词；<see cref="ProfessionType.None"/> / 未知职业返回 false。</summary>
        public static bool TryGetProfessionName(ProfessionType type, out string name)
        {
            return ProfessionNameByType.TryGetValue(type, out name) && !string.IsNullOrEmpty(name);
        }

        /// <summary>可播类别判定（主线事件 / 特殊事件 / 好感度对话）。</summary>
        public static bool IsPlayableCategory(string category)
        {
            for (int i = 0; i < PlayableCategories.Length; i++)
            {
                if (string.Equals(PlayableCategories[i], category, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>阶段是否匹配该 NPC 的职业（profession 列等于该 NPC 职业中文名，或为「不限」）。</summary>
        public static bool MatchesProfession(ConfigEventRow row, CharacterData npc)
        {
            if (row == null || npc == null)
            {
                return false;
            }

            if (string.Equals(row.Profession, AnyProfession, StringComparison.Ordinal))
            {
                return true;
            }

            string name;
            return TryGetProfessionName(npc.Profession, out name)
                   && string.Equals(row.Profession, name, StringComparison.Ordinal);
        }

        /// <summary>
        /// 闲聊池判定：profession 列等于该 NPC 职业中文词，或为「不限」。
        /// 「不限」池按 Event 表备注进一步收窄——仅无职业（ProfessionType.None）且身份匹配的 NPC 可选
        /// （默认客人；传 <c>identitySpeaker</c>=<see cref="SpeakerBartender"/> 可取酒保闲聊）。
        /// </summary>
        private static bool MatchesChatPool(ConfigEventRow row, CharacterData npc, string identitySpeaker, string groupSpeaker)
        {
            string name;
            bool hasProfession = TryGetProfessionName(npc.Profession, out name);
            if (hasProfession)
            {
                return string.Equals(row.Profession, name, StringComparison.Ordinal);
            }

            if (!string.Equals(row.Profession, AnyProfession, StringComparison.Ordinal))
            {
                return false;
            }

            string want = string.IsNullOrEmpty(identitySpeaker) ? SpeakerGuest : identitySpeaker;
            return string.IsNullOrEmpty(groupSpeaker) || string.Equals(groupSpeaker, want, StringComparison.Ordinal);
        }

        // ------------------------------------------------------------------
        // 抽取入口
        // ------------------------------------------------------------------

        /// <summary>
        /// 开始闲聊：按 NPC 职业从 Event 表 category=普通闲聊 的阶段里按 <c>value</c> 权重抽一段，
        /// 播其全部 stage 行。
        /// 同一天对同一 NPC 重复调用返回同一段（当天沿用），次日优先换另一段。
        /// </summary>
        public DialogueSession StartChat(CharacterData npc, int dayOverall)
        {
            return StartChat(npc, dayOverall, null);
        }

        /// <summary>
        /// 开始闲聊（带身份筛选）。<paramref name="identitySpeaker"/> 仅对 profession=「不限」的闲聊池生效：
        /// null（默认）按「客人」，传 <see cref="SpeakerBartender"/> 取酒保闲聊。
        /// 池空或表缺失时返回 null（并写入 <see cref="Warnings"/>）。
        /// </summary>
        public DialogueSession StartChat(CharacterData npc, int dayOverall, string identitySpeaker)
        {
            if (npc == null)
            {
                return null;
            }

            EnsureIndex();

            ChatPick picked;
            if (_chatByNpc.TryGetValue(npc.Id, out picked) && picked.Day == dayOverall && !string.IsNullOrEmpty(picked.GroupId))
            {
                // 当天沿用：返回同一段的新会话（Cursor 归零，从头复读）。
                return StartGroup(picked.GroupId, npc);
            }

            string lastGroupId = picked != null ? picked.GroupId : null;

            var candidates = new List<ConfigEventRow>();
            for (int i = 0; i < _eventRows.Count; i++)
            {
                var row = _eventRows[i];
                if (!string.Equals(row.Category, ChatCategory, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(row.PreviousId))
                {
                    continue;
                }

                if (!MatchesChatPool(row, npc, identitySpeaker, GroupSpeaker(row.Id)))
                {
                    continue;
                }

                candidates.Add(row);
            }

            if (candidates.Count == 0)
            {
                Warn("NPC " + npc.Id + "（职业 " + npc.Profession + "）没有可用闲聊（category=普通闲聊 且 profession 匹配）。");
                return null;
            }

            // 次日优先换另一条：排除上一次的段（仍保留至少一个候选时）。
            var pool = candidates;
            if (candidates.Count > 1 && !string.IsNullOrEmpty(lastGroupId))
            {
                var filtered = new List<ConfigEventRow>(candidates.Count - 1);
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (!string.Equals(candidates[i].Id, lastGroupId, StringComparison.Ordinal))
                    {
                        filtered.Add(candidates[i]);
                    }
                }

                if (filtered.Count > 0)
                {
                    pool = filtered;
                }
            }

            var chosen = PickWeighted(pool, delegate(ConfigEventRow r) { return r.Value; });
            if (chosen == null)
            {
                return null;
            }

            _chatByNpc[npc.Id] = new ChatPick { Day = dayOverall, GroupId = chosen.Id };
            return StartGroup(chosen.Id, npc);
        }

        /// <summary>
        /// 旧口径闲聊：<c>ht.TbDialogue01</c> 按 <c>weight</c> 抽取（不做当天沿用，每次重新抽）。
        /// 池空或表缺失时返回 null（并写入 <see cref="Warnings"/>）。
        /// </summary>
        public DialogueSession StartLegacyChat01(CharacterData npc)
        {
            if (npc == null)
            {
                return null;
            }

            return StartLegacyGroup(PickFromLegacy(npc));
        }

        /// <summary>旧口径闲聊（当天沿用版本：同一天对同一 NPC 返回同一条）。</summary>
        public DialogueSession StartLegacyChat01(CharacterData npc, int dayOverall)
        {
            if (npc == null)
            {
                return null;
            }

            ChatPick picked;
            if (_legacyByNpc.TryGetValue(npc.Id, out picked) && picked.Day == dayOverall && !string.IsNullOrEmpty(picked.GroupId))
            {
                return StartLegacyGroup(picked.GroupId);
            }

            var groupId = PickFromLegacy(npc);
            if (string.IsNullOrEmpty(groupId))
            {
                return null;
            }

            _legacyByNpc[npc.Id] = new ChatPick { Day = dayOverall, GroupId = groupId };
            return StartLegacyGroup(groupId);
        }

        /// <summary>
        /// 播一条旧口径闲聊（groupId 形如 <c>dialogue01_3</c>）：单行会话，speaker 取 Dialogue01 表原值，
        /// 不走 TbDialogue/TbEvent 索引。找不到该行时返回「零行会话」。
        /// </summary>
        private DialogueSession StartLegacyGroup(string groupId)
        {
            var session = new DialogueSession
            {
                EventId = LegacyEventId,
                GroupId = groupId,
            };

            if (string.IsNullOrEmpty(groupId))
            {
                Warn("StartLegacyChat01 没有抽到可用文案行。");
                return session;
            }

            int dialogueId;
            if (!TryParseLegacyDialogueId(groupId, out dialogueId))
            {
                Warn("旧口径会话编号 " + groupId + " 非法。");
                return session;
            }

            var tables = ResolveTables();
            if (tables != null)
            {
                try
                {
                    var row = tables.TbDialogue01.GetOrDefault(dialogueId);
                    if (row != null)
                    {
                        session.Lines.Add(new DialogueLine(groupId, row.Speaker, row.Text));
                    }
                }
                catch (Exception e)
                {
                    Warn("读取 TbDialogue01 行 " + dialogueId + " 失败：" + e.Message);
                }
            }

            if (session.Lines.Count == 0)
            {
                Warn("旧口径会话 " + groupId + " 没有文案行。");
            }

            var handler = SessionStarted;
            if (handler != null)
            {
                handler(session);
            }

            return session;
        }

        private static bool TryParseLegacyDialogueId(string groupId, out int dialogueId)
        {
            dialogueId = 0;
            const string prefix = LegacyEventId + "_";
            if (string.IsNullOrEmpty(groupId) || !groupId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            return int.TryParse(groupId.Substring(prefix.Length), out dialogueId);
        }

        /// <summary>抽一条旧口径闲聊（返回其 groupId 形如 <c>dialogue01_3</c>），失败返回 null。</summary>
        private string PickFromLegacy(CharacterData npc)
        {
            EnsureIndex();

            var tables = ResolveTables();
            if (tables == null)
            {
                Warn("未拿到 GameConfig.Tables：StartLegacyChat01 无法抽取。");
                return null;
            }

            try
            {
                var rows = tables.TbDialogue01.DataList;
                var pool = new List<ConfigDialogue01Row>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i] != null)
                    {
                        pool.Add(rows[i]);
                    }
                }

                if (pool.Count == 0)
                {
                    Warn("TbDialogue01 无可用行：旧口径闲聊返回空。");
                    return null;
                }

                var chosen = PickWeighted(pool, delegate(ConfigDialogue01Row r) { return r.Weight; });
                return chosen == null ? null : LegacyGroupId(chosen.DialogueId);
            }
            catch (Exception e)
            {
                Warn("读取 TbDialogue01 失败：" + e.Message);
                return null;
            }
        }

        private static string LegacyGroupId(int dialogueId)
        {
            return "dialogue01_" + dialogueId;
        }

        /// <summary>
        /// 播指定阶段：取该 group_id 的全部 Dialogue 行，按 order 升序组成会话并触发 <see cref="SessionStarted"/>。
        /// 找不到阶段或该阶段没有任何文案行时返回「零行会话」（<c>IsFinished</c> 立即为 true），不返回 null。
        /// </summary>
        public DialogueSession StartGroup(string groupId, CharacterData npc)
        {
            var session = new DialogueSession();
            session.GroupId = groupId;

            var row = GetEventRow(groupId);
            if (row == null)
            {
                Warn("StartGroup 找不到阶段 " + groupId + "（TbEvent 无此 id）。");
            }
            else
            {
                session.EventId = row.EventId;
            }

            EnsureIndex();
            ConfigDialogueRow[] lines;
            if (_linesByGroup.TryGetValue(groupId ?? string.Empty, out lines))
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    session.Lines.Add(new DialogueLine(lines[i].Id, lines[i].Speaker, lines[i].Text));
                }
            }

            if (session.Lines.Count == 0)
            {
                Warn("阶段 " + groupId + " 没有对话行（TbDialogue 无该 group_id）。");
            }

            var handler = SessionStarted;
            if (handler != null)
            {
                handler(session);
            }

            return session;
        }

        /// <summary>
        /// 记录某阶段已播，并把它绑定到该 NPC。
        ///
        /// 主线事件绑定口径（Event 表备注「首次播放本主线时绑定当前厨师/商人/…」）：
        /// - 只在该事件<b>首段</b>（previous_id 为空）播放时写入事件级绑定，
        ///   之后同一事件的每一段都播给这个 NPC（同职业的其他 NPC 不共享，见 GetPlayableGroups）；
        /// - 事件绑定一直保留到该事件全部阶段读完（<see cref="IsEventCompleted"/>），或由
        ///   <see cref="ReleaseEventBinding"/> 显式释放。
        ///   TODO(M9/M11)：主线「任务结束」的世界侧钩子（职业服务/悬赏生命周期）落地后，
        ///   由 WorldSimulation 在主线任务真正结束时调用 ReleaseEventBinding 换人，
        ///   避免旧绑定永久挡住同职业的新 NPC。
        /// </summary>
        public void MarkGroupPlayed(string groupId, int npcId)
        {
            if (string.IsNullOrEmpty(groupId))
            {
                return;
            }

            EnsureIndex();
            _playedGroups.Add(groupId);

            if (npcId > 0)
            {
                _groupNpcByGroup[groupId] = npcId;
            }

            var row = GetEventRow(groupId);
            if (row == null || npcId <= 0)
            {
                return;
            }

            if (!string.Equals(row.Category, CategoryMain, StringComparison.Ordinal))
            {
                return;
            }

            if (string.IsNullOrEmpty(row.PreviousId))
            {
                _eventNpcByEvent[row.EventId] = npcId;
            }
        }

        /// <summary>该阶段是否已播。</summary>
        public bool IsGroupPlayed(string groupId)
        {
            return !string.IsNullOrEmpty(groupId) && _playedGroups.Contains(groupId);
        }

        /// <summary>该事件全部阶段是否都已播完。</summary>
        public bool IsEventCompleted(string eventId)
        {
            if (string.IsNullOrEmpty(eventId))
            {
                return false;
            }

            EnsureIndex();
            List<string> groups;
            if (!_groupsByEvent.TryGetValue(eventId, out groups) || groups.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < groups.Count; i++)
            {
                if (!_playedGroups.Contains(groups[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>该阶段当前绑定的 NPC（没绑过返回 0）。</summary>
        public int GetGroupNpc(string groupId)
        {
            int npcId;
            return !string.IsNullOrEmpty(groupId) && _groupNpcByGroup.TryGetValue(groupId, out npcId) ? npcId : 0;
        }

        /// <summary>该事件当前绑定的 NPC（主线的「绑定本人」，没绑过返回 0）。</summary>
        public int GetEventNpc(string eventId)
        {
            int npcId;
            return !string.IsNullOrEmpty(eventId) && _eventNpcByEvent.TryGetValue(eventId, out npcId) ? npcId : 0;
        }

        /// <summary>
        /// 绑定双 NPC 事件（Ex02「不该说的笑话」）的双方。
        /// TODO(M6 首版)：双 NPC 播放只支持单 NPC，另一方仅用于关系类 trigger 判定；
        /// 真正的「A 说完 → 切到 B」播放流程留待 UI（M12 DialoguePanel）落地后再接。
        /// </summary>
        public void BindEventNpcs(string eventId, int npcId, int relatedNpcId)
        {
            if (string.IsNullOrEmpty(eventId))
            {
                return;
            }

            if (npcId > 0)
            {
                _eventNpcByEvent[eventId] = npcId;
            }

            if (relatedNpcId > 0)
            {
                _eventRelatedNpcByEvent[eventId] = relatedNpcId;
            }
        }

        /// <summary>释放事件绑定（主线任务结束 / 事件播完后由世界侧调用，让同职业新 NPC 能接手）。</summary>
        public void ReleaseEventBinding(string eventId)
        {
            if (string.IsNullOrEmpty(eventId))
            {
                return;
            }

            _eventNpcByEvent.Remove(eventId);
            _eventRelatedNpcByEvent.Remove(eventId);
        }

        // ------------------------------------------------------------------
        // 可播阶段筛选
        // ------------------------------------------------------------------

        /// <summary>
        /// 列出该 NPC 当前可播的事件阶段（group_id 列表）：
        /// category ∈ {主线事件, 特殊事件, 好感度对话}，profession 匹配（或「不限」），
        /// 未播过，previous_id 为空或已播，且 trigger 条件满足（<see cref="IsConditionMet(ConfigEventRow,IWorldQuery,int,int)"/>）。
        ///
        /// 已绑定到别的 NPC 的事件不再返回——这正是「首次播放本主线时绑定当前厨师」
        /// 「其他同职业 NPC 不共用这段往事」的落点。
        /// <paramref name="query"/> 为 null 时只做前置链 / 职业判定，所有依赖世界状态的 trigger 返回 false。
        /// </summary>
        public IReadOnlyList<string> GetPlayableGroups(CharacterData npc, IWorldQuery query)
        {
            EnsureIndex();

            var result = new List<string>();
            if (npc == null)
            {
                return result;
            }

            for (int i = 0; i < _eventRows.Count; i++)
            {
                var row = _eventRows[i];
                if (!IsPlayableCategory(row.Category))
                {
                    continue;
                }

                if (_playedGroups.Contains(row.Id))
                {
                    continue;
                }

                if (!MatchesProfession(row, npc))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(row.PreviousId) && !_playedGroups.Contains(row.PreviousId))
                {
                    continue;
                }

                int bound;
                if (_eventNpcByEvent.TryGetValue(row.EventId, out bound) && bound != npc.Id)
                {
                    continue;
                }

                int relatedNpcId;
                if (!_eventRelatedNpcByEvent.TryGetValue(row.EventId, out relatedNpcId))
                {
                    relatedNpcId = 0;
                }

                if (!IsConditionMet(row, query, npc.Id, relatedNpcId))
                {
                    continue;
                }

                result.Add(row.Id);
            }

            return result;
        }

        /// <summary>该 NPC 当前可播的阶段（<see cref="GetPlayableGroups"/> 的行视图，供 UI 显示标题）。</summary>
        public IReadOnlyList<ConfigEventRow> GetPlayableEventRows(CharacterData npc, IWorldQuery query)
        {
            var groups = GetPlayableGroups(npc, query);
            var rows = new List<ConfigEventRow>(groups.Count);
            for (int i = 0; i < groups.Count; i++)
            {
                var row = GetEventRow(groups[i]);
                if (row != null)
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        // ------------------------------------------------------------------
        // trigger 判定
        // ------------------------------------------------------------------

        /// <summary>
        /// 判断一个阶段的 trigger 是否满足（不带 NPC 上下文：npcId=0 / relatedNpcId=0，
        /// 只有不依赖 NPC 的 trigger 会给出确定结果）。带上下文的重载见
        /// <see cref="IsConditionMet(ConfigEventRow,IWorldQuery,int,int)"/>。
        /// </summary>
        public bool IsConditionMet(ConfigEventRow row, IWorldQuery query)
        {
            return IsConditionMet(row, query, 0, 0);
        }

        /// <summary>
        /// 按 Event 表 trigger 中文词分派条件判定。
        /// 未收录的 trigger 词默认<b>满足</b>并写入 <see cref="Warnings"/>（策划新增词表先放行，不阻塞剧情）。
        ///
        /// <paramref name="npcId"/>：该阶段绑定的 NPC（NPC 自身的职业服务 / 好感 / 交付 / 购买判定）；
        /// <paramref name="relatedNpcId"/>：双 NPC 事件的另一方（Ex02 系列，未绑定为 0 → 判 false）。
        /// <paramref name="query"/> 为 null 时，依赖世界状态的 trigger 一律判 false。
        /// </summary>
        public bool IsConditionMet(ConfigEventRow row, IWorldQuery query, int npcId, int relatedNpcId)
        {
            if (row == null)
            {
                return false;
            }

            string trigger = row.Trigger ?? string.Empty;
            int count = row.RequiredCount > 0 ? row.RequiredCount : 1;

            switch (trigger)
            {
                // ---- 无需世界状态的闲聊 / 首段 ----
                case TriggerTalk:
                case TriggerFirstTalk:
                    // 闲聊由 StartChat 的池与缓存口径保证；首段由 previous_id 为空保证。
                    return true;

                // ---- 物品门槛 ----
                case TriggerHoldItem:
                    // 「库存可用即可，不代表已备齐悬赏用料」：只看玩家背包当前持有量。
                    return query != null && row.ItemId > 0 && query.PlayerHolds(row.ItemId, count);

                case TriggerBoughtFromNpc:
                    // 向<b>该</b> NPC 成功买入累计（赠与、索取、其他商人交易不计）。
                    return query != null && npcId > 0 && row.ItemId > 0 && query.PlayerBoughtFrom(npcId, row.ItemId, count);

                case TriggerDeliveredToNpc:
                    // 通过交易确认赠与或卖出、实际转入对方背包累计（不要求已使用/装备）。
                    return query != null && npcId > 0 && row.ItemId > 0 && query.PlayerDeliveredTo(npcId, row.ItemId, count);

                // ---- NPC 侧行为里程碑（悬赏由该 NPC 自主接取，交谈只读记录）----
                case TriggerNpcCooked:
                case TriggerNpcForged:
                    return query != null && npcId > 0 && query.NpcFinishedServiceForPlayer(npcId, row.ItemId);

                case TriggerNpcBattleDone:
                    // 本人存活返馆（item_id=0 → 任意一次战斗服务）。
                    return query != null && npcId > 0 && query.NpcFinishedServiceForPlayer(npcId, row.ItemId);

                case TriggerNpcHealed:
                    // 为一名存活且受伤的城镇 NPC 实际回血（自然恢复 / 食用物品不计）。
                    return query != null && npcId > 0 && query.NpcFinishedServiceForPlayer(npcId, row.ItemId);

                case TriggerNpcBountyDied:
                    // 系统提示：绑定 NPC 在<b>本次已绑定</b>悬赏的战斗中阵亡，且该悬赏未完成。
                    return query != null && npcId > 0 && query.BountyHunterDied(npcId, row.MonsterId)
                           && !query.BountyCompleted(npcId, row.MonsterId);

                // ---- 情报 ----
                case TriggerIntelBought:
                    // 目标实际刷新 + 玩家向知情 NPC 成功买到这次情报。
                    return query != null && npcId > 0 && row.MonsterId > 0
                           && query.MonsterSpawned(row.MonsterId)
                           && query.BoughtMonsterIntel(npcId, row.MonsterId);

                // ---- 悬赏分支（互斥条件由世界侧保证：完成 / 阵亡 / 终止 三选一）----
                case TriggerBountyPublished:
                case TriggerBountyPublishedTalk:
                    return query != null && npcId > 0 && query.BountyPublished(npcId, row.MonsterId);

                case TriggerBountyCompleted:
                case TriggerBountyCompletedTalk:
                    return query != null && npcId > 0 && query.BountyCompleted(npcId, row.MonsterId);

                case TriggerBountyDied:
                case TriggerBountyDiedTalk:
                    return query != null && npcId > 0 && query.BountyHunterDied(npcId, row.MonsterId);

                case TriggerBountyTerminated:
                    return query != null && npcId > 0 && query.BountyFailed(npcId, row.MonsterId);

                // ---- 目标消失 ----
                case TriggerTargetCleared:
                    // 购买情报后、发布悬赏前，同一怪物被别人清除。
                    return query != null && row.MonsterId > 0 && query.MonsterSpawned(row.MonsterId)
                           && !query.MonsterAlive(row.MonsterId);

                // ---- NPC 关系（Ex02 双 NPC 事件，首版单 NPC 播放）----
                case TriggerRelationHostile:
                    // A 对 B 的好感由非负降为负（「本次变化」由世界侧判定并维护绑定）。
                    return query != null && npcId > 0 && relatedNpcId > 0 && query.IsHostileWith(npcId, relatedNpcId);

                case TriggerTalkOtherParty:
                    // 与绑定的另一方 B 交谈；两人存活且 A 对 B 仍为负。
                    return query != null && npcId > 0 && relatedNpcId > 0 && query.GetFavorBetween(npcId, relatedNpcId) < 0;

                case TriggerTalkEventNpc:
                    // 返回绑定的 A 交谈；A 对 B 仍为负。
                    return query != null && npcId > 0 && relatedNpcId > 0 && query.GetFavorBetween(npcId, relatedNpcId) < 0;

                // ---- 好感门槛（表内取值 5 / 10）----
                case TriggerFavorReached:
                    return query != null && npcId > 0 && query.GetFavorToPlayer(npcId) >= row.FavorThreshold;

                default:
                    Warn("未识别的 trigger 词「" + trigger + "」（阶段 " + row.Id + "）默认按满足处理。");
                    return true;
            }
        }

        /// <summary>某 trigger 词是否在已收录词表内（供策划补词表的自检）。</summary>
        public static bool IsKnownTrigger(string trigger)
        {
            switch (trigger)
            {
                case TriggerTalk:
                case TriggerFirstTalk:
                case TriggerHoldItem:
                case TriggerBoughtFromNpc:
                case TriggerDeliveredToNpc:
                case TriggerNpcCooked:
                case TriggerNpcForged:
                case TriggerNpcBattleDone:
                case TriggerNpcBountyDied:
                case TriggerNpcHealed:
                case TriggerIntelBought:
                case TriggerBountyPublishedTalk:
                case TriggerBountyPublished:
                case TriggerBountyCompleted:
                case TriggerBountyCompletedTalk:
                case TriggerBountyDied:
                case TriggerBountyDiedTalk:
                case TriggerBountyTerminated:
                case TriggerTargetCleared:
                case TriggerRelationHostile:
                case TriggerTalkOtherParty:
                case TriggerTalkEventNpc:
                case TriggerFavorReached:
                    return true;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------
        // 事件
        // ------------------------------------------------------------------

        /// <summary>会话开播（<see cref="StartGroup"/> 产出非空会话时触发，供 HUD / 音效 / 暂停世界用）。</summary>
        public event Action<DialogueSession> SessionStarted;

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------

        /// <summary>按权重抽一项（权重 &lt;=0 视为 0；总权重为 0 时等概率抽）。</summary>
        private T PickWeighted<T>(List<T> items, Func<T, int> weightOf)
        {
            if (items == null || items.Count == 0)
            {
                return default(T);
            }

            int total = 0;
            for (int i = 0; i < items.Count; i++)
            {
                int w = weightOf(items[i]);
                if (w > 0)
                {
                    total += w;
                }
            }

            if (total <= 0)
            {
                return items[NextIndex(items.Count)];
            }

            int roll = NextIndex(total);
            int acc = 0;
            for (int i = 0; i < items.Count; i++)
            {
                int w = weightOf(items[i]);
                if (w <= 0)
                {
                    continue;
                }

                acc += w;
                if (roll < acc)
                {
                    return items[i];
                }
            }

            return items[items.Count - 1];
        }

        /// <summary>取 [0, exclusive) 随机下标；随机源缺失时退回 0，保证行为可复现。</summary>
        private int NextIndex(int exclusive)
        {
            if (exclusive <= 0)
            {
                return 0;
            }

            if (_random == null)
            {
                return 0;
            }

            int v = _random.Next(0, exclusive);
            if (v < 0 || v >= exclusive)
            {
                return 0;
            }

            return v;
        }

        private void Warn(string message)
        {
            if (!_warnings.Contains(message))
            {
                _warnings.Add(message);
            }
        }
    }
}
