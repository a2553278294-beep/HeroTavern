using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Unity.Pipeline.Commands;

namespace HtmlToUGUI
{
    /// <summary>
    /// Pipeline CLI commands consumed by the project workflow (.agents/scripts/workflow.py).
    /// Parameter names and the dry_run/confirm contract are bound to workflow_lib/domains.py
    /// (json_path/config_path/output_path/legacy_text) and workflow_lib/unity.py; do not rename casually.
    /// </summary>
    public static class WorkflowCommands
    {
        [CliCommand("tengine_bake_ugui", "Bake HTML/UI-DSL JSON into a UGUI prefab inside a PreviewScene (user scenes are never touched). Overwrites the prefab path: requires confirm=true (use dry_run=true to preview). Not undoable via Ctrl+Z.", Tags = new[] { "assets" })]
        public static object BakeUgui(
            [CliArg("json_path", "UI-DSL JSON file: absolute filesystem path or project-relative TextAsset path.")] string jsonPath = null,
            [CliArg("config_path", "HtmlToUGUIConfig asset path; optional, defaults to window preferences.")] string configPath = null,
            [CliArg("output_path", "Target prefab path (Assets/.../*.prefab). Required when applying.")] string outputPath = null,
            [CliArg("json", "Inline UI-DSL JSON content (wins over json_path).")] string json = null,
            [CliArg("resolution", "Index into the config supported resolutions (v1 JSON only; v2 carries its own design size).")] int resolution = 0,
            [CliArg("legacy_text", "Use UGUI Text instead of TextMeshPro components.")] bool legacyText = false,
            [CliArg("normalize_naming", "Rename nodes with the script-generator prefixes.")] bool normalizeNaming = true,
            [CliArg("clear_existing", "Remove an existing child with the same root name before baking.")] bool clearExisting = false,
            [CliArg("confirm", "Apply the bake. Without it the call is refused.")] bool confirm = false,
            [CliArg("dry_run", "Preview the bake without writing the prefab.")] bool dryRun = false)
        {
            return HtmlToUGUIBakeService.Bake(new HtmlToUGUIBakeService.Request
            {
                JsonContent = json,
                JsonPath = jsonPath,
                ConfigAssetPath = configPath,
                ResolutionIndex = resolution,
                UseLegacyText = legacyText,
                NormalizeNaming = normalizeNaming,
                ClearExisting = clearExisting,
                PrefabPath = outputPath,
                DryRun = dryRun,
                Confirm = confirm,
            });
        }

        [CliCommand("tengine_validate_assets", "Validate that the given asset paths exist and report their importer state. Read-only.", Tags = new[] { "assets" })]
        public static object ValidateAssets(
            [CliArg("paths", "Project-relative asset paths to validate.", Required = true)] string[] paths)
        {
            if (paths == null || paths.Length == 0)
            {
                throw new ArgumentException("paths is required and must not be empty.");
            }

            var assets = new List<object>();
            var missing = new List<string>();
            foreach (string raw in paths)
            {
                string path = raw?.Replace('\\', '/');
                var mainAsset = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (mainAsset == null)
                {
                    missing.Add(path);
                    continue;
                }
                var importer = AssetImporter.GetAtPath(path);
                assets.Add(new
                {
                    path,
                    type = AssetDatabase.GetMainAssetTypeAtPath(path)?.Name,
                    importer = importer != null ? importer.GetType().Name : null,
                });
            }

            if (missing.Count > 0)
            {
                return new { success = false, total = paths.Length, missing, assets };
            }
            return new { success = true, total = paths.Length, assets };
        }
    }
}
