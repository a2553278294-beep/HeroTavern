using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.UI
{
    /// <summary>
    /// HUD 皮肤加载器（ui01）：把 <c>Resources/ui01</c> 下的切图按 IMGUI 方式绘制到 HUD 上。
    ///
    /// 设计约束：
    /// - 贴图缓存：<see cref="Tex"/> 以「不含扩展名的相对路径」为键缓存 <see cref="Texture2D"/>，
    ///   同一路径只 <c>Resources.Load</c> 一次；加载失败的路径记入 <see cref="MissingPaths"/> 并只告警一次。
    /// - 零依赖：只依赖 UnityEngine，可在 OnGUI 原型与正式 UIWindow 之间自由迁移。
    /// - 美术约定（来自 Resources/ui01/说明.txt）：
    ///   1. <c>common</c> 为通用框，Unity 内用九宫格拼装其它界面；
    ///   2. <c>ui_common_frame_lv1..lv4</c> 是 icon 等级框；
    ///   3. <c>ui_main_icon_01_s1</c> 是选中态，<c>_s2</c> 是未选中态（<c>ui_mixology_btn_01_s1</c> 可选、
    ///      <c>_s2</c> 不可选），故 <see cref="StateIcon"/> 一律 <c>selected ? _s1 : _s2</c>；
    ///   4. icon 尺寸均按 100x100 像素适配，<see cref="DrawIcon"/> 内部等比居中。
    ///
    /// ===== ui01 贴图清单（与 Resources/ui01 实际拷贝结果一致，由 M12 生成）=====
    /// common（11）：
    ///   ui_common_btn_close.png, ui_common_frame_01.png, ui_common_frame_02.png
    ///   ui_common_frame_03.png, ui_common_frame_04.png, ui_common_frame_05.png
    ///   ui_common_frame_lv1.png, ui_common_frame_lv2.png, ui_common_frame_lv3.png
    ///   ui_common_frame_lv4.png, ui_common_line_01.png
    /// main（13）：
    ///   ui_main_clock.png, ui_main_frame_01.png, ui_main_frame_02.png, ui_main_frame_03.png
    ///   ui_main_icon_01_s1.png, ui_main_icon_01_s2.png, ui_main_icon_02_s1.png
    ///   ui_main_icon_02_s2.png, ui_main_icon_03_s1.png, ui_main_icon_03_s2.png
    ///   ui_main_icon_04_s1.png, ui_main_icon_04_s2.png, ui_main_line_01.png
    /// mixology（5）：
    ///   ui_mixology_btn_01_s1.png, ui_mixology_btn_01_s2.png, ui_mixology_btn_02.png
    ///   ui_mixology_btn_03.png, ui_mixology_frame_01.png
    /// sence（11）：
    ///   sence_base_01.png, sence_item_01.png, sence_item_02.png, sence_item_03.png
    ///   sence_item_04.png, sence_item_05.png, sence_item_06.png, sence_item_07.png
    ///   sence_item_08.png, sence_item_09.png, sence_item_10.png
    /// shop（3）：
    ///   ui_shop_btn_01.png, ui_shop_frame_01.png, ui_shop_frame_02.png
    /// talk（2）：
    ///   ui_talk_frame_01.png, ui_talk_frame_02.png
    /// ref（8，仅参考效果图，运行时不加载）：
    ///   main/ui主界面.jpg, main/ui主界面2.jpg, main/ui主界面3.jpg, mixology/ui酒单.png, shop/ui交易1.png
    ///   shop/ui交易2.png, shop/ui交易3.png, talk/ui对话.png
    /// </summary>
    public static class HudSkin
    {
        /// <summary>Resources 根目录（不含斜杠结尾之外的额外前缀）。</summary>
        public const string ResourceRoot = "ui01/";

        /// <summary>参考效果图目录，仅供对照，运行时不加载。</summary>
        public const string RefRoot = "ui01/ref/";

        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        /// <summary>已记录缺失的贴图路径（不含扩展名），便于美术核对资源。</summary>
        public static readonly HashSet<string> MissingPaths = new HashSet<string>();

        /// <summary>
        /// 加载 ui01 贴图：<paramref name="path"/> 为不含扩展名的相对路径（如 "main/ui_main_frame_02"）。
        /// 缓存命中直接返回；缺失时返回 null 并记入 <see cref="MissingPaths"/>。
        /// </summary>
        public static Texture2D Tex(string path)
        {
            string key = Normalize(path);
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            Texture2D tex;
            if (Cache.TryGetValue(key, out tex))
            {
                return tex;
            }

            tex = Resources.Load<Texture2D>(ResourceRoot + key);
            if (tex == null)
            {
                if (MissingPaths.Add(key))
                {
                    Debug.LogWarning("[HudSkin] 缺少贴图: " + ResourceRoot + key);
                }

                return null;
            }

            Cache[key] = tex;
            return tex;
        }

        /// <summary>
        /// 绘制九宫格边框贴图到 <paramref name="r"/>。
        /// 说明：九宫格（美术约定 common 为通用框，需在 Unity 内九宫格拉伸拼接）。
        /// 当前实现简化为 <see cref="ScaleMode.StretchToFill"/> 直接拉伸，
        /// 正式版应改为 <see cref="ScaleMode.ScaleToFit"/> 不适用时的九宫格方案——
        /// 即为贴图设置 border（GUIStyle/NineSlice）后以 <c>ScaleMode.ScaleToFill</c> 配合
        /// <c>GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, alpha, 0, color, border, border)</c> 绘制，
        /// 或在 TEngine UIWindow 中使用 <c>Image.type = Sliced</c>。
        /// </summary>
        public static void DrawFrame(Rect r, string framePath)
        {
            Texture2D tex = Tex(framePath);
            if (tex == null)
            {
                GUI.Box(r, GUIContent.none);
                return;
            }

            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
        }

        /// <summary>
        /// 绘制 100x100 规格的 icon：按贴图宽高比等比缩放到 <paramref name="r"/> 内并居中。
        /// </summary>
        public static void DrawIcon(Rect r, string iconPath)
        {
            Texture2D tex = Tex(iconPath);
            if (tex == null)
            {
                GUI.Box(r, GUIContent.none);
                return;
            }

            GUI.DrawTexture(FitCentered(r, tex), tex, ScaleMode.ScaleToFit);
        }

        /// <summary>
        /// 选中态图标：传入不含 <c>_sN</c> 的基路径（如 "main/ui_main_icon_01"），
        /// 选中返回 <c>_s1</c>、未选中返回 <c>_s2</c>；某态缺失时回退到另一态，避免界面开天窗。
        /// </summary>
        public static Texture2D StateIcon(string basePath, bool selected)
        {
            string key = Normalize(basePath);
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            string on = key + "_s1";
            string off = key + "_s2";
            Texture2D tex = Tex(selected ? on : off);
            if (tex == null)
            {
                tex = Tex(selected ? off : on);
            }

            return tex;
        }

        /// <summary>预载 common 组：5 个通用 frame + btn_close（进入战斗/主界面前调用）。</summary>
        public static void PreloadCommon()
        {
            for (int i = 1; i <= 5; i++)
            {
                Tex("common/ui_common_frame_0" + i);
            }

            Tex("common/ui_common_btn_close");
        }

        /// <summary>清空贴图引用缓存与缺失记录（贴图本体由 Resources 系统持有，不在此销毁）。</summary>
        public static void ClearCache()
        {
            Cache.Clear();
            MissingPaths.Clear();
        }

        /// <summary>等比缩放并居中（美术约定 icon 统一 100x100 像素）。</summary>
        private static Rect FitCentered(Rect r, Texture2D tex)
        {
            float texW = tex != null && tex.width > 0 ? tex.width : 1f;
            float texH = tex != null && tex.height > 0 ? tex.height : 1f;
            float scale = Mathf.Min(r.width / texW, r.height / texH);
            float w = texW * scale;
            float h = texH * scale;
            return new Rect(r.x + (r.width - w) * 0.5f, r.y + (r.height - h) * 0.5f, w, h);
        }

        /// <summary>规范化路径：去空白、反斜杠转正斜杠、去掉 .png/.jpg 扩展名。</summary>
        private static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            string p = path.Trim().Replace('\\', '/');
            if (p.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring(0, p.Length - 4);
            }
            else if (p.EndsWith(".jpg", System.StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring(0, p.Length - 4);
            }

            return p;
        }
    }
}
