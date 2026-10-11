namespace GameLogic.World
{
    /// <summary>
    /// 职业（R3/R4）。文档矛盾：职业案写“共 6 种”但列出 7 种，本实现按 7 种（任务文档矛盾记录 1）。
    /// 数值对齐预设角色表 profession 键位（0=无，1=厨师，2=商人，3=铁匠，4=农民），其余顺延。
    /// </summary>
    public enum ProfessionType
    {
        None = 0,
        Chef = 1,
        Merchant = 2,
        Blacksmith = 3,
        Farmer = 4,
        Warrior = 5,
        Priest = 6,
        Bard = 7,
    }

    /// <summary>
    /// 性格（R3：单一性格，personality=1/2/3/4...）。词表见 Character 表（本地缺失），先收录文档示例三项。
    /// </summary>
    public enum PersonalityType
    {
        None = 0,
        Cheerful = 1,
        Calm = 2,
        Cautious = 3,
    }
}
