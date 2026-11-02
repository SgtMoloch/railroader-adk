#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Railroader.AssetTools
{
    /// <summary>Generates Unity materials and remaps them only on imported FBX/OBJ models.
    /// Prefab creation belongs to RailroaderAssetImporter; it does not change the scene.</summary>
    public sealed class RailroaderMaterialBuilder : EditorWindow
    {
        private bool manualMode;
        private int selectedTab;
        private string pack;
        private string manualTexturePath = "Assets/Textures";
        private GameObject manualModel;
        // Automatic (default) chooses the Railroader shader independently for each texture set.
        private RailroaderShaderSelection shaderSelection = RailroaderShaderSelection.Automatic;
        private Shader customShader;
        private string buildStatus;
        private string refreshStatus;
        private int refreshTargetIndex;
        private string refreshPack;
        private string textureRefreshSource = "";
        private bool updateMaterialsAfterRefresh = true;
        private Vector2 buildScroll;
        private Vector2 refreshScroll;

        [MenuItem("Railroader/Material Builder")]
        public static void Open()
        {
            GetWindow<RailroaderMaterialBuilder>("Material Builder");
        }

        private void OnEnable()
        {
            pack = EditorPrefs.GetString("RailroaderTools.SelectedPack", "");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Railroader Material Builder v3.0 Beta 1.5", EditorStyles.boldLabel);
            selectedTab = GUILayout.Toolbar(selectedTab, new[] { "Build / Map Materials", "Reimport Textures" });
            EditorGUILayout.Space();

            if (selectedTab == 0)
            {
                using (var scroll = new EditorGUILayout.ScrollViewScope(buildScroll, GUILayout.ExpandHeight(true)))
                {
                    buildScroll = scroll.scrollPosition;
                    DrawBuildTab();
                }
            }
            else
            {
                using (var scroll = new EditorGUILayout.ScrollViewScope(refreshScroll, GUILayout.ExpandHeight(true)))
                {
                    refreshScroll = scroll.scrollPosition;
                    DrawReimportTab();
                }
            }
        }

        private void DrawBuildTab()
        {
            manualMode = GUILayout.Toolbar(manualMode ? 1 : 0, new[] { "ADK Asset Pack", "Manual" }) == 1;
            EditorGUILayout.Space();
            if (!manualMode)
            {
                pack = RailroaderAssetPackPicker.Draw("Asset Pack", pack);
                EditorGUILayout.HelpBox("Creates/updates .mat files from per-model Models/<name>/Textures " +
                    "(or legacy Model/Textures) and remaps matching FBX/OBJ materials. " +
                    "Existing-FBX liveries use per-prefab material overrides; base models use FBX remaps.", MessageType.Info);
            }
            else
            {
                manualTexturePath = EditorGUILayout.TextField("Textures folder", manualTexturePath);
                manualModel = (GameObject)EditorGUILayout.ObjectField("FBX / OBJ model", manualModel,
                    typeof(GameObject), false);
            }

            RailroaderMaterialShaderUI.Draw(ref shaderSelection, ref customShader);
            using (new EditorGUI.DisabledScope(
                (shaderSelection == RailroaderShaderSelection.Custom && customShader == null) ||
                (!manualMode && !RailroaderAssetPackTools.ValidPack(pack)) ||
                (manualMode && manualModel == null)))
            {
                if (!manualMode && GUILayout.Button("Preview Pack", GUILayout.Height(24)))
                {
                    try
                    {
                        buildStatus = RailroaderMaterialService.PreviewPack(pack);
                    }
                    catch (Exception ex)
                    {
                        buildStatus = "Preview failed: " + ex.Message;
                        RailroaderEditorFeedback.ShowError("Material Preview", ex);
                    }
                }
                if (GUILayout.Button("Build / Update Materials", GUILayout.Height(30)))
                {
                    try
                    {
                        buildStatus = manualMode
                            ? RailroaderMaterialService.BuildManual(manualTexturePath, shaderSelection,
                                customShader, AssetDatabase.GetAssetPath(manualModel))
                            : RailroaderMaterialService.BuildPack(pack, shaderSelection, customShader);
                        Debug.Log("[Railroader Material Builder] " + buildStatus);
                    }
                    catch (Exception ex)
                    {
                        buildStatus = "Build failed: " + ex.Message;
                        RailroaderEditorFeedback.ShowError("Material Builder", ex);
                    }
                }
            }

            if (!string.IsNullOrEmpty(buildStatus))
            {
                EditorGUILayout.HelpBox(buildStatus, MessageType.Info);
            }
        }

        private void DrawReimportTab()
        {
            pack = RailroaderAssetPackPicker.Draw("Asset Pack", pack);
            if (!RailroaderAssetPackTools.ValidPack(pack))
            {
                EditorGUILayout.HelpBox("Select an existing asset pack to reimport its textures.", MessageType.Info);
                return;
            }

            DrawTextureRefresh();
            if (!string.IsNullOrEmpty(refreshStatus))
            {
                EditorGUILayout.HelpBox(refreshStatus, MessageType.Info);
            }
        }

        private void DrawTextureRefresh()
        {
            if (!string.Equals(pack, refreshPack, StringComparison.OrdinalIgnoreCase))
            {
                refreshPack = pack;
                refreshTargetIndex = 0;
            }

            EditorGUILayout.LabelField("Reimport Substance Painter Textures", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Refresh one existing model or livery without importing the FBX " +
                "or recreating its prefab. Existing texture .meta GUIDs are preserved. " +
                "For shared-FBX liveries, choose the livery (not its source FBX).", MessageType.Info);
            var targets = RailroaderTextureRefresh.Targets(pack);
            if (targets.Count == 0)
            {
                EditorGUILayout.HelpBox("No models or liveries in this pack yet.", MessageType.None);
                return;
            }

            refreshTargetIndex = Mathf.Clamp(refreshTargetIndex, 0, targets.Count - 1);
            refreshTargetIndex = EditorGUILayout.Popup("Model / Livery", refreshTargetIndex,
                targets.Select(t => t.Label).ToArray());
            var target = targets[refreshTargetIndex];
            EditorGUILayout.LabelField("Destination", target.TextureRoot, EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                textureRefreshSource = EditorGUILayout.TextField("Painter export folder", textureRefreshSource);
                if (GUILayout.Button("Browse...", GUILayout.Width(90)))
                {
                    string picked = EditorUtility.OpenFolderPanel("Select updated Painter textures",
                        RefreshBrowseDirectory(), "");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        textureRefreshSource = picked;
                    }
                }
            }

            updateMaterialsAfterRefresh = EditorGUILayout.ToggleLeft(
                "Update existing .mat assets after copying (no FBX remap)", updateMaterialsAfterRefresh);
            if (updateMaterialsAfterRefresh)
            {
                RailroaderMaterialShaderUI.Draw(ref shaderSelection, ref customShader);
            }
            EditorGUILayout.HelpBox("Use the same texture-set names as before. Updating image files " +
                "in place keeps existing material assignments. The optional .mat update changes " +
                "texture slots/shaders but does not reimport or remap the FBX. " +
                "For new material names, use Build / Update Materials separately.", MessageType.None);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(textureRefreshSource) ||
                (updateMaterialsAfterRefresh && shaderSelection == RailroaderShaderSelection.Custom &&
                 customShader == null)))
            {
                if (GUILayout.Button("Preview Texture Reimport", GUILayout.Height(24)))
                {
                    try
                    {
                        refreshStatus = RailroaderTextureRefresh.Prepare(target, textureRefreshSource).Describe();
                    }
                    catch (Exception ex)
                    {
                        refreshStatus = "Texture preview failed: " + ex.Message;
                    }
                }
                if (GUILayout.Button("Reimport Textures for Selected Model / Livery", GUILayout.Height(30)))
                {
                    try
                    {
                        var plan = RailroaderTextureRefresh.Prepare(target, textureRefreshSource);
                        if (EditorUtility.DisplayDialog("Reimport Substance Painter textures?",
                            plan.Describe() + "\n\nExisting textures will be overwritten in place. " +
                            "FBX and prefab assets will not be replaced.", "Reimport", "Cancel"))
                        {
                            refreshStatus = RailroaderTextureRefresh.Execute(plan, updateMaterialsAfterRefresh,
                                shaderSelection, customShader);
                            Debug.Log("[Railroader Material Builder] " + refreshStatus);
                        }
                    }
                    catch (Exception ex)
                    {
                        refreshStatus = "Texture refresh failed: " + ex.Message;
                        RailroaderEditorFeedback.ShowError("Material Builder", ex);
                    }
                }
            }
        }

        private static string RefreshBrowseDirectory()
        {
            string[] documents = {
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Documents"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            foreach (string folder in documents)
            {
                if (string.IsNullOrEmpty(folder))
                {
                    continue;
                }
                string painter = System.IO.Path.Combine(folder, "Adobe", "Adobe Substance 3D Painter", "export");
                if (Directory.Exists(painter))
                {
                    return painter;
                }
            }
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return Directory.Exists(desktop) ? desktop : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    /// <summary>Shared material service, optionally called by Asset Importer.</summary>
    internal static class RailroaderMaterialService
    {
        internal static string PreviewPack(string pack)
        {
            if (!RailroaderAssetPackTools.ValidPack(pack))
            {
                throw new InvalidOperationException("Select an existing ADK asset pack");
            }
            var models = RailroaderAssetPackTools.Models(pack);
            List<string> lines = new List<string>();
            foreach (string model in models)
            {
                string textureRoot = TextureRootForModel(pack, model);
                var groups = textureRoot == null ? new List<TextureGroup>() : MaterialGroups(textureRoot, true);
                lines.Add(Path.GetFileName(model) + ": " + groups.Count + " texture sets" +
                    (groups.Count == 0 ? "" : " (" + String.Join(", ", groups.Select(g => g.Name)) + ")"));
            }
            foreach (var livery in RailroaderLiveryManifest.FindInPack(pack))
            {
                string folder = Path.GetDirectoryName(livery.ManifestPath).Replace('\\', '/') + "/Textures";
                lines.Add("Livery " + Path.GetFileNameWithoutExtension(livery.PrefabPath) + ": " +
                    (Directory.Exists(RailroaderAssetPackTools.Absolute(folder))
                        ? MaterialGroups(folder, true).Count.ToString() + " texture sets" : "no texture folder"));
            }
            return "Pack: " + RailroaderAssetPackTools.PackIdentifier(pack) +
                   "\nImported models: " + models.Count + "\n" + String.Join("\n", lines);
        }

        // Called by the Importer: only the just-imported model is modified.
        internal static string BuildSingleModel(string model, string textureFolder,
            RailroaderShaderSelection selection, Shader customShader)
        {
            if (!(AssetImporter.GetAtPath(model) is ModelImporter))
            {
                throw new InvalidOperationException("Not an imported FBX/OBJ: " + model);
            }
            if (!Directory.Exists(RailroaderAssetPackTools.Absolute(textureFolder)))
            {
                throw new InvalidOperationException("No textures in " + textureFolder);
            }
            var groups = MaterialGroups(textureFolder, true);
            if (groups.Count == 0)
            {
                throw new InvalidOperationException("No recognized maps under " + textureFolder);
            }
            var materials = MakeMaterials(groups, selection, customShader, out int created, out int updated);
            AssetDatabase.SaveAssets();
            RemapResult result = RemapModel(model, materials);
            AssetDatabase.SaveAssets();
            return created + " new, " + updated + " updated; " + result.Matched +
                "/" + result.Total + " source material names matched (" + result.Changed + " remapped)";
        }

        /// <summary>
        /// Builds materials specific to a livery prefab. A shared FBX importer MUST NOT
        /// be remapped here; multiple liveries may reference the exact same model asset.
        /// </summary>
        internal static string BuildLiveryPrefab(string modelPath, string prefabPath,
            string textureFolder, RailroaderShaderSelection selection, Shader customShader)
        {
            if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter))
            {
                throw new InvalidOperationException("Livery source is not an imported FBX/OBJ: " + modelPath);
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                throw new InvalidOperationException("Livery prefab is missing: " + prefabPath);
            }
            if (!Directory.Exists(RailroaderAssetPackTools.Absolute(textureFolder)))
            {
                throw new InvalidOperationException("Livery texture folder is missing: " + textureFolder);
            }
            var groups = MaterialGroups(textureFolder, true);
            if (groups.Count == 0)
            {
                throw new InvalidOperationException("No recognized texture maps under " + textureFolder);
            }
            var materials = MakeMaterials(groups, selection, customShader, out int created, out int updated);
            AssetDatabase.SaveAssets();
            // The FBX's materials may already be remapped to different .mat assets.
            // Recover the original source names through the importer when possible.
            Dictionary<Material, string> originalNames = new Dictionary<Material, string>();
            var modelImporter = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            foreach (var item in modelImporter.GetExternalObjectMap())
                if (item.Key.type == typeof(Material) && item.Value is Material material &&
                    !originalNames.ContainsKey(material))
                    originalNames.Add(material, item.Key.name);
            int matched = 0, slots = 0;
            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(prefabPath);
                foreach (Renderer renderer in contents.GetComponentsInChildren<Renderer>(true))
                {
                    var sourceRenderer = PrefabUtility.GetCorrespondingObjectFromSource(renderer) as Renderer;
                    if (sourceRenderer == null)
                    {
                        continue;
                    }
                    // The FBX may have a base livery remapped already. Use the
                    // original renderer's material names, NOT any current override.
                    var sourceMaterials = sourceRenderer.sharedMaterials;
                    var slotsArray = renderer.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < slotsArray.Length; ++i)
                    {
                        ++slots;
                        Material original = i < sourceMaterials.Length ? sourceMaterials[i] : null;
                        string name = null;
                        if (original != null)
                            name = originalNames.TryGetValue(original, out string sourceName)
                                ? sourceName : original.name;
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            continue;
                        }
                        name = name.Replace(" (Instance)", "");
                        if (!materials.TryGetValue(name, out Material generated))
                        {
                            continue;
                        }
                        ++matched;
                        if (slotsArray[i] == generated)
                        {
                            continue;
                        }
                        slotsArray[i] = generated;
                        changed = true;
                    }
                    if (changed)
                    {
                        renderer.sharedMaterials = slotsArray;
                        // Save renderer material overrides on the nested FBX instance.
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    }
                }
                if (matched == 0)
                    throw new InvalidOperationException("No livery texture names matched this FBX's " +
                        "material slots. Source materials must match the Painter texture-set names. " +
                        "Nothing was saved to the prefab.");
                if (PrefabUtility.SaveAsPrefabAsset(contents, prefabPath) == null)
                {
                    throw new InvalidOperationException("Could not save livery prefab: " + prefabPath);
                }
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
            }
            return created + " new, " + updated + " updated; " + matched + "/" + slots +
                " livery material slots matched on prefab (shared FBX untouched)";
        }

        internal static string TextureRootForModel(string pack, string model)
        {
            string folder = Path.GetDirectoryName(model).Replace('\\', '/');
            // The new convention isolates textures/materials for each model.
            string scoped = folder + "/Textures";
            if (Directory.Exists(RailroaderAssetPackTools.Absolute(scoped)))
            {
                return scoped;
            }
            // Legacy model structure may contain multiple FBXs sharing one texture folder.
            if (model.StartsWith(pack + "/Model/", StringComparison.OrdinalIgnoreCase))
            {
                return FindPackTextureRoot(pack);
            }
            return null;
        }

        internal static string BuildPack(string pack, RailroaderShaderSelection selection, Shader customShader)
        {
            if (!RailroaderAssetPackTools.ValidPack(pack))
            {
                throw new InvalidOperationException("Select an existing ADK asset pack");
            }
            var models = RailroaderAssetPackTools.Models(pack);
            if (models.Count == 0)
            {
                throw new InvalidOperationException("No FBX/OBJ assets under Model or Models");
            }
            List<string> summaries = new List<string>();
            foreach (string model in models)
            {
                string root = TextureRootForModel(pack, model);
                if (root == null)
                {
                    summaries.Add(Path.GetFileName(model) + ": skipped (no texture folder)");
                    continue;
                }
                summaries.Add(Path.GetFileName(model) + ": " +
                    BuildSingleModel(model, root, selection, customShader));
            }
            foreach (var livery in RailroaderLiveryManifest.FindInPack(pack))
            {
                string root = Path.GetDirectoryName(livery.ManifestPath).Replace('\\', '/') + "/Textures";
                if (!Directory.Exists(RailroaderAssetPackTools.Absolute(root)))
                {
                    summaries.Add(Path.GetFileNameWithoutExtension(livery.PrefabPath) +
                        ": skipped (livery textures missing)");
                    continue;
                }
                summaries.Add(Path.GetFileNameWithoutExtension(livery.PrefabPath) + ": " +
                    BuildLiveryPrefab(livery.SourceModelPath, livery.PrefabPath, root, selection, customShader));
            }
            return String.Join("\n", summaries);
        }

        /// <summary>Update material asset properties without touching FBX remaps or prefab overrides.
        /// Existing .mat assets keep their GUIDs, so all prefab and MaterialMap references survive.
        /// New .mat assets are generated but are not automatically assigned to renderers.</summary>
        internal static string UpdateMaterialAssetsOnly(string textureFolder,
            RailroaderShaderSelection selection, Shader customShader)
        {
            if (!Directory.Exists(RailroaderAssetPackTools.Absolute(textureFolder)))
            {
                throw new InvalidOperationException("No texture folder: " + textureFolder);
            }
            var groups = MaterialGroups(textureFolder, true);
            if (groups.Count == 0)
            {
                throw new InvalidOperationException("No supported texture maps in " + textureFolder);
            }
            MakeMaterials(groups, selection, customShader, out int created, out int updated);
            AssetDatabase.SaveAssets();
            return "Material assets: " + created + " created, " + updated + " updated";
        }

        internal static string BuildManual(string source, RailroaderShaderSelection selection,
            Shader customShader, string model)
        {
            string folder = source?.Trim().Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(folder))
            {
                throw new InvalidOperationException("Choose a texture folder");
            }
            if (!folder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                string converted = RailroaderAssetPackTools.AssetPathFromAbsolute(folder);
                if (converted != null)
                {
                    folder = converted;
                }
            }
            if (!folder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(RailroaderAssetPackTools.Absolute(folder)))
                throw new InvalidOperationException("Texture folder must be inside Unity Assets: " + folder);
            if (string.IsNullOrEmpty(model) || !(AssetImporter.GetAtPath(model) is ModelImporter))
            {
                throw new InvalidOperationException("Select an imported FBX/OBJ model asset");
            }
            var groups = MaterialGroups(folder, true);
            var materials = MakeMaterials(groups, selection, customShader, out int created, out int updated);
            AssetDatabase.SaveAssets();
            RemapResult result = RemapModel(model, materials);
            AssetDatabase.SaveAssets();
            return "Materials: " + created + " new, " + updated + " updated; " +
                "FBX material remaps: " + result.Changed + " changed, " + result.Already +
                " already correct; " + result.Matched + "/" + result.Total + " material names matched.";
        }

        private static string FindPackTextureRoot(string pack)
        {
            string[] preferred = { pack + "/Model/Textures", pack + "/Textures" };
            var existing = preferred.Where(p => Directory.Exists(RailroaderAssetPackTools.Absolute(p))).ToList();
            if (existing.Count > 1) throw new InvalidOperationException(
                "Both Model/Textures and Textures exist. Choose one folder structure before building.");
            if (existing.Count == 1)
            {
                return existing[0];
            }
            string model = pack + "/Model";
            return Directory.Exists(RailroaderAssetPackTools.Absolute(model)) && HasMaps(model) ? model : null;
        }

        private static bool ImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga";
        }

        private sealed class TextureGroup
        {
            internal string Name;
            internal string OutputFolder;
            internal readonly Dictionary<string, string> Maps =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static bool HasMaps(string folder) =>
            Directory.GetFiles(RailroaderAssetPackTools.Absolute(folder))
                .Any(f => ImageFile(f) && RailroaderTextureNames.TryParse(f, out _, out _));

        /// <summary>
        /// Group by the texture-set prefix, even if Painter dumped all of its maps
        /// in a single directory or an older importer put them in a generic folder.
        /// Keep existing per-material directories and material assets in place.
        /// Never move image files, which could break their existing Unity GUIDs.
        /// </summary>
        private static List<TextureGroup> MaterialGroups(string root, bool recursive)
        {
            string absolute = RailroaderAssetPackTools.Absolute(root);
            var folders = new List<string> { root };
            foreach (string child in Directory.GetDirectories(absolute, "*",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                string asset = RailroaderAssetPackTools.AssetPathFromAbsolute(child);
                if (asset != null)
                {
                    folders.Add(asset);
                }
            }
            Dictionary<string, TextureGroup> groups = new Dictionary<string, TextureGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in folders)
            {
                foreach (string file in Directory.GetFiles(RailroaderAssetPackTools.Absolute(folder))
                    .Where(ImageFile).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    if (!RailroaderTextureNames.TryParse(file, out string prefix, out string property))
                    {
                        continue;
                    }
                    string name = !string.IsNullOrEmpty(prefix) ? prefix : Path.GetFileName(folder);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }
                    // A normal Body/ folder already present in an asset pack should
                    // retain its existing .mat path; otherwise create Body/Body.mat
                    // alongside the group root without relocating the PNG textures.
                    string destination = string.Equals(Path.GetFileName(folder), name,
                        StringComparison.OrdinalIgnoreCase) ? folder : root + "/" + name;
                    if (!groups.TryGetValue(name, out TextureGroup group))
                    {
                        group = new TextureGroup { Name = name, OutputFolder = destination };
                        groups.Add(name, group);
                    }
                    else if (folder == group.OutputFolder)
                    {
                        group.OutputFolder = destination;
                    }
                    string assetFile = folder + "/" + Path.GetFileName(file);
                    if (group.Maps.TryGetValue(property, out string previous))
                    {
                        // Upgrading an old import can leave a duplicate in the old
                        // catch-all folder. Prefer the explicit Body/Body_* map
                        // rather than breaking reimport or modifying either PNG.
                        string oldFolder = Path.GetDirectoryName(previous)?.Replace('\\', '/');
                        bool oldCanonical = string.Equals(Path.GetFileName(oldFolder), name,
                            StringComparison.OrdinalIgnoreCase);
                        bool newCanonical = string.Equals(Path.GetFileName(folder), name,
                            StringComparison.OrdinalIgnoreCase);
                        if (newCanonical && !oldCanonical)
                        {
                            group.Maps[property] = assetFile;
                        }
                        else if (!oldCanonical || newCanonical == oldCanonical)
                            throw new InvalidOperationException("Ambiguous " + property + " maps for '" +
                                name + "': " + previous + " and " + assetFile +
                                ". Remove or rename the duplicate texture.");
                        continue;
                    }
                    group.Maps.Add(property, assetFile);
                }
            }
            return groups.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string DetectWorkflow(TextureGroup group)
        {
            bool metallic = group.Maps.ContainsKey("_MetallicGlossMap");
            bool specular = group.Maps.ContainsKey("_SpecGlossMap");
            if (metallic && specular)
            {
                return "CONFLICT: metallic + specular";
            }
            if (specular)
            {
                return "Specular";
            }
            if (metallic)
            {
                return "Metallic";
            }
            return "Metallic default (no workflow map)";
        }

        private static Shader ResolveShader(TextureGroup group, RailroaderShaderSelection selection,
            Shader customShader)
        {
            bool metallic = group.Maps.ContainsKey("_MetallicGlossMap");
            bool specular = group.Maps.ContainsKey("_SpecGlossMap");
            if (metallic && specular)
                throw new InvalidOperationException("Material '" + group.Name +
                    "' contains both metallic and specular maps. Keep one export workflow per material.");

            if (selection == RailroaderShaderSelection.Metallic && specular ||
                selection == RailroaderShaderSelection.Specular && metallic)
                throw new InvalidOperationException("Material '" + group.Name + "' is " + DetectWorkflow(group) +
                    ", but the selected shader workflow is " + selection + ". Choose Automatic.");

            bool useSpecular = selection == RailroaderShaderSelection.Specular ||
                (selection == RailroaderShaderSelection.Automatic && specular);
            Shader shader = selection == RailroaderShaderSelection.Custom
                ? customShader : RailroaderAssetPackTools.FindCarShader(useSpecular);
            if (shader == null)
            {
                throw new InvalidOperationException("No shader selected for material '" + group.Name + "'.");
            }

            string required = metallic ? "_MetallicGlossMap" : specular ? "_SpecGlossMap" : null;
            if (required != null && shader.FindPropertyIndex(required) < 0)
                throw new InvalidOperationException("Shader '" + shader.name + "' does not support " + required +
                    " required by material '" + group.Name + "'. Use Automatic shader selection.");
            return shader;
        }

        private static Dictionary<string, Material> MakeMaterials(List<TextureGroup> groups,
            RailroaderShaderSelection selection, Shader customShader, out int created, out int updated)
        {
            if (groups.Count == 0)
            {
                throw new InvalidOperationException("No supported textures found");
            }
            Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            created = 0;
            updated = 0;
            // Resolve all shader choices before touching .mat assets, so an invalid
            // workflow or missing ADK shader never leaves the pack half-updated.
            var shaders = groups.ToDictionary(g => g.Name,
                g => ResolveShader(g, selection, customShader), StringComparer.OrdinalIgnoreCase);
            foreach (TextureGroup group in groups)
            {
                // Detect per material, not per asset pack: hybrid packs are supported.
                Shader shader = shaders[group.Name];
                string destination = group.OutputFolder;
                if (!AssetDatabase.IsValidFolder(destination))
                {
                    Directory.CreateDirectory(RailroaderAssetPackTools.Absolute(destination));
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
                string path = destination + "/" + group.Name + ".mat";
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool fresh = mat == null;
                if (fresh)
                {
                    if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                    {
                        throw new InvalidOperationException("A non-material asset exists at " + path);
                    }
                    mat = new Material(shader) { name = group.Name };
                }
                else
                {
                    Undo.RecordObject(mat, "Update Railroader material");
                    mat.shader = shader;
                }
                // Prevent old workflow keywords lingering when updating an existing .mat.
                if (group.Maps.ContainsKey("_SpecGlossMap"))
                {
                    mat.DisableKeyword("_METALLICGLOSSMAP");
                }
                if (group.Maps.ContainsKey("_MetallicGlossMap"))
                {
                    mat.DisableKeyword("_SPECGLOSSMAP");
                }
                int assigned = 0;
                foreach (var map in group.Maps)
                {
                    string property = map.Key;
                    string texAsset = map.Value;
                    if (!mat.HasProperty(property))
                    {
                        Debug.LogWarning("[Material Builder] Shader '" + shader.name +
                            "' lacks " + property + " for " + texAsset);
                        continue;
                    }
                    TextureImporter textureImporter = AssetImporter.GetAtPath(texAsset) as TextureImporter;
                    if (textureImporter != null)
                    {
                        bool changed = false;
                        if (property == "_BumpMap" &&
                            textureImporter.textureType != TextureImporterType.NormalMap)
                        {
                            textureImporter.textureType = TextureImporterType.NormalMap;
                            changed = true;
                        }
                        // Both packed workflows store smoothness in alpha. Preserve it
                        // even when Unity had previously imported this as RGB-only.
                        if ((property == "_SpecGlossMap" || property == "_MetallicGlossMap") &&
                            textureImporter.alphaSource != TextureImporterAlphaSource.FromInput)
                        {
                            textureImporter.alphaSource = TextureImporterAlphaSource.FromInput;
                            changed = true;
                        }
                        // AO, packed metallic, and normals contain linear data.
                        // Base color and specular RGB are sRGB.
                        bool srgb = property == "_MainTex" || property == "_SpecGlossMap";
                        if (textureImporter.sRGBTexture != srgb)
                        {
                            textureImporter.sRGBTexture = srgb;
                            changed = true;
                        }
                        if (changed)
                        {
                            textureImporter.SaveAndReimport();
                        }
                    }
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texAsset);
                    if (texture == null)
                    {
                        Debug.LogWarning("[Material Builder] Texture unavailable: " + texAsset);
                        continue;
                    }
                    mat.SetTexture(property, texture);
                    assigned++;
                    if (property == "_BumpMap")
                    {
                        mat.EnableKeyword("_NORMALMAP");
                    }
                    if (property == "_MetallicGlossMap")
                    {
                        mat.EnableKeyword("_METALLICGLOSSMAP");
                    }
                    if (property == "_SpecGlossMap")
                    {
                        mat.EnableKeyword("_SPECGLOSSMAP");
                    }
                    if ((property == "_MetallicGlossMap" || property == "_SpecGlossMap") &&
                        mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.92f);
                }
                if (assigned == 0)
                    throw new InvalidOperationException("No compatible texture maps could be assigned to '" +
                        group.Name + "' using shader '" + shader.name + "'.");
                if (fresh)
                {
                    AssetDatabase.CreateAsset(mat, path); created++;
                }
                else
                {
                    EditorUtility.SetDirty(mat); updated++;
                }
                materials[group.Name] = mat;
            }
            return materials;
        }

        private sealed class RemapResult
        {
            internal int Changed;
            internal int Already;
            internal int Matched;
            internal int Total;
        }

        /// <summary>Map generated materials to the material identifiers of the imported model.
        /// Does not edit prefab renderer slots or scene instances.</summary>
        private static RemapResult RemapModel(string model, Dictionary<string, Material> generated)
        {
            ModelImporter importer = AssetImporter.GetAtPath(model) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("Not an FBX/OBJ import: " + model);
            }

            Dictionary<string, AssetImporter.SourceAssetIdentifier> sources = new Dictionary<string, AssetImporter.SourceAssetIdentifier>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Some Unity versions expose sourceMaterials differently. Reflection
                // makes the source names available without hard-coding an API version.
                PropertyInfo property = typeof(ModelImporter).GetProperty("sourceMaterials",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.GetValue(importer, null) is AssetImporter.SourceAssetIdentifier[] list)
                    foreach (var id in list)
                        if (id.type == typeof(Material) && !string.IsNullOrEmpty(id.name))
                        {
                            sources[id.name] = id;
                        }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Material Builder] Unable to inspect sourceMaterials on " +
                    model + ": " + ex.Message);
            }
            // External object map preserves original source identifiers even when
            // the .mat asset it used to point to has been deleted.
            var current = importer.GetExternalObjectMap();
            foreach (var pair in current)
                if (pair.Key.type == typeof(Material) && !string.IsNullOrEmpty(pair.Key.name) &&
                    !sources.ContainsKey(pair.Key.name))
                    sources[pair.Key.name] = pair.Key;
            // Embedded FBX material subassets provide another fallback.
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(model))
                if (sub is Material mat && !sources.ContainsKey(mat.name))
                {
                    sources[mat.name] = new AssetImporter.SourceAssetIdentifier(typeof(Material), mat.name);
                }

            var result = new RemapResult { Total = sources.Count };
            foreach (var pair in sources)
            {
                if (!generated.TryGetValue(pair.Key, out Material replacement))
                {
                    continue;
                }
                result.Matched++;
                if (current.TryGetValue(pair.Value, out UnityEngine.Object existing) && existing == replacement)
                {
                    result.Already++;
                    continue;
                }
                importer.AddRemap(pair.Value, replacement);
                result.Changed++;
            }
            if (result.Changed > 0)
            {
                importer.SaveAndReimport();
            }
            if (result.Total == 0)
                Debug.LogWarning("[Material Builder] No FBX material identifiers discovered in " + model +
                    ". Check the imported model's Materials tab.");
            else if (result.Matched == 0)
                Debug.LogWarning("[Material Builder] No matching material names for " + model +
                    ". FBX names: " + String.Join(", ", sources.Keys) +
                    "; generated: " + String.Join(", ", generated.Keys));
            return result;
        }
    }

    internal enum RailroaderShaderSelection

    {

        Automatic,

        Metallic,

        Specular,

        Custom

    }

    /// <summary>Shared shader selection in Importer and Builder.</summary>
    internal static class RailroaderMaterialShaderUI
    {
        internal static void Draw(ref RailroaderShaderSelection choice, ref Shader customShader)
        {
            int selected = EditorGUILayout.Popup("Shader workflow", (int)choice,
                new[] { "Automatic (by texture names)", "Metallic", "Specular", "Custom shader" });
            choice = (RailroaderShaderSelection)selected;
            if (choice == RailroaderShaderSelection.Custom)
                customShader = (Shader)EditorGUILayout.ObjectField("Custom Shader", customShader,
                    typeof(Shader), false);
            else if (choice == RailroaderShaderSelection.Automatic)
                EditorGUILayout.HelpBox("Uses Railroader Metallic for _MetalAOSm/_MetallicGlossMap, " +
                    "Railroader Specular for _SpecularSmoothness/_SpecGlossMap, " +
                    "and Metallic if neither is present. Detection is per material.", MessageType.None);
        }
    }

    /// <summary>Shared dropdown for both windows; only packs already created in ADK are eligible.</summary>
    internal static class RailroaderAssetPackPicker
    {
        internal static string Draw(string label, string selected)
        {
            // Sort by identifier, not the arbitrary local folder path, so all packs
            // belonging to one domain stay together in the dropdown.
            var entries = RailroaderAssetPackTools.Packs()
                .Select(p => new { Path = p, Identifier = RailroaderAssetPackTools.PackIdentifier(p) })
                .OrderBy(e => e.Identifier, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<string> packs = entries.Select(e => e.Path).ToList();
            // EditorGUILayout.Popup interprets '/' as a submenu separator, e.g.
            // sgtmoloch/test appears under sgtmoloch > test. Use Info.json identity,
            // not the asset pack's directory name (which ADK does not require to match).
            var counts = entries.GroupBy(e => e.Identifier, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            string[] identifiers = entries.Select(e => counts[e.Identifier] > 1
                ? e.Identifier + " (" + System.IO.Path.GetFileName(e.Path) + ")" : e.Identifier).ToArray();
            string[] options = (new[] { "<Select existing pack>" }).Concat(identifiers).ToArray();
            int current = Math.Max(0, packs.FindIndex(p => string.Equals(p, selected, StringComparison.OrdinalIgnoreCase)) + 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                int next = EditorGUILayout.Popup(label, current, options);
                if (next != current)
                {
                    selected = next == 0 ? "" : packs[next - 1];
                    EditorPrefs.SetString("RailroaderTools.SelectedPack", selected);
                }
                if (GUILayout.Button("Refresh", GUILayout.Width(72)))
                {
                    AssetDatabase.Refresh();
                }
            }
            DefaultAsset folder = string.IsNullOrEmpty(selected) ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(selected);
            DefaultAsset newFolder = (DefaultAsset)EditorGUILayout.ObjectField("Or drag pack folder", folder, typeof(DefaultAsset), false);
            if (newFolder != folder)
            {
                string newPath = newFolder ? AssetDatabase.GetAssetPath(newFolder) : "";
                if (!string.IsNullOrEmpty(newPath) && !RailroaderAssetPackTools.ValidPack(newPath))
                {
                    Debug.LogWarning("[Railroader Tools] Select an existing asset pack folder with Info.json");
                }
                else
                {
                    selected = newPath;
                    EditorPrefs.SetString("RailroaderTools.SelectedPack", selected);
                }
            }
            return selected;
        }
    }
}
#endif
