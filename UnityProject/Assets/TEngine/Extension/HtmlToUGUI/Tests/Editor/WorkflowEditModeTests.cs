using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace HtmlToUGUI.Tests
{
    /// <summary>
    /// EditMode regression for the headless bake service. Tests only touch their own TempGenerated folder.
    /// </summary>
    public class WorkflowEditModeTests
    {
        private const string TempFolder = "Assets/TEngine/Extension/HtmlToUGUI/Tests/Editor/TempGenerated";
        private const string ConfigPath = "Assets/TEngine/Extension/HtmlToUGUI/HtmlToUGUIConfig.asset";

        private const string V1Json = "{\"name\":\"Root\",\"type\":\"div\",\"x\":0,\"y\":0,\"width\":800,\"height\":600,\"children\":[" +
            "{\"name\":\"Label\",\"type\":\"text\",\"x\":10,\"y\":10,\"width\":200,\"height\":40,\"text\":\"Hello\",\"fontSize\":24}," +
            "{\"name\":\"Volume\",\"type\":\"slider\",\"x\":10,\"y\":60,\"width\":180,\"height\":30,\"value\":0}]}";

        private const string V2Json = "{\"schemaVersion\":2,\"designWidth\":1080,\"designHeight\":1920,\"name\":\"Root\",\"type\":\"div\"," +
            "\"x\":0,\"y\":0,\"width\":1080,\"height\":1920,\"children\":[{\"name\":\"Label\",\"type\":\"text\",\"x\":0,\"y\":0,\"width\":100,\"height\":40,\"text\":\"Hi\"}]}";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets/TEngine/Extension/HtmlToUGUI/Tests/Editor", "TempGenerated");
            }
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TempFolder);
        }

        private static string PrefabPath(string name) => $"{TempFolder}/{name}.prefab";

        [Test]
        public void Bake_DryRun_DoesNotWritePrefab()
        {
            string prefabPath = PrefabPath("DryRunProbe");
            var result = HtmlToUGUIBakeService.Bake(new HtmlToUGUIBakeService.Request
            {
                JsonContent = V1Json,
                PrefabPath = prefabPath,
                DryRun = true,
            });
            Assert.AreEqual("dry_run", result.status);
            Assert.AreEqual(3, result.nodeCount);
            Assert.IsFalse(File.Exists(HtmlToUGUIBakeService.ToFullAssetPath(prefabPath)), "dry_run must not write the prefab.");
        }

        [Test]
        public void Bake_Apply_CreatesPrefabAndPreservesSliderZero()
        {
            string prefabPath = PrefabPath("Applied");
            var result = HtmlToUGUIBakeService.Bake(new HtmlToUGUIBakeService.Request
            {
                JsonContent = V1Json,
                PrefabPath = prefabPath,
                Confirm = true,
            });
            Assert.AreEqual("applied", result.status);
            Assert.IsFalse(string.IsNullOrEmpty(result.prefabGuid));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.NotNull(prefab);
            var slider = prefab.GetComponentInChildren<Slider>(true);
            Assert.NotNull(slider, "slider node missing from prefab");
            Assert.AreEqual(0f, slider.value, "zero slider value must survive the bake");
            Assert.NotNull(prefab.GetComponentInChildren<TextMeshProUGUI>(true), "TMP text missing by default");
        }

        [Test]
        public void Bake_Rerun_KeepsPrefabGuid()
        {
            string prefabPath = PrefabPath("Stable");
            var first = HtmlToUGUIBakeService.Bake(new HtmlToUGUIBakeService.Request { JsonContent = V1Json, PrefabPath = prefabPath, Confirm = true });
            var second = HtmlToUGUIBakeService.Bake(new HtmlToUGUIBakeService.Request { JsonContent = V1Json, PrefabPath = prefabPath, Confirm = true });
            Assert.AreEqual(first.prefabGuid, second.prefabGuid, "re-bake to the same path must keep the prefab GUID");
        }

        [Test]
        public void Bake_WithoutConfirm_IsRefused()
        {
            Assert.Throws<System.ArgumentException>(() => HtmlToUGUIBakeService.Bake(new HtmlToUGUIBakeService.Request
            {
                JsonContent = V1Json,
                PrefabPath = PrefabPath("Refused"),
            }));
            Assert.IsFalse(File.Exists(HtmlToUGUIBakeService.ToFullAssetPath(PrefabPath("Refused"))));
        }

        [Test]
        public void Bake_InvalidJson_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => HtmlToUGUIBakeService.Bake(new HtmlToUGUIBakeService.Request
            {
                JsonContent = "{not json",
                DryRun = true,
                PrefabPath = PrefabPath("Bad"),
            }));
        }

        [Test]
        public void Bake_V2_UsesDesignSize()
        {
            var result = HtmlToUGUIBakeService.Bake(new HtmlToUGUIBakeService.Request
            {
                JsonContent = V2Json,
                PrefabPath = PrefabPath("V2"),
                DryRun = true,
            });
            Assert.AreEqual(1080f, result.canvasIntegration.referenceResolution[0]);
            Assert.AreEqual(1920f, result.canvasIntegration.referenceResolution[1]);
        }

        [Test]
        public void Bake_ConfigResolutions_AreHonored()
        {
            var config = AssetDatabase.LoadAssetAtPath<HtmlToUGUIConfig>(ConfigPath);
            Assert.NotNull(config, "workflow config asset missing");
            Assert.GreaterOrEqual(config.supportedResolutions.Count, 3);
            for (int i = 0; i < 3; i++)
            {
                var result = HtmlToUGUIBakeService.Bake(new HtmlToUGUIBakeService.Request
                {
                    JsonContent = V1Json,
                    ConfigAssetPath = ConfigPath,
                    ResolutionIndex = i,
                    PrefabPath = PrefabPath($"Res{i}"),
                    DryRun = true,
                });
                Assert.AreEqual(config.supportedResolutions[i].resolution.x, result.canvasIntegration.referenceResolution[0]);
                Assert.AreEqual(config.supportedResolutions[i].resolution.y, result.canvasIntegration.referenceResolution[1]);
            }
        }

        [Test]
        public void Bake_LegacyText_UsesUguiText()
        {
            string prefabPath = PrefabPath("Legacy");
            HtmlToUGUIBakeService.Bake(new HtmlToUGUIBakeService.Request
            {
                JsonContent = V1Json,
                PrefabPath = prefabPath,
                UseLegacyText = true,
                Confirm = true,
            });
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.NotNull(prefab.GetComponentInChildren<Text>(true), "legacy Text expected");
            Assert.IsNull(prefab.GetComponentInChildren<TextMeshProUGUI>(true), "TMP must not appear in legacy mode");
        }

        [Test]
        public void ValidateAssets_ReportsMissing()
        {
            var result = WorkflowCommands.ValidateAssets(new[] { "Assets/__definitely_missing__.png" });
            string json = JsonUtilityOrEcho(result);
            StringAssert.Contains("__definitely_missing__", json);
        }

        [Test]
        public void ValidateAssets_AcceptsExisting()
        {
            var result = WorkflowCommands.ValidateAssets(new[] { ConfigPath });
            StringAssert.Contains("config", JsonUtilityOrEcho(result).ToLowerInvariant());
        }

        private static string JsonUtilityOrEcho(object value)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(value);
        }
    }
}
