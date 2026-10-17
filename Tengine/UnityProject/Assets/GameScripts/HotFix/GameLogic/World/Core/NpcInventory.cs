using System.Collections.Generic;

using ConfigItem = GameConfig.ht.Item;

namespace GameLogic.World
{
    /// <summary>
    /// 装备槽（与 Item 表 type 列一一对应：<c>1=武器</c> / <c>2=防具</c> / <c>3=饰品</c>；
    /// 取值常量来自 <see cref="GameConfigCatalog.TypeWeapon"/> 等，避免在本类重复定义魔法数字）。
    /// </summary>
    public enum EquipmentSlot
    {
        /// <summary>空槽。</summary>
        None = 0,

        /// <summary>武器槽（Item type=1）。</summary>
        Weapon = 1,

        /// <summary>防具槽（Item type=2）。</summary>
        Armor = 2,

        /// <summary>饰品槽（Item type=3）。</summary>
        Accessory = 3,
    }

    /// <summary>
    /// NPC 背包与装备（master-plan M7）。
    ///
    /// 语义约定（与 plan M7 规格 / 《角色系统 v3.1》3.5.2、《角色行为系统策划案》「死亡后背包物品可被拾取」对齐）：
    /// - <b>背包</b>：<see cref="Items"/> 为 <c>itemId → 数量</c>；数量扣到 0 时条目直接移除，不留 0 值脏数据。
    /// - <b>装备槽</b>：三个 <c>Equipped*Id</c> 字段，<c>0 = 空</c>。
    /// - <b>装备消耗</b>：<see cref="Equip"/> 采用「从背包取出 1 件并移入装备槽」，即<b>装备后该物品不再留在
    ///   <see cref="Items"/> 里</b>（<see cref="Unequip(int)"/> 会把它原样放回背包）。好处：<see cref="ExportAll"/>
    ///   天然覆盖「死亡掉落 = 背包 + 身上装备」，不需要额外判断物品是否被穿在身上。
    /// - <b>配置回退</b>：<see cref="GameConfigCatalog"/> 未 Init / Item 表读不到该行时，无法判定 type，
    ///   此时<b>回退允许装备</b>并按「武器 → 防具 → 饰品」顺序找空槽（全部占用则替换武器槽），
    ///   每次回退都写一条去重后的告警到 <see cref="Warnings"/>；装备加成在配置缺失时按 <b>0</b> 计。
    ///
    /// 设计约束：纯逻辑，不依赖 UnityEngine；不修改本类以外的任何状态（背包/装备自包含）。
    /// </summary>
    public sealed class NpcInventory
    {
        // ------------------------------------------------------------------
        // 状态
        // ------------------------------------------------------------------

        private readonly Dictionary<int, int> _items = new Dictionary<int, int>();
        private readonly List<string> _warnings = new List<string>();

        /// <summary>武器槽物品 id（0 = 空；对应 Item type=1）。</summary>
        public int EquippedWeaponId;

        /// <summary>防具槽物品 id（0 = 空；对应 Item type=2）。</summary>
        public int EquippedArmorId;

        /// <summary>饰品槽物品 id（0 = 空；对应 Item type=3）。</summary>
        public int EquippedAccessoryId;

        // ------------------------------------------------------------------
        // 只读视图
        // ------------------------------------------------------------------

        /// <summary>背包内容（<c>itemId → 数量</c>，只读视图；写入请走 <see cref="Add"/> / <see cref="TryRemove"/>）。</summary>
        public IReadOnlyDictionary<int, int> Items
        {
            get { return _items; }
        }

        /// <summary>配置回退 / 非法操作告警（已去重；由 <see cref="Warnings"/> 追加，供 HUD / 日志排查）。</summary>
        public IReadOnlyList<string> Warnings
        {
            get { return _warnings; }
        }

        /// <summary>背包是否为空（不含装备槽）。</summary>
        public bool IsEmpty
        {
            get { return _items.Count == 0; }
        }

        // ------------------------------------------------------------------
        // 背包读写
        // ------------------------------------------------------------------

        /// <summary>背包中某物品的数量；没有为 0。</summary>
        public int Count(int itemId)
        {
            int count;
            return _items.TryGetValue(itemId, out count) ? count : 0;
        }

        /// <summary>背包中是否持有指定数量（<paramref name="count"/> 默认 1；&lt;= 0 视为「不需要持有」，恒 true）。</summary>
        public bool HasItem(int itemId, int count = 1)
        {
            if (count <= 0)
            {
                return true;
            }

            return Count(itemId) >= count;
        }

        /// <summary>
        /// 背包总件数（各物品数量求和；装备槽里的物品不计入）。
        /// </summary>
        public int TotalCount()
        {
            int total = 0;
            foreach (KeyValuePair<int, int> pair in _items)
            {
                total += pair.Value;
            }

            return total;
        }

        /// <summary>
        /// 放入物品。<paramref name="itemId"/> &lt;= 0 或 <paramref name="count"/> &lt;= 0 时<b>静默忽略</b>
        /// （不产生告警，避免每帧调用刷屏），数量累加。
        /// </summary>
        public void Add(int itemId, int count)
        {
            if (itemId <= 0 || count <= 0)
            {
                return;
            }

            _items[itemId] = Count(itemId) + count;
        }

        /// <summary>
        /// 取出物品：数量足够才扣减，扣到 0 的条目直接移除（不保留 0 值）。
        /// <paramref name="count"/> &lt;= 0 视为「无事可做」返回 true（与 <see cref="Inventory.TryRemove"/> 口径一致）。
        /// </summary>
        public bool TryRemove(int itemId, int count)
        {
            if (itemId <= 0)
            {
                return false;
            }

            if (count <= 0)
            {
                return true;
            }

            int have = Count(itemId);
            if (have < count)
            {
                return false;
            }

            int left = have - count;
            if (left <= 0)
            {
                _items.Remove(itemId);
            }
            else
            {
                _items[itemId] = left;
            }

            return true;
        }

        // ------------------------------------------------------------------
        // 装备
        // ------------------------------------------------------------------

        /// <summary>
        /// 装备物品：校验「物品在背包」且「Item 表 type ∈ {1,2,3}」，确定槽位后
        /// <b>先卸下同槽已有装备（放回背包）</b>，再从背包取出 1 件放入该槽。
        ///
        /// 返回值：true=装备成功；false=itemId 非法、背包里没有、或配置里 type 不是 1/2/3（非装备）。
        /// 已经穿在目标槽上的同一物品重复调用视为幂等成功（true），不重复消耗。
        ///
        /// 副作用：背包减少 1 件；同槽旧装备回到背包；配置缺失时允许装备并写入 <see cref="Warnings"/>。
        /// </summary>
        public bool Equip(int itemId)
        {
            if (itemId <= 0)
            {
                return false;
            }

            SlotResolve resolve = ResolveSlot(itemId, out EquipmentSlot slot);

            if (resolve == SlotResolve.NotEquipment)
            {
                return false;
            }

            if (resolve == SlotResolve.MissingConfig && IsEquipped(itemId))
            {
                // 槽位不可判定（配置缺失）且该物品已穿在身上：幂等返回，
                // 避免同一 id 在「回退槽位」与「原槽位」上各占一格、物品凭空多出一件。
                return true;
            }

            if (resolve == SlotResolve.Resolved && GetEquippedId(slot) == itemId)
            {
                // 幂等：已经穿在目标槽上，不重复消耗背包里的同名物品。
                return true;
            }

            if (!TryRemove(itemId, 1))
            {
                // 背包里没有这件装备（不满足「物品在背包」前提）。
                return false;
            }

            // 同槽已装备 → 先卸下（旧装备回到背包），再装新的。
            Unequip(slot);

            SetEquippedId(slot, itemId);
            return true;
        }

        /// <summary>
        /// 卸下指定槽位（<paramref name="slot"/> ∈ {1,2,3}，其余值返回 false），装备物放回背包。
        /// </summary>
        public bool Unequip(int slot)
        {
            if (slot < (int)EquipmentSlot.Weapon || slot > (int)EquipmentSlot.Accessory)
            {
                return false;
            }

            return Unequip((EquipmentSlot)slot);
        }

        /// <summary>卸下指定槽位，装备物放回背包；槽位为空返回 false。</summary>
        public bool Unequip(EquipmentSlot slot)
        {
            int itemId = GetEquippedId(slot);
            if (itemId <= 0)
            {
                return false;
            }

            SetEquippedId(slot, 0);
            Add(itemId, 1);
            return true;
        }

        /// <summary>取某槽位当前装备的物品 id（空槽返回 0）。</summary>
        public int GetEquippedId(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.Weapon:
                    return EquippedWeaponId;
                case EquipmentSlot.Armor:
                    return EquippedArmorId;
                case EquipmentSlot.Accessory:
                    return EquippedAccessoryId;
                default:
                    return 0;
            }
        }

        /// <summary>取某槽位当前装备的物品 id（<paramref name="slot"/> ∈ {1,2,3}，其余值返回 0）。</summary>
        public int GetEquippedId(int slot)
        {
            return GetEquippedId((EquipmentSlot)slot);
        }

        /// <summary>物品是否正穿在身上（任意槽位）。</summary>
        public bool IsEquipped(int itemId)
        {
            if (itemId <= 0)
            {
                return false;
            }

            return EquippedWeaponId == itemId
                   || EquippedArmorId == itemId
                   || EquippedAccessoryId == itemId;
        }

        /// <summary>槽位写入（集中一处，避免 Equip / Unequip 漏改某个字段）。</summary>
        private void SetEquippedId(EquipmentSlot slot, int itemId)
        {
            switch (slot)
            {
                case EquipmentSlot.Weapon:
                    EquippedWeaponId = itemId;
                    break;
                case EquipmentSlot.Armor:
                    EquippedArmorId = itemId;
                    break;
                case EquipmentSlot.Accessory:
                    EquippedAccessoryId = itemId;
                    break;
            }
        }

        /// <summary>槽位解析结果。</summary>
        private enum SlotResolve
        {
            /// <summary>配置命中且 type ∈ {1,2,3}，已给出槽位。</summary>
            Resolved = 0,

            /// <summary>配置命中但 type 不是 1/2/3，不允许装备。</summary>
            NotEquipment = 1,

            /// <summary>配置缺失（目录未 Init / 表读不到），回退允许装备。</summary>
            MissingConfig = 2,
        }

        /// <summary>
        /// 按 Item 表 type 列解析目标槽位。
        /// 配置缺失时按「武器 → 防具 → 饰品」找空槽（都占用则替换武器槽）并写告警；
        /// 配置命中但 type 非 1/2/3 时返回 <see cref="SlotResolve.NotEquipment"/>（调用方直接失败）。
        /// </summary>
        private SlotResolve ResolveSlot(int itemId, out EquipmentSlot slot)
        {
            var catalog = GameConfigCatalog.Instance;
            ConfigItem row = catalog != null ? catalog.GetItemRow(itemId) : null;

            if (row == null)
            {
                slot = FallbackSlot();
                Warn(catalog == null
                    ? "GameConfigCatalog 未 Init，物品 " + itemId + " 的 type 取不到，回退按空槽顺序装备（武器→防具→饰品），实际槽位=" + (int)slot + "。"
                    : "Item 表查不到物品 " + itemId + " 的配置行，type 取不到，回退按空槽顺序装备（武器→防具→饰品），实际槽位=" + (int)slot + "。");
                return SlotResolve.MissingConfig;
            }

            if (row.Type == GameConfigCatalog.TypeWeapon)
            {
                slot = EquipmentSlot.Weapon;
            }
            else if (row.Type == GameConfigCatalog.TypeArmor)
            {
                slot = EquipmentSlot.Armor;
            }
            else if (row.Type == GameConfigCatalog.TypeAccessory)
            {
                slot = EquipmentSlot.Accessory;
            }
            else
            {
                slot = EquipmentSlot.None;
                Warn("物品 " + itemId + "《" + row.Name + "》的 type=" + row.Type + " 不是 1/2/3，不能装备。");
                return SlotResolve.NotEquipment;
            }

            return SlotResolve.Resolved;
        }

        /// <summary>配置缺失时的槽位回退：武器 → 防具 → 饰品；全占用则替换武器槽。</summary>
        private EquipmentSlot FallbackSlot()
        {
            if (EquippedWeaponId == 0)
            {
                return EquipmentSlot.Weapon;
            }

            if (EquippedArmorId == 0)
            {
                return EquipmentSlot.Armor;
            }

            if (EquippedAccessoryId == 0)
            {
                return EquipmentSlot.Accessory;
            }

            return EquipmentSlot.Weapon;
        }

        // ------------------------------------------------------------------
        // 装备加成（Item 表 hp / str / intelligence / def 列求和）
        // ------------------------------------------------------------------

        private static readonly EquipmentBonus ZeroBonus = new EquipmentBonus(0, 0, 0, 0);

        private GameConfigCatalog _bonusCatalog;
        private int _bonusWeapon = int.MinValue;
        private int _bonusArmor = int.MinValue;
        private int _bonusAccessory = int.MinValue;
        private EquipmentBonus _bonus;

        /// <summary>装备提供的生命加成总和（三个槽位 hp 列求和；配置缺失按 0）。</summary>
        public int BonusHp
        {
            get { EnsureBonus(); return _bonus.Hp; }
        }

        /// <summary>装备提供的力量加成总和（三个槽位 str 列求和；配置缺失按 0）。</summary>
        public int BonusStr
        {
            get { EnsureBonus(); return _bonus.Str; }
        }

        /// <summary>装备提供的智力加成总和（三个槽位 intelligence 列求和；配置缺失按 0）。</summary>
        public int BonusInt
        {
            get { EnsureBonus(); return _bonus.Intelligence; }
        }

        /// <summary>装备提供的防御加成总和（三个槽位 def 列求和；配置缺失按 0）。</summary>
        public int BonusDef
        {
            get { EnsureBonus(); return _bonus.Def; }
        }

        /// <summary>
        /// 加成缓存刷新：仅在「装备三槽 id 或配置目录实例」变化时重读 Item 表，
        /// 因此直接改写 <see cref="EquippedWeaponId"/> 等公开字段也会在下一次读加成时自动生效。
        /// </summary>
        private void EnsureBonus()
        {
            var catalog = GameConfigCatalog.Instance;

            if (ReferenceEquals(catalog, _bonusCatalog)
                && EquippedWeaponId == _bonusWeapon
                && EquippedArmorId == _bonusArmor
                && EquippedAccessoryId == _bonusAccessory)
            {
                return;
            }

            EquipmentBonus w = ReadBonus(catalog, EquippedWeaponId);
            EquipmentBonus a = ReadBonus(catalog, EquippedArmorId);
            EquipmentBonus x = ReadBonus(catalog, EquippedAccessoryId);

            _bonus = new EquipmentBonus(
                w.Hp + a.Hp + x.Hp,
                w.Str + a.Str + x.Str,
                w.Intelligence + a.Intelligence + x.Intelligence,
                w.Def + a.Def + x.Def);

            _bonusCatalog = catalog;
            _bonusWeapon = EquippedWeaponId;
            _bonusArmor = EquippedArmorId;
            _bonusAccessory = EquippedAccessoryId;
        }

        /// <summary>读单件装备的加成列；物品为空 / 目录未 Init / 行缺失都按 0 计（行缺失写去重告警）。</summary>
        private EquipmentBonus ReadBonus(GameConfigCatalog catalog, int itemId)
        {
            if (itemId <= 0 || catalog == null)
            {
                return ZeroBonus;
            }

            ConfigItem row = catalog.GetItemRow(itemId);
            if (row == null)
            {
                Warn("已装备物品 " + itemId + " 在 Item 表没有配置行，装备加成按 0 计。");
                return ZeroBonus;
            }

            return new EquipmentBonus(row.Hp, row.Str, row.Intelligence, row.Def);
        }

        // ------------------------------------------------------------------
        // 死亡掉落 / 整体搬运
        // ------------------------------------------------------------------

        /// <summary>
        /// 导出全部持有物（死亡掉落用）：<b>背包 + 三个装备槽合并</b>，同名物品数量相加。
        /// 返回新字典，调用方可以安全持有（与本对象解耦）；本对象状态不受影响。
        /// 金币不在此列（金币由 <c>CharacterData.Gold</c> 单独处理）。
        /// </summary>
        public Dictionary<int, int> ExportAll()
        {
            var result = new Dictionary<int, int>(_items.Count + 3);

            foreach (KeyValuePair<int, int> pair in _items)
            {
                result[pair.Key] = pair.Value;
            }

            MergeEquipped(result, EquippedWeaponId);
            MergeEquipped(result, EquippedArmorId);
            MergeEquipped(result, EquippedAccessoryId);

            return result;
        }

        private static void MergeEquipped(Dictionary<int, int> target, int itemId)
        {
            if (itemId <= 0)
            {
                return;
            }

            int have;
            target[itemId] = target.TryGetValue(itemId, out have) ? have + 1 : 1;
        }

        /// <summary>
        /// 清空背包、全部装备槽与 <see cref="Warnings"/>。<b>调用方需先 <see cref="ExportAll"/> 取走掉落物</b>，
        /// 否则物品会永久丢失（死亡结算流程：先 ExportAll 落地 LootBag，再 ClearAll）。
        /// </summary>
        public void ClearAll()
        {
            _items.Clear();
            EquippedWeaponId = 0;
            EquippedArmorId = 0;
            EquippedAccessoryId = 0;
            _warnings.Clear();
        }

        // ------------------------------------------------------------------
        // 内部
        // ------------------------------------------------------------------

        /// <summary>告警追加（按文本去重，避免同一缺失配置每帧刷屏）。</summary>
        private void Warn(string message)
        {
            if (!_warnings.Contains(message))
            {
                _warnings.Add(message);
            }
        }

        public override string ToString()
        {
            return "items=" + _items.Count
                   + ",total=" + TotalCount()
                   + ",w=" + EquippedWeaponId
                   + ",a=" + EquippedArmorId
                   + ",acc=" + EquippedAccessoryId
                   + ",bonus=" + BonusHp + "/" + BonusStr + "/" + BonusInt + "/" + BonusDef
                   + ",warnings=" + _warnings.Count;
        }

        /// <summary>单件装备的属性加成（Item 表 hp / str / intelligence / def 列）。</summary>
        private struct EquipmentBonus
        {
            public readonly int Hp;
            public readonly int Str;
            public readonly int Intelligence;
            public readonly int Def;

            public EquipmentBonus(int hp, int str, int intelligence, int def)
            {
                Hp = hp;
                Str = str;
                Intelligence = intelligence;
                Def = def;
            }
        }
    }
}
