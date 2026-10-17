using System;
using System.Collections.Generic;

namespace GameLogic.World
{
    /// <summary>
    /// 职业服务（M9，出处《英雄酒馆职业系统策划案》「各职业规则」；矛盾 #2：三类怪物情报统一由吟游诗人出售）。
    ///
    /// 七职业的功能性服务，纯逻辑、<b>不依赖 UnityEngine</b>：
    /// <list type="bullet">
    /// <item><b>厨师</b>：<see cref="ChefCook"/>，每日 1 次，原料不足失败（原料出 <paramref name="playerBag"/>，产物入同一仓库）；</item>
    /// <item><b>商人</b>：<see cref="GetMerchantGoods"/>，到店补货，2-5 件<b>非</b>农产品；</item>
    /// <item><b>铁匠</b>：<see cref="BlacksmithForge"/>，每日 1 次，材料不足失败（口径同厨师）；</item>
    /// <item><b>农民</b>：<see cref="GetFarmerGoods"/>，农产品原材料清单；</item>
    /// <item><b>战士</b>：<see cref="WarriorAccept(BountyEntry)"/>，接取战斗悬赏的资格判断；</item>
    /// <item><b>牧师</b>：<see cref="PriestHeal"/>，次数不限，恢复至 <see cref="CharacterData.EffMaxHp"/><b>不改</b> <see cref="CharacterData.ActionLockDays"/>；</item>
    /// <item><b>吟游诗人</b>：<see cref="BardAnnounceMonster"/> + <see cref="GetBardIntel"/>，传播怪物名/地点/到来时间，同一次怪物到来只通知一次。</item>
    /// </list>
    ///
    /// 每日限制口径：厨师 / 铁匠<b>各 1 次</b>，计数器按 <b>角色 Id</b> 分桶（同一职业多人各算各的），
    /// 由 <see cref="OnDaySettled"/> 在 <b>0:00</b> 清空。失败的调用（原料不足 / 参数非法 / 次数已用尽）<b>不消耗</b>次数。
    ///
    /// 商人 / 农民 / 牧师 / 战士「次数不限」；吟游诗人的「只通知一次」由 <see cref="HasAnnounced"/> 判重。
    ///
    /// 数据来源：商品目录读 <see cref="GameConfigCatalog"/>（未 Init 时回退为空清单，不抛异常）；
    /// 情报走注入的 <see cref="IntelSystem"/>（<see cref="IntelSystem.CreateAllForMonster"/> →
    /// 有吟游诗人则 <see cref="IntelSystem.GrantToNpc"/>，否则 <see cref="IntelSystem.ParkIntel"/> 暂存）。
    /// 随机一律走注入的 <see cref="IRandomSource"/>（种子可复现）。
    /// </summary>
    public sealed class ProfessionService
    {
        // ------------------------------------------------------------------
        // 占位常量
        // ------------------------------------------------------------------

        /// <summary>厨师每日烹饪次数（《职业系统策划案》「每天限一次」）。</summary>
        public const int ChefDailyLimit = 1;

        /// <summary>铁匠每日锻造次数（《职业系统策划案》「每天限一次」）。</summary>
        public const int BlacksmithDailyLimit = 1;

        /// <summary>商人到店补货的<b>最少</b>件数（占位区间 2-5，待策划定档）。</summary>
        public const int MerchantGoodsMin = 2;

        /// <summary>商人到店补货的<b>最多</b>件数（占位区间 2-5，待策划定档）。</summary>
        public const int MerchantGoodsMax = 5;

        /// <summary>
        /// 农产品判定词表（<b>占位常量</b>，待策划补录）：命中「名称或标签」即视为农产品原材料。
        /// 仅对 Item 表 <c>type=4</c>（材料，见 <see cref="GameConfigCatalog.TypeMaterial"/>）的行生效。
        /// 当前表内实际命中：小麦(4)、大麦(25)、卷心菜(27)、硬木(35)。
        /// </summary>
        public static readonly string[] FarmProductTagWords =
        {
            "农产品", "植物", "谷物", "粮", "麦", "稻", "玉米", "豆", "薯", "菜", "果", "莓", "葡萄", "柑橘", "香草", "草本", "木",
        };

        private readonly IRandomSource _random;
        private readonly IntelSystem _intel;
        private readonly List<string> _warnings = new List<string>();

        /// <summary>角色 Id → 当日已烹饪次数（按人分桶，见 <see cref="ChefDailyLimit"/>）。</summary>
        private readonly Dictionary<int, int> _chefCookedToday = new Dictionary<int, int>();

        /// <summary>角色 Id → 当日已锻造次数（按人分桶，见 <see cref="BlacksmithDailyLimit"/>）。</summary>
        private readonly Dictionary<int, int> _smithForgedToday = new Dictionary<int, int>();

        /// <summary>怪物定义 id → 已传播的刷新日（<b>判重</b>：同 id 同日只通知一次，次日同种怪物可再次通知）。</summary>
        private readonly Dictionary<int, int> _announcedDayByMonsterDef = new Dictionary<int, int>();

        /// <summary>当前可接情报的吟游诗人（由 <see cref="RegisterBards"/> 维护；空则情报进系统池）。</summary>
        private readonly List<CharacterData> _bards = new List<CharacterData>();

        private int _bardCursor;

        public ProfessionService(IRandomSource random, IntelSystem intel)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _intel = intel;
        }

        /// <summary>解析 / 口径回退告警（已去重）。</summary>
        public IReadOnlyList<string> Warnings => _warnings;

        /// <summary>当前经营日（= 情报「到来时间」口径）；未注入 <see cref="IntelSystem"/> 时为 0。</summary>
        public int CurrentDayOverall => _intel != null ? _intel.CurrentDayOverall : 0;

        // ------------------------------------------------------------------
        // 厨师 / 铁匠：每日 1 次
        // ------------------------------------------------------------------

        /// <summary>
        /// 厨师烹饪（《职业系统策划案》「将所需原料加工为指定食物。每天限一次，原料不足时无法执行」）。
        /// 成功：<paramref name="playerBag"/> 扣 <paramref name="materialItemId"/> ×1、<paramref name="productItemId"/> +1，
        /// 该厨师当日次数 +1；失败（次数用尽 / 原料不足 / 参数非法 / 服务者不可用）：<b>不改动任何状态</b>。
        /// </summary>
        public bool ChefCook(CharacterData chef, int materialItemId, int productItemId, Inventory playerBag)
        {
            if (!CanServe(chef) || playerBag == null || materialItemId <= 0 || productItemId <= 0)
            {
                return false;
            }

            if (CountToday(_chefCookedToday, chef.Id) >= ChefDailyLimit)
            {
                return false;
            }

            // 先扣原料再入产物：TryRemove 在数量不足时返回 false 且不改动，这里再兜一层 Count 判定，
            // 保证「原料不足」不会把当日次数消耗掉。
            if (playerBag.Count(materialItemId) < 1 || !playerBag.TryRemove(materialItemId, 1))
            {
                return false;
            }

            playerBag.Add(productItemId, 1);
            _chefCookedToday[chef.Id] = CountToday(_chefCookedToday, chef.Id) + 1;
            return true;
        }

        /// <summary>
        /// 铁匠锻造（《职业系统策划案》「将所需材料锻造成指定武器。每天限一次，材料不足时无法执行」）。
        /// 与 <see cref="ChefCook"/> 同口径，只是每日计数器独立（两人互不影响，各 1 次）。
        /// </summary>
        public bool BlacksmithForge(CharacterData smith, int materialItemId, int productItemId, Inventory bag)
        {
            if (!CanServe(smith) || bag == null || materialItemId <= 0 || productItemId <= 0)
            {
                return false;
            }

            if (CountToday(_smithForgedToday, smith.Id) >= BlacksmithDailyLimit)
            {
                return false;
            }

            if (bag.Count(materialItemId) < 1 || !bag.TryRemove(materialItemId, 1))
            {
                return false;
            }

            bag.Add(productItemId, 1);
            _smithForgedToday[smith.Id] = CountToday(_smithForgedToday, smith.Id) + 1;
            return true;
        }

        /// <summary>该厨师当日剩余烹饪次数（UI 灰显 / 自检用）。</summary>
        public int ChefRemainingToday(CharacterData chef)
        {
            if (chef == null)
            {
                return 0;
            }

            return Math.Max(0, ChefDailyLimit - CountToday(_chefCookedToday, chef.Id));
        }

        /// <summary>该铁匠当日剩余锻造次数（UI 灰显 / 自检用）。</summary>
        public int BlacksmithRemainingToday(CharacterData smith)
        {
            if (smith == null)
            {
                return 0;
            }

            return Math.Max(0, BlacksmithDailyLimit - CountToday(_smithForgedToday, smith.Id));
        }

        // ------------------------------------------------------------------
        // 商人 / 农民：到店货架
        // ------------------------------------------------------------------

        /// <summary>
        /// 商人到店补货（《职业系统策划案》「到达酒馆后补充商品，提供农产品之外的道具交易。交易次数不限」）。
        /// 从 <see cref="GameConfigCatalog.AllItems"/> 里排除农产品（<see cref="IsFarmProduct"/>）后随机取
        /// <see cref="MerchantGoodsMin"/>~<see cref="MerchantGoodsMax"/> 件（不重复），
        /// <b>把这批货放进该商人的 <see cref="CharacterData.Bag"/>（到店补货）</b>并返回清单（供 M8 交易 / M11 行为 AI 读取）。
        /// 目录未就绪或非农产品不足 2 件时返回空列表（不抛异常）；每次调用都是一次新的到店补货，故可重复调用。
        /// </summary>
        public IReadOnlyList<int> GetMerchantGoods(CharacterData npc)
        {
            if (!CanServe(npc))
            {
                return Array.Empty<int>();
            }

            var pool = new List<int>();
            var all = GameConfigCatalog.Instance != null ? GameConfigCatalog.Instance.AllItems : null;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (!IsFarmProduct(all[i].Id))
                    {
                        pool.Add(all[i].Id);
                    }
                }
            }

            if (pool.Count == 0)
            {
                return Array.Empty<int>();
            }

            int want = _random.Next(MerchantGoodsMin, MerchantGoodsMax + 1);
            if (want > pool.Count)
            {
                want = pool.Count;
            }

            var goods = PickDistinct(pool, want, out bool duplicated);
            if (duplicated)
            {
                Warn("商人 " + npc.Name + " 的非农产品不足 " + MerchantGoodsMin + " 件，本次补货少于下限（占位区间 " +
                     MerchantGoodsMin + "-" + MerchantGoodsMax + "）。");
            }

            for (int i = 0; i < goods.Count; i++)
            {
                npc.Bag.Add(goods[i], 1);
            }

            return goods;
        }

        /// <summary>
        /// 农民农产品清单（《职业系统策划案》「到达酒馆后提供农产品原材料，交易次数不限」）。
        /// 口径 = Item 表 <c>type=4</c> 且名称/标签命中 <see cref="FarmProductTagWords"/>（占位标签词表）。
        /// 目录未就绪时返回空列表；不写 <paramref name="npc"/> 的背包（农民供货由调用方按交易结算走 <see cref="CharacterData.Bag"/>）。
        /// </summary>
        public IReadOnlyList<int> GetFarmerGoods(CharacterData npc)
        {
            if (!CanServe(npc))
            {
                return Array.Empty<int>();
            }

            var goods = new List<int>();
            var all = GameConfigCatalog.Instance != null ? GameConfigCatalog.Instance.AllItems : null;
            if (all == null)
            {
                return Array.Empty<int>();
            }

            for (int i = 0; i < all.Count; i++)
            {
                if (IsFarmProduct(all[i].Id))
                {
                    goods.Add(all[i].Id);
                }
            }

            return goods;
        }

        /// <summary>
        /// 是否农产品原材料：Item 表 <c>type=4</c>（材料）且名称或标签命中 <see cref="FarmProductTagWords"/>。
        /// 配置目录未 Init / 行读不到时返回 false（保守：宁可当商品也不当农产品）。
        /// </summary>
        public bool IsFarmProduct(int itemId)
        {
            var catalog = GameConfigCatalog.Instance;
            if (catalog == null || itemId <= 0)
            {
                return false;
            }

            var def = catalog.GetItem(itemId);
            if (def == null)
            {
                return false;
            }

            // 配置行能读到时以行的 type 为准；读不到（表懒加载失败）时用 ItemKind 近似：type=4 材料映射到
            // BaseWine / Ingredient / Ice 三档（见 GameConfigCatalog.MapTypeToKind），这里再排除冰块。
            var row = catalog.GetItemRow(itemId);
            if (row != null)
            {
                if (row.Type != GameConfigCatalog.TypeMaterial)
                {
                    return false;
                }
            }
            else if (def.Kind == ItemKind.Ice)
            {
                return false;
            }

            return ContainsAnyWord(def.Name, def.Tags);
        }

        // ------------------------------------------------------------------
        // 牧师 / 战士
        // ------------------------------------------------------------------

        /// <summary>
        /// 牧师治疗（《职业系统策划案》「城镇NPC受伤时为其恢复血量…次数不限，恢复后的血量不超过生命上限」）。
        /// 成功：<paramref name="patient"/>.Hp = <see cref="CharacterData.EffMaxHp"/>（含装备加成），
        /// <b>不改 <see cref="CharacterData.ActionLockDays"/>（只回血，不解除行动锁定）</b>；
        /// 失败：任一方为 null / 死亡 / 隐藏。<b>次数不限</b>，重复调用始终返回 true。
        /// </summary>
        public bool PriestHeal(CharacterData priest, CharacterData patient)
        {
            if (!CanServe(priest) || patient == null || !patient.IsAlive || patient.IsHidden)
            {
                return false;
            }

            patient.Hp = patient.EffMaxHp;
            return true;
        }

        /// <summary>
        /// 战士接取战斗悬赏的<b>条目</b>资格判断（《职业系统策划案》「接取战斗悬赏后进行战斗」）。
        /// 条目非空、有目标怪物名、且处于「可接取」状态（未占槽；已发布/完成/失败条目不再受理）时返回 true。
        /// 角色的职业 + 存活 + 未锁定 + 未隐藏判定见 <see cref="WarriorAccept(CharacterData, BountyEntry)"/>
        /// （先用 <see cref="TownPopulation.FindByProfession"/> 选出城镇内的战士，再逐条判定）。
        /// </summary>
        public bool WarriorAccept(BountyEntry e)
        {
            if (e == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(e.TargetMonsterName))
            {
                return false;
            }

            return !e.Occupied;
        }

        /// <summary>
        /// 战士接取战斗悬赏的<b>完整</b>资格判断 = 职业战士 + 存活 + 未行动锁定 + 未隐藏 + 条目可接取。
        /// 「次数不限」，本方法不做任何状态写入（接取/结算由 M5 悬赏系统与 M10/M11 战斗闭环负责）。
        /// </summary>
        public bool WarriorAccept(CharacterData warrior, BountyEntry e)
        {
            if (warrior == null || warrior.Profession != ProfessionType.Warrior)
            {
                return false;
            }

            if (!warrior.IsAlive || warrior.IsHidden || warrior.ActionLockDays > 0)
            {
                return false;
            }

            return WarriorAccept(e);
        }

        // ------------------------------------------------------------------
        // 吟游诗人：情报
        // ------------------------------------------------------------------

        /// <summary>
        /// 登记当前在城镇内可接收情报的吟游诗人（组合根在开局 / 每日补员后调用，
        /// 数据源 <see cref="TownPopulation.FindByProfession"/>；死亡或隐藏者自动剔除）。
        /// 轮流游标重置，<see cref="BardAnnounceMonster"/> 从这里轮流取人。
        /// </summary>
        public void RegisterBards(IReadOnlyList<CharacterData> bards)
        {
            _bards.Clear();
            _bardCursor = 0;
            if (bards == null)
            {
                return;
            }

            for (int i = 0; i < bards.Count; i++)
            {
                var bard = bards[i];
                if (bard != null && bard.Profession == ProfessionType.Bard && bard.IsAlive && !bard.IsHidden)
                {
                    _bards.Add(bard);
                }
            }
        }

        /// <summary>当前登记的吟游诗人数。</summary>
        public int BardCount => _bards.Count;

        /// <summary>
        /// 吟游诗人持有的待售情报清单（<see cref="IntelSystem.IntelOfNpc"/> 透传）。
        /// 未登记过该吟游诗人或未注入情报系统时返回空列表（不分配、可安全遍历）。
        /// </summary>
        public IReadOnlyList<IntelRecord> GetBardIntel(CharacterData bard)
        {
            if (_intel == null || bard == null)
            {
                return Array.Empty<IntelRecord>();
            }

            return _intel.IntelOfNpc(bard.Id);
        }

        /// <summary>
        /// 吟游诗人传播怪物情报（《职业系统策划案》「到达酒馆后，传播怪物名称、地点和到来时间…同一次怪物到来只通知一次」）。
        ///
        /// 传播内容落在 <see cref="IntelRecord"/> 三件套上：
        /// <b>名称</b> = <see cref="IntelRecord.MonsterName"/>、<b>地点</b> = <see cref="IntelRecord.MapId"/>
        /// （怪物刷新那一刻的地图，见 <see cref="IntelRecord"/>）、<b>到来时间</b> = <see cref="IntelRecord.DayOverall"/>（= 刷新日）。
        ///
        /// <b>判重</b>：按「怪物定义 id + 刷新日」记账（<see cref="HasAnnounced"/>）。同一只怪物重复调用不会重复产出情报条目；
        /// 次日同种怪物再次到来是新的一次到来，可再次传播。
        /// 产出落到轮流选中的吟游诗人账上（<see cref="IntelSystem.GrantToNpc"/>），城镇里没有吟游诗人时进系统池
        /// （<see cref="IntelSystem.ParkIntel"/>，吟游诗人日后到店可从那里取货）。
        /// </summary>
        public void BardAnnounceMonster(Monster m)
        {
            if (m == null || m.Def == null || !m.IsAlive)
            {
                return;
            }

            int day = CurrentDayOverall;
            if (_announcedDayByMonsterDef.TryGetValue(m.Def.Id, out int announcedDay) && announcedDay == day)
            {
                return; // 同一次怪物到来只通知一次。
            }

            _announcedDayByMonsterDef[m.Def.Id] = day;

            if (_intel == null)
            {
                return;
            }

            var records = _intel.CreateAllForMonster(m);
            var bard = NextBard();
            for (int i = 0; i < records.Count; i++)
            {
                if (bard != null)
                {
                    _intel.GrantToNpc(bard.Id, records[i]);
                }
                else
                {
                    _intel.ParkIntel(records[i]);
                }
            }
        }

        /// <summary>这只怪物本经营日是否已经传播过（UI「已播报」标记 / 自检用）。</summary>
        public bool HasAnnounced(Monster m)
        {
            if (m == null || m.Def == null)
            {
                return false;
            }

            return _announcedDayByMonsterDef.TryGetValue(m.Def.Id, out int announcedDay) && announcedDay == CurrentDayOverall;
        }

        /// <summary>
        /// 吟游诗人播报文案（怪物名 / 地点 / 到来时间；地点用地图 id + 坐标，地图名文案待地图表落地）。
        /// 与 <see cref="BardAnnounceMonster"/> 的产出同源，便于 M12 HUD / M11 行为 AI 直接显示。
        /// </summary>
        public string BuildAnnouncementText(Monster m)
        {
            if (m == null || m.Def == null)
            {
                return string.Empty;
            }

            return "据说 " + m.Def.Name + " 出现在了地图" + m.Position.MapId + "(" + m.Position.X + "," + m.Position.Y + ")，将于第 "
                   + CurrentDayOverall + " 天到来。";
        }

        // ------------------------------------------------------------------
        // 0:00
        // ------------------------------------------------------------------

        /// <summary>
        /// 0:00（<see cref="WorldSimulation.DaySettled"/>）重置每日次数：清空厨师 / 铁匠的当日计数器。
        /// 「吟游诗人同日只播报一次」按日推进，<b>不在这里清</b>——不同日的同种怪物是新的一次到来，仍可传播。
        /// </summary>
        public void OnDaySettled()
        {
            _chefCookedToday.Clear();
            _smithForgedToday.Clear();
        }

        // ------------------------------------------------------------------
        // 内部工具
        // ------------------------------------------------------------------

        /// <summary>服务者可用性：非 null、存活、未隐藏（不校验职业——「谁来服务」由 <see cref="TownPopulation.FindByProfession"/> 决定）。</summary>
        private static bool CanServe(CharacterData npc)
        {
            return npc != null && npc.IsAlive && !npc.IsHidden;
        }

        private static int CountToday(Dictionary<int, int> counters, int characterId)
        {
            return counters.TryGetValue(characterId, out int used) ? used : 0;
        }

        /// <summary>从 <paramref name="pool"/> 不放回地随机抽 <paramref name="count"/> 个；<paramref name="duplicated"/> = 是否因池子偏小而少抽。</summary>
        private List<int> PickDistinct(List<int> pool, int count, out bool duplicated)
        {
            var picked = new List<int>(count);
            var remaining = new List<int>(pool);
            int take = Math.Min(count, remaining.Count);
            duplicated = take < count;

            for (int i = 0; i < take; i++)
            {
                int index = _random.Next(0, remaining.Count);
                picked.Add(remaining[index]);
                remaining.RemoveAt(index);
            }

            return picked;
        }

        private CharacterData NextBard()
        {
            if (_bards.Count == 0)
            {
                return null;
            }

            var bard = _bards[_bardCursor % _bards.Count];
            _bardCursor = (_bardCursor + 1) % _bards.Count;
            return bard;
        }

        private bool ContainsAnyWord(string name, string[] tags)
        {
            for (int i = 0; i < FarmProductTagWords.Length; i++)
            {
                if (Contains(name, FarmProductTagWords[i]))
                {
                    return true;
                }

                if (tags != null)
                {
                    for (int t = 0; t < tags.Length; t++)
                    {
                        if (Contains(tags[t], FarmProductTagWords[i]))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool Contains(string text, string word)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(word, StringComparison.Ordinal) >= 0;
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
