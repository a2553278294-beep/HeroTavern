using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic.World;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 主界面 HUD（OnGUI 原型实现，正式版按 html-to-ugui 流程重做为 TEngine UIWindow）。
    /// 顶栏（时钟/档位/暂停）、货币、结算分栏（5 秒自动收起）、厨房采购/调酒、NPC 查看、悬赏栏情报、游戏结束遮罩。
    /// </summary>
    public sealed class WorldHud
    {
        private enum Panel
        {
            None,
            KitchenChoice, // 经营案：点击厨房生成采购/调酒两个小型 UI
            Purchase,
            Brew,
            Character,
            Board,
        }

        private readonly WorldSceneBootstrap _boot;
        private readonly List<Rect> _panelRects = new List<Rect>();
        private Panel _panel = Panel.None;
        private CharacterData _selected;
        private SettlementReport _lastReport;
        private float _reportUntil;
        private bool _gameOver;

        // 调酒面板选择状态。
        private int _baseIdx;
        private int _ingAIdx;
        private int _ingBIdx;
        private bool _ice;
        private string _brewMessage = string.Empty;

        public WorldHud(WorldSceneBootstrap boot)
        {
            _boot = boot;
            var sim = boot.Sim;
            if (sim != null)
            {
                sim.SettlementReady += OnSettlementReady;
                sim.GameOver += OnGameOver;
            }
        }

        public CharacterData SelectedCharacter => _selected;

        private WorldSimulation Sim => _boot.Sim;

        public void OpenCharacterPanel(CharacterData character)
        {
            _selected = character;
            _panel = Panel.Character;
        }

        public void OpenKitchenChoice()
        {
            _panel = Panel.KitchenChoice;
        }

        public void OpenBoardPanel()
        {
            _panel = Panel.Board;
        }

        public void ClosePanels()
        {
            _panel = Panel.None;
            _selected = null;
        }

        /// <summary>鼠标是否落在任一 HUD 面板上（防止穿透点击世界）。</summary>
        public bool IsPointerOverPanel(Vector2 mouseScreenPosition)
        {
            var guiPos = new Vector2(mouseScreenPosition.x, Screen.height - mouseScreenPosition.y);
            for (int i = 0; i < _panelRects.Count; i++)
            {
                if (_panelRects[i].Contains(guiPos))
                {
                    return true;
                }
            }

            return false;
        }

        public void Draw()
        {
            var sim = Sim;
            if (sim == null || !sim.IsRunning)
            {
                return;
            }

            _panelRects.Clear();
            DrawWorldLabels(sim);
            DrawTopBar(sim);
            DrawMoney(sim);

            switch (_panel)
            {
                case Panel.KitchenChoice: DrawKitchenChoice(); break;
                case Panel.Purchase: DrawPurchase(sim); break;
                case Panel.Brew: DrawBrew(sim); break;
                case Panel.Character: DrawCharacter(sim); break;
                case Panel.Board: DrawBoard(sim); break;
            }

            DrawSettlement(sim);
            if (_gameOver)
            {
                DrawGameOver(sim);
            }
        }

        // ==================== 顶栏 / 货币 ====================

        private void DrawTopBar(WorldSimulation sim)
        {
            var bar = new Rect(0, 0, Screen.width, 30);
            GUI.Box(bar, GUIContent.none);
            _panelRects.Add(bar);

            // 点击时间暂停（经营案主界面规则）。
            var t = sim.Time;
            if (GUI.Button(new Rect(6, 3, 210, 24), $"第{t.Year}年 第{t.DayOfYear}天 {t.Hour:00}:{t.Minute:00}"))
            {
                GameModule.World.SetPaused(!sim.IsPaused);
            }

            // 五档加速 ×1/×2/×4/×8/×16。
            int mult = 1;
            for (int level = 1; level <= WorldSimulation.MaxSpeedLevel; level++)
            {
                string label = (sim.SpeedLevel == level ? "[×" : "×") + mult + (sim.SpeedLevel == level ? "]" : "");
                if (GUI.Button(new Rect(220 + (level - 1) * 46, 3, 42, 24), label))
                {
                    GameModule.World.SetSpeedLevel(level);
                }

                mult *= 2;
            }

            if (GUI.Button(new Rect(460, 3, 60, 24), sim.IsPaused ? "继续" : "暂停"))
            {
                GameModule.World.SetPaused(!sim.IsPaused);
            }

            GUI.Label(new Rect(530, 5, 320, 22),
                $"人口 {sim.Population.Residents.Count}/{TownPopulation.MaxResidents}  暂住 {sim.Population.Visitors.Count}/{TownPopulation.MaxVisitors}  怪物 {sim.Monsters.Monsters.Count}  流水 {sim.Tavern.TotalRevenue}");
        }

        private void DrawMoney(WorldSimulation sim)
        {
            var rect = new Rect(Screen.width - 170, 34, 160, 26);
            GUI.Box(rect, $"金币 {sim.Wallet.Currency}");
            _panelRects.Add(rect);
        }

        // ==================== 世界标签 ====================

        private void DrawWorldLabels(WorldSimulation sim)
        {
            var cam = _boot.SceneCamera;
            if (cam == null)
            {
                return;
            }

            foreach (var pair in _boot.Views)
            {
                var view = pair.Value;
                if (view == null || view.Data == null)
                {
                    continue;
                }

                Vector3 sp = cam.WorldToScreenPoint(view.transform.position + Vector3.up * 0.7f);
                if (sp.z <= 0f)
                {
                    continue;
                }

                var rect = new Rect(sp.x - 50, Screen.height - sp.y - 10, 100, 20);
                var prev = GUI.color;
                GUI.color = CharacterView.ProfessionColor(view.Data.Profession);
                GUI.Label(rect, view.Data.Name);
                GUI.color = prev;
            }

            // 酒保三态（送单中与制作中合并显示为制作中，原型口径）。
            Vector3 bp = cam.WorldToScreenPoint(WorldSceneBootstrap.CellToWorld(WorldMap.TavernPos, 1.6f));
            if (bp.z > 0f)
            {
                string state = sim.Tavern.IsMaking ? $"制作中(排队{sim.Tavern.PendingOrderCount})" : "待命";
                GUI.Label(new Rect(bp.x - 60, Screen.height - bp.y, 140, 20), $"酒保：{state}");
            }

            GUI.Label(new Rect(8, 34, 400, 20), "点击：NPC=查看 厨房=采购/调酒 悬赏栏=怪物情报 空白=关闭 时钟=暂停");
        }

        // ==================== 厨房：采购 / 调酒 ====================

        private void DrawKitchenChoice()
        {
            var rect = CenterRect(220, 90);
            BeginPanel(rect, "厨房");
            if (GUI.Button(new Rect(20, 30, 80, 40), "采购"))
            {
                _panel = Panel.Purchase;
            }

            if (GUI.Button(new Rect(120, 30, 80, 40), "调酒"))
            {
                _panel = Panel.Brew;
            }

            EndPanel();
        }

        private void DrawPurchase(WorldSimulation sim)
        {
            var rect = CenterRect(560, 320);
            BeginPanel(rect, sim.Tavern.IsLockedToday ? "采购（今日 10:00 已锁定）" : "采购（10:00 前可修改份数）");
            GUILayout.BeginArea(new Rect(10, 24, rect.width - 20, rect.height - 34));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("返回", GUILayout.Width(60)))
            {
                _panel = Panel.KitchenChoice;
            }

            if (GUILayout.Button("关闭", GUILayout.Width(60)))
            {
                ClosePanels();
            }

            GUILayout.EndHorizontal();

            foreach (int wineId in sim.Brewing.UnlockedWines)
            {
                var def = ItemCatalog.Get(wineId);
                if (def == null)
                {
                    continue;
                }

                GUILayout.BeginHorizontal();
                bool onShelf = sim.Tavern.Shelf.Contains(wineId);
                string toggle = onShelf ? "下架" : "上架";
                if (GUILayout.Button(toggle, GUILayout.Width(50)))
                {
                    var shelf = new List<int>(sim.Tavern.Shelf);
                    if (onShelf)
                    {
                        shelf.Remove(wineId);
                    }
                    else
                    {
                        shelf.Add(wineId);
                    }

                    if (!sim.Tavern.SetShelf(shelf))
                    {
                        _brewMessage = $"上架失败：槽位上限 {TavernSystem.ShelfSlotLimit} 或酒未解锁";
                    }
                }

                GUILayout.Label($"{def.Name} 价{def.Price} 时长{def.MakeMinutes / 60}h 今日剩{sim.Tavern.RemainingCountOf(wineId)}", GUILayout.Width(280));

                bool wasEnabled = GUI.enabled;
                GUI.enabled = wasEnabled && !sim.Tavern.IsLockedToday && onShelf;
                if (GUILayout.Button("-", GUILayout.Width(24)))
                {
                    sim.Tavern.SetPlannedCount(wineId, Math.Max(0, sim.Tavern.PlannedCountOf(wineId) - 1));
                }

                GUILayout.Label($"份数 {sim.Tavern.PlannedCountOf(wineId)}", GUILayout.Width(60));
                if (GUILayout.Button("+", GUILayout.Width(24)))
                {
                    sim.Tavern.SetPlannedCount(wineId, sim.Tavern.PlannedCountOf(wineId) + 1);
                }

                GUI.enabled = wasEnabled;
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
            EndPanel();
        }

        private void DrawBrew(WorldSimulation sim)
        {
            var rect = CenterRect(560, 360);
            BeginPanel(rect, "调酒");
            GUILayout.BeginArea(new Rect(10, 24, rect.width - 20, rect.height - 34));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("返回", GUILayout.Width(60)))
            {
                _panel = Panel.KitchenChoice;
            }

            if (GUILayout.Button("关闭", GUILayout.Width(60)))
            {
                ClosePanels();
            }

            GUILayout.EndHorizontal();

            var bases = AvailableMaterials(sim, ItemKind.BaseWine);
            var ings = AvailableMaterials(sim, ItemKind.Ingredient);
            int iceCount = sim.Warehouse.Count(ItemCatalog.IceCube);

            DrawSelector("基酒", bases, ref _baseIdx);
            DrawSelector("配料1", ings, ref _ingAIdx);
            DrawSelector("配料2", ings, ref _ingBIdx);

            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && iceCount > 0;
            if (GUILayout.Button($"加冰：{(_ice ? "开" : "关")}（库存 {iceCount}）"))
            {
                _ice = !_ice;
            }

            GUI.enabled = wasEnabled;

            bool canBrew = bases.Count > 0 && ings.Count > 0;
            GUI.enabled = wasEnabled && canBrew; // 三槽都有内容才点亮（调酒案）。
            if (GUILayout.Button("调酒"))
            {
                var result = sim.Brewing.Brew(sim.Warehouse, bases[_baseIdx].Id, ings[_ingAIdx].Id, ings[_ingBIdx].Id, _ice);
                switch (result.Outcome)
                {
                    case BrewOutcome.NewDiscovery:
                        _brewMessage = $"酿出了 {ItemCatalog.Get(result.Recipe.ProductWineId).Name}！新酒已入图鉴";
                        break;
                    case BrewOutcome.AlreadyKnown:
                        _brewMessage = "该酒已研发，材料已返还";
                        break;
                    default:
                        _brewMessage = $"得到一杯神秘鸡尾酒（{result.MysteryTags}）";
                        break;
                }

                ClampSelectors(bases.Count, ings.Count);
            }

            GUI.enabled = wasEnabled;

            if (!string.IsNullOrEmpty(_brewMessage))
            {
                GUILayout.Label(_brewMessage);
            }

            GUILayout.Label("已解锁酒单：" + BuildWineListText(sim));
            GUILayout.EndArea();
            EndPanel();
        }

        private static List<ItemDef> AvailableMaterials(WorldSimulation sim, ItemKind kind)
        {
            var list = new List<ItemDef>();
            foreach (var def in ItemCatalog.Items)
            {
                if (def.Kind == kind && sim.Warehouse.Count(def.Id) > 0)
                {
                    list.Add(def);
                }
            }

            return list;
        }

        private void DrawSelector(string label, List<ItemDef> options, ref int index)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(50));
            if (options.Count == 0)
            {
                GUILayout.Label("（仓库无存货）");
            }
            else
            {
                ClampIndex(options.Count, ref index);
                var def = options[index];
                if (GUILayout.Button($"{def.Name} ×{Sim.Warehouse.Count(def.Id)}", GUILayout.Width(160)))
                {
                    index = (index + 1) % options.Count;
                }
            }

            GUILayout.EndHorizontal();
        }

        private static void ClampIndex(int count, ref int index)
        {
            if (count <= 0)
            {
                index = 0;
            }
            else if (index >= count)
            {
                index = count - 1;
            }
        }

        private void ClampSelectors(int baseCount, int ingCount)
        {
            ClampIndex(baseCount, ref _baseIdx);
            ClampIndex(ingCount, ref _ingAIdx);
            ClampIndex(ingCount, ref _ingBIdx);
        }

        private static string BuildWineListText(WorldSimulation sim)
        {
            var parts = new List<string>();
            foreach (int wineId in sim.Brewing.UnlockedWines)
            {
                var def = ItemCatalog.Get(wineId);
                if (def != null)
                {
                    parts.Add($"{def.Name}(价{def.Price}/标签:{string.Join("/", def.Tags)})");
                }
            }

            return parts.Count > 0 ? string.Join("、", parts) : "（空）";
        }

        // ==================== NPC 查看 ====================

        private void DrawCharacter(WorldSimulation sim)
        {
            var rect = new Rect(Screen.width - 340, 70, 330, 380);
            BeginPanel(rect, "角色查看");
            GUILayout.BeginArea(new Rect(10, 24, rect.width - 20, rect.height - 34));
            if (_selected == null || !_selected.IsAlive)
            {
                GUILayout.Label("（角色已不在）");
            }
            else
            {
                var c = _selected;
                GUILayout.Label($"{_selected.Name}（#{c.Id} {c.Profession} {c.Personality}）");
                GUILayout.Label($"年龄 {c.Age}  好感 {c.FavorToPlayer}/100  社交 {CharacterScores.Social(sim.Population, c)}");
                GUILayout.Label($"生命 {c.Hp}/{c.MaxHp}  力量 {c.Str}  智力 {c.Int}  防御 {c.Def}");
                GUILayout.Label($"金币 {c.Gold}  体力 {c.Stamina}  精神 {c.Spirit}  健康分 {c.HealthScore}");
                GUILayout.Label($"荣誉 {c.HonorScore}  信息 {c.InfoScore}  战斗分 {CharacterScores.Combat(c)}");
                GUILayout.Label($"位置 {c.Position}" + (c.MoveTarget.HasValue ? $" → {c.MoveTarget.Value}" : string.Empty));
                GUILayout.Label(c.ActionLockDays > 0 ? $"受伤休养中（剩 {c.ActionLockDays} 天）" : "行动正常");
            }

            if (GUILayout.Button("关闭"))
            {
                ClosePanels();
            }

            GUILayout.EndArea();
            EndPanel();
        }

        // ==================== 悬赏栏（怪物情报占位，悬赏系统缺失） ====================

        private void DrawBoard(WorldSimulation sim)
        {
            var rect = new Rect(10, 70, 360, 300);
            BeginPanel(rect, "悬赏布告栏");
            GUILayout.BeginArea(new Rect(10, 24, rect.width - 20, rect.height - 34));
            if (sim.Monsters.Monsters.Count == 0)
            {
                GUILayout.Label("（暂无可接取的悬赏——悬赏系统待落地）");
            }

            foreach (var m in sim.Monsters.Monsters)
            {
                if (!m.IsAlive)
                {
                    continue;
                }

                var map = sim.Map.GetMap(m.Position.MapId);
                string where = map != null ? map.Name : m.Position.MapId.ToString();
                GUILayout.Label($"{m.Def.Name}（{where}） HP {m.Hp}/{m.Def.Hp} 饱食 {m.Satiety} {(m.IsHunting ? "狩猎中!" : "潜伏")}");
            }

            if (GUILayout.Button("关闭"))
            {
                ClosePanels();
            }

            GUILayout.EndArea();
            EndPanel();
        }

        // ==================== 结算分栏 / 游戏结束 ====================

        private void OnSettlementReady(SettlementReport report)
        {
            _lastReport = report;
            _reportUntil = Time.realtimeSinceStartup + 5f; // 经营案：保留 5 秒自动收起，期间不可收起。
        }

        private void OnGameOver()
        {
            _gameOver = true;
            GameModule.World.SetPaused(true);
        }

        private void DrawSettlement(WorldSimulation sim)
        {
            if (_lastReport == null || Time.realtimeSinceStartup > _reportUntil)
            {
                return;
            }

            var rect = new Rect(Screen.width / 2f - 180, Screen.height - 190, 360, 150);
            BeginPanel(rect, $"第 {_lastReport.DayOverall} 天结算");
            GUILayout.BeginArea(new Rect(10, 24, rect.width - 20, rect.height - 34));
            GUILayout.Label($"收入：酒水 {_lastReport.Income} + 悬赏 {_lastReport.BountyReward}");
            GUILayout.Label($"支出：酒保工资 {_lastReport.Wage}、营业成本 {_lastReport.Cost}");
            GUILayout.Label($"余额：{_lastReport.BalanceAfter}");
            GUILayout.EndArea();
            EndPanel();
        }

        private void DrawGameOver(WorldSimulation sim)
        {
            var rect = new Rect(0, 0, Screen.width, Screen.height);
            _panelRects.Add(rect);
            GUI.Box(rect, GUIContent.none);
            var center = CenterRect(420, 160);
            GUI.Box(center, "游戏结束");
            GUILayout.BeginArea(new Rect(center.x + 20, center.y + 40, center.width - 40, center.height - 50));
            GUILayout.Label($"酒馆无法支付工资与成本，经营失败。");
            GUILayout.Label($"总流水：{sim.Tavern.TotalRevenue}（评分口径待结算系统）");
            GUILayout.EndArea();
        }

        // ==================== 工具 ====================

        private static Rect CenterRect(float width, float height)
        {
            return new Rect(Screen.width / 2f - width / 2f, Screen.height / 2f - height / 2f, width, height);
        }

        private void BeginPanel(Rect rect, string title)
        {
            GUI.Box(rect, title);
            _panelRects.Add(rect);
        }

        private void EndPanel()
        {
        }
    }
}
