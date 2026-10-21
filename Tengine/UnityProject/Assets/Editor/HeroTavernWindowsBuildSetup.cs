using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// 临时打包辅助：Windows 端出包所需的平台设置与 HybridCLR 产物生成。
/// 打包完成后应删除本文件。
/// </summary>
public static class HeroTavernWindowsBuildSetup
{
    private const string PackageName = "com.herotavern.game";
    private const string HotUpdateDefine = "ENABLE_HYBRIDCLR";

    public static void ApplyWindowsPlayerSettings()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
        {
            Debug.Log("[HeroTavernBuild] 切换活动平台到 StandaloneWindows64 ...");
            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        }

        NamedBuildTarget target = NamedBuildTarget.Standalone;
        PlayerSettings.SetApplicationIdentifier(target, PackageName);
        PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
        // 本机 MSVC 14.43 cl.exe 在优化阶段编译重模板 TU（lump.cpp / UnityEngine.IMGUIModule__2.cpp）
        // 触发 C1001 内部编译器错误（memmove 访问违例），与 Android NDK 同款问题，降为 /Od 规避。
        PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Debug);

        string defines = PlayerSettings.GetScriptingDefineSymbols(target);
        string[] parts = defines.Split(';');
        bool has = false;
        foreach (var part in parts)
        {
            if (part == HotUpdateDefine)
            {
                has = true;
            }
        }

        if (!has)
        {
            PlayerSettings.SetScriptingDefineSymbols(target, string.IsNullOrEmpty(defines)
                ? HotUpdateDefine
                : defines + ";" + HotUpdateDefine);
        }

        Debug.Log("[HeroTavernBuild] target=" + EditorUserBuildSettings.activeBuildTarget
            + " identifier=" + PlayerSettings.GetApplicationIdentifier(target)
            + " backend=" + PlayerSettings.GetScriptingBackend(target)
            + " defines=" + PlayerSettings.GetScriptingDefineSymbols(target));
    }

    public static void GenerateHybridCLRForWindows()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
        {
            Debug.Log("[HeroTavernBuild] 切换活动平台到 StandaloneWindows64 ...");
            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        }

        Debug.Log("[HeroTavernBuild] GenerateAll for " + EditorUserBuildSettings.activeBuildTarget);
        HybridCLR.Editor.Commands.PrebuildCommand.GenerateAll();
        Debug.Log("[HeroTavernBuild] GenerateAll 完成");
    }
}
