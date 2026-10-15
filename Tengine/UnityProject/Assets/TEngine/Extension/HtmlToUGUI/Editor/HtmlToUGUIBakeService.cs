using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HtmlToUGUI
{
    /// <summary>
    /// Headless HTML/UI-DSL -> UGUI bake pipeline shared by the HtmlToUGUIBaker window and the
    /// tengine_bake_ugui CLI command. All work happens inside a temporary PreviewScene:
    /// the user's open scenes are never modified or saved.
    /// dry_run reports the bake without writing; apply requires confirm and overwrites the prefab
    /// path (same path keeps its GUID). The dry_run result is deterministic: the workflow digests
    /// it again at apply time and refuses to continue when it changed.
    /// </summary>
    public static class HtmlToUGUIBakeService
    {
        public sealed class Request
        {
            public string JsonContent;      // inline JSON; wins over JsonPath
            public string JsonPath;         // absolute filesystem path OR project-relative TextAsset path
            public string ConfigAssetPath;  // optional HtmlToUGUIConfig asset
            public int ResolutionIndex;
            public bool UseLegacyText;
            public bool NormalizeNaming = true;
            public bool ClearExisting;
            public string PrefabPath;       // required when applying
            public bool DryRun;
            public bool Confirm;
        }

        public sealed class CanvasIntegration
        {
            public string uiScaleMode;
            public float[] referenceResolution;
            public float matchWidthOrHeight;
        }

        public sealed class Result
        {
            public string status;
            public string rootName;
            public int nodeCount;
            public int imageCount;
            public float[] designSize;
            public CanvasIntegration canvasIntegration;
            public string[] imageAssets;    // resolved project asset paths for every bound image
            public string[] inputFiles;     // absolute paths of every input byte source (json, config, images)
            public string prefabPath;
            public string prefabGuid;       // apply only
            public bool wouldOverwrite;     // dry_run only
            public string existingGuid;     // dry_run only
            public string contentHash;
            public string scope = "Prefab contains no temporary Canvas; wire canvasIntegration onto the host Canvas.";
        }

        public static string ToFullAssetPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        public static Result Bake(Request request)
        {
            if (request == null) throw new ArgumentException("request is required.");
            if (!request.DryRun && !request.Confirm)
            {
                throw new ArgumentException("Refusing to write the prefab without confirm=true. Call with dry_run=true to preview.");
            }
            string prefabPath = (request.PrefabPath ?? string.Empty).Replace('\\', '/');
            if (!request.DryRun && string.IsNullOrWhiteSpace(prefabPath))
            {
                throw new ArgumentException("prefab path is required when applying.");
            }
            if (!string.IsNullOrEmpty(prefabPath) &&
                (!prefabPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"prefab path must be an Assets/.../*.prefab path: {prefabPath}");
            }

            string json = request.JsonContent;
            string jsonFullPath = null;       // absolute path of the JSON bytes, for input binding
            string jsonDirectory = null;      // base directory for relative image sources
            TextAsset jsonAsset = null;
            if (string.IsNullOrWhiteSpace(json) && !string.IsNullOrWhiteSpace(request.JsonPath))
            {
                string candidate = request.JsonPath;
                if (Path.IsPathRooted(candidate) || File.Exists(candidate))
                {
                    jsonFullPath = Path.GetFullPath(candidate);
                    if (!File.Exists(jsonFullPath))
                    {
                        throw new ArgumentException($"JSON file not found: {candidate}");
                    }
                    json = File.ReadAllText(jsonFullPath);
                    jsonDirectory = Path.GetDirectoryName(jsonFullPath);
                }
                else
                {
                    jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(candidate.Replace('\\', '/'));
                    if (jsonAsset == null)
                    {
                        throw new ArgumentException($"JSON asset not found: {candidate}");
                    }
                    json = jsonAsset.text;
                    jsonFullPath = ToFullAssetPath(AssetDatabase.GetAssetPath(jsonAsset));
                }
            }
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("No UI-DSL JSON supplied (json or json_path).");
            }

            UIDataNode rootNode;
            try
            {
                rootNode = JsonConvert.DeserializeObject<UIDataNode>(json);
            }
            catch (Exception e)
            {
                throw new ArgumentException($"UI-DSL JSON parse failed: {e.Message}");
            }
            if (rootNode == null)
            {
                throw new ArgumentException("UI-DSL JSON parsed to null; check the schema.");
            }

            HtmlToUGUIConfig config = null;
            if (!string.IsNullOrWhiteSpace(request.ConfigAssetPath))
            {
                config = AssetDatabase.LoadAssetAtPath<HtmlToUGUIConfig>(request.ConfigAssetPath.Replace('\\', '/'));
                if (config == null)
                {
                    throw new ArgumentException($"HtmlToUGUIConfig asset not found: {request.ConfigAssetPath}");
                }
            }

            // Drive the exact same pipeline as the editor window through a headless instance.
            var baker = ScriptableObject.CreateInstance<HtmlToUGUIBaker>();
            Scene previewScene = default;
            try
            {
                baker.config = config;
                baker.selectedResolutionIndex = request.ResolutionIndex;
                baker.useLegacyText = request.UseLegacyText;
                baker.useScriptGeneratorNaming = request.NormalizeNaming;
                baker.clearSameNameBeforeBake = request.ClearExisting;
                baker.selectGeneratedRoot = false;
                if (jsonAsset != null)
                {
                    baker.currentMode = HtmlToUGUIBaker.InputMode.FileAsset;
                    baker.jsonAsset = jsonAsset;
                }
                else
                {
                    baker.currentMode = HtmlToUGUIBaker.InputMode.RawString;
                    baker.rawJsonString = json;
                    baker.jsonDirectoryOverride = jsonDirectory;
                }

                previewScene = EditorSceneManager.NewPreviewScene();

                // Preview scenes cannot be made active; create in the active scene, then move over.
                var canvasGo = new GameObject("HtmlToUGUIPreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                SceneManager.MoveGameObjectToScene(canvasGo, previewScene);
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                if (request.NormalizeNaming)
                {
                    baker.NormalizeNodeNamesByScriptGenerator(rootNode);
                }
                var bakeContext = baker.CreateBakeContext(rootNode);
                baker.ConfigureCanvasScaler(canvas, bakeContext);
                if (request.ClearExisting)
                {
                    baker.RemoveExistingRoot(canvas.transform, rootNode.name);
                }
                GameObject rootGo = baker.CreateUINode(rootNode, canvas.transform, null, bakeContext, 0f, 0f);
                // The prefab ships without the temporary Canvas; only the UI root is saved.
                rootGo.transform.SetParent(null, false);
                SceneManager.MoveGameObjectToScene(rootGo, previewScene);

                var imageAssets = new List<string>();
                CollectImageAssets(rootNode, baker, bakeContext, imageAssets);
                string[] sortedImageAssets = imageAssets.Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();

                var inputs = new List<string>();
                if (!string.IsNullOrEmpty(jsonFullPath)) inputs.Add(NormalizeFull(jsonFullPath));
                if (config != null)
                {
                    string configPath = AssetDatabase.GetAssetPath(config);
                    if (!string.IsNullOrEmpty(configPath)) inputs.Add(NormalizeFull(ToFullAssetPath(configPath)));
                }
                inputs.AddRange(sortedImageAssets.Select(p => NormalizeFull(ToFullAssetPath(p))));
                string[] inputFiles = inputs.Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();

                var scaler = canvas.GetComponent<CanvasScaler>();
                var result = new Result
                {
                    rootName = rootGo.name,
                    nodeCount = baker.CountNodes(rootNode),
                    imageCount = baker.CountImageNodes(rootNode),
                    designSize = new[] { bakeContext.designSize.x, bakeContext.designSize.y },
                    canvasIntegration = new CanvasIntegration
                    {
                        uiScaleMode = scaler.uiScaleMode.ToString(),
                        referenceResolution = new[] { scaler.referenceResolution.x, scaler.referenceResolution.y },
                        matchWidthOrHeight = scaler.matchWidthOrHeight,
                    },
                    imageAssets = sortedImageAssets,
                    inputFiles = inputFiles,
                    prefabPath = prefabPath,
                    contentHash = ComputeHash(json, request),
                };

                if (request.DryRun)
                {
                    result.status = "dry_run";
                    if (!string.IsNullOrEmpty(prefabPath))
                    {
                        result.wouldOverwrite = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
                        result.existingGuid = result.wouldOverwrite ? AssetDatabase.AssetPathToGUID(prefabPath) : null;
                    }
                }
                else
                {
                    EnsureAssetFolders(Path.GetDirectoryName(prefabPath));
                    PrefabUtility.SaveAsPrefabAsset(rootGo, prefabPath);
                    result.status = "applied";
                    result.prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
                }
                return result;
            }
            finally
            {
                if (previewScene.IsValid())
                {
                    EditorSceneManager.ClosePreviewScene(previewScene);
                }
                UnityEngine.Object.DestroyImmediate(baker);
            }
        }

        private static void CollectImageAssets(UIDataNode node, HtmlToUGUIBaker baker, HtmlToUGUIBaker.BakeContext bakeContext, List<string> acc)
        {
            if (node == null) return;
            string source = !string.IsNullOrWhiteSpace(node.imageSrc) ? node.imageSrc : node.backgroundImageSrc;
            if (!string.IsNullOrWhiteSpace(source))
            {
                string resolved = baker.ResolveImageAssetPath(source, node, bakeContext);
                if (!string.IsNullOrEmpty(resolved)) acc.Add(resolved);
            }
            if (node.children == null) return;
            foreach (var child in node.children)
            {
                CollectImageAssets(child, baker, bakeContext, acc);
            }
        }

        private static void EnsureAssetFolders(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder)) return;
            string normalized = assetFolder.Replace('\\', '/').TrimEnd('/');
            if (!normalized.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"prefab folder must live under Assets: {assetFolder}");
            }
            string current = "Assets";
            foreach (string segment in normalized.Substring("Assets".Length).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string next = current + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segment);
                }
                current = next;
            }
        }

        private static string NormalizeFull(string fullPath)
        {
            return fullPath.Replace('\\', '/');
        }

        private static string ComputeHash(string json, Request request)
        {
            string material = string.Join("|", json, request.ConfigAssetPath, request.ResolutionIndex,
                request.UseLegacyText, request.NormalizeNaming, request.ClearExisting);
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(material))).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
