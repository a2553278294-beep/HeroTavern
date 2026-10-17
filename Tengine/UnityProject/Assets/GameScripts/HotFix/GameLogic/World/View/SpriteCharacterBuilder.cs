using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GameLogic
{
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
        /// 组装一个三层贴图角色（body/face/hair 子物体，微 z 偏移保证层序），返回根物体（pivot=脚底）。
        /// seed 决定发型（01/03）与身体配色（01/02，目前配表仅 body_01 落地了两组配色）；
        /// 各层默认显示 idle / 前(a) / 第 1 帧（方向口径：a=前 b=右 c=左 d=后）。
        /// </summary>
        public static GameObject Build(string name, int seed)
        {
            string hairId = (seed & 1) == 0 ? "01" : "03";
            string colorId = (seed & 2) == 0 ? "01" : "02";

            var root = new GameObject(name);
            root.transform.localScale = Vector3.one * DefaultScale;

            string[] parts = { "body", "face", "hair" };
            string[] styleIds = { "01", "01", hairId };
            for (int i = 0; i < parts.Length; i++)
            {
                var child = new GameObject(parts[i]);
                child.transform.SetParent(root.transform, false);
                // face/hair 相对 body 微前移（相机沿 +Z 看，z 小者在前），避免同 order 下的闪面。
                child.transform.localPosition = new Vector3(0f, 0f, -0.01f * (i + 1));
                child.AddComponent<SpriteRenderer>();
                child.AddComponent<CharacterPartLayer>().Init(parts[i], styleIds[i], colorId);
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
            string[] colorIds = { "01", "02" };
            // 形象由 Id 派生（SpriteCharacterBuilder.Build）：body/face 恒 01，hair ∈ {01, 03}；配色 ∈ {01, 02}。
            var variants = new[] { ("body", "01"), ("face", "01"), ("hair", "01"), ("hair", "03") };
            var poses = new[] { ("walk", 4), ("idle", 2) };

            int sinceYield = 0;
            foreach (var (pose, frames) in poses)
            {
                foreach (var (part, styleId) in variants)
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
            var main = LoadTex($"character_c_{pose}_{part}_{styleId}_{dir}_{frame}");
            if (main == null)
            {
                return null;
            }

            // Mask 在部件序号后紧接 m（规范：不是 _m 后缀）。
            var mask = LoadTex($"character_c_{pose}_{part}_{styleId}m_{dir}_{frame}");
            var palette = ResolvePalette(part, styleId, colorId);

            var basePx = main.GetPixels32();
            var maskPx = mask != null && mask.width == main.width && mask.height == main.height
                ? mask.GetPixels32()
                : null;
            var outPx = new Color32[basePx.Length];

            for (int i = 0; i < basePx.Length; i++)
            {
                var b = basePx[i];
                if (maskPx == null)
                {
                    outPx[i] = b;
                    continue;
                }

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

            var tex = new Texture2D(main.width, main.height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels32(outPx);
            tex.Apply(false, false);

            // pivot=底部中心、ppu=100，与导入设置一致，根物体直接落脚底落点。
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0f), 100f);
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
