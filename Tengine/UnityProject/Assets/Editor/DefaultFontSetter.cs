using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 默认字体工具。
/// 1. 编辑器中新建/添加 Text 组件时自动使用 zpix 字体。
/// 2. 提供菜单一键把项目内所有 Prefab 的内置字体替换为 zpix。
/// </summary>
[InitializeOnLoad]
public static class DefaultFontSetter
{
    private const string FontPath = "Assets/AssetRaw/Fonts/zpix.ttf";

    static DefaultFontSetter()
    {
        ObjectFactory.componentWasAdded += OnComponentWasAdded;
    }

    private static Font DefaultFont => AssetDatabase.LoadAssetAtPath<Font>(FontPath);

    private static void OnComponentWasAdded(Component component)
    {
        if (component is Text text && IsBuiltinFont(text.font))
        {
            var font = DefaultFont;
            if (font != null)
            {
                Undo.RecordObject(text, "Set Default Font");
                text.font = font;
            }
        }
    }

    private static bool IsBuiltinFont(Font font)
    {
        return font == null || font.name == "Arial" || font.name == "LegacyRuntime";
    }

    [MenuItem("Tools/默认字体/应用 zpix 到所有 UI Prefab")]
    private static void ApplyToAllPrefabs()
    {
        var font = DefaultFont;
        if (font == null)
        {
            Debug.LogError($"[DefaultFontSetter] 找不到字体: {FontPath}");
            return;
        }

        int changed = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            bool dirty = false;
            foreach (var text in prefab.GetComponentsInChildren<Text>(true))
            {
                if (IsBuiltinFont(text.font))
                {
                    text.font = font;
                    dirty = true;
                }
            }
            if (dirty)
            {
                PrefabUtility.SavePrefabAsset(prefab);
                changed++;
            }
        }
        Debug.Log($"[DefaultFontSetter] 已将 {changed} 个 Prefab 的字体替换为 zpix。");
    }
}
