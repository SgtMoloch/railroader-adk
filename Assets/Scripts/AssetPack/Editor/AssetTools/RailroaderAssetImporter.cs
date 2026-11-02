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
    /// Imports source FBX/OBJ and texture images into an EXISTING Railroader ADK asset pack,
    /// creates or preserves its own root prefab, and optionally builds its materials or a starter definition.
    /// Does not create asset packs or save the scene.
    /// </summary>
    internal enum RailroaderImportMode
    {
        NewModel,
        NewLiveryFromExistingModel
    }

    public sealed class RailroaderAssetImporter : EditorWindow
    {
        private string pack;
        private string sourceModel = "";
        private string sourceTextureFolder = "";
        private string prefabName = "";
        private RailroaderImportMode importMode;
        private int existingModelIndex;
        private RailroaderImportMode lastDisplayedMode;
        private string selectedPackForDefaults;
        private bool previousDefinitionsExists;
        private bool createDefinitionsFile;
        private bool appendDefinitionEntry = true;
        // Optional and off by default: useful for liveries sharing car/truck configuration.
        private bool copyExistingDefinition;
        private int copySourceIndex;
        private bool copyLinkedCarStyles = true;
        private string newReportingMark = "";
        private string copiedSeriesName = "";
        private string copiedSeriesStart = "";
        private string copiedSeriesEnd = "";
        private readonly RailroaderDefinitionCopier.RoadNumberOverrides appearanceOverrides =
            new RailroaderDefinitionCopier.RoadNumberOverrides();
        private RailroaderStarterDefinitionKind definitionKind = RailroaderStarterDefinitionKind.Truck;
        private string definitionName = "";
        private bool buildMaterials;  // Intentionally DEFAULT OFF, per the importer workflow.
        private bool setupAnimations = true; // ADK AnimationMap setup on the animated FBX root.
        private RailroaderShaderSelection shaderSelection = RailroaderShaderSelection.Automatic;
        private Shader customShader;
        private string status;
        private readonly RailroaderDefinitionOptions definitionOptions = new RailroaderDefinitionOptions();

        [MenuItem("Railroader/Asset Importer")]
        public static void Open()
        {
            GetWindow<RailroaderAssetImporter>("Asset Importer");
        }

        private void OnEnable()
        {
            pack = EditorPrefs.GetString("RailroaderTools.SelectedPack", "");
        }

        private Vector2 windowScroll;

        private void OnGUI()
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(windowScroll, GUILayout.ExpandHeight(true)))
            {
                windowScroll = scroll.scrollPosition;
                DrawContents();
            }
        }

        private void DrawContents()
        {
            EditorGUILayout.LabelField("Railroader Asset Importer v3.0 Beta 1.5.3", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Import a new FBX/OBJ, or create an additional livery from an FBX " +
                "already in this pack. Asset packs must first be created in the ADK.", MessageType.Info);
            if (GUILayout.Button("Reimport an Existing FBX / Textures..."))
                RailroaderModelReimport.Open();
            string priorPack = pack;
            pack = RailroaderAssetPackPicker.Draw("Asset Pack", pack);
            bool validPack = RailroaderAssetPackTools.ValidPack(pack);
            bool fileExists = validPack && File.Exists(
                RailroaderAssetPackTools.Absolute(pack + "/Definitions.json"));
            if (pack != selectedPackForDefaults || fileExists != previousDefinitionsExists)
            {
                createDefinitionsFile = validPack && !fileExists;
                selectedPackForDefaults = pack;
                previousDefinitionsExists = fileExists;
                if (pack != priorPack)
                {
                    existingModelIndex = 0;
                    prefabName = "";
                    definitionName = "";
                }
            }

            importMode = (RailroaderImportMode)GUILayout.Toolbar((int)importMode,
                new[] { "Import New FBX", "New Livery (Reuse FBX)" });
            EditorGUILayout.Space();
            var models = validPack ? RailroaderAssetPackTools.Models(pack) : new List<string>();
            if (importMode != lastDisplayedMode)
            {
                lastDisplayedMode = importMode;
                if (importMode == RailroaderImportMode.NewLiveryFromExistingModel)
                {
                    // The original model usually already owns <model>.prefab. Don't add
                    // an unwanted _Livery suffix or prefill a conflicting prefab name.
                    // A new repaint needs a deliberate, unique name from the creator.
                    prefabName = "";
                }
                else if (importMode == RailroaderImportMode.NewModel && !string.IsNullOrWhiteSpace(sourceModel))
                {
                    prefabName = Path.GetFileNameWithoutExtension(sourceModel);
                }
                definitionName = RailroaderStarterDefinitions.DisplayName(prefabName);
            }
            string selectedModel = null;
            if (importMode == RailroaderImportMode.NewModel)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    sourceModel = EditorGUILayout.TextField("FBX / OBJ", sourceModel);
                    if (GUILayout.Button("Browse...", GUILayout.Width(90)))
                    {
                        string chosen = EditorUtility.OpenFilePanel("Choose FBX or OBJ model",
                            InitialDirectory(sourceModel, DesktopDirectory()), "fbx,obj");
                        if (!string.IsNullOrEmpty(chosen))
                        {
                            sourceModel = chosen;
                            prefabName = Path.GetFileNameWithoutExtension(chosen);
                            definitionName = RailroaderStarterDefinitions.DisplayName(prefabName);
                        }
                    }
                }
            }
            else
            {
                if (models.Count == 0)
                    EditorGUILayout.HelpBox("Import an FBX into this pack first. " +
                        "Liveries reuse a model already imported under Model/ or Models/.", MessageType.Warning);
                else
                {
                    existingModelIndex = Mathf.Clamp(existingModelIndex, 0, models.Count - 1);
                    existingModelIndex = EditorGUILayout.Popup("Shared FBX / OBJ", existingModelIndex,
                        models.Select(x => x.Substring(pack.Length + 1)).ToArray());
                    selectedModel = models[existingModelIndex];
                    EditorGUILayout.HelpBox("The new livery gets its own root prefab and textures. " +
                        "The shared FBX importer's material mappings remain untouched. " +
                        "Materials are assigned only on the new livery prefab.", MessageType.Info);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                sourceTextureFolder = EditorGUILayout.TextField("Texture folder", sourceTextureFolder);
                if (GUILayout.Button("Browse...", GUILayout.Width(90)))
                {
                    string chosen = EditorUtility.OpenFolderPanel("Choose exported textures folder",
                        InitialDirectory(sourceTextureFolder, TextureBrowseDirectory()), "");
                    if (!string.IsNullOrEmpty(chosen))
                    {
                        sourceTextureFolder = chosen;
                    }
                }
            }
            EditorGUILayout.Space();
            prefabName = EditorGUILayout.TextField(importMode == RailroaderImportMode.NewModel
                ? "Root prefab name" : "New livery name", prefabName);
            if (importMode == RailroaderImportMode.NewModel)
                EditorGUILayout.HelpBox("Each model uses Models/<prefab-name>/ and a root prefab " +
                    "named <prefab-name>.prefab (an FBX-based prefab variant). " +
                    "Import a second FBX with a different name to add another model.", MessageType.None);
            else
                EditorGUILayout.HelpBox("Enter a unique name, such as GP38_CSX or GP38_NS. " +
                    "Each gets Models/<name>/Textures/ and <name>.prefab while both " +
                    "reference the same original FBX. No _Livery suffix is added.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Definitions", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(fileExists || !validPack))
                createDefinitionsFile = EditorGUILayout.ToggleLeft(
                    "Create Definitions.json if missing", createDefinitionsFile);
            EditorGUILayout.HelpBox(fileExists
                ? "This pack already has Definitions.json. New model definitions are appended to its objects array."
                : "No Definitions.json detected. File creation is selected by default.", MessageType.None);
            using (new EditorGUI.DisabledScope(!fileExists && !createDefinitionsFile))
                appendDefinitionEntry = EditorGUILayout.ToggleLeft(
                    "Add starter definition for imported prefab (if missing)", appendDefinitionEntry);
            if (appendDefinitionEntry && (fileExists || createDefinitionsFile))
            {
                definitionKind = (RailroaderStarterDefinitionKind)EditorGUILayout.EnumPopup("Definition kind", definitionKind);
                definitionName = EditorGUILayout.TextField("Display name", definitionName);
                EditorGUILayout.HelpBox("Adds a starter entry for each new prefab. Existing entries are preserved.",
                    MessageType.None);
                List<RailroaderDefinitionCopier.Choice> sources;
                try
                {
                    sources = RailroaderDefinitionCopier.Sources(pack, definitionKind.ToString());
                }
                catch (Exception ex)
                {
                    sources = new List<RailroaderDefinitionCopier.Choice>();
                    EditorGUILayout.HelpBox("Could not read existing definitions for copying: " + ex.Message,
                        MessageType.Warning);
                }
                using (new EditorGUI.DisabledScope(sources.Count == 0))
                    copyExistingDefinition = EditorGUILayout.ToggleLeft(
                        "Copy settings from another model definition", copyExistingDefinition);
                if (copyExistingDefinition && sources.Count > 0)
                {
                    copySourceIndex = Mathf.Clamp(copySourceIndex, 0, sources.Count - 1);
                    copySourceIndex = EditorGUILayout.Popup("Copy from", copySourceIndex,
                        sources.Select(x => x.Id).ToArray());
                    EditorGUILayout.HelpBox("After creating a NEW definition, copies the selected model's " +
                        "configuration (components, dimensions, animations, etc.). Keeps the new identifier, " +
                        "display name and model reference. Preserves baked Colorizer settings. " +
                        "Will not overwrite an existing definition. Disabled by default.", MessageType.Warning);
                    if (definitionKind == RailroaderStarterDefinitionKind.Car)
                    {
                        bool sourceHasStyles = RailroaderDefinitionCopier.HasLinkedStyles(pack,
                            sources[copySourceIndex].Id);
                        copyLinkedCarStyles = EditorGUILayout.ToggleLeft(
                            "Copy / create CarStyle and ReportingMark", copyLinkedCarStyles);
                        newReportingMark = EditorGUILayout.TextField("New reporting mark", newReportingMark);
                        if (copyLinkedCarStyles)
                        {
                            EditorGUILayout.HelpBox(sourceHasStyles
                                ? "Enter the new livery's reporting mark to copy its CarStyle. " +
                                  "Source numbering and decal styles are inherited unless overridden below."
                                : "The source has no linked CarStyle. Enter a reporting mark to " +
                                  "create a new CarStyle and ReportingMark, even without numbering " +
                                  "or decal overrides. Leave blank to create neither.", MessageType.Info);
                        }
                    }
                    EditorGUILayout.LabelField("Optional numbering and decal overrides", EditorStyles.boldLabel);
                    if (definitionKind == RailroaderStarterDefinitionKind.Car ||
                        definitionKind == RailroaderStarterDefinitionKind.DieselLocomotive)
                    {
                        EditorGUILayout.HelpBox("Use the single Base road number field in the starter settings " +
                            "below. Leave it blank to inherit the selected definition's value.", MessageType.None);
                    }
                    if (definitionKind == RailroaderStarterDefinitionKind.Car)
                    {
                        copiedSeriesName = EditorGUILayout.TextField("Numbering series key", copiedSeriesName);
                        copiedSeriesStart = EditorGUILayout.TextField("Series start", copiedSeriesStart);
                        copiedSeriesEnd = EditorGUILayout.TextField("Series end", copiedSeriesEnd);
                        EditorGUILayout.HelpBox("Series key is the name inside carSeries (e.g. ACFR), not " +
                            "the reporting mark. Leave it blank to edit the only existing series. " +
                            "For a new series, enter a key, start, end, and reporting mark.", MessageType.None);
                        RailroaderDefinitionOptionsUI.DrawDecalFinish(appearanceOverrides);
                    }
                }
                else if (copyExistingDefinition)
                    EditorGUILayout.HelpBox("No existing definition of this kind to copy. " +
                        "The Importer will create a starter definition instead.", MessageType.Info);
            }
            if (appendDefinitionEntry)
                RailroaderDefinitionOptionsUI.Draw(definitionKind, definitionOptions, prefabName,
                    validPack ? pack + "/" + prefabName + ".prefab" : null);
            EditorGUILayout.HelpBox("Texture picker defaults to " + TextureBrowseDirectory() +
                ". Select one model/livery export folder rather than the entire Substance export library.",
                MessageType.None);

            EditorGUILayout.Space();
            buildMaterials = EditorGUILayout.ToggleLeft("Generate and assign materials after import", buildMaterials);
            if (buildMaterials)
            {
                RailroaderMaterialShaderUI.Draw(ref shaderSelection, ref customShader);
                if (importMode == RailroaderImportMode.NewModel)
                {
                    EditorGUILayout.HelpBox("Material mappings are applied to the new FBX/OBJ importer.", MessageType.None);
                }
                else
                    EditorGUILayout.HelpBox("Materials are applied only as overrides on this livery's " +
                        "root prefab. The shared FBX is unchanged.", MessageType.None);
            }
            else
                EditorGUILayout.HelpBox("Default: copy source assets, create/preserve a prefab, " +
                    "without placing it in the scene. You can build materials separately.", MessageType.None);

            setupAnimations = EditorGUILayout.ToggleLeft(
                "Set up AnimationMap from imported FBX clips (if available)", setupAnimations);
            if (setupAnimations)
                EditorGUILayout.HelpBox("Clips are mapped on the prefab's animated FBX root; existing " +
                    "AnimationMap entries are preserved. Trucks get the required map even if no " +
                    "clips are found. Use Railroader > Animation Builder to review or rename mappings.",
                    MessageType.None);

            bool hasModel = importMode == RailroaderImportMode.NewModel
                ? !string.IsNullOrWhiteSpace(sourceModel) : selectedModel != null;
            using (new EditorGUI.DisabledScope(!validPack || !hasModel ||
                !RailroaderAssetPackTools.ValidAssetName(prefabName) ||
                (buildMaterials && shaderSelection == RailroaderShaderSelection.Custom && customShader == null)))
            {
                if (GUILayout.Button("Import Assets", GUILayout.Height(34)))
                {
                    Import(selectedModel);
                }
            }
            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.HelpBox(status, MessageType.Info);
            }
        }

        private static string DesktopDirectory()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return Directory.Exists(desktop) ? desktop :
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private static string TextureBrowseDirectory()
        {
            // Resolve the logged-in Windows user; never hardcode "C:\Users\User".
            string[] candidateDocuments = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            foreach (string documents in candidateDocuments)
            {
                if (string.IsNullOrEmpty(documents))
                {
                    continue;
                }
                string export = Path.Combine(documents, "Adobe", "Adobe Substance 3D Painter", "export");
                if (Directory.Exists(export))
                {
                    return export;
                }
            }
            return DesktopDirectory();
        }

        private static string InitialDirectory(string path, string fallback)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    return path;
                }
                if (File.Exists(path))
                {
                    return Path.GetDirectoryName(path);
                }
            }
            catch (Exception)
            {
            }
            return fallback;
        }

        private static bool IsImage(string file)
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" ||
                   extension == ".jpeg" || extension == ".tga";
        }

        private static string MaterialPrefix(string filename, string fallback)
        {
            // Substance Painter exports e.g. Body_BaseColor, Body_MetalAOSm,
            // Body_Normal; also support the earlier MainTex/BumpMap conventions.
            return RailroaderTextureNames.TryParse(filename, out string materialName, out _)
                   && !string.IsNullOrWhiteSpace(materialName) ? materialName : fallback;
        }

        private sealed class CopyFile
        {
            internal string From;
            internal string To;
        }

        private static List<CopyFile> PlanCopies(string modelFolder, string modelFile, string textures,
            bool includeModel)
        {
            List<CopyFile> result = new List<CopyFile>();
            if (includeModel)
            {
                string modelName = Path.GetFileName(modelFile);
                result.Add(new CopyFile
                {
                    From = Path.GetFullPath(modelFile),
                    To = RailroaderAssetPackTools.Absolute(modelFolder + "/" + modelName)
                });
                // OBJ sometimes uses an adjacent .mtl sidecar. FBX is recommended.
                if (Path.GetExtension(modelFile).Equals(".obj", StringComparison.OrdinalIgnoreCase))
                {
                    string mtl = Path.ChangeExtension(modelFile, ".mtl");
                    if (File.Exists(mtl))
                        result.Add(new CopyFile { From = Path.GetFullPath(mtl),
                            To = RailroaderAssetPackTools.Absolute(modelFolder + "/" + Path.GetFileName(mtl)) });
                }
            }

            if (!string.IsNullOrWhiteSpace(textures))
            {
                string folder = Path.GetFullPath(textures).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destRoot = RailroaderAssetPackTools.Absolute(modelFolder + "/Textures");
                string fallback = Path.GetFileName(folder);
                foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    if (!IsImage(file)) continue; // Never copy .meta, Blender projects, etc.
                    string relative = file.Substring(folder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    bool directlyInside = !relative.Contains(Path.DirectorySeparatorChar.ToString()) &&
                                          !relative.Contains(Path.AltDirectorySeparatorChar.ToString());
                    string relativeOutput = directlyInside
                        ? Path.Combine(MaterialPrefix(file, fallback), Path.GetFileName(file))
                        : relative;
                    result.Add(new CopyFile { From = Path.GetFullPath(file), To = Path.Combine(destRoot, relativeOutput) });
                }
            }
            return result;
        }

        private void Import(string selectedSharedModel)
        {
            try
            {
                if (!RailroaderAssetPackTools.ValidPack(pack))
                {
                    throw new InvalidOperationException("Choose an existing, loaded ADK asset pack");
                }
                bool livery = importMode == RailroaderImportMode.NewLiveryFromExistingModel;
                string srcModel;
                if (livery)
                {
                    if (string.IsNullOrWhiteSpace(selectedSharedModel) ||
                        !(AssetImporter.GetAtPath(selectedSharedModel) is ModelImporter))
                        throw new InvalidOperationException("Select an existing imported FBX/OBJ in this pack");
                    srcModel = selectedSharedModel;
                    if (!srcModel.StartsWith(pack + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("The shared model must belong to the selected pack");
                    }
                }
                else
                {
                    srcModel = Path.GetFullPath(sourceModel.Trim().Trim('"'));
                    if (!File.Exists(srcModel))
                    {
                        throw new FileNotFoundException("Model not found", srcModel);
                    }
                    string extension = Path.GetExtension(srcModel).ToLowerInvariant();
                    if (extension != ".fbx" && extension != ".obj")
                    {
                        throw new InvalidOperationException("Choose an FBX or OBJ model");
                    }
                }
                string srcTextures = sourceTextureFolder.Trim().Trim('"');
                if (!string.IsNullOrEmpty(srcTextures) && !Directory.Exists(srcTextures))
                {
                    throw new DirectoryNotFoundException("Texture folder not found: " + srcTextures);
                }
                string cleanName = prefabName.Trim();
                if (!RailroaderAssetPackTools.ValidAssetName(cleanName))
                {
                    throw new InvalidOperationException("Enter a valid prefab name without slashes or extensions");
                }

                string prefabPath = pack + "/" + cleanName + ".prefab";
                string modelFolder;
                string modelAssetPath;
                if (livery)
                {
                    modelFolder = pack + "/Models/" + cleanName;
                    modelAssetPath = srcModel;
                    string manifestPath = RailroaderLiveryManifest.PathFor(pack, cleanName);
                    bool existingManifest = File.Exists(RailroaderAssetPackTools.Absolute(manifestPath));
                    if (!existingManifest && (AssetDatabase.LoadMainAssetAtPath(prefabPath) != null ||
                        File.Exists(RailroaderAssetPackTools.Absolute(prefabPath))))
                        throw new InvalidOperationException("A root prefab already uses this livery name. " +
                            "Choose a new name, so existing models are not modified: " + prefabPath);
                    RailroaderLiveryManifest.ValidateExisting(manifestPath, modelAssetPath, prefabPath);
                    if (!existingManifest && string.IsNullOrEmpty(srcTextures))
                    {
                        throw new InvalidOperationException("Choose a Substance Painter texture folder for the new livery");
                    }
                }
                else
                {
                    // Reimports preserve existing old layout when applicable.
                    string legacy = pack + "/Model/" + Path.GetFileName(srcModel);
                    modelFolder = AssetDatabase.LoadAssetAtPath<GameObject>(legacy) != null &&
                        AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null
                        ? pack + "/Model" : pack + "/Models/" + cleanName;
                    modelAssetPath = modelFolder + "/" + Path.GetFileName(srcModel);
                }
                RailroaderAssetPackTools.ValidateNamedPrefab(pack, modelAssetPath, cleanName);
                List<CopyFile> copies = PlanCopies(modelFolder, srcModel, srcTextures, !livery);
                if (copies.Count == 0 && !livery)
                {
                    throw new InvalidOperationException("No importable files selected");
                }
                if (livery && !string.IsNullOrEmpty(srcTextures) && copies.Count == 0)
                {
                    throw new InvalidOperationException("No supported images found in the selected livery folder");
                }
                if (!string.IsNullOrEmpty(srcTextures) && copies.Count == (livery ? 0 : 1))
                {
                    Debug.LogWarning("[Railroader Importer] No supported image files found in texture folder");
                }

                bool definitionsPresent = File.Exists(RailroaderAssetPackTools.Absolute(pack + "/Definitions.json"));
                string copySourceId = null;
                bool definitionAlreadyExists = definitionsPresent &&
                    RailroaderDefinitionCopier.Exists(pack,
                        RailroaderStarterDefinitions.DefinitionId(pack, cleanName));
                if (appendDefinitionEntry && copyExistingDefinition && !definitionAlreadyExists)
                {
                    var sources = RailroaderDefinitionCopier.Sources(pack, definitionKind.ToString());
                    if (sources.Count != 0)
                    {
                        copySourceIndex = Mathf.Clamp(copySourceIndex, 0, sources.Count - 1);
                        copySourceId = sources[copySourceIndex].Id;
                        if (definitionKind == RailroaderStarterDefinitionKind.Car)
                        {
                            bool copiesLinkedStyles = copyLinkedCarStyles &&
                                RailroaderDefinitionCopier.HasLinkedStyles(pack, copySourceId);
                            bool createsOrOverridesStyle = copiesLinkedStyles ||
                                (copyLinkedCarStyles && !string.IsNullOrWhiteSpace(newReportingMark)) ||
                                !string.IsNullOrWhiteSpace(copiedSeriesName) ||
                                !string.IsNullOrWhiteSpace(copiedSeriesStart) ||
                                !string.IsNullOrWhiteSpace(copiedSeriesEnd) ||
                                appearanceOverrides.OverrideDecalAppearance;
                            if (createsOrOverridesStyle)
                            {
                                // Reject malformed marks before copying any imported files.
                                RailroaderDefinitionCopier.ValidateReportingMark(newReportingMark, cleanName);
                            }
                            RailroaderDefinitionCopier.ValidateDecalFinish(appearanceOverrides, cleanName);
                        }
                    }
                }
                if (appendDefinitionEntry && !definitionsPresent && !createDefinitionsFile)
                    throw new InvalidOperationException("Enable 'Create Definitions.json if missing' " +
                        "or turn off 'Add starter definition' before importing");
                string duplicate = copies.GroupBy(x => Path.GetFullPath(x.To), StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(x => x.Count() > 1)?.Key;
                if (duplicate != null)
                {
                    throw new InvalidOperationException("Multiple input files target " + duplicate);
                }
                var overwrite = copies.Where(c => File.Exists(c.To) &&
                    !string.Equals(Path.GetFullPath(c.From), Path.GetFullPath(c.To), StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (overwrite.Count > 0)
                {
                    string sample = String.Join("\n", overwrite.Take(8).Select(c => Path.GetFileName(c.To)));
                    if (overwrite.Count > 8)
                    {
                        sample += "\n...";
                    }
                    if (!EditorUtility.DisplayDialog("Replace existing source files?",
                        overwrite.Count + " destination file(s) already exist:\n" + sample +
                        "\n\nReplace in place? Existing Unity .meta files and prefab references " +
                        "are preserved; this will not delete a populated prefab.",
                        "Replace files", "Cancel"))
                    {
                        status = "Import canceled. Nothing changed.";
                        return;
                    }
                }
                int written = 0, unchanged = 0;
                foreach (CopyFile item in copies)
                {
                    if (string.Equals(Path.GetFullPath(item.From), Path.GetFullPath(item.To),
                        StringComparison.OrdinalIgnoreCase)) { unchanged++; continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(item.To));
                    File.Copy(item.From, item.To, true);
                    written++;
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(modelAssetPath) == null)
                {
                    throw new InvalidOperationException("Unity could not recognize the model " + modelAssetPath);
                }
                string actualPrefabPath = RailroaderAssetPackTools.EnsureNamedPrefab(pack,
                    modelAssetPath, cleanName, out string prefabAction);
                if (livery)
                    RailroaderLiveryManifest.Save(RailroaderLiveryManifest.PathFor(pack, cleanName),
                        modelAssetPath, prefabPath);

                string materialAction = "Not requested";
                if (buildMaterials)
                {
                    string textureRoot = modelFolder + "/Textures";
                    materialAction = livery
                        ? RailroaderMaterialService.BuildLiveryPrefab(modelAssetPath, actualPrefabPath,
                            textureRoot, shaderSelection, customShader)
                        : RailroaderMaterialService.BuildSingleModel(modelAssetPath,
                            textureRoot, shaderSelection, customShader);
                }
                bool clickable = definitionOptions.ClickableCollider &&
                    (definitionKind == RailroaderStarterDefinitionKind.Car ||
                     definitionKind == RailroaderStarterDefinitionKind.DieselLocomotive);
                bool truckMap = definitionKind == RailroaderStarterDefinitionKind.Truck && definitionOptions.TruckColor;
                string prefabSetupAction = RailroaderPrefabSetup.Configure(actualPrefabPath, clickable, truckMap);
                // Map animations BEFORE generating the starter definition so the editor can
                // subsequently validate clipName references without guessing any behavior.
                string animationAction = setupAnimations
                    ? RailroaderAnimationService.AutoSetup(actualPrefabPath,
                        definitionKind == RailroaderStarterDefinitionKind.Truck)
                    : "Not requested";
                string containerAction = createDefinitionsFile
                    ? RailroaderStarterDefinitions.CreateEmptyFileIfMissing(pack)
                    : "Existing Definitions.json kept or creation not requested";
                string definitionAction = appendDefinitionEntry
                    ? RailroaderStarterDefinitions.CreateIfMissing(pack, cleanName, definitionKind,
                        string.IsNullOrWhiteSpace(definitionName)
                            ? RailroaderStarterDefinitions.DisplayName(cleanName) : definitionName.Trim(),
                        definitionOptions)
                    : "Not requested";
                string copiedSettingsAction = "Not requested";
                if (appendDefinitionEntry && copyExistingDefinition &&
                    !definitionAction.StartsWith("Definition creation canceled", StringComparison.Ordinal))
                {
                    if (definitionAlreadyExists)
                    {
                        copiedSettingsAction = "Skipped: definition already existed (preserved)";
                    }
                    else if (copySourceId == null)
                        copiedSettingsAction = "No matching source definition; starter kept";
                    else
                    {
                        string targetId = RailroaderStarterDefinitions.DefinitionId(pack, cleanName);
                        var related = new RailroaderDefinitionCopier.RelatedCopyOptions {
                            IncludeCarStyles = definitionKind == RailroaderStarterDefinitionKind.Car && copyLinkedCarStyles
                        };
                        if (definitionKind == RailroaderStarterDefinitionKind.Car &&
                            !string.IsNullOrWhiteSpace(newReportingMark))
                        {
                            // Number/decal overrides may create a style even when linked-style
                            // copying is disabled; they still need the destination mark.
                            related.TargetMarks[targetId] = newReportingMark;
                        }
                        related.RoadNumbers[targetId] = new RailroaderDefinitionCopier.RoadNumberOverrides {
                            BaseRoadNumber = definitionOptions.BaseRoadNumber,
                            SeriesName = copiedSeriesName,
                            SeriesStart = copiedSeriesStart,
                            SeriesEnd = copiedSeriesEnd,
                            OverrideDecalAppearance = appearanceOverrides.OverrideDecalAppearance,
                            DecalColorHex = appearanceOverrides.DecalColorHex,
                            DecalShininess = appearanceOverrides.DecalShininess
                        };
                        copiedSettingsAction = RailroaderDefinitionCopier.CopyToTargets(pack,
                            copySourceId, new[] { targetId }, false, related);
                    }
                }
                status = $"Copied {written} files ({unchanged} already in place).\n" +
                         $"Prefab: {prefabAction}\n" +
                         $"Materials: {materialAction}\n" +
                         $"Prefab setup: {prefabSetupAction}\n" +
                         $"Animations: {animationAction}\n" +
                         $"Definitions file: {containerAction}\n" +
                         $"Definition entry: {definitionAction}\n" +
                         $"Copied definition settings: {copiedSettingsAction}";
                Debug.Log("[Railroader Importer] " + status.Replace("\n", " | "));
                // On the next draw, file presence updates the 'Create file' default.
            }
            catch (Exception ex)
            {
                status = "Import stopped: " + ex.Message +
                    "\nIf copying had started, files may remain in the pack.";
                RailroaderEditorFeedback.ShowError("Asset Importer", ex);
            }
        }

    }
}
#endif
