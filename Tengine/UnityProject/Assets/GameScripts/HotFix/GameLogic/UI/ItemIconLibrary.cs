using System.Collections.Generic;
using GameLogic.World;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// 物品图标库（M13 资源补全）：按 Item 表 <c>icon</c> 列（如 "icon_2_.png"）从
    /// <c>Resources/ui01/icons/</c> 加载 Sprite，静态缓存（含 null，避免重复磁盘查询）。
    ///
    /// 缺图口径：本批只交付 icon_1—icon_24（png），表内 25 号及以后引用的 .jpg 图标尚未交付，
    /// 未加载到一律返回 null，由调用方回退文字展示——不虚构图标。
    /// </summary>
    public static class ItemIconLibrary
    {
        /// <summary>Resources 下的图标目录。</summary>
        public const string ResourceDir = "ui01/icons";

        private static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();

        /// <summary>取物品图标 Sprite；未配置图标列或贴图未交付时返回 null。</summary>
        public static Sprite Get(int itemId)
        {
            if (itemId <= 0)
            {
                return null;
            }

            if (Cache.TryGetValue(itemId, out var cached))
            {
                return cached;
            }

            Sprite sprite = null;
            var row = GameConfigCatalog.Instance != null ? GameConfigCatalog.Instance.GetItemRow(itemId) : null;
            string iconFile = row != null ? row.Icon : null;
            if (!string.IsNullOrEmpty(iconFile))
            {
                // 配置值带扩展名（icon_2_.png / icon_25.jpg），Resources.Load 需要去掉扩展名。
                int dot = iconFile.LastIndexOf('.');
                string name = dot > 0 ? iconFile.Substring(0, dot) : iconFile;
                sprite = Resources.Load<Sprite>($"{ResourceDir}/{name}");
            }

            Cache[itemId] = sprite;
            return sprite;
        }
    }

    /// <summary>
    /// UI 美术注入小工具：运行时给烘焙 prefab 补 Image 子节点（立绘 / 物品图标），
    /// 不改 prefab 文件（html-to-ugui 烘焙产物，改动会被下次烘焙覆盖）。
    /// </summary>
    public static class UiArtInjector
    {
        /// <summary>
        /// 确保 parent 下存在名为 name 的 Image 子节点（锚点区间铺满，offset 归零），首次创建；
        /// 新建的节点默认 <b>SetActive(false)</b>（无 sprite 时 Image 渲染为白色块，必须先藏起来），
        /// 由调用方设置 sprite 后自行激活。firstSibling=true 置于既有装饰（边框/角花/文字）之下。
        /// </summary>
        public static Image EnsureChildImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, bool firstSibling)
        {
            if (parent == null)
            {
                return null;
            }

            var existing = parent.Find(name);
            if (existing != null)
            {
                return existing.GetComponent<Image>();
            }

            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            if (firstSibling)
            {
                rt.SetAsFirstSibling();
            }

            var img = go.GetComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            go.SetActive(false);
            return img;
        }

        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();

        /// <summary>按 Resources 相对路径加载 ui01 Sprite（静态缓存含 null，避免重复磁盘查询）。</summary>
        public static Sprite LoadSprite(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
            {
                return null;
            }

            if (SpriteCache.TryGetValue(resourcePath, out var cached))
            {
                return cached;
            }

            var sprite = Resources.Load<Sprite>(resourcePath);
            SpriteCache[resourcePath] = sprite;
            return sprite;
        }

        /// <summary>
        /// 给烘焙按钮补美术：烘焙产物按钮统一 alpha=0 / 无贴图（按钮全隐形，「错位」观感来源之一），
        /// 运行时贴图并恢复不透明；贴图缺失时回退羊皮纸色底，保证按钮仍可见可点。返回是否贴到图。
        /// </summary>
        public static bool ApplyButtonArt(Image target, string spritePath)
        {
            if (target == null)
            {
                return false;
            }

            var sprite = LoadSprite(spritePath);
            if (sprite != null)
            {
                target.sprite = sprite;
                target.color = Color.white;
                target.preserveAspect = true;
                return true;
            }

            target.color = new Color(0.95f, 0.92f, 0.82f, 0.95f);
            return false;
        }

        /// <summary>
        /// 把角色立绘贴进 Image：外观由角色 Id 派生（<see cref="CharacterAppearance.Derive"/>），
        /// 与场景 Q 版形象同源；立绘缺失（贴图未交付）时隐藏节点并返回 false，调用方保持占位。
        /// </summary>
        public static bool ApplyPortrait(Image target, int characterId)
        {
            if (target == null)
            {
                return false;
            }

            var sprite = characterId >= 0
                ? SpriteCharacterLibrary.GetPortrait(CharacterAppearance.Derive(characterId))
                : null;
            target.sprite = sprite;
            target.gameObject.SetActive(sprite != null);
            return sprite != null;
        }
    }
}
