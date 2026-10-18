using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 角色外观（Q 版部件组编号 + 配色 id）：由角色 Id 确定性派生，跨对局/重开稳定，
    /// Q 版场景形象（<see cref="SpriteCharacterBuilder"/>）与立绘（<see cref="SpriteCharacterLibrary.GetPortrait"/>）共用。
    /// 分桶（seed % 10）：
    /// - 0：00 预染色套装（美术直出彩色主图，无 Mask 不染色；注意 idle 身体主图文件名带 m 后缀
    ///   —— character_c_idle_body_00m_{dir}_{frame}，配表组名同步登记为 character_c_idle_body_00m）；
    /// - 1-2：body_02 灰斗篷 + face_01 + hair 01/03（配色留白 = 原灰白稿，不虚构配色）；
    /// - 其余：body_01 + face_01 + hair 01/03，配色 01/02（配表目前仅 body_01 落地了 01/02 两组配色）。
    /// </summary>
    public readonly struct CharacterAppearance
    {
        /// <summary>身体部件组编号（"00"/"01"/"02"）。</summary>
        public readonly string Body;
        /// <summary>脸部部件组编号（"00"/"01"）。</summary>
        public readonly string Face;
        /// <summary>发型部件组编号（"00"/"01"/"03"）。</summary>
        public readonly string Hair;
        /// <summary>配色 id（CharacterPart.color_id，文本 "01"—"06"）；无配色部件组固定传 "01"（留白 = 原色）。</summary>
        public readonly string ColorId;

        public CharacterAppearance(string body, string face, string hair, string colorId)
        {
            Body = body;
            Face = face;
            Hair = hair;
            ColorId = colorId;
        }

        /// <summary>00 预染色套装（主图即彩色终稿，染色流程整体跳过；立绘 = character_r_idle_hair_00 整图）。</summary>
        public bool IsPreColored
        {
            get { return Body == "00"; }
        }

        /// <summary>按 seed 确定性派生外观（同一 seed 恒定同形象）。</summary>
        public static CharacterAppearance Derive(int seed)
        {
            string hair = (seed & 1) == 0 ? "01" : "03";
            switch (seed % 10)
            {
                case 0:
                    return new CharacterAppearance("00", "00", "00", "01");
                case 1:
                case 2:
                    return new CharacterAppearance("02", "01", hair, "01");
                default:
                    return new CharacterAppearance("01", "01", hair, (seed & 2) == 0 ? "01" : "02");
            }
        }
    }

    /// <summary>
    /// Q 版角色组装器，严格按《美术角色资源说明》+ CharacterPart 配表实现：
    /// - 命名：主图 <c>character_c_{动作}_{部位}_{编号}_{方向}_{帧}</c>，Mask 在部件编号后紧跟 m
    ///   （如 character_c_idle_body_01m_a_1）；方向 a/b/c/d，idle/sit 2 帧、walk 4 帧。
    /// - 主图为灰白明暗稿，颜色由 Mask 的 R/G/B 通道 × 配表 r_color/g_color/b_color 染色得到；
    ///   Q 版行本身不存配色，经 <c>counterpart_id</c> 找到同部位同编号立绘行取色
    ///   （walk/sit 复用 idle 组配色）；配表留白的通道按原色（白=不染色）处理，不虚构配色。
    /// - 染色在运行时烘焙成贴图并缓存（同部件组+配色+方向+帧共享一张 Sprite）。
    /// </summary>
    public static class SpriteCharacterBuilder
    {
        /// <summary>Resources 下的部件图目录。</summary>
        public const string ResourceDir = "ui01/characters";

        /// <summary>256px × 0.43 ≈ 110px 高，对齐参考图 Q 版角色体量（ppu=100）。</summary>
        public const float DefaultScale = 0.43f;

        /// <summary>
        /// 部件层序（<b>由后到前</b>）：face（头/颈）→ body（衣，衣领压住脖颈）→ hair（发，刘海压住额头）。
        /// Q 版三层子物体与立绘 alpha 合成共用这一份顺序，避免两处口径漂移。
        /// </summary>
        public static readonly string[] LayerOrderFromBack = { "face", "body", "hair" };

        /// <summary>取部件在 <see cref="CharacterAppearance"/> 中对应的编号（层序数组与外观字段解耦的唯一入口）。</summary>
        internal static string StyleIdOf(CharacterAppearance appearance, string part)
        {
            switch (part)
            {
                case "face": return appearance.Face;
                case "body": return appearance.Body;
                case "hair": return appearance.Hair;
                default: return null;
            }
        }

        /// <summary>
        /// 组装一个三层贴图角色（face/body/hair 子物体，微 z 偏移保证层序，见 <see cref="LayerOrderFromBack"/>），返回根物体（pivot=脚底）。
        /// 外观由 <see cref="CharacterAppearance.Derive"/> 按 seed 确定性派生；
        /// 各层默认显示 idle / 前(a) / 第 1 帧（方向口径：a=前 b=右 c=左 d=后）。
        /// </summary>
        public static GameObject Build(string name, int seed)
        {
            return Build(name, CharacterAppearance.Derive(seed));
        }

        /// <summary>按指定外观组装三层贴图角色（调用方已持有外观对象时走这里）。</summary>
        public static GameObject Build(string name, CharacterAppearance appearance)
        {
            string colorId = appearance.ColorId;

            var root = new GameObject(name);
            root.transform.localScale = Vector3.one * DefaultScale;

            // 相机在 z=-10 沿 +Z 看：z 越小越靠前，故按「由后到前」顺序逐层前移（-0.01/层），
            // 既落实 face→body→hair 层序，也避免同 sortingOrder 下的闪面。
            string[] parts = LayerOrderFromBack;
            for (int i = 0; i < parts.Length; i++)
            {
                var child = new GameObject(parts[i]);
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = new Vector3(0f, 0f, -0.01f * (i + 1));
                child.AddComponent<SpriteRenderer>();
                child.AddComponent<CharacterPartLayer>().Init(parts[i], StyleIdOf(appearance, parts[i]), colorId);
            }

            return root;
        }
    }

    /// <summary>
    /// 单部件层：按「姿态 + 方向 + 帧号」切换本层 Sprite（经 <see cref="SpriteCharacterLibrary"/> 染色烘焙）。
    /// </summary>
    public sealed class CharacterPartLayer : MonoBehaviour
    {
        private string _part;
        private string _styleId;
        private string _colorId;
        private SpriteRenderer _renderer;
        private string _currentKey;

        public void Init(string part, string styleId, string colorId)
        {
            _part = part;
            _styleId = styleId;
            _colorId = colorId;
            _renderer = GetComponent<SpriteRenderer>();
            SetFrame("idle", "a", 1);
        }

        /// <summary>切换姿态帧（pose: idle/sit/walk；dir: a=前 b=右 c=左 d=后；frame 从 1 开始）。</summary>
        public void SetFrame(string pose, string dir, int frame)
        {
            string key = $"{pose}/{_part}/{_styleId}/{_colorId}/{dir}/{frame}";
            if (key == _currentKey)
            {
                return;
            }

            var sprite = SpriteCharacterLibrary.Get(pose, _part, _styleId, _colorId, dir, frame);
            if (sprite != null)
            {
                _renderer.sprite = sprite;
                _currentKey = key;
            }
        }
    }

    /// <summary>
    /// 部件贴图库：加载主图 + Mask，按 CharacterPart 配表染色并烘焙成 Sprite（静态缓存）。
    /// 配表未提供的部件/配色回退原灰白图；缺帧回退第 1 帧，缺方向回退前(a)。
    /// </summary>
    public static class SpriteCharacterLibrary
    {
        private sealed class Palette
        {
            public Color R = Color.white, G = Color.white, B = Color.white;
            public float EmR, EmG, EmB;
        }

        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Palette> PaletteCache = new Dictionary<string, Palette>();
        private static readonly Dictionary<string, Texture2D> TexCache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, string> CounterpartCache = new Dictionary<string, string>();

        /// <summary>
        /// 预热染色缓存：walk/idle 全方向全帧提前烘出（协程分帧摊派，每帧烘 <paramref name="bakesPerStep"/> 张）。
        /// 否则首个整点 20+ 角色同帧切 walk，几十次 <see cref="Bake"/>（Resources.Load + 逐像素染色 + Apply）
        /// 挤在同一帧造成明显卡顿——实证「第一次移动会卡」即此。缺失贴图经 null 缓存跳过，无副作用。
        /// </summary>
        public static IEnumerator PrewarmCache(int bakesPerStep = 3)
        {
            string[] dirs = { "a", "b", "c", "d" };
            // 形象由 Id 派生（CharacterAppearance.Derive）：body ∈ {00/01/02}、face ∈ {00/01}、hair ∈ {00/01/03}；
            // 仅 body_01 落地配色 01/02，其余组配色留白（固定 colorId "01"，烘焙走原色快速路径）。
            var variants = new[]
            {
                ("body", "01", new[] { "01", "02" }),
                ("body", "02", new[] { "01" }),
                ("body", "00", new[] { "01" }),
                ("face", "01", new[] { "01", "02" }),
                ("face", "00", new[] { "01" }),
                ("hair", "01", new[] { "01", "02" }),
                ("hair", "03", new[] { "01", "02" }),
                ("hair", "00", new[] { "01" }),
            };
            var poses = new[] { ("walk", 4), ("idle", 2) };

            int sinceYield = 0;
            foreach (var (pose, frames) in poses)
            {
                foreach (var (part, styleId, colorIds) in variants)
                {
                    foreach (string colorId in colorIds)
                    {
                        foreach (string dir in dirs)
                        {
                            for (int frame = 1; frame <= frames; frame++)
                            {
                                Get(pose, part, styleId, colorId, dir, frame);
                                if (++sinceYield >= bakesPerStep)
                                {
                                    sinceYield = 0;
                                    yield return null;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>取一张染色后的部件 Sprite；任何缺失都返回 null（由调用方保持旧帧）。</summary>
        public static Sprite Get(string pose, string part, string styleId, string colorId, string dir, int frame)
        {
            string key = $"{pose}/{part}/{styleId}/{colorId}/{dir}/{frame}";
            if (SpriteCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var sprite = Bake(pose, part, styleId, colorId, dir, frame);
            if (sprite == null && frame != 1)
            {
                sprite = Bake(pose, part, styleId, colorId, dir, 1); // 缺帧回退首帧
            }

            if (sprite == null && dir != "a")
            {
                sprite = Bake(pose, part, styleId, colorId, "a", 1); // 缺方向回退前(a)
            }

            SpriteCache[key] = sprite;
            return sprite;
        }

        private static Sprite Bake(string pose, string part, string styleId, string colorId, string dir, int frame)
        {
            // 00 预染色系列特例（本批交付口径）：idle 身体主图文件名自带 m 后缀
            // （character_c_idle_body_00m_{dir}_{frame}，配表组名同步登记为 character_c_idle_body_00m）；
            // 整组无 Mask、不染色，主图即终稿。
            bool preColored = styleId == "00";
            string mainName = preColored && pose == "idle" && part == "body"
                ? $"character_c_{pose}_{part}_{styleId}m_{dir}_{frame}"
                : $"character_c_{pose}_{part}_{styleId}_{dir}_{frame}";

            var main = LoadTex(mainName);
            if (main == null)
            {
                return null;
            }

            // Mask 在部件序号后紧接 m（规范：不是 _m 后缀）；00 预染色组没有 Mask。
            var mask = preColored ? null : LoadTex($"character_c_{pose}_{part}_{styleId}m_{dir}_{frame}");
            var maskPx = mask != null && mask.width == main.width && mask.height == main.height
                ? mask.GetPixels32()
                : null;

            var palette = ResolvePalette(part, styleId, colorId);
            if (maskPx == null || PaletteIsIdentity(palette))
            {
                // 无 Mask（00 预染色 / 美术未交付）或配色留白：主图即终稿，直接出 Sprite，不逐像素拷贝。
                return CreateSprite(main, BottomCenter);
            }

            var outPx = TintPixels(main.GetPixels32(), maskPx, palette);
            var tex = new Texture2D(main.width, main.height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels32(outPx);
            tex.Apply(false, false);

            // pivot=底部中心、ppu=100，与导入设置一致，根物体直接落脚底落点。
            return CreateSprite(tex, BottomCenter);
        }

        private static readonly Vector2 BottomCenter = new Vector2(0.5f, 0f);
        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        private static Sprite CreateSprite(Texture2D tex, Vector2 pivot)
        {
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), pivot, 100f);
        }

        /// <summary>配色是否为「全白无发光」恒等（配表留白 = 原色，跳过逐像素染色）。</summary>
        private static bool PaletteIsIdentity(Palette p)
        {
            return IsWhite(p.R) && IsWhite(p.G) && IsWhite(p.B)
                && p.EmR == 0f && p.EmG == 0f && p.EmB == 0f;
        }

        private static bool IsWhite(Color c)
        {
            return c.r >= 0.999f && c.g >= 0.999f && c.b >= 0.999f;
        }

        /// <summary>Mask 通道 × 配表色 逐像素染色（边缘抗锯齿区按通道占比归一，再乘回灰白明暗）。</summary>
        private static Color32[] TintPixels(Color32[] basePx, Color32[] maskPx, Palette palette)
        {
            var outPx = new Color32[basePx.Length];
            for (int i = 0; i < basePx.Length; i++)
            {
                var b = basePx[i];
                var m = maskPx[i];
                float sum = m.r + m.g + m.b;
                if (sum < 1f)
                {
                    outPx[i] = b; // Mask 未覆盖区域保持灰白原稿
                    continue;
                }

                // 通道加权取色（边缘抗锯齿区按通道占比归一），再乘回灰白明暗。
                float t = Mathf.Clamp01(sum / 255f);
                float tr = (m.r * palette.R.r * (1f + palette.EmR) + m.g * palette.G.r * (1f + palette.EmG) + m.b * palette.B.r * (1f + palette.EmB)) / sum;
                float tg = (m.r * palette.R.g * (1f + palette.EmR) + m.g * palette.G.g * (1f + palette.EmG) + m.b * palette.B.g * (1f + palette.EmB)) / sum;
                float tb = (m.r * palette.R.b * (1f + palette.EmR) + m.g * palette.G.b * (1f + palette.EmG) + m.b * palette.B.b * (1f + palette.EmB)) / sum;
                float br = b.r / 255f, bg = b.g / 255f, bb = b.b / 255f;
                outPx[i] = new Color32(
                    (byte)(Mathf.Clamp01(Mathf.Lerp(1f, tr, t) * br) * 255f),
                    (byte)(Mathf.Clamp01(Mathf.Lerp(1f, tg, t) * bg) * 255f),
                    (byte)(Mathf.Clamp01(Mathf.Lerp(1f, tb, t) * bb) * 255f),
                    b.a);
            }

            return outPx;
        }

        // ==================== 立绘合成（character_r_*，768×768 全画布分层） ====================

        /// <summary>
        /// 取角色立绘 Sprite（对话 / 查看面板用）：00 套装为美术直出整图 character_r_idle_hair_00；
        /// 其余按 face → body → hair（<see cref="SpriteCharacterBuilder.LayerOrderFromBack"/>）三层 alpha 叠合合成，
        /// 各层经本组 Mask × 配表色染色（留白 = 原色）。
        /// 任何关键层缺失返回 null（调用方保持占位）。
        /// </summary>
        public static Sprite GetPortrait(CharacterAppearance appearance)
        {
            string key = $"portrait/{appearance.Body}/{appearance.Face}/{appearance.Hair}/{appearance.ColorId}";
            if (SpriteCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var sprite = BakePortrait(appearance);
            SpriteCache[key] = sprite;
            return sprite;
        }

        private static Sprite BakePortrait(CharacterAppearance appearance)
        {
            // 00 预染色套装：立绘为整图（无分层、无 Mask）。
            if (appearance.IsPreColored)
            {
                var full = LoadTex("character_r_idle_hair_00");
                return full != null ? CreateSprite(full, Center) : null;
            }

            var body = LoadTex($"character_r_idle_body_{appearance.Body}");
            if (body == null)
            {
                return null;
            }

            int w = body.width;
            int h = body.height;
            var canvas = new Color32[w * h]; // 全透明底
            // 与 Q 版三层共用层序：face（头/颈）垫底，body（衣领）压住脖颈，hair（刘海）最上。
            foreach (string part in SpriteCharacterBuilder.LayerOrderFromBack)
            {
                ComposePortraitLayer(canvas, w, h, part, SpriteCharacterBuilder.StyleIdOf(appearance, part), appearance.ColorId);
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels32(canvas);
            tex.Apply(false, false);
            return CreateSprite(tex, Center);
        }

        /// <summary>叠合一层立绘部件（主图 + _m Mask 染色后 alpha-over 到画布）。</summary>
        private static void ComposePortraitLayer(Color32[] canvas, int w, int h, string part, string styleId, string colorId)
        {
            var main = LoadTex($"character_r_idle_{part}_{styleId}");
            if (main == null || main.width != w || main.height != h)
            {
                return;
            }

            var src = main.GetPixels32();
            var mask = LoadTex($"character_r_idle_{part}_{styleId}_m");
            if (mask != null && mask.width == w && mask.height == h)
            {
                var palette = ResolvePortraitPalette($"character_r_idle_{part}_{styleId}", colorId);
                if (!PaletteIsIdentity(palette))
                {
                    src = TintPixels(src, mask.GetPixels32(), palette);
                }
            }

            // alpha-over：源覆盖到目标上。
            for (int i = 0; i < src.Length; i++)
            {
                var s = src[i];
                if (s.a == 0)
                {
                    continue;
                }
                if (s.a == 255)
                {
                    canvas[i] = s;
                    continue;
                }

                var d = canvas[i];
                float sa = s.a / 255f;
                float da = d.a / 255f;
                float oa = sa + da * (1f - sa);
                if (oa <= 0f)
                {
                    canvas[i] = s;
                    continue;
                }

                float inv = 1f / oa;
                float dr = (s.r * sa + d.r * da * (1f - sa)) * inv;
                float dg = (s.g * sa + d.g * da * (1f - sa)) * inv;
                float db = (s.b * sa + d.b * da * (1f - sa)) * inv;
                canvas[i] = new Color32((byte)dr, (byte)dg, (byte)db, (byte)(oa * 255f));
            }
        }

        /// <summary>立绘组配色直查（r 组行自带配色；留白或表未就绪 → 全白 = 原色）。</summary>
        private static Palette ResolvePortraitPalette(string rGroup, string colorId)
        {
            string key = rGroup + "/" + colorId;
            if (PaletteCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var palette = new Palette();
            try
            {
                var rows = ConfigSystem.Instance.Tables.TbCharacterPart.DataList;
                foreach (var row in rows)
                {
                    if (row.Id == rGroup && row.ColorId == colorId)
                    {
                        palette.R = ParseHex(row.RColor, Color.white);
                        palette.G = ParseHex(row.GColor, Color.white);
                        palette.B = ParseHex(row.BColor, Color.white);
                        palette.EmR = row.REmission;
                        palette.EmG = row.GEmission;
                        palette.EmB = row.BEmission;
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                TEngine.Log.Warning($"SpriteCharacterLibrary: 配色表未就绪，{rGroup} 按原色渲染（{e.Message}）");
            }

            PaletteCache[key] = palette;
            return palette;
        }

        private static Texture2D LoadTex(string name)
        {
            if (TexCache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            // meta 已开 isReadable，GetPixels32 可用。
            var tex = Resources.Load<Texture2D>($"{SpriteCharacterBuilder.ResourceDir}/{name}");
            TexCache[name] = tex; // null 也缓存，避免重复磁盘查询
            return tex;
        }

        /// <summary>
        /// 查配色：Q 版组（character_c_idle_{part}_{style}）经 counterpart_id 找到立绘行，
        /// 再按 color_id 取 R/G/B 通道色与发光值；配表留白或表未就绪 → 全白（不染色）。
        /// </summary>
        private static Palette ResolvePalette(string part, string styleId, string colorId)
        {
            string group = $"character_c_idle_{part}_{styleId}"; // walk/sit 复用 idle 组配色（README）
            string key = group + "/" + colorId;
            if (PaletteCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var palette = new Palette();
            try
            {
                var rows = ConfigSystem.Instance.Tables.TbCharacterPart.DataList;
                string rId = FindCounterpart(rows, group);
                if (rId != null)
                {
                    foreach (var row in rows)
                    {
                        if (row.Id == rId && row.ColorId == colorId)
                        {
                            palette.R = ParseHex(row.RColor, Color.white);
                            palette.G = ParseHex(row.GColor, Color.white);
                            palette.B = ParseHex(row.BColor, Color.white);
                            palette.EmR = row.REmission;
                            palette.EmG = row.GEmission;
                            palette.EmB = row.BEmission;
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                TEngine.Log.Warning($"SpriteCharacterLibrary: 配色表未就绪，{group} 按原色渲染（{e.Message}）");
            }

            PaletteCache[key] = palette;
            return palette;
        }

        /// <summary>找 Q 版组对应的立绘组 id（counterpart_id 反查，结果缓存）。</summary>
        private static string FindCounterpart(System.Collections.Generic.IReadOnlyList<GameConfig.ht.CharPart> rows, string cGroup)
        {
            if (CounterpartCache.TryGetValue(cGroup, out var cached))
            {
                return cached;
            }

            string found = null;
            foreach (var row in rows)
            {
                if (row.CounterpartId == cGroup)
                {
                    found = row.Id;
                    break;
                }
            }

            CounterpartCache[cGroup] = found;
            return found;
        }

        private static Color ParseHex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length != 6)
            {
                return fallback;
            }

            if (!byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) ||
                !byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) ||
                !byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                return fallback;
            }

            return new Color32(r, g, b, 255);
        }
    }
}
