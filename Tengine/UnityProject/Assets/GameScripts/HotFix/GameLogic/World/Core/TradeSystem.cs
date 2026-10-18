using System;
using System.Collections.Generic;

using ConfigItem = GameConfig.ht.Item;

namespace GameLogic.World
{
    /// <summary>
    /// 交易定价公式（master-plan M8；出处《角色系统 v3.1》3.4 + 《立项规划》交易节）。
    ///
    /// 口径：
    /// - <b>好感度区间 0-100</b>（master-plan §2 矛盾 #6：角色系统 3.4 的「0—20」视为笔误，取 v3.1 的上限 100）。
    /// - <b>总价</b> = 单价(value) × 数量；单价<b>优先</b>取 <see cref="GameConfigCatalog.GetItemRow"/> 的 <c>Value</c> 列，
    ///   取不到（或 &lt;= 0）才回退 <see cref="ItemCatalog.Get"/> 的占位 <c>Price</c>。
    /// - <b>出售价</b> = 总价 ×(100−好感)÷100，<b>向上取整</b>；结果为 <b>0 表示不可出售</b>（好感太高时 NPC 不收货）。
    /// - <b>购买价</b> = 总价 ÷2，<b>向下取整</b>（整数除法天然下取整）。
    /// - <b>公平交易</b>：两侧总价差 ≤ 较高价的 10%（master-plan §2 矛盾 #7：立项「差值 &gt; 10%」与角色系统 3.4
    ///   「出售 &gt; 购买即赠予」合并为该阈值）。
    /// - <b>赠予</b>好感 += 礼物总价 ÷100（整除下取整）；<b>索取</b>好感 −= 差值 ×3 ÷100（<b>向上取整</b>）。
    ///
    /// 全部整数运算（无浮点），取整口径固定；纯逻辑、不依赖 UnityEngine。
    /// </summary>
    public static class TradePricing
    {
        /// <summary>好感下限（0）。</summary>
        public const int MinFavor = 0;

        /// <summary>好感上限（100；master-plan §2 矛盾 #6）。</summary>
        public const int MaxFavor = 100;

        /// <summary>百分比基数（分母固定 100）。</summary>
        private const int PercentBase = 100;

        /// <summary>
        /// 索取扣好感的倍率（《角色系统 v3.1》3.4：差值 ×3 ÷100）。
        /// </summary>
        private const int BegPenaltyMultiplier = 3;

        /// <summary>赠予加好感的除数（礼物总价 ÷100）。</summary>
        private const int GiftBonusDivisor = 100;

        /// <summary>把任意好感值钳进 [0,100]（组合根 <c>WorldSimulation.ChangeFavor</c> 同一口径）。</summary>
        public static int ClampFavor(int favor)
        {
            if (favor < MinFavor)
            {
                return MinFavor;
            }

            return favor > MaxFavor ? MaxFavor : favor;
        }

        /// <summary>
        /// 玩家出售给 NPC 的价格：总价 ×(100−好感) ÷100，<b>向上取整</b>（整数式 <c>(a*b+99)/100</c>）。
        ///
        /// 自检：总价 100、好感 20 → (100×80+99)/100 = 80。
        /// 返回 <b>0</b> 表示不可出售（总价 &lt;= 0，或好感高到折算后不足 1 金）。
        /// </summary>
        public static int SellPriceToNpc(int totalValue, int favor)
        {
            if (totalValue <= 0)
            {
                return 0;
            }

            int rate = MaxFavor - ClampFavor(favor);
            if (rate <= 0)
            {
                return 0;
            }

            long scaled = ((long)totalValue * rate + PercentBase - 1) / PercentBase;
            return scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
        }

        /// <summary>
        /// 玩家向 NPC 购买的价格：总价 ÷2，<b>向下取整</b>（整数除法天然下取整）。
        ///
        /// 自检：总价 100 → 50。
        /// </summary>
        public static int BuyPriceFromNpc(int totalValue)
        {
            if (totalValue <= 0)
            {
                return 0;
            }

            return totalValue / 2;
        }

        /// <summary>
        /// 是否公平交易：两侧总价差 ≤ <b>较高价</b> 的 10%（<c>差 ×10 ≤ 较高价</c>，整数式避开浮点）。
        /// 两侧同为 0（无交易）视为公平。
        ///
        /// 自检：110 vs 100 → 差 10，10×10=100 ≤ 100 → 公平；
        ///       111 vs 100 → 差 11，110 &gt; 100 → 不公平。
        /// </summary>
        public static bool IsFairTrade(int playerOffer, int npcOffer)
        {
            int player = playerOffer > 0 ? playerOffer : 0;
            int npc = npcOffer > 0 ? npcOffer : 0;

            int higher = Math.Max(player, npc);
            int lower = Math.Min(player, npc);
            return ((long)(higher - lower) * PercentBase) <= (long)higher * 10;
        }

        /// <summary>
        /// 赠予带来的好感增量：礼物总价 ÷100（<b>下取整</b>）。
        /// 自检：300 → 3；99 → 0（不足 100 不加好感，但仍算赠予行为）。
        /// </summary>
        public static int GiftFavorDelta(int offerValue)
        {
            if (offerValue <= 0)
            {
                return 0;
            }

            return offerValue / GiftBonusDivisor;
        }

        /// <summary>
        /// 索取的扣好感<b>惩罚量</b>（正数）：差值 ×3 ÷100，<b>向上取整</b>。
        /// 自检：差 100 → (100×3+99)/100 = 3。
        /// </summary>
        public static int BegFavorPenalty(int difference)
        {
            if (difference <= 0)
            {
                return 0;
            }

            return (int)(((long)difference * BegPenaltyMultiplier + PercentBase - 1) / PercentBase);
        }
    }

    /// <summary>
    /// 交易结果（master-plan M8）。<see cref="Success"/> 为 false 时 <see cref="FailReason"/> 必有值，
    /// 且**不发生任何物品 / 金币 / 好感变动**（先校验后转移）。
    /// </summary>
    public readonly struct TradeResult
    {
        /// <summary>交易是否成交（物品与金币均已转移）。</summary>
        public readonly bool Success;

        /// <summary>是否为「赠予」：玩家方价值高出 NPC 方 10% 以上，好感上升。</summary>
        public readonly bool IsGift;

        /// <summary>是否为「索取」：NPC 方价值高出玩家方 10% 以上，好感下降。</summary>
        public readonly bool IsBeg;

        /// <summary>
        /// 规则算出的好感增减（赠予为正、索取为负、公平交易为 0；<b>未计 0-100 钳制</b>，
        /// 实际生效值由 <c>WorldSimulation.ChangeFavor</c> 钳制）。
        /// </summary>
        public readonly int FavorDelta;

        /// <summary>失败原因（成功时为 null）。</summary>
        public readonly string FailReason;

        public TradeResult(bool success, bool isGift, bool isBeg, int favorDelta, string failReason)
        {
            Success = success;
            IsGift = isGift;
            IsBeg = isBeg;
            FavorDelta = favorDelta;
            FailReason = failReason;
        }

        // ---- 失败原因常量（UI 层可直接展示，不要拼接额外文案）----

        /// <summary>缺少交易对手。</summary>
        public const string FailNpcMissing = "缺少交易对象";

        /// <summary>参数非法（钱包 / 玩家背包为 null）。</summary>
        public const string FailArgument = "交易参数非法";

        /// <summary>交易对象已死亡。</summary>
        public const string FailNpcDead = "对方已死亡，无法交易";

        /// <summary>双方都没有摆出物品。</summary>
        public const string FailNothingToTrade = "没有可交易的内容";

        /// <summary>玩家拿不出承诺的物品。</summary>
        public const string FailPlayerMissingItem = "玩家持有的物品不足";

        /// <summary>NPC 拿不出承诺的物品。</summary>
        public const string FailNpcMissingItem = "对方拿不出约定的物品";

        /// <summary>玩家出售的折算价为 0（好感过高 → 不可出售）。</summary>
        public const string FailNotSellable = "对方好感过高，该物品已不可出售";

        /// <summary>NPC 好感为 0 时拒绝索取。</summary>
        public const string FailBegRejected = "对方好感已归零，拒绝索取";

        /// <summary>NPC 金币不足支付差额（交易失败）。</summary>
        public const string FailNpcGold = "对方金币不足，支付失败";

        /// <summary>玩家金币不足支付差额。</summary>
        public const string FailPlayerGold = "玩家金币不足";

        /// <summary>成交。</summary>
        public static TradeResult Ok(int favorDelta, bool isGift, bool isBeg)
        {
            return new TradeResult(true, isGift, isBeg, favorDelta, null);
        }

        /// <summary>失败（不产生任何副作用）。</summary>
        public static TradeResult Fail(string reason)
        {
            return new TradeResult(false, false, false, 0, reason ?? FailArgument);
        }
    }

    /// <summary>
    /// 一笔玩家 ↔ NPC 的交易会话（master-plan M8）。
    ///
    /// 用法：<c>new TradeSession(npc)</c> → 往 <see cref="PlayerOffer"/> / <see cref="NpcOffer"/> 摆物品
    /// （<see cref="PlayerOfferValue"/> / <see cref="NpcOfferValue"/> 实时按 <see cref="TradePricing"/> 定价）
    /// → <see cref="CanBuy"/> 校验 → <see cref="Execute"/> 成交。
    ///
    /// 规则（《角色系统 v3.1》3.4 + 立项规划交易节）：
    /// - <b>公平</b>（两侧差 ≤ 较高价 10%）→ 纯交换，<b>好感不变</b>；
    /// - <b>赠予</b>（玩家方 &gt; NPC 方 10%+）→ 好感 += 礼物总价 ÷100；
    /// - <b>索取</b>（NPC 方 &gt; 玩家方 10%+）→ 好感 −= 差值 ×3 ÷100（向上取整），<b>好感 = 0 时直接拒绝</b>。
    ///
    /// 校验（全部通过才转移物品 / 金币）：参数合法 → 双方摆出的物品各自拿得出 →
    /// 玩家侧折算价不为 0 → 索取拒绝判定 → 金币充足（<b>NPC 金币不足支付时交易失败</b>）。
    ///
    /// <b>依赖方向</b>：本类不引用 <c>WorldSimulation</c>（组合根 → 系统的单向注入，避免反向依赖）。
    /// 好感增减由调用方传入的 <c>Action&lt;CharacterData,int&gt;</c> 回调落到
    /// <c>WorldSimulation.ChangeFavor</c>（钳 0-100 + 触发 <c>FavorChanged</c>）；
    /// 不传回调时退化为「就地钳写」，仅供纯逻辑单测 / 未接入组合根的场景使用。
    /// 纯逻辑、不依赖 UnityEngine。
    /// </summary>
    public sealed class TradeSession
    {
        /// <summary>交易对象（NPC）。</summary>
        public CharacterData Npc { get; }

        /// <summary>
        /// 玩家摆出的物品（<c>itemId → 数量</c>）。字典语义天然支持「拆分」：
        /// 同一 id 写 3 就是摆 3 件（plan M8 验收：拆分规则只在 UI 层 M12 做，逻辑层由本字典表达）。
        /// </summary>
        public Dictionary<int, int> PlayerOffer { get; } = new Dictionary<int, int>();

        /// <summary>NPC 摆出的物品（<c>itemId → 数量</c>；语义同上）。</summary>
        public Dictionary<int, int> NpcOffer { get; } = new Dictionary<int, int>();

        public TradeSession(CharacterData npc)
        {
            Npc = npc;
        }

        // ------------------------------------------------------------------
        // 摆货
        // ------------------------------------------------------------------

        /// <summary>
        /// 玩家摆出物品（同 id 覆盖旧数量；<paramref name="count"/> &lt;= 0 视为取消摆出）。
        /// </summary>
        public void OfferPlayerItem(int itemId, int count)
        {
            SetOffer(PlayerOffer, itemId, count);
        }

        /// <summary>NPC 摆出物品（同 id 覆盖旧数量；<paramref name="count"/> &lt;= 0 视为取消摆出）。</summary>
        public void OfferNpcItem(int itemId, int count)
        {
            SetOffer(NpcOffer, itemId, count);
        }

        private static void SetOffer(Dictionary<int, int> offer, int itemId, int count)
        {
            if (itemId <= 0)
            {
                return;
            }

            if (count <= 0)
            {
                offer.Remove(itemId);
                return;
            }

            offer[itemId] = count;
        }

        // ------------------------------------------------------------------
        // 定价
        // ------------------------------------------------------------------

        /// <summary>
        /// 玩家摆出物品的<b>出售总价</b>（已按 <see cref="TradePricing.SellPriceToNpc"/> 随当前好感折算，
        /// 好感变化后自动重算）。为 0 表示「不可出售」。
        /// </summary>
        public int PlayerOfferValue
        {
            get { return TradePricing.SellPriceToNpc(TotalValue(PlayerOffer), Favor); }
        }

        /// <summary>NPC 摆出物品的<b>购买总价</b>（<see cref="TradePricing.BuyPriceFromNpc"/>：总价 ÷2 下取整）。</summary>
        public int NpcOfferValue
        {
            get { return TradePricing.BuyPriceFromNpc(TotalValue(NpcOffer)); }
        }

        /// <summary>交易对象当前好感（0-100；无对象时为 0）。</summary>
        public int Favor
        {
            get { return TradePricing.ClampFavor(Npc != null ? Npc.FavorToPlayer : TradePricing.MinFavor); }
        }

        /// <summary>字典里各物品「单价 × 数量」之和（未定价的原始总价）。</summary>
        public static int TotalValue(Dictionary<int, int> offer)
        {
            if (offer == null || offer.Count == 0)
            {
                return 0;
            }

            int total = 0;
            foreach (KeyValuePair<int, int> pair in offer)
            {
                total += UnitValue(pair.Key) * Math.Max(0, pair.Value);
            }

            return total;
        }

        /// <summary>
        /// 单个物品的基础单价（总价 = 单价 × 数量）。
        /// 优先取配置 Item 表的 <c>Value</c> 列；目录未 Init / 行缺失 / 值 &lt;= 0 时回退
        /// <see cref="ItemCatalog"/> 的占位 <c>Price</c>；都没有则按 0（定价为 0，不参与交易价值）。
        /// </summary>
        public static int UnitValue(int itemId)
        {
            if (itemId <= 0)
            {
                return 0;
            }

            var catalog = GameConfigCatalog.Instance;
            if (catalog != null)
            {
                ConfigItem row = catalog.GetItemRow(itemId);
                if (row != null && row.Value > 0)
                {
                    return row.Value;
                }
            }

            var def = ItemCatalog.Get(itemId);
            return def != null && def.Price > 0 ? def.Price : 0;
        }

        // ------------------------------------------------------------------
        // 购买资格
        // ------------------------------------------------------------------

        /// <summary>
        /// 物品好感门槛（<b>占位</b>：品质 × <see cref="QualityFavorUnit"/>）。
        /// 品质取配置 Item 表 <c>quality</c> 列（当前 1—4），取不到回退 0。
        /// TODO：Item 表<b>尚无好感门槛列</b>，落地后改读该列，本占位随之作废。
        /// </summary>
        public int FavorThreshold(int itemId)
        {
            if (itemId <= 0)
            {
                return 0;
            }

            var catalog = GameConfigCatalog.Instance;
            if (catalog == null)
            {
                return 0;
            }

            ConfigItem row = catalog.GetItemRow(itemId);
            if (row == null || row.Quality <= 0)
            {
                return 0;
            }

            return row.Quality * QualityFavorUnit;
        }

        /// <summary>
        /// 每点品质折算的好感门槛单位（占位常量，待 Item 表好感门槛列落地后作废）。
        /// 原为 10：门槛落在 10—40，而 NPC 初始好感为 0、开局无法建立，
        /// 导致所有 NPC 道具整片置灰不可点。现降到 1：门槛 1—4 点，
        /// 开局即可购买低品质道具，高品质需先积累少量好感。
        /// </summary>
        public const int QualityFavorUnit = 1;

        /// <summary>
        /// 能否向该 NPC 购买此物：<b>好感 ≥ 物品好感门槛</b>。
        /// 取「大于等于」与 <see cref="DialogueSystem"/> 的同类好感门槛判定保持一致
        /// （此处原为严格大于，门槛为 0 时会把所有道具判成不可买）。
        /// 无交易对象 / 物品 id 非法一律 false。
        /// </summary>
        public bool CanBuy(int itemId)
        {
            if (Npc == null || itemId <= 0)
            {
                return false;
            }

            return Favor >= FavorThreshold(itemId);
        }

        // ------------------------------------------------------------------
        // 成交
        // ------------------------------------------------------------------

        /// <summary>
        /// 成交（不带好感回调版本：好感就地钳写，<b>不触发</b> <c>WorldSimulation.FavorChanged</c>）。
        /// 组合根 / UI 层请优先调用带 <see cref="Action{CharacterData, Int32}"/> 的重载。
        /// </summary>
        public TradeResult Execute(PlayerWallet wallet, Inventory playerBag)
        {
            return Execute(wallet, playerBag, null);
        }

        /// <summary>
        /// 成交：校验双方物品 / 金币充足 → 判定公平 / 赠予 / 索取 → 转移物品与金币 → 按回调增减好感。
        ///
        /// <paramref name="changeFavor"/>：好感增减的<b>唯一写入口</b>，由组合根传入
        /// <c>WorldSimulation.ChangeFavor</c>（钳 0-100 + 触发 FavorChanged）；
        /// 传 null 时退化为就地钳写（单测 / 未接入组合根）。
        /// </summary>
        public TradeResult Execute(PlayerWallet wallet, Inventory playerBag, Action<CharacterData, int> changeFavor)
        {
            // ---- 0. 参数与基本状态 ----
            if (Npc == null)
            {
                return TradeResult.Fail(TradeResult.FailNpcMissing);
            }

            if (wallet == null || playerBag == null)
            {
                return TradeResult.Fail(TradeResult.FailArgument);
            }

            if (!Npc.IsAlive)
            {
                return TradeResult.Fail(TradeResult.FailNpcDead);
            }

            if (PlayerOffer.Count == 0 && NpcOffer.Count == 0)
            {
                return TradeResult.Fail(TradeResult.FailNothingToTrade);
            }

            // ---- 1. 物品充足（先校验，不满足则一件都不动）----
            if (!HasAll(playerBag, PlayerOffer))
            {
                return TradeResult.Fail(TradeResult.FailPlayerMissingItem);
            }

            if (!NpcBagHasAll(NpcOffer))
            {
                return TradeResult.Fail(TradeResult.FailNpcMissingItem);
            }

            // ---- 2. 定价与交易性质（公平 / 赠予 / 索取）----
            int playerValue = PlayerOfferValue;
            int npcValue = NpcOfferValue;

            // 出售价 0 = 不可出售：玩家摆出物品却折不出 1 金，直接判失败。
            if (PlayerOffer.Count > 0 && playerValue <= 0)
            {
                return TradeResult.Fail(TradeResult.FailNotSellable);
            }

            bool isGift = false;
            bool isBeg = false;
            int favorDelta = 0;

            if (!TradePricing.IsFairTrade(playerValue, npcValue))
            {
                if (playerValue > npcValue)
                {
                    isGift = true;
                    favorDelta = TradePricing.GiftFavorDelta(playerValue);
                }
                else
                {
                    // 索取：好感为 0 时 NPC 直接翻脸拒绝。
                    isBeg = true;
                    if (Npc.FavorToPlayer <= TradePricing.MinFavor)
                    {
                        return TradeResult.Fail(TradeResult.FailBegRejected);
                    }

                    favorDelta = -TradePricing.BegFavorPenalty(npcValue - playerValue);
                }
            }

            // ---- 3. 金币充足（差额 = 玩家收付，正=玩家收，负=玩家付）----
            int goldDelta = playerValue - npcValue;
            if (goldDelta > 0)
            {
                if (Npc.Gold < goldDelta)
                {
                    return TradeResult.Fail(TradeResult.FailNpcGold);
                }
            }
            else if (goldDelta < 0 && !wallet.CanAfford(-goldDelta))
            {
                return TradeResult.Fail(TradeResult.FailPlayerGold);
            }

            // ---- 4. 物品转移（校验已全部通过，这里必然成功）----
            foreach (KeyValuePair<int, int> pair in PlayerOffer)
            {
                playerBag.TryRemove(pair.Key, pair.Value);
                Npc.Bag.Add(pair.Key, pair.Value);
            }

            foreach (KeyValuePair<int, int> pair in NpcOffer)
            {
                Npc.Bag.TryRemove(pair.Key, pair.Value);
                playerBag.Add(pair.Key, pair.Value);
            }

            // ---- 5. 金币转移 ----
            if (goldDelta > 0)
            {
                Npc.Gold -= goldDelta;
                wallet.Earn(goldDelta);
            }
            else if (goldDelta < 0)
            {
                int paid = -goldDelta;
                wallet.TrySpend(paid);
                Npc.Gold += paid;
            }

            // ---- 6. 好感增减（走组合根回调；无回调就地钳写）----
            if (favorDelta != 0)
            {
                ApplyFavor(changeFavor, favorDelta);
            }

            return TradeResult.Ok(favorDelta, isGift, isBeg);
        }

        /// <summary>
        /// 好感增减落点：有回调交回调（组合根 → <c>WorldSimulation.ChangeFavor</c>，钳制 + 事件都在那里），
        /// 无回调则就地钳写，保证 <see cref="TradeSession"/> 单独使用时行为仍然一致。
        /// </summary>
        private void ApplyFavor(Action<CharacterData, int> changeFavor, int delta)
        {
            if (changeFavor != null)
            {
                changeFavor(Npc, delta);
                return;
            }

            Npc.FavorToPlayer = TradePricing.ClampFavor(Npc.FavorToPlayer + delta);
        }

        /// <summary>NPC 背包是否拿得出 <paramref name="offer"/> 里的全部物品（空报价恒 true）。</summary>
        private bool NpcBagHasAll(Dictionary<int, int> offer)
        {
            foreach (KeyValuePair<int, int> pair in offer)
            {
                if (!Npc.Bag.HasItem(pair.Key, pair.Value))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasAll(Inventory bag, Dictionary<int, int> offer)
        {
            if (offer.Count == 0)
            {
                return true;
            }

            foreach (KeyValuePair<int, int> pair in offer)
            {
                if (bag.Count(pair.Key) < pair.Value)
                {
                    return false;
                }
            }

            return true;
        }

        public override string ToString()
        {
            return "npc=" + (Npc != null ? Npc.Name : "(null)")
                   + ",favor=" + Favor
                   + ",playerOffer=" + PlayerOfferValue + "(×" + SumCount(PlayerOffer) + ")"
                   + ",npcOffer=" + NpcOfferValue + "(×" + SumCount(NpcOffer) + ")";
        }

        private static int SumCount(Dictionary<int, int> offer)
        {
            int total = 0;
            foreach (KeyValuePair<int, int> pair in offer)
            {
                total += pair.Value;
            }

            return total;
        }
    }
}
