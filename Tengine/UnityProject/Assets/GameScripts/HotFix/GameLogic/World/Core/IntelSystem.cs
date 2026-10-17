using System;
using System.Collections.Generic;

namespace GameLogic.World
{
    /// <summary>
    /// 情报种类（M4，出处《立项规划》「怪物刷新后占卜师 / 画家 / 吟游诗人分别获得位置 / 形象 / 属性情报」）。
    /// 矛盾 #2：三类情报统一由吟游诗人出售，因此本枚举只描述<strong>情报内容类别</strong>，不描述获取者职业。
    /// </summary>
    public enum IntelKind
    {
        /// <summary>位置情报（记录怪物刷新所在地图 <see cref="IntelRecord.MapId"/>）。</summary>
        Location = 1,

        /// <summary>形象情报（美术/描述用，本期只落类型标记）。</summary>
        Appearance = 2,

        /// <summary>属性情报（数值参考，本期只落类型标记）。</summary>
        Stats = 3,
    }

    /// <summary>
    /// 一条情报/线索（M4，纯逻辑值对象，无 UnityEngine 依赖）。
    ///
    /// 核心口径：
    /// - <b>不堆叠</b>（《悬赏系统需求案》3.1）：同一条 <see cref="Id"/> 不会被重复持有，
    ///   <see cref="IntelSystem.TryConsumePlayerIntel"/> 消耗后即从持有列表移除，扣完即无（需重新购买 / 重新获知）。
    /// - <see cref="Id"/> 既是内部主键，也是悬赏发布时填写的「线索编号」；
    ///   编号段见 <see cref="IntelSystem.IntelItemBaseId"/>（占位，Item 表落地后迁配置）。
    /// </summary>
    public sealed class IntelRecord
    {
        /// <summary>情报唯一编号（= 悬赏表的 intel 字段填写的值）。</summary>
        public int Id;

        /// <summary>情报类别（位置 / 形象 / 属性）。</summary>
        public IntelKind Kind;

        /// <summary>对应怪物定义 id（<see cref="MonsterDef.Id"/>）。</summary>
        public int MonsterDefId;

        /// <summary>对应怪物名（<see cref="MonsterDef.Name"/>，悬赏 target 匹配用）。</summary>
        public string MonsterName;

        /// <summary>情报内容里的地图 id（<see cref="IntelKind.Location"/> 记录怪物<strong>刷新时</strong>所在地图；后续迁图不回写）。</summary>
        public int MapId;

        /// <summary>情报<strong>到来时间 = 怪物刷新日</strong>（非商店上架日、非购买日）。</summary>
        public int DayOverall;

        /// <summary>
        /// 不堆叠：同编号只可能存在一份，消耗（<see cref="IntelSystem.TryConsumePlayerIntel"/>）后即无。
        /// 调链式重入时保留此说明，避免误以为存在数量/堆叠字段。
        /// </summary>
        public override string ToString()
        {
            return $"#{Id} {MonsterName} {Kind} map:{MapId} day:{DayOverall}";
        }
    }

    /// <summary>
    /// 情报/线索系统（M4，纯逻辑，无 UnityEngine 依赖）。
    ///
    /// 数据流：
    /// 1. 怪物刷新 → <see cref="WorldSimulation"/> 订阅 <see cref="MonsterSystem.Spawned"/>，
    ///    对每只新怪物调用 <see cref="CreateForMonster"/> 生成 3 条（<see cref="IntelKind.Location"/> /
    ///    <see cref="IntelKind.Appearance"/> / <see cref="IntelKind.Stats"/> 各一）；
    /// 2. 优先交给「当日在城镇图的存活吟游诗人」（<see cref="GrantToNpc"/>，作为待售清单，M9 补充货源）；
    /// 3. 没有吟游诗人时线索进系统池暂存（<see cref="ParkIntel"/>），并按占位口径给玩家一条位置情报
    ///    （<see cref="GrantToPlayer"/>，<b>占位待策划确认</b>：正式口径应以吟游诗人售卖为准）；
    /// 4. 玩家从吟游诗人处购买 / 获赠 → <see cref="GrantToPlayer"/> 入账；
    /// 5. 发布悬赏（M5）按情报编号消耗 → <see cref="TryConsumePlayerIntel"/>，<b>消耗即无，不堆叠</b>。
    ///
    /// 事件：<see cref="IntelObtained"/> 在线索归属落定（玩家入账 / NPC 入账）时触发，
    /// 由 <see cref="WorldSimulation"/> 转发到 <c>WorldEventIds.IntelObtained</c> 供 UI 刷新。
    /// </summary>
    public sealed class IntelSystem
    {
        /// <summary>
        /// 情报 id 起始段（占位常量）：<see cref="CreateForMonster"/> 生成的第一条线索取 9000，其后逐条自增。
        /// 占位口径：真正落地时应迁到 Item 表 / 配置目录（情报即物品），避免与真实物品 id 段冲突。
        /// </summary>
        public const int IntelItemBaseId = 9000;

        /// <summary>每只新怪物固定生成的三类情报顺序（位置 / 形象 / 属性）。</summary>
        private static readonly IntelKind[] SpawnKinds =
        {
            IntelKind.Location, IntelKind.Appearance, IntelKind.Stats,
        };

        private readonly List<IntelRecord> _playerIntel = new List<IntelRecord>();
        private readonly List<IntelRecord> _parked = new List<IntelRecord>();
        private readonly Dictionary<int, List<IntelRecord>> _npcIntel = new Dictionary<int, List<IntelRecord>>();

        /// <summary>下一条情报的编号（自 <see cref="IntelItemBaseId"/> 起自增）。</summary>
        private int _nextIntelId = IntelItemBaseId;

        /// <summary>玩家当前持有的线索（不堆叠，同编号至多一条）。</summary>
        public IReadOnlyList<IntelRecord> PlayerIntel => _playerIntel;

        /// <summary>系统池暂存的线索（刷新时城镇里没有存活吟游诗人可接收的线索）。</summary>
        public IReadOnlyList<IntelRecord> ParkedIntel => _parked;

        /// <summary>
        /// 当前经营日，作为新线索的 <see cref="IntelRecord.DayOverall"/>（口径：到来时间 = 刷新日）。
        /// 由 <see cref="WorldSimulation"/> 在生成线索前调用 <see cref="SetCurrentDay"/> 更新。
        /// </summary>
        public int CurrentDayOverall { get; private set; } = 1;

        /// <summary>情报落定（玩家入账 / NPC 入账）时触发。参数：情报记录。M5 悬赏面板 / UI 刷新用。</summary>
        public event Action<IntelRecord> IntelObtained;

        /// <summary>设置当前经营日（<see cref="IntelRecord.DayOverall"/> 取值来源）。非法值（&lt;1）钳到 1。</summary>
        public void SetCurrentDay(int dayOverall)
        {
            CurrentDayOverall = Math.Max(1, dayOverall);
        }

        /// <summary>
        /// 为一只新刷新的怪物生成一条情报（编号 <see cref="_nextIntelId"/> 自增，取值从 <see cref="IntelItemBaseId"/> 起）。
        /// 只<strong>生成</strong>不<strong>入账</strong>：归属由调用方决定（交吟游诗人 / 暂存系统池 / 给玩家）。
        /// <paramref name="monster"/> 为 null 或 <c>Def</c> 为空时返回 null（不抛异常，刷新链路不能因情报中断）。
        /// </summary>
        public IntelRecord CreateForMonster(Monster monster, IntelKind kind)
        {
            if (monster == null || monster.Def == null)
            {
                return null;
            }

            var record = new IntelRecord
            {
                Id = _nextIntelId++,
                Kind = kind,
                MonsterDefId = monster.Def.Id,
                MonsterName = monster.Def.Name,
                // 位置情报记录的是「刷新那一刻」的地图；怪物后续狩猎迁入城镇不回写本字段（情报是快照）。
                MapId = monster.Position.MapId,
                DayOverall = CurrentDayOverall,
            };
            return record;
        }

        /// <summary>按怪物刷新生成三类情报（位置 / 形象 / 属性各一）的便捷入口（顺序同 <see cref="SpawnKinds"/>）。</summary>
        public IReadOnlyList<IntelRecord> CreateAllForMonster(Monster monster)
        {
            var list = new List<IntelRecord>(SpawnKinds.Length);
            for (int i = 0; i < SpawnKinds.Length; i++)
            {
                var record = CreateForMonster(monster, SpawnKinds[i]);
                if (record != null)
                {
                    list.Add(record);
                }
            }

            return list;
        }

        /// <summary>玩家是否持有该编号的线索（M5 悬赏发布「线索未持有」禁用判定用）。</summary>
        public bool PlayerHas(int intelId)
        {
            return IndexOfPlayer(intelId) >= 0;
        }

        /// <summary>
        /// 消耗玩家线索（M5 悬赏发布时按 intel 字段扣一条）。命中即从持有列表<strong>移除</strong>并返回 true。
        /// 不堆叠 ⇒ 消耗后 <see cref="PlayerHas"/> 立刻变 false，重复调用返回 false（不会二次扣减）。
        /// </summary>
        public bool TryConsumePlayerIntel(int intelId)
        {
            int index = IndexOfPlayer(intelId);
            if (index < 0)
            {
                return false;
            }

            _playerIntel.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// 线索入玩家账（购买 / 赠送 / 占位直给）。null 忽略；同编号已持有时忽略（<b>不堆叠</b>）；
        /// 入账成功触发 <see cref="IntelObtained"/>。
        /// </summary>
        public void GrantToPlayer(IntelRecord record)
        {
            if (record == null || PlayerHas(record.Id))
            {
                return;
            }

            _playerIntel.Add(record);
            IntelObtained?.Invoke(record);
        }

        /// <summary>
        /// 暂存到系统池（刷新时城镇内没有存活吟游诗人可接收）。
        /// 暂存不等于入账：玩家不可见，<see cref="PlayerHas"/> 不受影响；
        /// 吟游诗人日后到城（M9 补充货源）时可以从这里取走，见 <see cref="ParkedIntel"/>。
        /// </summary>
        public void ParkIntel(IntelRecord record)
        {
            if (record == null)
            {
                return;
            }

            _parked.Add(record);
            IntelObtained?.Invoke(record);
        }

        /// <summary>某角色（吟游诗人）当前持有的待售情报清单；未持有过该角色时返回空列表（不分配、可安全遍历）。</summary>
        public IReadOnlyList<IntelRecord> IntelOfNpc(int characterId)
        {
            return _npcIntel.TryGetValue(characterId, out var list) ? list : (IReadOnlyList<IntelRecord>)new IntelRecord[0];
        }

        /// <summary>
        /// 情报入某 NPC 账（吟游诗人待售清单）。null / 空入参忽略；同编号重复入账忽略（<b>不堆叠</b>）；
        /// 入账成功触发 <see cref="IntelObtained"/>（供 UI「吟游诗人有新线索」提示）。
        /// </summary>
        public void GrantToNpc(int characterId, IntelRecord record)
        {
            if (record == null)
            {
                return;
            }

            if (!_npcIntel.TryGetValue(characterId, out var list))
            {
                list = new List<IntelRecord>();
                _npcIntel[characterId] = list;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == record.Id)
                {
                    return;
                }
            }

            list.Add(record);
            IntelObtained?.Invoke(record);
        }

        /// <summary>从某 NPC 账上移除一条情报（NPC 售出 / 消耗；不回退到玩家或系统池）。命中返回 true。</summary>
        public bool TryConsumeNpcIntel(int characterId, int intelId)
        {
            if (!_npcIntel.TryGetValue(characterId, out var list))
            {
                return false;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == intelId)
                {
                    list.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>测试 / 调试用：按编号找一条玩家线索（找不到返回 null）。</summary>
        public IntelRecord FindPlayerIntel(int intelId)
        {
            int index = IndexOfPlayer(intelId);
            return index >= 0 ? _playerIntel[index] : null;
        }

        private int IndexOfPlayer(int intelId)
        {
            for (int i = 0; i < _playerIntel.Count; i++)
            {
                if (_playerIntel[i].Id == intelId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
