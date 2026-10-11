using System.Collections.Generic;
using GameLogic.World;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 酒馆主场景引导（可玩原型）：运行时代码构建 9×9 城镇网格、酒馆/厨房/悬赏栏、酒保与 NPC 视图，
    /// 每帧驱动 WorldModule.Tick（1 真实秒 = 1 游戏分钟，五档加速），失去焦点自动暂停。
    /// 正式场景资产化与 TEngine UIWindow 化待 Unity CLI 可用后重做（任务 20261011-main-scene-bootstrap）。
    /// </summary>
    public sealed class WorldSceneBootstrap : SingletonBehaviour<WorldSceneBootstrap>
    {
        private readonly Dictionary<int, CharacterView> _views = new Dictionary<int, CharacterView>();
        private readonly Dictionary<int, int> _orderCooldowns = new Dictionary<int, int>();
        private Camera _camera;
        private WorldHud _hud;
        private Renderer _bartenderRenderer;

        /// <summary>可被虚化的建筑渲染器（遮挡 NPC 时半透明）。</summary>
        private readonly HashSet<Renderer> _occludables = new HashSet<Renderer>();

        /// <summary>当前处于虚化状态的渲染器。</summary>
        private readonly HashSet<Renderer> _faded = new HashSet<Renderer>();

        /// <summary>虚化后的不透明度。</summary>
        private const float FadeAlpha = 0.25f;

        public WorldSimulation Sim => GameModule.World.Sim;
        public Camera SceneCamera => _camera;
        public IReadOnlyDictionary<int, CharacterView> Views => _views;

        /// <summary>网格坐标 → 世界坐标（x→X，y→Z，平面 y=0）。</summary>
        public static Vector3 CellToWorld(GridPos pos, float height = 0f)
        {
            return new Vector3(pos.X, height, pos.Y);
        }

        protected override void OnLoad()
        {
            BuildScene();
            _hud = new WorldHud(this);
            if (Sim != null)
            {
                Sim.HourChanged += OnHourChanged;
            }

            Log.Info("WorldSceneBootstrap 场景构建完成");
        }

        protected override void OnDestroy()
        {
            if (Sim != null)
            {
                Sim.HourChanged -= OnHourChanged;
            }

            base.OnDestroy();
        }

        private void Update()
        {
            GameModule.World.Tick(Time.deltaTime);
            SyncCharacterViews();
            UpdateOcclusion();
            HandleWorldClick();
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
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                cam.enabled = false;
            }

            var camGo = new GameObject("WorldCamera");
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.16f, 0.13f, 0.1f); // 羊皮纸底色
            // 2.5D 对角俯视（立项：2.5D 中世纪羊皮纸风；经营案主界面：45° 等距俯视）。
            // 正交 + 俯仰 45° + 偏航 45°，网格呈菱形对角构图；原型用 3D 素模近似，正式版为 2.5D 精灵。
            camGo.transform.rotation = Quaternion.Euler(45f, 45f, 0f);
            camGo.transform.position = new Vector3(4f, 0f, 4f) - camGo.transform.forward * 16f;
            // 9×9 网格对角投影高约 9（俯仰 45° 下对角 12.7×sin45°），留边取 5.5。
            _camera.orthographicSize = 5.5f;

            var lightGo = new GameObject("WorldLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.85f);
            lightGo.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.5f, 0.45f);

            // 9×9 地面格子。
            var map = Sim?.Map.GetMap(WorldMapIds.Town);
            int w = map != null ? GridMap.Width : 9;
            int h = map != null ? GridMap.Height : 9;
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.name = $"Tile_{x}_{y}";
                    tile.transform.position = new Vector3(x, -0.05f, y);
                    tile.transform.localScale = new Vector3(0.96f, 0.1f, 0.96f);
                    bool alt = (x + y) % 2 == 0;
                    tile.GetComponent<Renderer>().material.color = alt
                        ? new Color(0.76f, 0.68f, 0.5f)
                        : new Color(0.7f, 0.62f, 0.45f);
                }
            }

            // 建筑：酒馆 / 厨房 / 悬赏布告栏。
            CreateBuilding("Tavern", WorldMap.TavernPos, new Vector3(1.6f, 1.2f, 1.6f), new Color(0.45f, 0.28f, 0.15f));
            CreateBuilding("Kitchen", WorldMap.KitchenPos, new Vector3(0.8f, 0.7f, 0.8f), new Color(0.5f, 0.5f, 0.55f));
            CreateBuilding("BountyBoard", WorldMap.BountyBoardPos, new Vector3(0.7f, 0.9f, 0.15f), new Color(0.85f, 0.75f, 0.4f));

            // 6 张桌子：坐标以核心地图登记为准（阻挡移动与视觉一致）。
            var town = Sim?.Map.GetMap(WorldMapIds.Town);
            if (town != null)
            {
                foreach (var building in town.Buildings)
                {
                    if (building.Id.StartsWith("table"))
                    {
                        CreateBuilding("Table", building.Pos, new Vector3(0.5f, 0.35f, 0.5f), new Color(0.55f, 0.38f, 0.22f));
                    }
                }
            }

            // 酒保（吧台位，酒馆格内偏移）。
            var bartender = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bartender.name = "Bartender";
            bartender.transform.position = CellToWorld(WorldMap.TavernPos, 0.5f) + new Vector3(0.3f, 0f, 0.3f);
            bartender.transform.localScale = new Vector3(0.35f, 0.5f, 0.35f);
            _bartenderRenderer = bartender.GetComponent<Renderer>();
            _bartenderRenderer.material.color = new Color(0.9f, 0.85f, 0.7f);
        }

        private void CreateBuilding(string name, GridPos pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = CellToWorld(pos, scale.y / 2f);
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.material.color = color;
            _occludables.Add(renderer); // 建筑/桌子参与遮挡虚化
        }

        // ==================== 遮挡虚化（2.5D 对角俯视：NPC 在建筑后时建筑半透明） ====================

        private void UpdateOcclusion()
        {
            if (_camera == null)
            {
                return;
            }

            // 找出当前帧所有挡在 NPC 与相机之间的建筑。
            var occluding = new HashSet<Renderer>();
            Vector3 camPos = _camera.transform.position;
            foreach (var pair in _views)
            {
                var view = pair.Value;
                if (view == null || view.Data == null || !view.Data.IsAlive)
                {
                    continue;
                }

                Vector3 targetPos = view.transform.position;
                Vector3 dir = targetPos - camPos;
                float dist = dir.magnitude;
                foreach (var hit in Physics.RaycastAll(camPos, dir / dist, dist))
                {
                    var renderer = hit.collider.GetComponent<Renderer>();
                    if (renderer != null && _occludables.Contains(renderer))
                    {
                        occluding.Add(renderer);
                    }
                }
            }

            // 恢复不再遮挡的，虚化新遮挡的。
            foreach (var renderer in _faded)
            {
                if (!occluding.Contains(renderer) && renderer != null)
                {
                    SetFade(renderer, false);
                }
            }

            _faded.RemoveWhere(r => !occluding.Contains(r));
            foreach (var renderer in occluding)
            {
                if (_faded.Add(renderer))
                {
                    SetFade(renderer, true);
                }
            }
        }

        /// <summary>内置 Standard shader 的透明/不透明模式切换（运行时素模材质均为实例，可直接改）。</summary>
        private static void SetFade(Renderer renderer, bool fade)
        {
            var mat = renderer.material;
            var color = mat.color;
            if (fade)
            {
                mat.SetFloat("_Mode", 3f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
                color.a = FadeAlpha;
            }
            else
            {
                color.a = 1f;
                mat.SetFloat("_Mode", 0f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetInt("_ZWrite", 1);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = -1;
            }

            mat.color = color;
        }

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

        // ==================== 占位环境驱动（行为系统落地前让经营闭环可观察） ====================

        private void OnHourChanged(int dayOverall, int hour)
        {
            if (Sim == null || Sim.Tavern.IsGameOver)
            {
                return;
            }

            var residents = Sim.Population.Residents;
            for (int i = 0; i < residents.Count; i++)
            {
                var c = residents[i];
                if (_orderCooldowns.TryGetValue(c.Id, out int cd) && cd > 0)
                {
                    _orderCooldowns[c.Id] = cd - 1;
                }
            }

            // 空闲居民走向酒馆；到店随机点一杯后离开。
            foreach (var c in residents)
            {
                if (!c.IsAlive || c.ActionLockDays > 0 || c.MoveTarget.HasValue)
                {
                    continue;
                }

                // 建筑阻挡移动：客人在吧台位（酒馆相邻服务格）点单，随后离开。
                if (c.Position == WorldMap.TavernBarPos)
                {
                    if (!_orderCooldowns.TryGetValue(c.Id, out int cd) || cd <= 0)
                    {
                        if (Sim.Tavern.GuestOrderRandom(c, SimRandom) >= 0)
                        {
                            _orderCooldowns[c.Id] = 3;
                        }
                    }

                    Sim.SetMoveTarget(c, RandomFreeCell());
                    continue;
                }

                // 少量居民每小时动身，避免全镇同步移动。
                if (SimRandom.Next(0, 100) < 25)
                {
                    Sim.SetMoveTarget(c, WorldMap.TavernBarPos);
                }
            }

            // 酒保三态着色：待命（浅）/制作中（暖）。
            if (_bartenderRenderer != null)
            {
                _bartenderRenderer.material.color = Sim.Tavern.IsMaking
                    ? new Color(1f, 0.75f, 0.3f)
                    : new Color(0.9f, 0.85f, 0.7f);
            }
        }

        private IRandomSource _simRandom;
        private IRandomSource SimRandom => _simRandom ??= new SystemRandomSource(System.Environment.TickCount);

        private GridPos RandomFreeCell()
        {
            var map = Sim.Map.GetMap(WorldMapIds.Town);
            for (int i = 0; i < 10; i++)
            {
                var cell = new GridPos(WorldMapIds.Town, SimRandom.Next(0, GridMap.Width), SimRandom.Next(0, GridMap.Height));
                if (map == null || map.IsWalkable(cell.X, cell.Y))
                {
                    return cell;
                }
            }

            return WorldMap.TownCenter;
        }

        // ==================== 世界点击（射线-平面求交，不依赖 Physics） ====================

        private void HandleWorldClick()
        {
            if (!Input.GetMouseButtonDown(0) || _camera == null || Sim == null)
            {
                return;
            }

            Vector2 mouse = Input.mousePosition;
            if (_hud != null && _hud.IsPointerOverPanel(mouse))
            {
                return; // 点击在面板上，不穿透到世界。
            }

            // 优先拾取 NPC（屏幕距离 40px 内最近者）。
            CharacterView picked = null;
            float best = 40f;
            foreach (var pair in _views)
            {
                Vector3 sp = _camera.WorldToScreenPoint(pair.Value.transform.position);
                if (sp.z <= 0f)
                {
                    continue;
                }

                float dist = Vector2.Distance(mouse, new Vector2(sp.x, sp.y));
                if (dist < best)
                {
                    best = dist;
                    picked = pair.Value;
                }
            }

            if (picked != null)
            {
                _hud.OpenCharacterPanel(picked.Data);
                return;
            }

            // 格子拾取：射线与 y=0 平面求交。
            Ray ray = _camera.ScreenPointToRay(mouse);
            if (Mathf.Abs(ray.direction.y) < 1e-5f)
            {
                return;
            }

            float t = -ray.origin.y / ray.direction.y;
            Vector3 hit = ray.origin + ray.direction * t;
            var cell = new GridPos(WorldMapIds.Town, Mathf.RoundToInt(hit.x), Mathf.RoundToInt(hit.z));

            if (cell == WorldMap.KitchenPos)
            {
                _hud.OpenKitchenChoice();
            }
            else if (cell == WorldMap.BountyBoardPos)
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
