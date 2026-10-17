using System;
using System.Collections.Generic;

using ConfigItem = GameConfig.ht.Item;
using ConfigMonsterRow = GameConfig.ht.MonsterRow;
using ConfigProfessionRow = GameConfig.ht.ProfessionRow;

namespace GameLogic.World
{
    /// <summary>
    /// 职业行轻量视图：<see cref="GameConfigCatalog.GetProfession"/> 直接返回生成类型时的替代出口，
    /// 供不想在逻辑层引用 <c>GameConfig.ht</c> 的调用方使用（结构字段与 ht.ProfessionRow 一致：id/name/weight）。
    /// </summary>
    public readonly struct ProfessionInfo
    {
        public readonly int Id;
        public readonly string Name;
        public readonly int Weight;

        public ProfessionInfo(int id, string name, int weight)
        {
            Id = id;
            Name = name;
            Weight = weight;
        }

        /// <summary>空值（职业 id=0 / NONE）。</summary>
        public static readonly ProfessionInfo None = new ProfessionInfo(0, string.Empty, 0);
    }

    /// <summary>
    /// 运行时配置目录（M2）：把 <see cref="GameConfig.Tables"/> 的生成行翻译成游戏语义查询
    /// （物品 / 调酒配方 / 怪物模板 / 姓名词表 / 全局参数 / 职业）。
    ///
    /// 设计约束：
    /// - 纯逻辑，不依赖 UnityEngine；生成表访问全部走 <c>GameConfig.*</c> 命名空间。
    /// - 未 <see cref="Init"/>、或表懒加载失败时：所有查询回退 null / 空数组，调用方继续使用
    ///   <see cref="ItemCatalog"/> / <see cref="MonsterSystem"/> 的代码占位常量。
    /// - 懒构建：<see cref="Init"/> 不触发任何表的字节流加载，首次查询时才构建索引；
    ///   构建异常被吞进 <see cref="ParseWarnings"/>，不会打断模拟主循环。
    ///
    /// ===== 与 master-plan M2 规格的冲突（以生成代码 / 实际表数据为准）=====
    /// 1) Item「类别(type)」实际取值来自 Datas/ItemGame.xlsx 的说明页：0=货币 1=武器 2=防具 3=饰品
    ///    4=材料 5=消耗品 6=酒水。生成的枚举 <c>GameConfig.ht.ItemType</c> 只到 CONSUMABLE=5，**没有 6=酒水**；
    ///    表内 type 列声明为 int（未引用枚举），所以成品酒（id 51-56）用的是枚举外的 6。
    ///    因此本类用 <see cref="TypeDrink"/> 常量表达 6，映射规则见 <see cref="MapTypeToKind"/>。
    /// 2) 计划写「配方 type=5」，但当前数据 type=5 是消耗品（面包/奶酪/情报/钱袋…），**不存在配方行**。
    ///    <see cref="RecipeRowType"/> 仍保留 5 作为显式配方行类型；显式配方行一个都没有时，
    ///    回退到「成品酒标签反推」合成（见 <see cref="SynthesizeRecipesFromWines"/>），保证调酒系统有配方可用。
    /// 3) 计划验收写「GetItem(1) 返回伏特加类条目」，但实际数据 id=1 是「铁剑」（type=1 武器），
    ///    伏特加是 id=40（type=4 材料，tag=伏特加）。取数请按 id=40。
    /// 4) Item 表没有「制作时长」列，<see cref="ItemDef.MakeMinutes"/> 只能从描述文本里的「N分钟/N小时」推断，
    ///    取不到为 0；0 表示配置未提供，由调用方回退占位常量。
    /// 5) TbGlobal 只有 num/id/description/value 四列，**没有 key 列**，且源表 description 列为空，
    ///    所以 <see cref="GetGlobalInt"/> 只能按数字 id 或 description 命中，中文键名需策划补录后才能用。
    /// </summary>
    public sealed class GameConfigCatalog
    {
        // ------------------------------------------------------------------
        // Item「类别」列取值（以 ItemGame.xlsx 实际数据为准，见类注释冲突 1）
        // ------------------------------------------------------------------

        /// <summary>0=货币。</summary>
        public const int TypeCurrency = 0;
        /// <summary>1=武器。</summary>
        public const int TypeWeapon = 1;
        /// <summary>2=防具。</summary>
        public const int TypeArmor = 2;
        /// <summary>3=饰品。</summary>
        public const int TypeAccessory = 3;
        /// <summary>4=材料（基酒 / 配料 / 冰块都在这一类）。</summary>
        public const int TypeMaterial = 4;
        /// <summary>5=消耗品；计划口径里这一类同时承载配方行。</summary>
        public const int TypeConsumable = 5;
        /// <summary>6=酒水（成品酒）。生成的 ht.ItemType 枚举没有这一项，实际数据在用。</summary>
        public const int TypeDrink = 6;

        /// <summary>显式配方行类别（计划口径：配方 type=5）。</summary>
        public const int RecipeRowType = TypeConsumable;

        /// <summary>配方行判定用的标记词（描述/标签/名称里出现即视为配方行）。</summary>
        public const string RecipeMarker = "配方";

        /// <summary>基酒标签词表（六基酒；当前表只用到 金酒/伏特加/朗姆，其余为占位）。</summary>
        public static readonly string[] BaseWineTagWords = { "金酒", "伏特加", "朗姆", "龙舌兰", "威士忌", "白兰地", "清酒", "苦艾酒" };

        /// <summary>冰块/加冰判定词。</summary>
        public const string IceMarker = "冰";

        private static readonly string[] EmptyTags = Array.Empty<string>();

        private static readonly char[] TagSeparatorChars = { '、', ',', '，', ';', '；', '|', '/', '+', ' ', '\t', '\n', '\r' };

        // ------------------------------------------------------------------
        // 状态与缓存
        // ------------------------------------------------------------------

        private readonly GameConfig.Tables _tables;
        private readonly List<string> _warnings = new List<string>();

        private readonly Dictionary<int, ItemDef> _itemById = new Dictionary<int, ItemDef>();
        private List<ItemDef> _allItems = new List<ItemDef>();
        private readonly Dictionary<string, int> _nameToId = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _baseWineTagToId = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _ingredientTagToId = new Dictionary<string, int>();

        private readonly List<RecipeDef> _recipes = new List<RecipeDef>();
        private RecipeDef[] _recipesArray = Array.Empty<RecipeDef>();

        private readonly List<MonsterDef> _monsters = new List<MonsterDef>();
        private MonsterDef[] _monstersArray = Array.Empty<MonsterDef>();
        private readonly Dictionary<int, ConfigMonsterRow> _monsterRows = new Dictionary<int, ConfigMonsterRow>();

        private string[] _surnames = Array.Empty<string>();
        private string[] _givenNames = Array.Empty<string>();

        private bool _built;

        private GameConfigCatalog(GameConfig.Tables tables)
        {
            _tables = tables;
        }

        /// <summary>全局目录实例；未 <see cref="Init"/> 时为 null。</summary>
        public static GameConfigCatalog Instance { get; private set; }

        /// <summary>是否已 Init（不代表表字节流一定加载成功，加载失败看 <see cref="ParseWarnings"/>）。</summary>
        public static bool IsInitialized
        {
            get { return Instance != null; }
        }

        /// <summary>
        /// 绑定配置表并成为 <see cref="Instance"/>。可重复调用（重建索引，丢弃旧缓存）。
        /// 不触发任何表的加载；表数据在首次查询时才惰性读取。
        /// </summary>
        public static void Init(GameConfig.Tables tables)
        {
            Instance = new GameConfigCatalog(tables);
        }

        /// <summary>清空目录（测试 / 重载资源用）。</summary>
        public static void Reset()
        {
            Instance = null;
        }

        /// <summary>
        /// 底层 <see cref="GameConfig.Tables"/> 的只读出口（M5 悬赏板读取入口：
        /// <c>ht.TbBounty</c>（悬赏行）与 <c>ht.TbBountyConst</c>（槽位数）由调用方自行解析，
        /// 本类不做悬赏相关的字段翻译。未 <see cref="Init"/> 时为 null；表本身仍是懒加载，访问属性时才读字节流。
        /// </summary>
        public GameConfig.Tables Tables => _tables;

        // ------------------------------------------------------------------
        // 查询
        // ------------------------------------------------------------------

        /// <summary>解析/构建告警文案（配方推导、标签解析、缺列等）。未构建时返回空列表。</summary>
        public List<string> ParseWarnings
        {
            get
            {
                EnsureBuilt();
                return _warnings;
            }
        }

        /// <summary>是否有解析告警。</summary>
        public bool HasWarnings
        {
            get
            {
                EnsureBuilt();
                return _warnings.Count > 0;
            }
        }

        /// <summary>按 id 取物品定义；未 Init / 表缺失 / 无此行时返回 null（调用方回退占位常量）。</summary>
        public ItemDef GetItem(int id)
        {
            EnsureBuilt();

            ItemDef def;
            return _itemById.TryGetValue(id, out def) ? def : null;
        }

        /// <summary>按 id 取原始配置行（需要 icon/quality/use/description 等 ItemDef 未承载的字段时使用）。</summary>
        public ConfigItem GetItemRow(int id)
        {
            EnsureBuilt();

            if (_tables == null)
            {
                return null;
            }

            try
            {
                return _tables.TbItemGame.GetOrDefault(id);
            }
            catch (Exception e)
            {
                Warn("读取 TbItemGame 行 " + id + " 失败：" + e.Message);
                return null;
            }
        }

        /// <summary>全部非配方行物品（按表行序）。酒单/仓库总量口径：含基酒、配料、冰块、成品酒、装备、消耗品、货币。</summary>
        public IReadOnlyList<ItemDef> AllItems
        {
            get
            {
                EnsureBuilt();
                return _allItems;
            }
        }

        /// <summary>按类别筛物品（酒单取 <see cref="ItemKind.Wine"/>）。</summary>
        public IReadOnlyList<ItemDef> GetItemsByKind(ItemKind kind)
        {
            EnsureBuilt();

            var list = new List<ItemDef>();
            for (int i = 0; i < _allItems.Count; i++)
            {
                if (_allItems[i].Kind == kind)
                {
                    list.Add(_allItems[i]);
                }
            }

            return list;
        }

        /// <summary>调酒配方数组（长度 0 表示配置未提供，调用方回退 <see cref="ItemCatalog.Recipes"/>）。</summary>
        public RecipeDef[] GetRecipes()
        {
            EnsureBuilt();
            return _recipesArray;
        }

        /// <summary>怪物模板目录（表行序）。权重为配置原值（千分制，见类注释）。</summary>
        public MonsterDef[] GetMonsterCatalog()
        {
            EnsureBuilt();
            return _monstersArray;
        }

        /// <summary>按 id 取怪物原始行（MonsterDef 承载不到的 Location / Description 在这里）。</summary>
        public ConfigMonsterRow GetMonsterRow(int id)
        {
            EnsureBuilt();

            ConfigMonsterRow row;
            return _monsterRows.TryGetValue(id, out row) ? row : null;
        }

        /// <summary>怪物出现场地号（配置缺失返回 0）。</summary>
        public int GetMonsterLocation(int id)
        {
            ConfigMonsterRow row = GetMonsterRow(id);
            return row == null ? 0 : row.Location;
        }

        /// <summary>姓名词表（姓 / 名）。未配置时返回两个空数组，调用方回退占位词表。</summary>
        public (string[] surnames, string[] given) GetNamePools()
        {
            EnsureBuilt();
            return (_surnames, _givenNames);
        }

        /// <summary>
        /// 全局参数：先按数字 id 命中 TbGlobal，其次按 description 包含 key 命中，均不中返回 fallback。
        /// 遗留：源表 description 列为空且无 key 列，中文键名（如「初始金币」）当前必然走 fallback，
        /// 需策划补录后本方法无需改动即可生效。
        /// </summary>
        public int GetGlobalInt(string key, int fallback)
        {
            EnsureBuilt();

            if (string.IsNullOrEmpty(key) || _tables == null)
            {
                return fallback;
            }

            try
            {
                var tb = _tables.TbGlobal;

                int id;
                if (int.TryParse(key, out id))
                {
                    var row = tb.GetOrDefault(id);
                    if (row != null)
                    {
                        return row.Value;
                    }

                    return fallback;
                }

                var list = tb.DataList;
                for (int i = 0; i < list.Count; i++)
                {
                    var r = list[i];
                    if (r != null && !string.IsNullOrEmpty(r.Description)
                        && r.Description.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return r.Value;
                    }
                }
            }
            catch (Exception e)
            {
                Warn("读取 TbGlobal 失败(" + key + ")：" + e.Message);
            }

            return fallback;
        }

        /// <summary>按 id 取职业行（生成类型 ht.ProfessionRow）；无此行返回 null。</summary>
        public ConfigProfessionRow GetProfession(int id)
        {
            EnsureBuilt();

            if (_tables == null)
            {
                return null;
            }

            try
            {
                return _tables.TbProfession.GetOrDefault(id);
            }
            catch (Exception e)
            {
                Warn("读取 TbProfession 行 " + id + " 失败：" + e.Message);
                return null;
            }
        }

        /// <summary>职业轻量视图（id / name / weight）；无此行返回 <see cref="ProfessionInfo.None"/>。</summary>
        public ProfessionInfo GetProfessionInfo(int id)
        {
            var row = GetProfession(id);
            return row == null
                ? ProfessionInfo.None
                : new ProfessionInfo(row.Id, row.Name, row.Weight);
        }

        /// <summary>全部职业行（表行序）；未配置返回空数组。</summary>
        public ConfigProfessionRow[] GetAllProfessions()
        {
            EnsureBuilt();

            if (_tables == null)
            {
                return Array.Empty<ConfigProfessionRow>();
            }

            try
            {
                var list = _tables.TbProfession.DataList;
                var arr = new ConfigProfessionRow[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    arr[i] = list[i];
                }

                return arr;
            }
            catch (Exception e)
            {
                Warn("读取 TbProfession 失败：" + e.Message);
                return Array.Empty<ConfigProfessionRow>();
            }
        }

        /// <summary>计数摘要，便于 HUD / 日志一眼确认配置是否落地。</summary>
        public string Summary()
        {
            EnsureBuilt();
            return "items=" + _allItems.Count
                   + ",recipes=" + _recipesArray.Length
                   + ",monsters=" + _monstersArray.Length
                   + ",surnames=" + _surnames.Length
                   + ",given=" + _givenNames.Length
                   + ",warnings=" + _warnings.Count;
        }

        // ------------------------------------------------------------------
        // 构建
        // ------------------------------------------------------------------

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            // 先置位：构建过程内部会回调 GetItem，避免递归。
            _built = true;

            if (_tables == null)
            {
                Warn("GameConfigCatalog 未 Init：所有查询回退 ItemCatalog / MonsterSystem 占位常量。");
                return;
            }

            BuildItems();
            BuildRecipes();
            BuildMonsters();
            BuildNames();
        }

        private void BuildItems()
        {
            try
            {
                var rows = _tables.TbItemGame.DataList;

                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row == null)
                    {
                        continue;
                    }

                    if (IsRecipeRow(row))
                    {
                        // 配方行不进酒单（计划 M2 口径）。
                        continue;
                    }

                    var kind = MapTypeToKind(row.Type, row.Name, row.Tag);
                    var def = new ItemDef(row.Id, row.Name, kind, row.Value, ParseMinutesFromText(row.Description), SplitTags(row.Tag));

                    _allItems.Add(def);
                    _itemById[def.Id] = def;

                    IndexItemForTagResolution(def);
                }
            }
            catch (Exception e)
            {
                Warn("构建物品目录失败（回退占位常量）：" + e.Message);
            }
        }

        /// <summary>为「成品酒标签反推配方」建立标签 → 物品 id 的解析索引（表行序，先到先得，保证确定性）。</summary>
        private void IndexItemForTagResolution(ItemDef def)
        {
            if (!string.IsNullOrEmpty(def.Name) && !_nameToId.ContainsKey(def.Name))
            {
                _nameToId[def.Name] = def.Id;
            }

            if (def.Kind != ItemKind.BaseWine && def.Kind != ItemKind.Ingredient)
            {
                return;
            }

            var target = def.Kind == ItemKind.BaseWine ? _baseWineTagToId : _ingredientTagToId;
            for (int i = 0; i < def.Tags.Length; i++)
            {
                if (!target.ContainsKey(def.Tags[i]))
                {
                    target[def.Tags[i]] = def.Id;
                }
            }
        }

        private void BuildRecipes()
        {
            try
            {
                var rows = _tables.TbItemGame.DataList;

                // 1) 显式配方行（type=RecipeRowType 且带配方标记 / 用料 id 列表）。
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row == null || !IsRecipeRow(row))
                    {
                        continue;
                    }

                    var recipe = ParseRecipeRow(row);
                    if (recipe != null)
                    {
                        _recipes.Add(recipe);
                    }
                }

                // 2) 没有显式配方行时，用成品酒（type=6）的标签反推（当前表只有这一条路径，见类注释冲突 2）。
                if (_recipes.Count == 0)
                {
                    SynthesizeRecipesFromWines(rows);
                }

                _recipesArray = _recipes.ToArray();
            }
            catch (Exception e)
            {
                Warn("构建配方目录失败（回退 ItemCatalog.Recipes 占位）：" + e.Message);
            }
        }

        /// <summary>
        /// 显式配方行解析规则（description 优先，其次 tag）：
        /// - 用料：形如 <c>12*2</c> / <c>12×2</c> / <c>12x2</c> / <c>12:2</c> / <c>12 2</c> 的「id+份数」记号，或纯 id 列表，
        ///   以 '、'，',','，',';','；','|','/','+',' ' 分隔。份数字段 >1 时按 1 份处理并告警（RecipeDef 无份数槽）。
        /// - 归类：BaseWine → 基酒槽（多于一份取首个并告警）；Ingredient → 配料 A/B（多于两份丢弃并告警）；
        ///   Ice → NeedIce=true；Equipment → 跳过并告警。
        /// - 产物：描述/标签里「产物」后的 id → 已列出的 Wine 类用料 → 配方行自身 id。
        /// - 缺基酒或完全没有配料 → 跳过该行并写入 <see cref="ParseWarnings"/>。
        /// </summary>
        private RecipeDef ParseRecipeRow(ConfigItem row)
        {
            bool hasCountField;
            var ids = ParseIdList(string.IsNullOrEmpty(row.Description) ? row.Tag : row.Description, out hasCountField);
            if (ids.Count == 0)
            {
                ids = ParseIdList(row.Tag, out hasCountField);
            }

            if (hasCountField)
            {
                Warn("配方行 " + row.Id + "《" + row.Name + "》用料份数不为 1，RecipeDef 无份数槽，已按每份 1 处理。");
            }

            if (ids.Count == 0)
            {
                Warn("配方行 " + row.Id + "《" + row.Name + "》未从描述/标签解析出用料 id，跳过。");
                return null;
            }

            int baseWineId = 0;
            int ingredientA = 0;
            int ingredientB = 0;
            int productWineId = 0;
            bool needIce = false;
            int extraBase = 0;
            int extraIngredients = 0;

            for (int i = 0; i < ids.Count; i++)
            {
                int id = ids[i];
                var def = GetItem(id);
                if (def == null)
                {
                    Warn("配方行 " + row.Id + "《" + row.Name + "》引用了不存在的用料 id=" + id + "，已忽略。");
                    continue;
                }

                switch (def.Kind)
                {
                    case ItemKind.BaseWine:
                        if (baseWineId == 0) baseWineId = id; else extraBase++;
                        break;
                    case ItemKind.Ingredient:
                        if (ingredientA == 0) ingredientA = id;
                        else if (ingredientB == 0) ingredientB = id;
                        else extraIngredients++;
                        break;
                    case ItemKind.Ice:
                        needIce = true;
                        break;
                    case ItemKind.Wine:
                        if (productWineId == 0) productWineId = id;
                        break;
                    default:
                        Warn("配方行 " + row.Id + "《" + row.Name + "》的用料 " + id + "《" + def.Name + "》不是调酒材料，已忽略。");
                        break;
                }
            }

            if (baseWineId == 0)
            {
                Warn("配方行 " + row.Id + "《" + row.Name + "》解析不到基酒，跳过。");
                return null;
            }

            if (ingredientA == 0)
            {
                Warn("配方行 " + row.Id + "《" + row.Name + "》解析不到配料，跳过。");
                return null;
            }

            if (ingredientB == 0)
            {
                Warn("配方行 " + row.Id + "《" + row.Name + "》只解析到 1 份配料，按同款两份填充（缺料需策划补 description）。");
                ingredientB = ingredientA;
            }

            if (extraBase > 0)
            {
                Warn("配方行 " + row.Id + "《" + row.Name + "》解析到 " + (extraBase + 1) + " 份基酒，取第一份。");
            }

            if (extraIngredients > 0)
            {
                Warn("配方行 " + row.Id + "《" + row.Name + "》多出 " + extraIngredients + " 份配料，RecipeDef 只有两槽，已丢弃。");
            }

            if (productWineId == 0)
            {
                productWineId = ParseProductId(row) ?? row.Id;
            }

            return new RecipeDef(row.Id, row.Name, baseWineId, ingredientA, ingredientB, needIce, productWineId);
        }

        /// <summary>
        /// 成品酒标签反推（当前唯一生效路径）：
        /// 逐个标签解析——冰类标签置 NeedIce；基酒标签先按「物品名 == 标签」再按「BaseWine 类物品带该标签」找基酒；
        /// 其余标签按同样顺序找 Ingredient 类物品。找不到基酒或一个配料都找不到 → 跳过并告警；
        /// 只找到 1 份配料 → 同款两份并告警；多于 2 份配料 → 取前两份并告警。产物 = 成品酒自身 id。
        /// </summary>
        private void SynthesizeRecipesFromWines(IReadOnlyList<ConfigItem> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null)
                {
                    continue;
                }

                var def = GetItem(row.Id);
                if (def == null || def.Kind != ItemKind.Wine)
                {
                    continue;
                }

                var tags = SplitTags(row.Tag);
                int baseWineId = 0;
                var ingredients = new List<int>();
                var unresolved = new List<string>();
                bool needIce = false;

                for (int t = 0; t < tags.Length; t++)
                {
                    string tag = tags[t];

                    if (HasWord(tag, IceMarker))
                    {
                        needIce = true;
                        continue;
                    }

                    int id = ResolveBaseWine(tag);
                    if (id != 0)
                    {
                        if (baseWineId == 0)
                        {
                            baseWineId = id;
                        }
                        else
                        {
                            Warn("成品酒 " + row.Id + "《" + row.Name + "》有多个基酒标签（" + tag + "），取 " + baseWineId + "。");
                        }

                        continue;
                    }

                    id = ResolveIngredient(tag);
                    if (id != 0)
                    {
                        if (!ingredients.Contains(id))
                        {
                            ingredients.Add(id);
                        }

                        continue;
                    }

                    unresolved.Add(tag);
                }

                if (unresolved.Count > 0)
                {
                    Warn("成品酒 " + row.Id + "《" + row.Name + "》标签无法解析为物品：" + string.Join("/", unresolved) + "。");
                }

                if (baseWineId == 0)
                {
                    Warn("成品酒 " + row.Id + "《" + row.Name + "》标签里没有基酒，跳过配方合成。");
                    continue;
                }

                if (ingredients.Count == 0)
                {
                    Warn("成品酒 " + row.Id + "《" + row.Name + "》标签里没有配料，跳过配方合成。");
                    continue;
                }

                int ingredientA = ingredients[0];
                int ingredientB;
                if (ingredients.Count == 1)
                {
                    Warn("成品酒 " + row.Id + "《" + row.Name + "》标签只解析到 1 份配料(" + ingredientA + ")，按同款两份填充。");
                    ingredientB = ingredientA;
                }
                else
                {
                    ingredientB = ingredients[1];
                    if (ingredients.Count > 2)
                    {
                        var dropped = new List<string>();
                        for (int k = 2; k < ingredients.Count; k++)
                        {
                            dropped.Add(ingredients[k].ToString());
                        }

                        Warn("成品酒 " + row.Id + "《" + row.Name + "》有 " + ingredients.Count + " 份配料，RecipeDef 只有两槽，已丢弃 " + string.Join("/", dropped) + "。");
                    }
                }

                _recipes.Add(new RecipeDef(row.Id, row.Name + "配方", baseWineId, ingredientA, ingredientB, needIce, row.Id));
            }
        }

        private void BuildMonsters()
        {
            try
            {
                var rows = _tables.TbMonster.DataList;

                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row == null)
                    {
                        continue;
                    }

                    // MonsterDef 承载不到 Location / Description，原始行另存一份供 M3/M4 查询。
                    _monsterRows[row.Id] = row;
                    _monsters.Add(new MonsterDef(row.Id, row.Name, row.Stage, row.Weight, row.Hp, row.Str, row.Intelligence, row.Def));
                }

                _monstersArray = _monsters.ToArray();
            }
            catch (Exception e)
            {
                Warn("构建怪物目录失败（回退 MonsterSystem.PlaceholderCatalog 占位，需调用 MonsterSystem.ReloadCatalog 注入）：" + e.Message);
            }
        }

        private void BuildNames()
        {
            try
            {
                var rows = _tables.TbName.DataList;
                var surnames = new List<string>();
                var given = new List<string>();

                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(row.Name1))
                    {
                        surnames.Add(row.Name1);
                    }

                    if (!string.IsNullOrEmpty(row.Name2))
                    {
                        given.Add(row.Name2);
                    }
                }

                _surnames = surnames.ToArray();
                _givenNames = given.ToArray();
            }
            catch (Exception e)
            {
                Warn("读取 TbName 失败（回退占位词表）：" + e.Message);
            }
        }

        // ------------------------------------------------------------------
        // 解析工具
        // ------------------------------------------------------------------

        /// <summary>
        /// ItemKind 推导规则（master-plan M2「ItemKind 推导规则」+ 实际数据修正，冲突见类注释）：
        /// 1) 名称或标签含「冰」→ <see cref="ItemKind.Ice"/>（冰块是 type=4 + tag=冰品，故冰判定先行）；
        /// 2) type=4 材料且标签含基酒词 → <see cref="ItemKind.BaseWine"/>；type=4 其余 → <see cref="ItemKind.Ingredient"/>；
        /// 3) type=1/2/3 → <see cref="ItemKind.Equipment"/>；
        /// 4) type=6 酒水 → <see cref="ItemKind.Wine"/>；type=5 且标签含酒类词 → <see cref="ItemKind.Wine"/>；
        /// 5) 其余（type=0 货币、type=5 消耗品、未知类型）→ <see cref="ItemKind.Ingredient"/>：
        ///    ItemKind 只有「酒 / 调酒材料 / 装备」三档，货币与食物不是酒也不是装备，归材料档可让它们正常进出仓库，
        ///    又不会混进酒单。若策划要求更严格口径，改这里即可（调用方统一走本方法）。
        /// </summary>
        public static ItemKind MapTypeToKind(int type, string name, string tag)
        {
            if (HasWord(name, IceMarker) || HasWord(tag, IceMarker))
            {
                return ItemKind.Ice;
            }

            if (type == TypeMaterial)
            {
                return IsBaseWineTag(tag) ? ItemKind.BaseWine : ItemKind.Ingredient;
            }

            if (type == TypeWeapon || type == TypeArmor || type == TypeAccessory)
            {
                return ItemKind.Equipment;
            }

            if (type == TypeDrink)
            {
                return ItemKind.Wine;
            }

            if (type == TypeConsumable && IsBaseWineTag(tag))
            {
                return ItemKind.Wine;
            }

            return ItemKind.Ingredient;
        }

        /// <summary>显式配方行判定：type=<see cref="RecipeRowType"/> 且带「配方」标记或用料 id 列表。</summary>
        public static bool IsRecipeRow(ConfigItem row)
        {
            if (row == null || row.Type != RecipeRowType)
            {
                return false;
            }

            if (HasWord(row.Name, RecipeMarker) || HasWord(row.Description, RecipeMarker) || HasWord(row.Tag, RecipeMarker))
            {
                return true;
            }

            return LooksLikeIdList(row.Description) || LooksLikeIdList(row.Tag);
        }

        /// <summary>标签是否命中基酒词表。</summary>
        public static bool IsBaseWineTag(string tag)
        {
            for (int i = 0; i < BaseWineTagWords.Length; i++)
            {
                if (HasWord(tag, BaseWineTagWords[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>标签切分（'、' , ',' '，' ';' '；' '|' '/' '+' 空格），去空去重，保持原序。</summary>
        public static string[] SplitTags(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return EmptyTags;
            }

            string[] parts = tag.Split(TagSeparatorChars);
            var result = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0 || result.Contains(p))
                {
                    continue;
                }

                result.Add(p);
            }

            return result.Count == 0 ? EmptyTags : result.ToArray();
        }

        private int ResolveBaseWine(string tag)
        {
            int id;
            if (_nameToId.TryGetValue(tag, out id))
            {
                var def = GetItem(id);
                if (def != null && def.Kind == ItemKind.BaseWine)
                {
                    return id;
                }
            }

            return _baseWineTagToId.TryGetValue(tag, out id) ? id : 0;
        }

        private int ResolveIngredient(string tag)
        {
            int id;
            if (_nameToId.TryGetValue(tag, out id))
            {
                var def = GetItem(id);
                if (def != null && def.Kind == ItemKind.Ingredient)
                {
                    return id;
                }
            }

            return _ingredientTagToId.TryGetValue(tag, out id) ? id : 0;
        }

        /// <summary>描述/标签里「产物」记号后的 id（没有则 null）。</summary>
        private static int? ParseProductId(ConfigItem row)
        {
            return ParseIdAfterMarker(row.Description, "产物") ?? ParseIdAfterMarker(row.Tag, "产物");
        }

        private static int? ParseIdAfterMarker(string text, string marker)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            int idx = text.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
            {
                return null;
            }

            for (int i = idx + marker.Length; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    if (text[i] == ':' || text[i] == '：' || text[i] == '=')
                    {
                        continue;
                    }

                    return null;
                }

                int end = i;
                while (end < text.Length && text[end] >= '0' && text[end] <= '9')
                {
                    end++;
                }

                int value;
                int.TryParse(text.Substring(i, end - i), out value);
                return value > 0 ? (int?)value : null;
            }

            return null;
        }

        /// <summary>文本里是否出现「数字 + 数量记号」，用于判定配方行。</summary>
        private static bool LooksLikeIdList(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9')
                {
                    continue;
                }

                int j = i;
                while (j < text.Length && text[j] >= '0' && text[j] <= '9')
                {
                    j++;
                }

                if (j < text.Length)
                {
                    char sep = text[j];
                    if ((sep == '*' || sep == 'x' || sep == 'X' || sep == '×' || sep == ':' || sep == '：')
                        && j + 1 < text.Length && text[j + 1] >= '0' && text[j + 1] <= '9')
                    {
                        return true;
                    }
                }

                i = j - 1;
            }

            return false;
        }

        /// <summary>
        /// 解析「id(+份数)」列表：<c>12*2</c> / <c>12×2</c> / <c>12x2</c> / <c>12:2</c> / <c>12 2</c> / <c>12</c>。
        /// hasCountField=true 表示原文里出现过份数不为 1 的记号（由调用方按每份 1 处理并告警）。
        /// </summary>
        private static List<int> ParseIdList(string text, out bool hasCountField)
        {
            hasCountField = false;
            var ids = new List<int>();
            if (string.IsNullOrEmpty(text))
            {
                return ids;
            }

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c < '0' || c > '9')
                {
                    i++;
                    continue;
                }

                int start = i;
                while (i < text.Length && text[i] >= '0' && text[i] <= '9')
                {
                    i++;
                }

                int id;
                if (!int.TryParse(text.Substring(start, i - start), out id) || id <= 0)
                {
                    continue;
                }

                ids.Add(id);

                int probe = i;
                while (probe < text.Length && (text[probe] == ' ' || text[probe] == '\t'))
                {
                    probe++;
                }

                if (probe < text.Length)
                {
                    char sep = text[probe];
                    if (sep == '*' || sep == 'x' || sep == 'X' || sep == '×' || sep == ':' || sep == '：')
                    {
                        int q = probe + 1;
                        while (q < text.Length && (text[q] == ' ' || text[q] == '\t'))
                        {
                            q++;
                        }

                        int countStart = q;
                        while (q < text.Length && text[q] >= '0' && text[q] <= '9')
                        {
                            q++;
                        }

                        if (q > countStart)
                        {
                            int count;
                            int.TryParse(text.Substring(countStart, q - countStart), out count);
                            if (count != 1)
                            {
                                hasCountField = true;
                            }

                            i = q;
                        }
                    }
                }
            }

            return ids;
        }

        /// <summary>从描述文本推断制作时长（分钟）；配置表无该列，取不到返回 0。</summary>
        private static int ParseMinutesFromText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < text.Length - 1; i++)
            {
                if (text[i] == '分' && text[i + 1] == '钟')
                {
                    int v = NumberBefore(text, i);
                    if (v > 0)
                    {
                        total += v;
                    }
                }
                else if (text[i] == '小' && text[i + 1] == '时')
                {
                    int v = NumberBefore(text, i);
                    if (v > 0)
                    {
                        total += v * 60;
                    }
                }
            }

            return total;
        }

        private static int NumberBefore(string text, int index)
        {
            int end = index;
            while (end > 0 && text[end - 1] >= '0' && text[end - 1] <= '9')
            {
                end--;
            }

            if (end == index)
            {
                return 0;
            }

            int value;
            int.TryParse(text.Substring(end, index - end), out value);
            return value;
        }

        private static bool HasWord(string text, string word)
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
