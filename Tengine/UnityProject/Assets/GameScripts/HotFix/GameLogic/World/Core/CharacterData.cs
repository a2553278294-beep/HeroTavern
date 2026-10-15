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

        /// <summary>对玩家好感度，初始 0，上限 100。</summary>
        public int FavorToPlayer;

        /// <summary>持有金币（初始值文档未定，占位 0，待配置）。</summary>
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
