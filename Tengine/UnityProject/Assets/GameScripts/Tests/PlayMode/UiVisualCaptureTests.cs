using System;
using System.Collections;
using System.IO;
using System.Linq;
using GameLogic.World;
using NUnit.Framework;
using TEngine;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GameLogic.Tests
{
    /// <summary>
    /// UI 视觉验收截图（任务 20261016-main-ui-scene-rework）：
    /// 加载 main 场景等框架与 HUD 就绪，依次打开主 HUD / 酒单 / 查看 / 对话 / 交易窗口，
    /// 截图到 UnityProject/.agents/screenshots/，供与 Resources/ui01 参考图逐屏比对。
    /// 不经点击模拟，直接调窗口公开 API（PendingNpc / ShowUIAsync）。
    /// </summary>
    public sealed class UiVisualCaptureTests
    {
        private const string ScenePath = "Assets/Scenes/main.unity";

        private static string ShotDir
        {
            get
            {
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".agents", "screenshots"));
                Directory.CreateDirectory(dir);
                return dir;
            }
        }


        private int _prevEditorPlayMode;

        [SetUp]
        public void ForceEditorSimulateMode()
        {
            // 编辑器下 ResourceModule.PlayMode 读 EditorPrefs["EditorPlayMode"]（工具栏可切换）。
            // 若被切到 Offline/Host，PlayMode 测试引导时 YooAsset 会去读
            // StreamingAssets/package/DefaultPackage/BuildinCatalog.bytes，
            // 未构建 AssetBundle 时该文件不存在 → HTTP 404。强制回模拟模式（EPlayMode.EditorSimulateMode = 0）。
            _prevEditorPlayMode = UnityEditor.EditorPrefs.GetInt("EditorPlayMode", 0);
            UnityEditor.EditorPrefs.SetInt("EditorPlayMode", 0);
        }

        [TearDown]
        public void RestoreEditorPlayMode()
        {
            UnityEditor.EditorPrefs.SetInt("EditorPlayMode", _prevEditorPlayMode);
        }

        [UnityTest]
        public IEnumerator CaptureMainUiScreens()
        {
            // 引导期日志噪声（YooAsset 警告等）不作为测试失败；断言只看截图产物。
            LogAssert.ignoreFailingMessages = true;

            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));

            // 注意：不能在此直接访问 WorldSceneBootstrap.Instance——SingletonBehaviour.Instance 是
            // 自动创建式 getter，提前访问会抢在正式引导（procedure）之前造出残缺实例并触发 UIModule 过早初始化。
            // 改为等 GameModule.World.Sim（模块懒加载安全；sim 由 GameApp.StartGameLogic 创建）。
            yield return WaitUntilOrTimeout(
                () => GameModule.World != null && GameModule.World.Sim != null && GameModule.World.Sim.IsRunning,
                60f, "世界仿真启动");
            yield return WaitUntilOrTimeout(
                () => UnityEngine.Object.FindFirstObjectByType<WorldSceneBootstrap>() != null,
                30f, "WorldSceneBootstrap 创建");
            yield return WaitUntilOrTimeout(() => MainHudUI.Current != null, 30f, "MainHudUI 打开");

            // 等场景摆件 / NPC / HUD 全部就位。
            yield return new WaitForSecondsRealtime(3f);
            DumpUiDiagnostics();
            yield return Capture("01_main_hud");

            // ---- 酒单（不暂停世界，直接开窗） ----
            GameModule.UI.ShowUIAsync<DrinkMenuUI>();
            yield return WaitUntilOrTimeout(() => DrinkMenuUI.Current != null, 15f, "DrinkMenuUI 打开");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Capture("02_drink_menu");
            GameModule.UI.CloseUI<DrinkMenuUI>();

            var boot = UnityEngine.Object.FindFirstObjectByType<WorldSceneBootstrap>();
            Assert.IsNotNull(boot, "WorldSceneBootstrap 应已创建");
            var npc = boot.Views.Values
                .Select(v => v.Data)
                .FirstOrDefault(c => c != null && c.IsAlive && !c.IsHidden);
            Assert.IsNotNull(npc, "场景里应至少有一个可交互 NPC");

            // ---- 查看 ----
            CharacterUI.PendingNpc = npc;
            GameModule.UI.ShowUIAsync<CharacterUI>();
            yield return WaitUntilOrTimeout(() => CharacterUI.Current != null, 15f, "CharacterUI 打开");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Capture("03_character");
            GameModule.UI.CloseUI<CharacterUI>();

            // ---- 对话 ----
            var sim = GameModule.World.Sim;
            DialogueUI.PendingSession = sim.Dialogue.StartChat(npc, sim.Time.DayOverall);
            DialogueUI.PendingNpc = npc;
            GameModule.UI.ShowUIAsync<DialogueUI>();
            yield return WaitUntilOrTimeout(() => DialogueUI.Current != null, 15f, "DialogueUI 打开");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Capture("04_dialogue");
            GameModule.UI.CloseUI<DialogueUI>();

            // ---- 交易 ----
            TradeUI.PendingNpc = npc;
            GameModule.UI.ShowUIAsync<TradeUI>();
            yield return WaitUntilOrTimeout(() => TradeUI.Current != null, 15f, "TradeUI 打开");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Capture("05_trade");
            GameModule.UI.CloseUI<TradeUI>();

            yield return new WaitForSecondsRealtime(0.5f);
        }

        /// <summary>诊断：uGUI 窗口不可见时输出 UIRoot/窗口 Canvas 状态到控制台。</summary>
        private static void DumpUiDiagnostics()
        {
            var sb = new System.Text.StringBuilder("[UiDiag] ");
            var uiRoot = GameObject.Find("UIRoot");
            if (uiRoot == null)
            {
                sb.Append("UIRoot 不存在; ");
            }
            else
            {
                foreach (var c in uiRoot.GetComponentsInChildren<Canvas>(true))
                {
                    sb.Append($"canvas[{c.gameObject.name}] mode={c.renderMode} cam={(c.worldCamera ? c.worldCamera.name : "null")} enabled={c.enabled} active={c.gameObject.activeInHierarchy} scaler={(c.GetComponent<UnityEngine.UI.CanvasScaler>() ? c.GetComponent<UnityEngine.UI.CanvasScaler>().uiScaleMode.ToString() : "none")}; ");
                }
            }
            var hud = MainHudUI.Current;
            if (hud != null)
            {
                var rt = hud.transform as RectTransform;
                var wc = hud.gameObject.GetComponent<Canvas>();
                sb.Append($"MainHudUI active={hud.gameObject.activeInHierarchy} pos={rt.position} rect={rt.rect} anchors=({rt.anchorMin}->{rt.anchorMax}) ownCanvas={(wc ? $"mode={wc.renderMode} override={wc.overrideSorting} enabled={wc.enabled}" : "null")}; ");
            }
            int total = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Length;
            sb.Append($"totalCanvases={total}");
            Debug.Log(sb.ToString());
        }

        private static IEnumerator Capture(string name)
        {
            string path = Path.Combine(ShotDir, name + ".png");
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path);

            float deadline = Time.realtimeSinceStartup + 5f;
            while (!File.Exists(path) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(File.Exists(path), "截图未生成：" + path);
        }

        private static IEnumerator WaitUntilOrTimeout(Func<bool> condition, float seconds, string what)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(condition(), "等待超时：" + what);
        }
    }
}
