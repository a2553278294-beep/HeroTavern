using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using HybridCLR.Editor.Installer;

/// <summary>
/// 临时打包辅助：补齐 Android 出包所需的 HybridCLR 安装与 PlayerSettings。
/// 打包完成后应删除本文件。
/// </summary>
public static class HeroTavernAndroidBuildSetup
{
    private const string PackageName = "com.herotavern.game";
    private const string HotUpdateDefine = "ENABLE_HYBRIDCLR";

    public static void InstallHybridCLR()
    {
        var controller = new InstallerController();
        if (controller.HasInstalledHybridCLR())
        {
            Debug.Log("[HeroTavernBuild] HybridCLR 已安装，跳过。");
            return;
        }

        Debug.Log("[HeroTavernBuild] 开始安装 HybridCLR（克隆 gitee 仓库并复制 il2cpp）...");
        controller.InstallDefaultHybridCLR();
        Debug.Log("[HeroTavernBuild] 安装结果 HasInstalledHybridCLR=" + controller.HasInstalledHybridCLR());
        if (!controller.HasInstalledHybridCLR())
        {
            throw new System.Exception("HybridCLR 安装失败");
        }
    }

    /// <summary>
    /// Android 出包前必须先生成 HybridCLR 侧产物（Il2CppDef / LinkXml / AOT dll / MethodBridge / AOT 泛型引用）。
    /// </summary>
    public static void GenerateHybridCLRAot()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Debug.Log("[HeroTavernBuild] 切换活动平台到 Android ...");
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }

        Debug.Log("[HeroTavernBuild] activeTarget=" + EditorUserBuildSettings.activeBuildTarget
            + " development=" + EditorUserBuildSettings.development);
        HybridCLR.Editor.Commands.PrebuildCommand.GenerateAll();
        Debug.Log("[HeroTavernBuild] GenerateAll 完成");
    }

    public static void ApplyAndroidPlayerSettings()
    {
        NamedBuildTarget target = NamedBuildTarget.Android;

        PlayerSettings.SetApplicationIdentifier(target, PackageName);
        PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
        // 本机 NDK clang 12 在优化阶段编译重模板 TU（Interpreter_Execute.cpp / Generics__*.cpp）
        // 会崩溃，降为 -O0 规避。
        PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Debug);
        // ARMv7 在 HybridCLR 解释器（Interpreter_Execute.cpp）上触发 clang 12 栈溢出崩溃，
        // 且纯 32 位包无法安装到 64 位独占的新机型，因此只出 ARM64。
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

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

        Debug.Log("[HeroTavernBuild] identifier=" + PlayerSettings.GetApplicationIdentifier(target)
            + " backend=" + PlayerSettings.GetScriptingBackend(target)
            + " arch=" + PlayerSettings.Android.targetArchitectures
            + " defines=" + PlayerSettings.GetScriptingDefineSymbols(target));
    }
}
