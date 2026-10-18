using System.Collections.Generic;
using GameLogic.World;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 酒馆主场景引导：以 sence_base_01 整图插画（1960×1080）为场景底景，正交相机正视；
    /// 9×9 逻辑网格继续驱动模拟，网格坐标经「双线形映射 + 关键格点像素标定」换算到画面位置
    /// （任务 20261016-main-ui-scene-rework：替换方块素模场景）。
    /// 每帧驱动 WorldModule.Tick（1 真实秒 = 1 游戏分钟，五档加速），失去焦点自动暂停。
    /// </summary>
    public sealed class WorldSceneBootstrap : SingletonBehaviour<WorldSceneBootstrap>
    {
        private readonly Dictionary<int, CharacterView> _views = new Dictionary<int, CharacterView>();

        /// <summary>城镇内怪物的卡图视图（M13 资源补全）：键为 <see cref="Monster"/> 实例（核心无怪物 id 字段，按引用跟踪）。</summary>
        private readonly Dictionary<Monster, SpriteRenderer> _monsterViews = new Dictionary<Monster, SpriteRenderer>();

        /// <summary>怪物卡图场景显示高度（世界单位；卡图 256px/ppu100 原高 2.56，收到与 Q 版角色相近的体量）。</summary>
        private const float MonsterViewHeight = 1.1f;
        private Camera _camera;
        private WorldHud _hud;
        private SpriteRenderer[] _bartenderParts;

        // ==================== 画面标定（对照 Resources/ui01/sence/sence_base_01.png） ====================

        /// <summary>底图设计分辨率（像素），sprite ppu=100。</summary>
        private const float DesignWidth = 1960f;
        private const float DesignHeight = 1080f;
        private const float PixelsPerUnit = 100f;

        /// <summary>底图世界尺寸（单位）。</summary>
        private const float WorldWidth = DesignWidth / PixelsPerUnit;   // 19.6
        private const float WorldHeight = DesignHeight / PixelsPerUnit; // 10.8

        /// <summary>
        /// 网格 → 像素的双线形映射四角（插图视角：网格 x=0 在画面右侧厨房一端，x=8 在左侧悬赏板一端；
        /// y=0 是靠墙里侧，y=8 是近镜头前排）。四角取地板可见范围的近似边界。
        /// </summary>
        private static readonly Vector2 QuadA = new Vector2(1650f, 280f);  // (0,0)
        private static readonly Vector2 QuadB = new Vector2(560f, 330f);   // (8,0)
        private static readonly Vector2 QuadC = new Vector2(1900f, 1060f); // (0,8)
        private static readonly Vector2 QuadD = new Vector2(250f, 1060f);  // (8,8)

        /// <summary>纵深伽马：插画透视把后半场压缩到画面上部，v 取 v^gamma 后再插值。</summary>
        private const float PerspectiveGamma = 1.7f;

        /// <summary>
        /// 关键格点的像素覆盖（插图不是严格等距网格，交互点按图手工标定；键为网格 x,y）。
        /// 吧台/悬赏板/厨房锚点由摆件切图模板匹配得出（sence_item_01/04 等），桌子对齐可见桌位。
        /// </summary>
        private static readonly Dictionary<long, Vector2> PixelOverrides = new Dictionary<long, Vector2>
        {
            [Key(6, 4)] = new Vector2(560f, 460f),   // 悬赏布告栏前
            [Key(5, 4)] = new Vector2(1150f, 620f),  // 吧台前（客人点单位）
            [Key(4, 4)] = new Vector2(1380f, 430f),  // 吧台主体（阻挡格，仅视觉锚点）
            [Key(2, 4)] = new Vector2(1820f, 600f),  // 厨房料理台前（画面最右侧绿桌台）
            [Key(1, 2)] = new Vector2(781f, 590f),   // 圆桌（左）
            [Key(3, 2)] = new Vector2(1023f, 850f),  // 长桌（中）
            [Key(5, 2)] = new Vector2(1573f, 980f),  // 圆桌（右下）
            [Key(1, 6)] = new Vector2(1150f, 560f),  // 吧台凳位 1
            [Key(3, 6)] = new Vector2(1330f, 600f),  // 吧台凳位 2
            [Key(5, 6)] = new Vector2(1510f, 620f),  // 吧台凳位 3
        };

        private static long Key(int x, int y)
        {
            return ((long)x << 32) | (uint)y;
        }

        /// <summary>
        /// M13：当前订阅整点事件的世界实例。<see cref="WorldModule.Restart"/> 会先 Unhook 旧实例再 StartNewGame，
        /// <c>GameModule.World.Sim</c> 因此指向<b>新实例</b>；本类若不换绑，<c>HourChanged</c>（酒保着色）
        /// 就一直挂在已作废的旧实例上（重开后酒保不再变色）。换绑由 <see cref="EnsureSimHooked"/> 每帧检出。
        /// </summary>
        private WorldSimulation _hookedSim;

        public WorldSimulation Sim => GameModule.World.Sim;
        public Camera SceneCamera => _camera;

        /// <summary>OnGUI 残留面板宿主（厨房/悬赏板/对话/交易/查看/破产遮罩）；顶栏与气泡在 <see cref="MainHudUI"/>。</summary>
        public WorldHud Hud => _hud;
        public IReadOnlyDictionary<int, CharacterView> Views => _views;

        /// <summary>
        /// 网格坐标 → 像素坐标（底图 1960×1080，原点左上，y 向下）：
        /// 优先取 <see cref="PixelOverrides"/> 手工标定，否则双线形映射（纵深伽马校正）。
        /// </summary>
        public static Vector2 CellToPixel(GridPos pos)
        {
            if (PixelOverrides.TryGetValue(Key(pos.X, pos.Y), out var px))
            {
                return px;
            }

            float u = Mathf.Clamp01(pos.X / 8f);
            float v = Mathf.Pow(Mathf.Clamp01(pos.Y / 8f), PerspectiveGamma);
            Vector2 back = Vector2.Lerp(QuadA, QuadB, u);
            Vector2 front = Vector2.Lerp(QuadC, QuadD, u);
            return Vector2.Lerp(back, front, v);
        }

        /// <summary>像素坐标 → 世界坐标（底图中心为原点，y 向上；z 按纵深排序：越靠下越贴近镜头）。</summary>
        public static Vector3 PixelToWorld(Vector2 pixel)
        {
            float x = (pixel.x - DesignWidth * 0.5f) / PixelsPerUnit;
            float y = (DesignHeight * 0.5f - pixel.y) / PixelsPerUnit;
            return new Vector3(x, y, DepthFromWorldY(y));
        }

        /// <summary>像素 y（世界 y）→ z：画面下方（世界 y 小）的物体更靠近相机，压住上方物体。</summary>
        private static float DepthFromWorldY(float worldY)
        {
            return worldY * 0.1f - 1.0f;
        }

        /// <summary>
        /// 网格坐标 → 世界坐标（新视觉：映射到底图平面 z≈-1 一层，height 参数保留兼容、不再使用）。
        /// </summary>
        public static Vector3 CellToWorld(GridPos pos, float height = 0f)
        {
            return PixelToWorld(CellToPixel(pos));
        }

        protected override void OnLoad()
        {
            BuildScene();
            _hud = new WorldHud(this);
            AttachSim(Sim);

            // 预热角色染色贴图（walk/idle 全方向全帧），分帧摊派；
            // 否则首个整点全员切 walk 时几十次 Bake 挤在一帧，表现为「第一次移动卡顿」。
            StartCoroutine(PrewarmCharacterSprites());

            // 顶栏 HUD（uGUI）：暂停/档位/日期/钟摆/金币/结算分栏/NPC 交互气泡。
            GameModule.UI.ShowUIAsync<MainHudUI>();

            Log.Info("WorldSceneBootstrap 场景构建完成");
        }

        protected override void OnDestroy()
        {
            // 用缓存实例解绑：销毁时 Sim 可能已被 WorldModule 换掉或清空（OnRelease），否则会漏解绑。
            if (_hookedSim != null)
            {
                _hookedSim.HourChanged -= OnHourChanged;
                _hookedSim = null;
            }

            GameModule.UI.CloseUI<MainHudUI>();

            base.OnDestroy();
        }

        /// <summary>
        /// M13：把整点事件绑到指定的世界实例（先解绑旧实例再绑新的）。
        /// <see cref="WorldModule.Restart"/> 换了新实例后由 <see cref="EnsureSimHooked"/> 每帧检出并重绑。
        /// 换实例同时清空按旧实例缓存的展示层状态（角色视图 / HUD 选中面板），避免旧对局的 NPC 残影与悬空引用。
        /// </summary>
        private void AttachSim(WorldSimulation sim)
        {
            if (_hookedSim != null)
            {
                _hookedSim.HourChanged -= OnHourChanged;
            }

            _hookedSim = sim;

            // 角色视图按 CharacterData 建立，新对局的人口是全新对象，旧视图不可复用：整体销毁并清表。
            foreach (var pair in _views)
            {
                if (pair.Value != null)
                {
                    Object.Destroy(pair.Value.gameObject);
                }
            }

            _views.Clear();

            // 怪物视图同样按旧对局实例缓存，换实例整体销毁清表。
            foreach (var pair in _monsterViews)
            {
                if (pair.Value != null)
                {
                    Object.Destroy(pair.Value.gameObject);
                }
            }

            _monsterViews.Clear();

            // HUD 可能还握着上一局选中的 CharacterData，先关面板，避免面板残留已销毁视图的数据。
            _hud?.ClosePanels();

            // 酒保回到待命色：上一局的「制作中」属于旧实例的 Tavern 状态。
            SetBartenderTint(Color.white);

            if (sim != null)
            {
                sim.HourChanged += OnHourChanged;
            }
        }

        /// <summary>M13：每帧检出 <see cref="WorldModule.Restart"/> 换过世界实例并重新绑定事件。</summary>
        private void EnsureSimHooked()
        {
            if (!ReferenceEquals(_hookedSim, Sim))
            {
                AttachSim(Sim);
            }
        }

        private void Update()
        {
            // M13：重开对局后世界实例会换新，先确认事件与视图缓存仍绑在当前实例上。
            EnsureSimHooked();

            GameModule.World.Tick(Time.deltaTime);
            FitCamera(); // 窗口拉伸时保持整图完整可见
            SyncCharacterViews();
            SyncMonsterViews();
            HandleWorldClick();
        }

        /// <summary>
        /// 等模拟就绪（<c>WorldModule.EnsureConfigReady</c> 已跑、配色表可取色）后再启动分帧预热。
        /// </summary>
        private System.Collections.IEnumerator PrewarmCharacterSprites()
        {
            while (Sim == null || !Sim.IsRunning)
            {
                yield return null;
            }

            yield return SpriteCharacterLibrary.PrewarmCache();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // R1：失去酒馆焦点时自动暂停。
            GameModule.World.HandleApplicationFocus(hasFocus);
        }

        private void OnGUI()
        {
            _hud?.Draw();
        }

        // ==================== 场景构建 ====================

        private void BuildScene()
        {
            // 禁用演示场景既有相机，避免双相机渲染冲突。
            // 注意不能误伤 UICamera：ScreenSpaceCamera 画布的相机被禁用会导致全部 uGUI 窗口不渲染。
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                cam.enabled = false;
            }
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera != null)
                {
                    canvas.worldCamera.enabled = true; // UI 相机恢复渲染
                }
            }

            var camGo = new GameObject("WorldCamera");
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.13f, 0.16f, 0.10f); // 黑边与藤蔓框同色系
            // 正视底图：相机沿 +Z 方向看 z=0 平面，contain 适配（窄屏上下留边，宽屏左右留边）。
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            camGo.transform.rotation = Quaternion.identity;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 50f;
            FitCamera();

            // 场景底景：sence_base_01 整图插画（吧台/桌椅/悬赏板/厨房已全部烘焙在底图内，
            // sence_item_01~10 为同位置摆件切图，预留给悬停高亮等交互表现，本期不重复摆放）。
            var backdropGo = new GameObject("SceneBackdrop");
            var backdrop = backdropGo.AddComponent<SpriteRenderer>();
            backdrop.sprite = Resources.Load<Sprite>("ui01/sence/sence_base_01");
            if (backdrop.sprite == null)
            {
                Log.Error("WorldSceneBootstrap: 缺少场景底图 Resources/ui01/sence/sence_base_01");
            }
            else
            {
                // 用 sprite 实际包围盒居中，不假设 pivot（导入 pivot 为中心而非左下角，
                // 直接 -w/2 平移会把底图错到左下象限——实机截图证实）。
                var b = backdrop.sprite.bounds;
                backdropGo.transform.position = new Vector3(-b.center.x, -b.center.y, 0f);
            }

            backdrop.sortingOrder = -100;

            // uGUI 交互前提：演示场景若没带 EventSystem 就补一个。
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            // 光照已移除：底图与角色（body/face/hair 分层贴图）全走 Sprites/Default 不受光。

            // 酒保贴图形象（seed 7 → 03 号发型 + body 配色 02，与客人 01 配色区分）：站在吧台内弧空地。
            var bartenderGo = SpriteCharacterBuilder.Build("Bartender", 7);
            bartenderGo.transform.position = PixelToWorld(new Vector2(1580f, 400f)); // 吧台内弧空地（紫晶地面区），pivot=脚底
            _bartenderParts = bartenderGo.GetComponentsInChildren<SpriteRenderer>();
            SetBartenderTint(Color.white);
        }

        /// <summary>正交相机 contain 适配 1960×1080 底图（任何宽高比下整图完整可见）。</summary>
        private void FitCamera()
        {
            if (_camera == null)
            {
                return;
            }

            float halfH = WorldHeight * 0.5f;
            float halfW = WorldWidth * 0.5f;
            _camera.orthographicSize = Mathf.Max(halfH, halfW / _camera.aspect);
        }

        // 遮挡说明：新视觉为整图插画，遮挡关系已由美术烘焙进底图；NPC 经 z 纵深排序
        // （DepthFromWorldY：画面下方更靠近镜头），不再需要 3D 射线虚化。

        // ==================== NPC 视图同步（轮询人口，核心暂无“角色加入”事件） ====================

        private void SyncCharacterViews()
        {
            if (Sim == null || !Sim.IsRunning)
            {
                return;
            }

            SyncList(Sim.Population.Residents);
            SyncList(Sim.Population.Visitors);

            var dead = new List<int>();
            foreach (var pair in _views)
            {
                if (pair.Value == null || pair.Value.Data == null || !pair.Value.Data.IsAlive)
                {
                    dead.Add(pair.Key);
                }
            }

            foreach (int id in dead)
            {
                if (_views.TryGetValue(id, out var view) && view != null)
                {
                    Object.Destroy(view.gameObject);
                }

                _views.Remove(id);
                MainHudUI.Current?.OnCharacterRemoved(id);
                if (_hud != null && _hud.SelectedCharacter != null && _hud.SelectedCharacter.Id == id)
                {
                    _hud.ClosePanels();
                }
            }
        }

        private void SyncList(IReadOnlyList<CharacterData> characters)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                var c = characters[i];
                if (c.IsAlive && c.Position.MapId == WorldMapIds.Town && !_views.ContainsKey(c.Id))
                {
                    _views[c.Id] = CharacterView.Create(c);
                }
            }
        }

        // ==================== 怪物视图同步（M13 资源补全） ====================

        /// <summary>
        /// 城镇图内的存活怪物显示对应卡图（monster 表 group_resource_name 优先，单只图兜底）；
        /// 城外/死亡怪物不显示。怪物迁移/狩猎移动由核心按小时改格，本处每帧把视图贴到格位。
        /// </summary>
        private void SyncMonsterViews()
        {
            if (Sim == null || !Sim.IsRunning || Sim.Monsters == null)
            {
                return;
            }

            var monsters = Sim.Monsters.Monsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                var m = monsters[i];
                if (m.IsAlive && m.Position.MapId == WorldMapIds.Town && !_monsterViews.ContainsKey(m))
                {
                    var view = CreateMonsterView(m);
                    if (view != null)
                    {
                        _monsterViews[m] = view;
                    }
                }
            }

            List<Monster> dead = null;
            foreach (var pair in _monsterViews)
            {
                var m = pair.Key;
                if (m == null || !m.IsAlive || m.Position.MapId != WorldMapIds.Town)
                {
                    if (dead == null)
                    {
                        dead = new List<Monster>();
                    }

                    dead.Add(m);
                    continue;
                }

                if (pair.Value != null)
                {
                    // 卡图 pivot 为中心：落点上抬半高；z 比同格角色略靠前。
                    var pos = CellToWorld(m.Position);
                    float halfH = pair.Value.bounds.extents.y;
                    pair.Value.transform.position = new Vector3(pos.x, pos.y + halfH, pos.z - 0.05f);
                }
            }

            if (dead != null)
            {
                foreach (var m in dead)
                {
                    if (_monsterViews.TryGetValue(m, out var view) && view != null)
                    {
                        Object.Destroy(view.gameObject);
                    }

                    _monsterViews.Remove(m);
                }
            }
        }

        /// <summary>建怪物卡图视图；配置缺资源名或贴图未交付时返回 null（不显示，不虚构占位）。
        /// 贴图口径：地图令牌 = 一只怪物，用<strong>单只图</strong> ResourceName（TbMonster resource_name 列）；
        /// 群组图 GroupResourceName（group_resource_name 列）由悬赏板图标使用（BountyPanel），这里只作兜底。</summary>
        private static SpriteRenderer CreateMonsterView(Monster m)
        {
            if (m == null || m.Def == null)
            {
                return null;
            }

            string resName = null;
            var catalog = GameConfigCatalog.Instance;
            var row = catalog != null ? catalog.GetMonsterRow(m.Def.Id) : null;
            if (row != null)
            {
                resName = !string.IsNullOrEmpty(row.ResourceName) ? row.ResourceName : row.GroupResourceName;
            }

            if (string.IsNullOrEmpty(resName))
            {
                return null;
            }

            var sprite = Resources.Load<Sprite>("ui01/monsters/" + resName);
            if (sprite == null)
            {
                Log.Warning("WorldSceneBootstrap: 怪物卡图缺失 Resources/ui01/monsters/" + resName);
                return null;
            }

            var go = new GameObject("Monster_" + m.Def.Name);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            float rawH = sprite.bounds.size.y;
            if (rawH > 0.01f)
            {
                go.transform.localScale = Vector3.one * (MonsterViewHeight / rawH);
            }

            return sr;
        }

        // ==================== 整点表现 ====================

        /// <summary>
        /// 整点只做<b>表现层</b>刷新：酒保按「待命 / 制作中」着色。
        /// <b>客源由 CharacterBehaviorSystem 驱动（M11）</b>：谁走向酒馆、到店点哪杯酒、什么时候回城待机，
        /// 全部由 <c>CharacterBehaviorSystem.TickHour</c> 按权重决策产出（接线见 <c>WorldSimulation.StartNewGame</c>），
        /// 因此本文件不再保留任何占位的点单冷却 / 随机落点 / 手动下达移动目标逻辑。
        /// </summary>
        /// <summary>酒保分层贴图整体染色（SpriteRenderer.color 正片叠底；白色=原色）。</summary>
        private void SetBartenderTint(Color color)
        {
            if (_bartenderParts == null)
            {
                return;
            }

            foreach (var part in _bartenderParts)
            {
                if (part != null)
                {
                    part.color = color;
                }
            }
        }

        private void OnHourChanged(int dayOverall, int hour)
        {
            if (Sim == null || Sim.Tavern.IsGameOver)
            {
                return;
            }

            // 酒保两态染色：待命（原色）/制作中（暖色调染）。
            SetBartenderTint(Sim.Tavern.IsMaking ? new Color(1f, 0.8f, 0.55f) : Color.white);
        }

        // ==================== 世界点击（正交正视：屏幕点 → 底图像素 → 交互锚点） ====================

        /// <summary>悬赏布告栏点击锚点（底图像素，由 sence_item_01 模板匹配定位）与半径。</summary>
        private static readonly Vector2 BoardAnchorPixel = new Vector2(549f, 300f);
        private const float BoardPickRadius = 170f;

        /// <summary>厨房料理台点击锚点（底图右侧绿桌台区域）与半径。</summary>
        private static readonly Vector2 KitchenAnchorPixel = new Vector2(1830f, 480f);
        private const float KitchenPickRadius = 190f;

        /// <summary>世界坐标 → 底图像素（<see cref="PixelToWorld"/> 的逆运算）。</summary>
        public static Vector2 WorldToPixel(Vector3 world)
        {
            return new Vector2(world.x * PixelsPerUnit + DesignWidth * 0.5f, DesignHeight * 0.5f - world.y * PixelsPerUnit);
        }

        /// <summary>点到线段的距离（NPC 纵向命中判定用）。</summary>
        private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        private void HandleWorldClick()
        {
            if (!Input.GetMouseButtonDown(0) || _camera == null || Sim == null)
            {
                return;
            }

            Vector2 mouse = Input.mousePosition;

            // uGUI（顶栏/气泡/结算分栏）拦截：指针落在 UI 上不穿透到世界。
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && es.IsPointerOverGameObject())
            {
                Log.Info("[ClickProbe] 被 uGUI 拦截（IsPointerOverGameObject=true）");
                return;
            }

            if (_hud != null && _hud.IsPointerOverPanel(mouse))
            {
                Log.Info("[ClickProbe] 被 OnGUI 面板拦截");
                return; // 点击在 OnGUI 面板上，不穿透到世界。
            }

            // 优先拾取 NPC：以「脚底→头顶」线段做命中（角色约 110px 高、pivot 在脚底，
            // 原来只对脚底点取 60px 半径，点上半身经常落空——实证「点击范围太小」）。
            CharacterView picked = null;
            float best = 70f; // 横向命中半径（屏幕 px）
            foreach (var pair in _views)
            {
                Vector3 spFeet = _camera.WorldToScreenPoint(pair.Value.transform.position);
                if (spFeet.z <= 0f)
                {
                    continue;
                }

                Vector3 spHead = _camera.WorldToScreenPoint(pair.Value.transform.position + Vector3.up * 1.1f);
                float dist = DistancePointToSegment(mouse, new Vector2(spFeet.x, spFeet.y), new Vector2(spHead.x, spHead.y));
                if (dist < best)
                {
                    best = dist;
                    picked = pair.Value;
                }
            }

            if (picked != null)
            {
                // 角色系统 3.2：点 NPC 弹交互气泡（交流/交易/查看），气泡由 MainHudUI 承载。
                Log.Info($"[ClickProbe] 点中 NPC {picked.Data.Name}（MainHudUI.Current={(MainHudUI.Current != null ? "在" : "为null")}）");
                if (MainHudUI.Current != null)
                {
                    MainHudUI.Current.ShowBubble(picked.Data);
                }

                return;
            }

            Log.Info("[ClickProbe] 未点中任何 NPC（60px 内无角色）");

            // 正交相机正视 z=0 平面：屏幕点直接反投影到底图平面，再换算到底图像素。
            Vector3 world = _camera.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, -_camera.transform.position.z));
            Vector2 pixel = WorldToPixel(world);

            if (Vector2.Distance(pixel, KitchenAnchorPixel) <= KitchenPickRadius)
            {
                _hud.OpenKitchenChoice();
            }
            else if (Vector2.Distance(pixel, BoardAnchorPixel) <= BoardPickRadius)
            {
                _hud.OpenBoardPanel();
            }
            else
            {
                _hud.ClosePanels(); // 点击空白区域关闭交互（角色系统 3.1）。
            }
        }
    }
}
