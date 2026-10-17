using System;
using System.Collections.Generic;

namespace GameLogic.World
{
    /// <summary>
    /// 记忆类型（《英雄酒馆角色行为系统策划案》「记忆」）。
    ///
    /// 长期记忆（生成即存在、永久有效）不走本枚举，走 <see cref="MemoryPool.TownPos"/> /
    /// <see cref="MemoryPool.TavernPos"/> / <see cref="MemoryPool.TownSafety"/> 三个直接字段。
    /// 本枚举全部对应<b>短期记忆</b>（广播写入、当日有效，0:00 由 <see cref="MemoryPool.ClearDaily"/> 清空）：
    /// - <see cref="DailySpecial"/>   每日特价：酒馆每日特价商品信息更新时广播（策划案「短期记忆池」）；
    /// - <see cref="BountyAvailable"/> 悬赏栏：存在可接悬赏时持续向酒馆内角色广播；
    /// - <see cref="MonsterInTown"/>   怪物：怪物处于城镇中时持续向城镇内角色广播；
    /// - <see cref="ShopGoods"/>       商品：与酒保交互获得酒馆商品信息（策划案「信息分」）；
    /// - <see cref="SafetyChanged"/>   城镇安全值变化的通知（长期记忆的安全值仍以 <see cref="MemoryPool.TownSafety"/> 为准）。
    /// </summary>
    public enum MemoryKind
    {
        /// <summary>每日特价商品（RefId = 商品 id）。</summary>
        DailySpecial = 1,

        /// <summary>可接悬赏（RefId = <see cref="BountyEntry.Id"/>）。</summary>
        BountyAvailable = 2,

        /// <summary>在城怪物（RefId = <see cref="MonsterDef.Id"/>）。</summary>
        MonsterInTown = 3,

        /// <summary>酒馆商品（RefId = 商品 id）。</summary>
        ShopGoods = 4,

        /// <summary>城镇安全值变化（RefId = 变化后的安全值）。</summary>
        SafetyChanged = 5,
    }

    /// <summary>
    /// 一条短期记忆（纯数据值对象，无 UnityEngine 依赖）。
    ///
    /// 口径（策划案「短期记忆池」）：
    /// - 记忆来自外部广播（每日特价 / 悬赏栏 / 在城怪物 / 酒保商品）；
    /// - <b>当日有效</b>：写入时记下当日经营日号 <see cref="DayOverall"/>，跨日由 <see cref="MemoryPool.ClearDaily"/> 整池清空，
    ///   避免「昨天的悬赏 / 昨天的特价」继续参与当天的权重计算；
    /// - 记忆池内以 <see cref="Kind"/> + <see cref="RefId"/> 去重：同一条目标只保留一条，重复广播只更新内容
    ///   （见 <see cref="MemoryPool.Broadcast(MemoryItem)"/>）。
    /// </summary>
    public sealed class MemoryItem
    {
        /// <summary>记忆类型。</summary>
        public MemoryKind Kind;

        /// <summary>记忆指向的目标 id：悬赏编号 / 怪物定义 id / 商品 id（语义随 <see cref="Kind"/>）。</summary>
        public int RefId;

        /// <summary>一句话摘要（供 HUD / 日志展示与信息分说明；可为空串）。</summary>
        public string Summary;

        /// <summary>写入当日的经营日号（与 <c>GameTime.DayOverall</c> 同口径，1 起）；用于排查与跨日清理。</summary>
        public int DayOverall;

        public MemoryItem()
        {
            Summary = string.Empty;
        }

        public MemoryItem(MemoryKind kind, int refId, string summary, int dayOverall)
        {
            Kind = kind;
            RefId = refId;
            Summary = summary ?? string.Empty;
            DayOverall = dayOverall;
        }

        /// <summary>同一条目标的判定（类型 + 目标 id 相同即视为同一条记忆）。</summary>
        public bool SameTarget(MemoryKind kind, int refId)
        {
            return Kind == kind && RefId == refId;
        }

        public override string ToString()
        {
            return Kind + "#" + RefId + " (" + Summary + ") @" + DayOverall;
        }
    }

    /// <summary>
    /// 记忆池（纯数据 + 小逻辑，无 UnityEngine 依赖）。出处《英雄酒馆角色行为系统策划案》「记忆 / 权重计算」。
    ///
    /// 长期记忆（角色生成时即存在、永久有效）：
    /// - <see cref="TownPos"/>   城镇坐标、<see cref="TavernPos"/> 酒馆坐标：默认取 <see cref="WorldMap"/> 常量，
    ///   可由 <see cref="LoadFromConfig()"/> 用 map 表（<c>ht.TbMap</c>「建筑坐标」）覆盖；
    /// - <see cref="TownSafety"/> 城镇当前安全值：初值 <see cref="World.TownSafety.InitialSafety"/>（=100），
    ///   接 <see cref="AttachSafety"/> 后由 <see cref="World.TownSafety.Changed"/> 广播更新。
    ///
    /// 短期记忆（广播写入、当日有效）：
    /// - 容器 <see cref="ShortTerm"/>，写入入口 <see cref="Broadcast(MemoryItem)"/>（同 Kind+RefId 去重更新）；
    /// - 0:00 日结调 <see cref="ClearDaily"/> 整池清空；
    /// - 查询：<see cref="OfKind"/>（按类型取全部）、<see cref="Has(MemoryKind, int)"/> 与
    ///   <see cref="HasBounty"/> / <see cref="HasMonsterInTown"/> / <see cref="HasDailySpecial"/> / <see cref="HasShopGoods"/> 便捷判定。
    ///
    /// 典型接线（行为系统侧）：
    /// <list type="bullet">
    /// <item>生成角色时 <c>new MemoryPool()</c>（长期记忆即生即有），或 <c>new MemoryPool(pool)</c> 克隆既有池的长期记忆；</item>
    /// <item><c>pool.AttachSafety(sim.Safety)</c> 同步安全值；</item>
    /// <item>广播源（悬赏发布 / 怪物进城 / 特价刷新）调 <see cref="Broadcast(MemoryItem)"/> 或 <see cref="Broadcast(MemoryKind, int, string, int)"/>。</item>
    /// </list>
    /// </summary>
    public sealed class MemoryPool
    {
        /// <summary>城镇坐标（长期记忆；map 表未配置时为 <see cref="WorldMap.TownCenter"/>）。</summary>
        public GridPos TownPos = WorldMap.TownCenter;

        /// <summary>酒馆坐标（长期记忆；map 表未配置时为 <see cref="WorldMap.TavernPos"/>）。</summary>
        public GridPos TavernPos = WorldMap.TavernPos;

        /// <summary>城镇当前安全值（长期记忆；0-100，由 <see cref="World.TownSafety.Changed"/> 广播更新）。</summary>
        public int TownSafety = World.TownSafety.InitialSafety;

        /// <summary>当日经营日号（1 起，与 <c>GameTime.DayOverall</c> 同口径）：广播不显式传日号时用它给记忆打戳。</summary>
        public int CurrentDayOverall = 1;

        private readonly List<MemoryItem> _shortTerm = new List<MemoryItem>();

        /// <summary>按类型分桶的视图缓存（与 <see cref="ShortTerm"/> 同步维护，<see cref="OfKind"/> 直接返回，避免每次调用都分配）。</summary>
        private readonly Dictionary<MemoryKind, List<MemoryItem>> _ofKind = new Dictionary<MemoryKind, List<MemoryItem>>();

        private TownSafety _safety;

        /// <summary>map 表加载告警（供 HUD / 日志排查；配置缺失时只是用常量兜底，不影响逻辑层运行）。</summary>
        private readonly List<string> _loadWarnings = new List<string>();

        public MemoryPool()
        {
        }

        /// <summary>克隆既有池的长期记忆（新角色一份池时用；只想共享同一份池实例时直接传同一个对象即可）。</summary>
        public MemoryPool(MemoryPool source)
        {
            if (source == null)
            {
                return;
            }

            TownPos = source.TownPos;
            TavernPos = source.TavernPos;
            TownSafety = source.TownSafety;
            CurrentDayOverall = source.CurrentDayOverall;
        }

        /// <summary>以指定长期记忆构造（地图表已就绪时由行为系统直接传入）。</summary>
        public MemoryPool(GridPos townPos, GridPos tavernPos, int townSafety)
        {
            TownPos = townPos;
            TavernPos = tavernPos;
            TownSafety = townSafety;
        }

        // ------------------------------------------------------------------
        // 只读视图
        // ------------------------------------------------------------------

        /// <summary>短期记忆池（当日有效；按只读口径使用，新增请走 <see cref="Broadcast(MemoryItem)"/>）。</summary>
        public List<MemoryItem> ShortTerm => _shortTerm;

        /// <summary>短期记忆条数。</summary>
        public int ShortTermCount => _shortTerm.Count;

        /// <summary>map 表加载告警。</summary>
        public IReadOnlyList<string> LoadWarnings => _loadWarnings;

        // ------------------------------------------------------------------
        // 长期记忆
        // ------------------------------------------------------------------

        /// <summary>
        /// 订阅 <see cref="World.TownSafety.Changed"/>：城镇安全值变化时把新值同步到长期记忆。
        /// 传入 null 时只解除旧订阅不新建；重复订阅同一实例不会重复回调。订阅时立刻用当前 <c>safety.Value</c> 同步一次。
        /// </summary>
        public void AttachSafety(TownSafety safety)
        {
            if (ReferenceEquals(_safety, safety))
            {
                if (safety != null)
                {
                    UpdateTownSafety(safety.Value);
                }

                return;
            }

            DetachSafety();

            if (safety == null)
            {
                return;
            }

            _safety = safety;
            _safety.Changed += OnSafetyChanged;
            UpdateTownSafety(_safety.Value);
        }

        /// <summary>解除安全值订阅（角色销毁 / 世界重开时调用）。</summary>
        public void DetachSafety()
        {
            if (_safety == null)
            {
                return;
            }

            _safety.Changed -= OnSafetyChanged;
            _safety = null;
        }

        /// <summary>
        /// 手动更新城镇安全值（长期记忆；正常路径是 <see cref="AttachSafety"/> 订阅 <c>Changed</c>）。
        /// 值钳制到 [<see cref="World.TownSafety.MinSafety"/>, <see cref="World.TownSafety.MaxSafety"/>]。
        /// 同时向短期记忆写一条 <see cref="MemoryKind.SafetyChanged"/> 通知，行为系统可据此重抽行动（动态决策）。
        /// </summary>
        public void UpdateTownSafety(int value)
        {
            if (value < World.TownSafety.MinSafety)
            {
                value = World.TownSafety.MinSafety;
            }
            else if (value > World.TownSafety.MaxSafety)
            {
                value = World.TownSafety.MaxSafety;
            }

            if (TownSafety == value)
            {
                return;
            }

            TownSafety = value;
            Broadcast(MemoryKind.SafetyChanged, value, "城镇安全值 -> " + value);
        }

        private void OnSafetyChanged(int newValue)
        {
            if (TownSafety == newValue)
            {
                return;
            }

            TownSafety = newValue;
            Broadcast(MemoryKind.SafetyChanged, newValue, "城镇安全值 -> " + newValue);
        }

        // ------------------------------------------------------------------
        // 短期记忆
        // ------------------------------------------------------------------

        /// <summary>
        /// 写入 / 更新一条短期记忆（策划案「短期记忆池」的广播）。
        /// <b>去重口径</b>：同 <see cref="MemoryItem.Kind"/> + <see cref="MemoryItem.RefId"/> 已存在时，
        /// 只就地更新 <see cref="MemoryItem.Summary"/> 与 <see cref="MemoryItem.DayOverall"/>，不新增条目、不改变顺序
        /// （「持续广播」是更新而不是堆积）。item 为 null 时忽略。
        /// </summary>
        public void Broadcast(MemoryItem item)
        {
            if (item == null)
            {
                return;
            }

            for (int i = 0; i < _shortTerm.Count; i++)
            {
                var exist = _shortTerm[i];
                if (!exist.SameTarget(item.Kind, item.RefId))
                {
                    continue;
                }

                exist.Summary = item.Summary ?? string.Empty;
                exist.DayOverall = item.DayOverall;
                return;
            }

            _shortTerm.Add(item);
            Bucket(item.Kind).Add(item);
        }

        /// <summary>广播简写；日号显式指定。</summary>
        public void Broadcast(MemoryKind kind, int refId, string summary, int dayOverall)
        {
            Broadcast(new MemoryItem(kind, refId, summary, dayOverall));
        }

        /// <summary>广播简写；不显式传日号时用 <see cref="CurrentDayOverall"/> 打戳（行为系统最常用的一条）。</summary>
        public void Broadcast(MemoryKind kind, int refId, string summary)
        {
            Broadcast(new MemoryItem(kind, refId, summary, CurrentDayOverall));
        }

        /// <summary>遗忘一条短期记忆（悬赏被接走 / 怪物离开城镇 / 特价下架时调用）。不存在时静默返回。</summary>
        public bool Forget(MemoryKind kind, int refId)
        {
            for (int i = 0; i < _shortTerm.Count; i++)
            {
                var exist = _shortTerm[i];
                if (!exist.SameTarget(kind, refId))
                {
                    continue;
                }

                _shortTerm.RemoveAt(i);
                Bucket(kind).Remove(exist);
                return true;
            }

            return false;
        }

        /// <summary>0:00 日结：清空全部短期记忆（短期记忆<b>当日有效</b>，跨日不保留）。</summary>
        public void ClearDaily()
        {
            _shortTerm.Clear();
            _ofKind.Clear();
        }

        /// <summary>0:00 日结：清空短期记忆并把当日日号推进到 <paramref name="nextDayOverall"/>（传 0 表示只清空不推进）。</summary>
        public void ClearDaily(int nextDayOverall)
        {
            ClearDaily();
            if (nextDayOverall > 0)
            {
                CurrentDayOverall = nextDayOverall;
            }
        }

        /// <summary>取某个类型的短期记忆（只读列表，随广播/遗忘/日结同步）。无该类型时返回空列表（不返回 null）。</summary>
        public IReadOnlyList<MemoryItem> OfKind(MemoryKind kind)
        {
            return Bucket(kind);
        }

        /// <summary>某个类型的记忆条数。</summary>
        public int CountOf(MemoryKind kind)
        {
            return Bucket(kind).Count;
        }

        /// <summary>某个类型的全部目标 id（供行为系统穷举可执行交互：悬赏 id / 怪物 id / 商品 id）。</summary>
        public List<int> RefIdsOf(MemoryKind kind)
        {
            var bucket = Bucket(kind);
            var ids = new List<int>(bucket.Count);
            for (int i = 0; i < bucket.Count; i++)
            {
                ids.Add(bucket[i].RefId);
            }

            return ids;
        }

        /// <summary>是否持有某条短期记忆。</summary>
        public bool Has(MemoryKind kind, int refId)
        {
            var bucket = Bucket(kind);
            for (int i = 0; i < bucket.Count; i++)
            {
                if (bucket[i].RefId == refId)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------
        // 便捷查询
        // ------------------------------------------------------------------

        /// <summary>是否记得可接悬赏 <paramref name="bountyId"/>（生成「接悬赏」交互链的前置）。</summary>
        public bool HasBounty(int bountyId) => Has(MemoryKind.BountyAvailable, bountyId);

        /// <summary>是否记得在城怪物 <paramref name="monsterDefId"/>（生成「狩猎」交互链的前置）。</summary>
        public bool HasMonsterInTown(int monsterDefId) => Has(MemoryKind.MonsterInTown, monsterDefId);

        /// <summary>是否记得每日特价商品 <paramref name="itemId"/>。</summary>
        public bool HasDailySpecial(int itemId) => Has(MemoryKind.DailySpecial, itemId);

        /// <summary>是否记得酒馆商品 <paramref name="itemId"/>。</summary>
        public bool HasShopGoods(int itemId) => Has(MemoryKind.ShopGoods, itemId);

        /// <summary>当日是否收到过城镇安全值变化通知。</summary>
        public bool HasSafetyChange => Has(MemoryKind.SafetyChanged, TownSafety);

        /// <summary>当前可接悬赏编号（按记忆写入顺序）。</summary>
        public List<int> AvailableBountyIds => RefIdsOf(MemoryKind.BountyAvailable);

        /// <summary>当前在城怪物定义 id（按记忆写入顺序）。</summary>
        public List<int> MonsterDefIdsInTown => RefIdsOf(MemoryKind.MonsterInTown);

        /// <summary>摘要：便于日志 / HUD 打印整个池的状态。</summary>
        public string BuildSnapshot()
        {
            return "长期 城镇" + TownPos + " 酒馆" + TavernPos + " 安全" + TownSafety
                   + " | 短期 " + _shortTerm.Count + " 条: " + string.Join("；", SnapshotItems());
        }

        private List<string> SnapshotItems()
        {
            var texts = new List<string>(_shortTerm.Count);
            for (int i = 0; i < _shortTerm.Count; i++)
            {
                texts.Add(_shortTerm[i].ToString());
            }

            return texts;
        }

        private List<MemoryItem> Bucket(MemoryKind kind)
        {
            if (!_ofKind.TryGetValue(kind, out var bucket))
            {
                bucket = new List<MemoryItem>();
                _ofKind[kind] = bucket;
            }

            return bucket;
        }

        // ------------------------------------------------------------------
        // map 表加载（长期记忆的城镇 / 酒馆坐标）
        // ------------------------------------------------------------------

        /// <summary>
        /// 从 map 表（<c>ht.TbMap</c>「建筑坐标」）读城镇 / 酒馆坐标覆盖长期记忆；
        /// 表未初始化、读不到或没有匹配行时保留 <see cref="WorldMap"/> 常量占位值，并记入 <see cref="LoadWarnings"/>。
        /// 只覆盖坐标，不动 <see cref="TownSafety"/>。
        /// </summary>
        public void LoadFromConfig()
        {
            var catalog = GameConfigCatalog.Instance;
            LoadFromConfig(catalog != null ? catalog.Tables : null);
        }

        /// <summary>从指定配置表读坐标（显式传入优先于 <see cref="LoadFromConfig()"/> 的自动解析）。</summary>
        public void LoadFromConfig(GameConfig.Tables tables)
        {
            if (tables == null)
            {
                _loadWarnings.Add("TbMap 未就绪，城镇/酒馆坐标保留 WorldMap 常量占位值。");
                return;
            }

            try
            {
                var rows = tables.TbMap.DataList;
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row == null || string.IsNullOrEmpty(row.Name))
                    {
                        continue;
                    }

                    var pos = new GridPos(WorldMapIds.Town, row.X, row.Y);
                    if (NameMatches(row.Name, "tavern", "酒馆"))
                    {
                        TavernPos = pos;
                    }
                    else if (NameMatches(row.Name, "town", "town_center", "城镇"))
                    {
                        TownPos = pos;
                    }
                }
            }
            catch (Exception e)
            {
                _loadWarnings.Add("读取 TbMap 失败，城镇/酒馆坐标保留常量占位值：" + e.Message);
            }
        }

        private static bool NameMatches(string name, params string[] keywords)
        {
            for (int i = 0; i < keywords.Length; i++)
            {
                if (name.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
