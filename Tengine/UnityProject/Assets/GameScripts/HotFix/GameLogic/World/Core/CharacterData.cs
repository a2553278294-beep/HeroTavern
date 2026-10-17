namespace GameLogic.World
{
    /// <summary>
    /// 角色数据（R3：形象、姓名、年龄、性格、属性、职业；形象随美术系统后续接入）。
    /// </summary>
    public sealed class CharacterData
    {
        /// <summary>实体唯一标识。</summary>
        public int Id;

        /// <summary>姓名（姓+名，全局不重复）。</summary>
        public string Name;

        /// <summary>年龄，初始 18-40，跨年 +1。</summary>
        public int Age;

        public PersonalityType Personality;
        public ProfessionType Profession;

        /// <summary>生命上限（生成时按权重公式抽取 0-100）。</summary>
        public int MaxHp;

        /// <summary>当前生命值。</summary>
        public int Hp;

        /// <summary>力量。</summary>
        public int Str;

        /// <summary>智力（文档中“智慧”与“智力”混用，统一为智力）。</summary>
        public int Int;

        /// <summary>防御。生成规则未覆盖（矛盾记录 2），默认 0，受装备影响。</summary>
        public int Def;

        /// <summary>
        /// M7：NPC 背包与装备（<see cref="NpcInventory"/>）。背包存物、三个装备槽存身上装备，
        /// 死亡时由 <c>WorldSimulation.Kill</c> 用 <see cref="NpcInventory.ExportAll"/> 整体掉落。
        /// 装备的属性加成不写回 <see cref="Str"/> / <see cref="Int"/> / <see cref="Def"/> / <see cref="MaxHp"/>，
        /// 战斗与分数统一读下面的 <c>Eff*</c> 访问器（基础字段保持「裸装值」语义）。
        /// </summary>
        public NpcInventory Bag = new NpcInventory();

        /// <summary>
        /// 有效生命上限 = 基础 <see cref="MaxHp"/> + 装备 hp 加成（<see cref="NpcInventory.BonusHp"/>）。
        /// M7 接线：怪物伤害 / 生命百分比相关计算一律走本访问器，不再直接读 <see cref="MaxHp"/>。
        /// </summary>
        public int EffMaxHp
        {
            get { return MaxHp + Bag.BonusHp; }
        }

        /// <summary>有效力量 = 基础 <see cref="Str"/> + 装备 str 加成（<see cref="NpcInventory.BonusStr"/>）。</summary>
        public int EffStr
        {
            get { return Str + Bag.BonusStr; }
        }

        /// <summary>有效智力 = 基础 <see cref="Int"/> + 装备 intelligence 加成（<see cref="NpcInventory.BonusInt"/>）。</summary>
        public int EffInt
        {
            get { return Int + Bag.BonusInt; }
        }

        /// <summary>有效防御 = 基础 <see cref="Def"/> + 装备 def 加成（<see cref="NpcInventory.BonusDef"/>）。</summary>
        public int EffDef
        {
            get { return Def + Bag.BonusDef; }
        }

        /// <summary>对玩家好感度，初始 0，上限 100。</summary>
        public int FavorToPlayer;

        /// <summary>
        /// 持有金币。初始值取 <see cref="CharacterFactory.MinInitialGold"/>~<see cref="CharacterFactory.MaxInitialGoldExclusive"/>
        /// 的随机整数（<b>占位</b>：文档未定，矛盾记录 14，<b>待策划定档</b>）；死亡时随背包一起落地成 <see cref="LootBag"/>。
        /// </summary>
        public int Gold;

        /// <summary>体力（初始 100；每小时 -1，移动每格 -1，待机每小时 +1——行为系统）。</summary>
        public int Stamina = 100;

        /// <summary>精神分（初始 100；每小时 -1）。</summary>
        public int Spirit = 100;

        /// <summary>健康分（按生命百分比每小时增减，见 CharacterScores）。</summary>
        public int HealthScore;

        /// <summary>荣誉分（击杀怪物/敌对角色获得）。</summary>
        public int HonorScore;

        /// <summary>信息分（每次获取信息 +1）。</summary>
        public int InfoScore;

        /// <summary>受伤无法行动的剩余天数（轻伤 1 天/重伤 3 天，0:00 递减）。</summary>
        public int ActionLockDays;

        public bool IsAlive = true;

        /// <summary>
        /// 是否处于「隐藏」状态（M3：城镇安全分低于 <see cref="TownSafety.HideThreshold"/> 时，
        /// 0:00 隐藏 1 名非必需居民；安全分回到阈值以上后 0:00 恢复 1 名）。
        ///
        /// 隐藏期间的语义：
        /// - <b>不参与行动 / 视图</b>：移动、格位互相避让、点单饮酒、怪物狩猎选目标、全员精神联动一律跳过；
        /// - <b>时间状态继续推进</b>：年龄（<see cref="Age"/>，含 &gt;40 的自然死亡判定）与受伤倒计时
        ///   （<see cref="ActionLockDays"/>）照常更新，隐藏不是「冻结」。
        ///
        /// TODO(M12)：View 层（<c>WorldSceneBootstrap.SyncList</c> / <c>CharacterView.Update</c>）尚未按本字段过滤显示，
        /// 当前只有逻辑层（移动 / 点单 / 被狩猎 / 精神联动）遵守隐藏规则。
        /// </summary>
        public bool IsHidden;

        /// <summary>true=本地定居人口，false=暂住人口（R4 分开统计）。</summary>
        public bool IsResident = true;

        /// <summary>当前所在网格坐标。</summary>
        public GridPos Position;

        /// <summary>移动目标；非空时每游戏小时沿最短路径走 1 格。</summary>
        public GridPos? MoveTarget;

        public override string ToString()
        {
            return $"#{Id} {Name} {Profession} age:{Age} hp:{Hp}/{MaxHp} str:{Str} int:{Int} pos:{Position}";
        }
    }
}
