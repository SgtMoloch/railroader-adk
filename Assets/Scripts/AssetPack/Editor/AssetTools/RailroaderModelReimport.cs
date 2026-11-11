#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Railroader.AssetTools
{
    /// <summary>
    /// Replaces an already imported model IN PLACE, without recreating the FBX-based
    /// prefab variants, their authoring components, material mappings, or definitions.
    /// A changed FBX hierarchy can still invalidate Unity prefab overrides; we report
    /// that explicitly instead of attempting an unsafe automated component migration.
    /// </summary>
    public sealed class RailroaderModelReimport : EditorWindow
    {
        private string pack;
        private string previousPack;
        private int modelIndex;
        private string sourceModel = "";
        private string sourceTextures = "";
        private bool includeModel = true;
        private bool includeTextures;
        private int textureTargetIndex;
        private bool updateMaterials;
        private RailroaderShaderSelection shaderSelection = RailroaderShaderSelection.Automatic;
        private Shader customShader;
        private string preview = "";
        private string status = "";
        private Vector2 scroll;
        private string cachedModelPath;
        private List<string> cachedLinkedPrefabs;

        [MenuItem("Railroader/Model Reimport")]
        public static void Open()
        {
            GetWindow<RailroaderModelReimport>("Model Reimport");
        }

        private void OnEnable()
        {
            pack = EditorPrefs.GetString("RailroaderTools.SelectedPack", "");
        }

        private void OnGUI()
        {
            using (var view = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = view.scrollPosition;
                DrawWindow();
            }
        }

        private void DrawWindow()
        {
            EditorGUILayout.LabelField("Railroader Model Reimport v3.0 Beta 1.5.5", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Reimport an existing FBX and/or its Substance Painter textures " +
                "without deleting, rebuilding, or resaving game and authoring prefabs. " +
                "The FBX must stay at its original Unity path to keep its GUID and references.", MessageType.Info);
            pack = RailroaderAssetPackPicker.Draw("Asset Pack", pack);
            if (pack != previousPack)
            {
                previousPack = pack;
                modelIndex = 0;
                textureTargetIndex = 0;
                preview = "";
                status = "";
                cachedModelPath = null;
            }
            if (!RailroaderAssetPackTools.ValidPack(pack))
            {
                EditorGUILayout.HelpBox("Choose an existing asset pack.", MessageType.Warning);
                return;
            }
            List<string> models = RailroaderAssetPackTools.Models(pack)
                .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)).ToList();
            if (models.Count == 0)
            {
                EditorGUILayout.HelpBox("No existing FBX was found in Model/ or Models/.", MessageType.Warning);
                return;
            }
            modelIndex = Mathf.Clamp(modelIndex, 0, models.Count - 1);
            modelIndex = EditorGUILayout.Popup("Existing FBX", modelIndex,
                models.Select(p => p.Substring(pack.Length + 1)).ToArray());
            string target = models[modelIndex];
            string absoluteTarget = RailroaderAssetPackTools.Absolute(target);
            EditorGUILayout.LabelField("Unity destination", target, EditorStyles.wordWrappedLabel);

            if (cachedLinkedPrefabs == null || !string.Equals(cachedModelPath, target, StringComparison.OrdinalIgnoreCase))
            {
                cachedModelPath = target;
                cachedLinkedPrefabs = RailroaderModelReimportService.DependentPrefabs(pack, target);
            }
            List<string> linked = cachedLinkedPrefabs;
            var liveries = RailroaderLiveryManifest.FindInPack(pack)
                .Where(l => string.Equals(l.SourceModelPath, target, StringComparison.OrdinalIgnoreCase))
                .ToList();
            EditorGUILayout.HelpBox("This FBX is shared by " + linked.Count + " existing prefab(s)" +
                (liveries.Count == 0 ? "." : " and " + liveries.Count + " registered livery/liveries.") +
                " Changing the mesh affects ALL of them, even if you only refresh one livery's textures. " +
                "Collider, Colorizer, animation, and material overrides remain stored in their existing prefabs, " +
                "but changed/renamed FBX objects can detach those overrides.", MessageType.None);
            if (linked.Count > 0)
            {
                EditorGUILayout.LabelField("Prefabs depending on this model:", EditorStyles.boldLabel);
                foreach (string path in linked.Take(12))
                    EditorGUILayout.LabelField("  " + (path.StartsWith(pack + "/", StringComparison.OrdinalIgnoreCase)
                        ? path.Substring(pack.Length + 1) : path), EditorStyles.wordWrappedMiniLabel);
                if (linked.Count > 12)
                    EditorGUILayout.LabelField("  ... and " + (linked.Count - 12) + " more");
            }

            EditorGUILayout.Space();
            includeModel = EditorGUILayout.ToggleLeft("Replace / reimport existing FBX", includeModel);
            if (includeModel)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    sourceModel = EditorGUILayout.TextField("Updated model file", sourceModel);
                    if (GUILayout.Button("Browse...", GUILayout.Width(90)))
                    {
                        string chosen = EditorUtility.OpenFilePanel("Choose updated FBX",
                            RailroaderModelReimportService.InitialDirectory(sourceModel, RailroaderAssetImporter.DesktopDirectory()), "fbx");
                        if (!string.IsNullOrEmpty(chosen))
                            sourceModel = chosen;
                    }
                }
                EditorGUILayout.HelpBox("Replaces only the contents of the existing model file. " +
                    "Its .meta, importer configuration, GUID, and prefab files stay in place. " +
                    "Keep mesh/object names and material slots stable to preserve overrides.", MessageType.None);
            }
            includeTextures = EditorGUILayout.ToggleLeft("Reimport Substance Painter textures (optional)", includeTextures);
            RailroaderTextureRefresh.Target textureTarget = null;
            if (includeTextures)
            {
                List<RailroaderTextureRefresh.Target> targets = RailroaderTextureRefresh.Targets(pack)
                    .Where(t => string.Equals(t.ModelPath, target, StringComparison.OrdinalIgnoreCase)).ToList();
                if (targets.Count == 0)
                    EditorGUILayout.HelpBox("No texture targets found for this model.", MessageType.Warning);
                else
                {
                    textureTargetIndex = Mathf.Clamp(textureTargetIndex, 0, targets.Count - 1);
                    textureTargetIndex = EditorGUILayout.Popup("Texture target", textureTargetIndex,
                        targets.Select(t => t.Label).ToArray());
                    textureTarget = targets[textureTargetIndex];
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    sourceTextures = EditorGUILayout.TextField("Painter export folder", sourceTextures);
                    if (GUILayout.Button("Browse...", GUILayout.Width(90)))
                    {
                        string chosen = EditorUtility.OpenFolderPanel("Choose updated textures",
                            RailroaderModelReimportService.InitialDirectory(sourceTextures, RailroaderAssetImporter.TextureBrowseDirectory()), "");
                        if (!string.IsNullOrEmpty(chosen))
                            sourceTextures = chosen;
                    }
                }
                updateMaterials = EditorGUILayout.ToggleLeft(
                    "Also update existing .mat texture slots/shaders (optional)", updateMaterials);
                if (updateMaterials)
                {
                    RailroaderMaterialShaderUI.Draw(ref shaderSelection, ref customShader);
                    EditorGUILayout.HelpBox("May change existing .mat shader or properties. " +
                        "Leave unchecked to preserve customized material settings and colors. " +
                        "No FBX remap or prefab material reassignment is performed.", MessageType.Warning);
                }
            }
            EditorGUILayout.Space();
            bool canRun = (includeModel || includeTextures) &&
                (!includeModel || !string.IsNullOrWhiteSpace(sourceModel)) &&
                (!includeTextures || (textureTarget != null && !string.IsNullOrWhiteSpace(sourceTextures))) &&
                (!includeTextures || !updateMaterials ||
                    shaderSelection != RailroaderShaderSelection.Custom || customShader != null);
            using (new EditorGUI.DisabledScope(!canRun))
            {
                if (GUILayout.Button("Preview Reimport", GUILayout.Height(25)))
                {
                    try
                    {
                        var plan = RailroaderModelReimportService.Prepare(pack, target,
                            includeModel ? sourceModel : null, includeTextures ? textureTarget : null,
                            includeTextures ? sourceTextures : null);
                        preview = plan.Describe();
                        status = "";
                    }
                    catch (Exception ex)
                    {
                        preview = "Preview failed: " + ex.Message;
                        status = "";
                    }
                }
                if (GUILayout.Button("Reimport FBX / Textures in Place", GUILayout.Height(33)))
                {
                    try
                    {
                        // Prepare again, not from cached UI state: protects against stale target paths.
                        var plan = RailroaderModelReimportService.Prepare(pack, target,
                            includeModel ? sourceModel : null, includeTextures ? textureTarget : null,
                            includeTextures ? sourceTextures : null);
                        preview = plan.Describe();
                        if (EditorUtility.DisplayDialog("Reimport existing FBX / textures?",
                            preview + "\n\nThis will back up the replaced source files under the " +
                            "project Library folder, then overwrite them in place. Existing prefabs " +
                            "will NOT be recreated or rewritten. Hierarchy/mesh name changes can " +
                            "break overrides. Make a project backup if the model is heavily customized.",
                            "Backup and Reimport", "Cancel"))
                        {
                            status = RailroaderModelReimportService.Execute(plan, updateMaterials,
                                shaderSelection, customShader);
                            cachedModelPath = null;
                            Debug.Log("[Railroader Model Reimport] " + status.Replace("\n", " | "));
                        }
                    }
                    catch (Exception ex)
                    {
                        status = "Reimport stopped: " + ex.Message;
                        RailroaderEditorFeedback.ShowError("Model Reimport", ex);
                    }
                }
            }
            if (!string.IsNullOrEmpty(preview))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Plan", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(preview, MessageType.None);
            }
            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(status, MessageType.Info);
            }
        }
    }

    internal static class RailroaderModelReimportService
    {
        internal sealed class Plan
        {
            internal string Pack;
            internal string ModelPath;
            internal string ModelSource;
            internal bool SameModelFile;
            internal RailroaderTextureRefresh.Plan TexturePlan;
            internal List<string> LinkedPrefabs;

            internal string Describe()
            {
                string text = "Model: " + ModelPath;
                text += ModelSource == null ? "\nFBX: unchanged" :
                    "\nFBX: " + (SameModelFile ? "Force reimport existing file" : "Replace from " + ModelSource);
                text += "\nExisting linked prefabs (preserved): " + LinkedPrefabs.Count;
                if (LinkedPrefabs.Count > 0)
                    text += "\n  " + string.Join("\n  ", LinkedPrefabs.Take(10).ToArray()) +
                        (LinkedPrefabs.Count > 10 ? "\n  ..." : "");
                text += "\nTextures: " + (TexturePlan == null ? "unchanged" : "\n" + TexturePlan.Describe());
                text += "\n\nDefinitions.json, prefab assets, authoring components, and original .meta files are NOT rewritten.";
                return text;
            }
        }

        internal static string InitialDirectory(string candidate, string fallback)
        {
            string path = (candidate ?? "").Trim().Trim('"');
            if (Directory.Exists(path)) return path;
            if (File.Exists(path)) return Path.GetDirectoryName(Path.GetFullPath(path));
            if (Directory.Exists(fallback)) return fallback;
            if (File.Exists(fallback)) return Path.GetDirectoryName(Path.GetFullPath(fallback));
            return Application.dataPath;
        }

        internal static List<string> DependentPrefabs(string pack, string modelPath)
        {
            // Search the entire ADK project, not just this pack: authoring prefabs
            // can live elsewhere while still referencing the imported FBX.
            return AssetDatabase.FindAssets("t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(p => AssetDatabase.GetDependencies(p, true)
                    .Any(d => string.Equals(d, modelPath, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static Plan Prepare(string pack, string modelPath, string inputModel,
            RailroaderTextureRefresh.Target textureTarget, string inputTextures)
        {
            if (!RailroaderAssetPackTools.ValidPack(pack) ||
                !modelPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
                !RailroaderAssetPackTools.Models(pack).Contains(modelPath, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Select an existing imported FBX in the asset pack.");
            if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter))
                throw new InvalidOperationException("No Unity ModelImporter exists for " + modelPath);
            if (!File.Exists(RailroaderAssetPackTools.Absolute(modelPath) + ".meta"))
                throw new InvalidOperationException("Model .meta file is missing; cannot guarantee GUID preservation.");
            string modelSource = null;
            bool sameFile = false;
            if (inputModel != null)
            {
                modelSource = Path.GetFullPath(inputModel.Trim().Trim('"'));
                string modelDest = RailroaderAssetPackTools.Absolute(modelPath);
                if (!File.Exists(modelSource))
                    throw new FileNotFoundException("Updated FBX not found", modelSource);
                if (!File.Exists(modelDest))
                    throw new FileNotFoundException("Existing model file not found", modelDest);
                if (!string.Equals(Path.GetExtension(modelSource), Path.GetExtension(modelDest),
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Updated model must be an FBX. " +
                        "Do not rename/move the original model; that would change its Unity identity.");
                sameFile = string.Equals(modelSource, Path.GetFullPath(modelDest),
                    StringComparison.OrdinalIgnoreCase);
            }
            RailroaderTextureRefresh.Plan tex = null;
            if (textureTarget != null)
            {
                if (!string.Equals(textureTarget.ModelPath, modelPath, StringComparison.OrdinalIgnoreCase) ||
                    !RailroaderTextureRefresh.Targets(pack).Any(t =>
                        string.Equals(t.TextureRoot, textureTarget.TextureRoot, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(t.ModelPath, modelPath, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(t.PrefabPath, textureTarget.PrefabPath, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Selected texture target does not belong to this model.");
                tex = RailroaderTextureRefresh.Prepare(textureTarget, inputTextures);
            }
            if (modelSource == null && tex == null)
                throw new InvalidOperationException("Select FBX and/or textures to reimport.");
            return new Plan {
                Pack = pack, ModelPath = modelPath, ModelSource = modelSource,
                SameModelFile = sameFile, TexturePlan = tex,
                LinkedPrefabs = DependentPrefabs(pack, modelPath)
            };
        }

        private static HashSet<string> ModelNodes(string modelPath)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            HashSet<string> nodes = new HashSet<string>(StringComparer.Ordinal);
            if (model == null) return nodes;
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
            {
                List<string> names = new List<string>();
                Transform cursor = child;
                while (cursor != null && cursor != model.transform)
                {
                    names.Add(cursor.name);
                    cursor = cursor.parent;
                }
                names.Reverse();
                string path = string.Join("/", names.ToArray());
                Renderer renderer = child.GetComponent<Renderer>();
                MeshFilter mesh = child.GetComponent<MeshFilter>();
                string materialNames = renderer == null ? "" : string.Join(",",
                    renderer.sharedMaterials.Select(m => m == null ? "<null>" : m.name).ToArray());
                string meshName = mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.name : "";
                nodes.Add(path + " | mesh=" + meshName + " | materials=" + materialNames);
            }
            return nodes;
        }

        private static void BackUp(string assetPath, string backupFolder)
        {
            string absolute = RailroaderAssetPackTools.Absolute(assetPath);
            if (!File.Exists(absolute)) return;
            string dest = Path.Combine(backupFolder, assetPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.Copy(absolute, dest, true);
            if (File.Exists(absolute + ".meta"))
                File.Copy(absolute + ".meta", dest + ".meta", true);
        }

        private static string CreateBackup(Plan plan, bool updateMaterials)
        {
            string project = Directory.GetParent(Application.dataPath).FullName;
            string folder = Path.Combine(project, "Library", "RailroaderADKTools", "ReimportBackups",
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            if (plan.ModelSource != null) BackUp(plan.ModelPath, folder);
            foreach (string prefab in plan.LinkedPrefabs) BackUp(prefab, folder);
            if (plan.TexturePlan != null)
                foreach (var file in plan.TexturePlan.Files.Where(f => f.Replaces && !f.SameFile))
                {
                    string assetPath = RailroaderAssetPackTools.AssetPathFromAbsolute(file.Destination);
                    if (assetPath == null)
                        throw new InvalidOperationException("Texture backup target is not inside Assets: " + file.Destination);
                    BackUp(assetPath, folder);
                }
            if (plan.TexturePlan != null && updateMaterials)
            {
                string textureFolder = RailroaderAssetPackTools.Absolute(plan.TexturePlan.Target.TextureRoot);
                if (Directory.Exists(textureFolder))
                    foreach (string file in Directory.GetFiles(textureFolder, "*.mat", SearchOption.AllDirectories))
                    {
                        string materialPath = RailroaderAssetPackTools.AssetPathFromAbsolute(file);
                        if (materialPath != null) BackUp(materialPath, folder);
                    }
            }
            return folder;
        }

        internal static string Execute(Plan plan, bool updateMaterials,
            RailroaderShaderSelection selection, Shader shader)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            // Validate again; the source files or target selection could have changed
            // since the confirmation dialog was drawn.
            Plan refreshed = Prepare(plan.Pack, plan.ModelPath, plan.ModelSource,
                plan.TexturePlan == null ? null : plan.TexturePlan.Target,
                plan.TexturePlan == null ? null : plan.TexturePlan.SourceRoot);
            if (plan.TexturePlan != null &&
                !plan.TexturePlan.Files.Select(x => x.Source + " => " + x.Destination)
                    .SequenceEqual(refreshed.TexturePlan.Files.Select(x => x.Source + " => " + x.Destination),
                        StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Texture export changed after confirmation. Preview again.");
            string guidBefore = AssetDatabase.AssetPathToGUID(plan.ModelPath);
            Dictionary<string, string> prefabGuids = refreshed.LinkedPrefabs.ToDictionary(
                p => p, AssetDatabase.AssetPathToGUID, StringComparer.OrdinalIgnoreCase);
            HashSet<string> nodesBefore = ModelNodes(plan.ModelPath);
            string backup = CreateBackup(refreshed, updateMaterials);
            List<string> results = new List<string> { "Backup: " + backup };
            bool modelWritten = false;
            try
            {
                if (refreshed.ModelSource != null)
                {
                    string destination = RailroaderAssetPackTools.Absolute(refreshed.ModelPath);
                    if (!refreshed.SameModelFile)
                    {
                        File.Copy(refreshed.ModelSource, destination, true);
                        modelWritten = true;
                    }
                    AssetDatabase.ImportAsset(refreshed.ModelPath,
                        ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(refreshed.ModelPath) == null)
                        throw new InvalidOperationException("Unity did not import the updated model asset.");
                    if (!string.Equals(guidBefore, AssetDatabase.AssetPathToGUID(refreshed.ModelPath),
                        StringComparison.Ordinal))
                        throw new InvalidOperationException("Model GUID changed unexpectedly. Restore from the backup.");
                    results.Add(refreshed.SameModelFile ? "Existing FBX force-reimported" :
                        "Existing FBX file replaced in place (same GUID)");
                    HashSet<string> after = ModelNodes(refreshed.ModelPath);
                    string[] removed = nodesBefore.Except(after).Take(8).ToArray();
                    string[] added = after.Except(nodesBefore).Take(8).ToArray();
                    if (removed.Length > 0 || added.Length > 0)
                    {
                        // This includes changes in source material slots/mesh names. Such
                        // changes are not automatically repaired, since creator edits can
                        // be deliberate and prefab overrides must never be overwritten.
                        results.Add("WARNING: Imported hierarchy, mesh, or material names changed. " +
                            "Check collider positions, Colorizer mappings, animation paths and materials.");
                        if (removed.Length > 0) results.Add("Previous entries missing/changed: " +
                            string.Join("; ", removed));
                        if (added.Length > 0) results.Add("New/changed entries: " + string.Join("; ", added));
                    }
                    else
                        results.Add("Imported node/mesh/material names unchanged (geometry may still have changed)");
                }
                if (refreshed.TexturePlan != null)
                {
                    results.Add(RailroaderTextureRefresh.Execute(refreshed.TexturePlan,
                        updateMaterials, selection, shader));
                }
                foreach (var kv in prefabGuids)
                {
                    if (!string.Equals(kv.Value, AssetDatabase.AssetPathToGUID(kv.Key),
                        StringComparison.Ordinal))
                        results.Add("WARNING: Prefab GUID changed unexpectedly: " + kv.Key);
                }
                results.Add("No prefab or Definitions.json was directly modified by this tool.");
                results.Add("Check models and all shared liveries in Unity before rebuilding the asset bundle.");
                return string.Join("\n", results.ToArray());
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Reimport failed " + (modelWritten ? "after overwriting the FBX" :
                    "during validation/import") + ". Backup available at: " + backup +
                    ". Partial changes may remain. " + ex.Message, ex);
            }
        }
    }
}
#endif
