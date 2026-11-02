#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Railroader.AssetTools
{
    // Starter definitions with editable presets; model-specific measurements still require review.
    internal enum RailroaderStarterDefinitionKind
    {
        Truck,
        Car,
        DieselLocomotive,
        SteamLocomotive,
        Prop
    }

    [Serializable]
    internal sealed class RailroaderDefinitionOptions
    {
        internal int Axles = 2;
        internal float WheelDiameterMeters = 1.0f;
        internal float TruckWheelbaseMeters = 2.8f;
        // Empty means detect wheel transform paths from the prefab hierarchy.
        internal string WheelPaths = "";
        internal bool BrakeAnimation = true;
        internal string BrakeClip = ""; // Empty => prefab / shared FBX model name
        internal bool TruckColor = true;
        internal int DieselFuelGallons = 600;
        internal bool ClickableCollider = true;
        internal string BaseRoadNumber = ""; // Optional, preserve leading zeros.
    }

    internal static class RailroaderStarterDefinitions
    {
        internal static string DisplayName(string baseName)
        {
            if (string.IsNullOrEmpty(baseName))
            {
                return "New Model";
            }
            return System.Text.RegularExpressions.Regex.Replace(baseName.Replace('_', ' ').Replace('-', ' '),
                @"\s+", " ").Trim();
        }

        private static string Domain(string pack)
        {
            string id = RailroaderAssetPackTools.PackIdentifier(pack);
            string[] parts = id.Split('/');
            if (parts.Length != 2 || parts[0] == "Other" || string.IsNullOrWhiteSpace(parts[0]))
            {
                throw new InvalidOperationException("The asset pack needs a valid domain/name identifier in Info.json");
            }
            return parts[0];
        }

        internal static string DefinitionId(string pack, string prefabName) => Domain(pack) + "/" + prefabName;

        // The current 0.6 game build deserializes Definitions.json into
        // Model.Definition.Container, requiring an object root. The public docs
        // currently show an array, but the game rejects that array on load.
        private static JArray LoadEntries(string path, out JToken root)
        {
            if (!File.Exists(path) || string.IsNullOrWhiteSpace(File.ReadAllText(path)))
            {
                root = new JObject { ["objects"] = new JArray() };
                return (JArray)root["objects"];
            }
            root = JToken.Parse(File.ReadAllText(path), new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
            if (root is JObject container && container["objects"] is JArray entries)
            {
                return entries;
            }
            if (root is JArray)
                throw new InvalidOperationException("This Definitions.json has the top-level array " +
                    "written by v2.8, which the current game rejects. Click 'Convert Array to Container' " +
                    "in Railroader > Definition Starter first.");
            throw new InvalidOperationException("Definitions.json must be an object containing an 'objects' array.");
        }

        // Intentional, explicit migration: never silently rewrite a user-managed
        // Definitions.json. A backup is made before conversion.
        internal static string ConvertArrayToContainer(string pack)
        {
            if (!RailroaderAssetPackTools.ValidPack(pack))
            {
                throw new InvalidOperationException("Choose an existing ADK asset pack first.");
            }
            string relative = pack + "/Definitions.json";
            string path = RailroaderAssetPackTools.Absolute(relative);
            if (!File.Exists(path) || string.IsNullOrWhiteSpace(File.ReadAllText(path)))
            {
                return "There is no existing Definitions.json to convert.";
            }
            JToken parsed = JToken.Parse(File.ReadAllText(path), new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
            if (parsed is JObject existing && existing["objects"] is JArray)
            {
                return "Already uses the object container format. Nothing changed.";
            }
            if (!(parsed is JArray list))
            {
                throw new InvalidOperationException("Expected a top-level array or a container with an 'objects' array. Nothing changed.");
            }
            if (list.Any(item => !(item is JObject)))
            {
                throw new InvalidOperationException("The array contains non-object entries. Nothing changed.");
            }
            if (!EditorUtility.DisplayDialog("Convert Definitions.json?",
                "The current Railroader 0.6 build requires an object root. " +
                "Wrap the " + list.Count + " existing definitions under 'objects'?\n\n" +
                "The original file will be backed up first. All definition entries will be kept.",
                "Convert", "Cancel"))
                return "Conversion canceled. Nothing changed.";
            string backupPath = path + ".before-container-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".bak";
            File.Copy(path, backupPath, false);
            JObject wrapped = new JObject { ["objects"] = list };
            File.WriteAllText(path, wrapped.ToString(Formatting.Indented) + "\n", new UTF8Encoding(false));
            AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceSynchronousImport);
            return "Converted " + list.Count + " entries to the object container format. Backup: " + backupPath;
        }

        internal static string CreateEmptyFileIfMissing(string pack)
        {
            if (!RailroaderAssetPackTools.ValidPack(pack))
            {
                throw new InvalidOperationException("Select an existing ADK asset pack");
            }
            string assetPath = pack + "/Definitions.json";
            string absolute = RailroaderAssetPackTools.Absolute(assetPath);
            if (File.Exists(absolute))
            {
                return "Definitions.json already exists (preserved)";
            }
            File.WriteAllText(absolute,
                new JObject { ["objects"] = new JArray() }.ToString(Formatting.Indented) + "\n",
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            return "Created empty Definitions.json container";
        }

        internal static JObject BuildDraft(string pack, string prefabName,
            RailroaderStarterDefinitionKind kind, string displayName,
            RailroaderDefinitionOptions options, out string warnings)
        {
            options = options ?? new RailroaderDefinitionOptions();
            string prefabPath = pack + "/" + prefabName + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                throw new InvalidOperationException("No root prefab exists at " + prefabPath);
            }
            System.Collections.Generic.List<string> notes = new System.Collections.Generic.List<string>();
            JObject definition = new JObject {
                ["kind"] = kind.ToString(),
                ["modelIdentifier"] = "./" + prefabName
            };
            if ((kind == RailroaderStarterDefinitionKind.Car ||
                 kind == RailroaderStarterDefinitionKind.DieselLocomotive) &&
                !string.IsNullOrWhiteSpace(options.BaseRoadNumber))
            {
                definition["baseRoadNumber"] = options.BaseRoadNumber.Trim();
            }
            if (kind == RailroaderStarterDefinitionKind.Truck)
            {
                if (options.Axles < 1 || options.Axles > 32)
                {
                    throw new InvalidOperationException("Number of axles must be between 1 and 32.");
                }
                if (options.WheelDiameterMeters <= 0 || options.TruckWheelbaseMeters < 0)
                {
                    throw new InvalidOperationException("Truck wheel diameter must be positive and wheelbase non-negative.");
                }
                definition["numberOfAxles"] = options.Axles;
                definition["diameter"] = options.WheelDiameterMeters;
                definition["length"] = options.TruckWheelbaseMeters;
                string[] paths = string.IsNullOrWhiteSpace(options.WheelPaths)
                    ? RailroaderPrefabSetup.WheelPaths(prefabPath)
                    : options.WheelPaths.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim()).Where(x => x.Length != 0).ToArray();
                definition["wheelTransforms"] = new JArray(paths.Select(x =>
                    new JObject { ["path"] = new JArray(x.Split('/').Select(part => (JToken)new JValue(part))) }));
                if (paths.Length == 0)
                {
                    notes.Add("No wheel transforms were detected. Add paths in the editor before using this truck in-game.");
                }
                if (options.BrakeAnimation)
                {
                    string clip = string.IsNullOrWhiteSpace(options.BrakeClip)
                        ? SharedModelName(prefabPath, prefabName) : options.BrakeClip.Trim();
                    definition["brakeAnimation"] = new JObject { ["clipName"] = clip };
                    notes.Add("Verify that AnimationMap contains the brake clip named '" + clip +
                        "'. v2.8.6 does not import or wire animation clips (planned for v2.9).");
                }
                if (options.TruckColor)
                {
                    if (RailroaderPrefabSetup.TruckMaterial(prefabPath) == null)
                    {
                        notes.Add("No material named Truck found. Truck coloring was omitted to avoid a broken material reference.");
                    }
                    else
                    {
                        definition["components"] = new JObject {
                            ["renderergroup-1"] = new JObject {
                                ["kind"] = "RendererGroup", ["styleClass"] = "truck",
                                ["material"] = new JObject { ["materialName"] = "Truck" },
                                ["colorGamma"] = 1.0,
                                ["name"] = "RendererGroup 1",
                                ["transform"] = new JObject {
                                    ["position"] = new JArray(0, 0, 0),
                                    ["rotation"] = new JArray(0, 0, 0, 1),
                                    ["scale"] = new JArray(1, 1, 1)
                                },
                                ["parent"] = JValue.CreateNull(), ["enabled"] = true
                            }
                        };
                    }
                }
                notes.Add("Check wheel diameter (meters), wheelbase (meters) and axle count against the actual truck.");
            }
            else if (kind == RailroaderStarterDefinitionKind.DieselLocomotive)
            {
                if (options.DieselFuelGallons <= 0)
                {
                    throw new InvalidOperationException("Diesel fuel capacity must be greater than zero.");
                }
                definition["carType"] = "LD";
                definition["loadSlots"] = new JArray(new JObject {
                    ["maximumCapacity"] = options.DieselFuelGallons,
                    ["loadUnits"] = "Gallons",
                    ["requiredLoadIdentifier"] = "rr/diesel-fuel"
                });
                notes.Add("Fuel slot is capacity, not starting fuel. Fill the locomotive in game if needed. " +
                    "Set physical dimensions, weight and diesel traction parameters before final testing.");
            }
            else if (kind == RailroaderStarterDefinitionKind.Car)
            {
                notes.Add("Car geometry, carType, weight, truckIdentifier and cargo slots remain model-specific.");
            }
            else if (kind == RailroaderStarterDefinitionKind.SteamLocomotive)
            {
                notes.Add("Steam wheelsets, boiler geometry and tender configuration require manual authoring. " +
                    "Steam automation is intentionally deferred.");
            }
            warnings = String.Join("\n\n", notes);
            return new JObject {
                ["identifier"] = DefinitionId(pack, prefabName),
                ["metadata"] = new JObject {
                    ["name"] = string.IsNullOrWhiteSpace(displayName) ? DisplayName(prefabName) : displayName.Trim()
                },
                ["definition"] = definition
            };
        }

        private static string SharedModelName(string prefabPath, string fallback)
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (root != null)
            {
                // On a prefab variant the corresponding imported model is the original source.
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(root);
                if (source != null && !string.IsNullOrWhiteSpace(source.name))
                {
                    return source.name;
                }
            }
            return fallback;
        }

        internal static string CreateIfMissing(string pack, string prefabName,
            RailroaderStarterDefinitionKind kind, string displayName,
            RailroaderDefinitionOptions options = null)
        {
            if (!RailroaderAssetPackTools.ValidPack(pack) ||
                !RailroaderAssetPackTools.ValidAssetName(prefabName))
                throw new InvalidOperationException("Invalid pack or prefab name");
            string jsonPath = RailroaderAssetPackTools.Absolute(pack + "/Definitions.json");
            JArray entries = LoadEntries(jsonPath, out JToken root);
            string id = DefinitionId(pack, prefabName);
            JObject[] matching = entries.OfType<JObject>()
                .Where(o => string.Equals((string)o["identifier"], id, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matching.Length > 1)
            {
                throw new InvalidOperationException("Duplicate definition identifier: " + id);
            }
            if (matching.Length == 1)
            {
                string current = (string)matching[0]["definition"]?["modelIdentifier"];
                string target = "./" + prefabName;
                return "Already exists: " + id + " (modelIdentifier: " +
                    (current ?? "not set") + "). Kept unchanged." +
                    (!string.Equals(current, target, StringComparison.Ordinal) ?
                        " Use Definition Starter to repair the model reference." : "");
            }
            JObject entry = BuildDraft(pack, prefabName, kind, displayName, options, out string warnings);
            if (!RailroaderDefinitionReview.Confirm(entry.ToString(Formatting.Indented), warnings))
            {
                return "Definition creation canceled. No definitions changed.";
            }
            // Confirmed: configure the prefab before adding any JSON entry, so a missing
            // material never produces an invalid RendererGroup reference.
            options = options ?? new RailroaderDefinitionOptions();
            bool addCollider = options.ClickableCollider &&
                (kind == RailroaderStarterDefinitionKind.Car ||
                 kind == RailroaderStarterDefinitionKind.DieselLocomotive);
            bool mapTruck = kind == RailroaderStarterDefinitionKind.Truck && options.TruckColor;
            if (addCollider || mapTruck)
            {
                RailroaderPrefabSetup.Configure(pack + "/" + prefabName + ".prefab", addCollider, mapTruck);
            }
            // Re-read after modal review; the user may have changed files externally.
            entries = LoadEntries(jsonPath, out root);
            if (entries.OfType<JObject>().Any(o => string.Equals((string)o["identifier"], id,
                StringComparison.OrdinalIgnoreCase)))
                return "Definition appeared during review; existing entry preserved: " + id;
            entries.Add(entry);
            File.WriteAllText(jsonPath, root.ToString(Formatting.Indented) + "\n", new UTF8Encoding(false));
            AssetDatabase.ImportAsset(pack + "/Definitions.json", ImportAssetOptions.ForceSynchronousImport);
            return "Created " + kind + " definition " + id + " (./" + prefabName + ")";
        }

        internal static string FixModelIdentifier(string pack, string prefabName)
        {
            string jsonPath = RailroaderAssetPackTools.Absolute(pack + "/Definitions.json");
            JArray entries = LoadEntries(jsonPath, out JToken root);
            string id = DefinitionId(pack, prefabName);
            JObject[] matches = entries.OfType<JObject>()
                .Where(o => string.Equals((string)o["identifier"], id, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1 || !(matches[0]["definition"] is JObject definition))
            {
                throw new InvalidOperationException("Expected exactly one existing definition for " + id);
            }
            string before = (string)definition["modelIdentifier"];
            string after = "./" + prefabName;
            if (before == after)
            {
                return "Already correct: " + id + " uses " + after;
            }
            if (!EditorUtility.DisplayDialog("Update modelIdentifier?",
                "Only update modelIdentifier for " + id + "?\n\n" +
                "Old: " + (before ?? "(missing)") + "\nNew: " + after +
                "\n\nOther definition fields will be preserved.", "Update", "Cancel"))
                return "Canceled. No files changed.";
            definition["modelIdentifier"] = after;
            File.WriteAllText(jsonPath, root.ToString(Formatting.Indented) + "\n", new UTF8Encoding(false));
            AssetDatabase.ImportAsset(pack + "/Definitions.json", ImportAssetOptions.ForceSynchronousImport);
            return "Updated " + id + " modelIdentifier to " + after;
        }
    }

    /// <summary>Works with existing prefabs; does not require reimporting their FBX.</summary>
    public sealed class RailroaderDefinitionStarter : EditorWindow
    {
        private string pack;
        private int prefabIndex;
        private int templateSourceIndex;
        private bool copyRelatedCarStyles = true;
        private readonly Dictionary<string, string> targetReportingMarks =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private RailroaderStarterDefinitionKind kind = RailroaderStarterDefinitionKind.Truck;
        private string displayName = "";
        private string lastSelected;
        private string status;
        private readonly RailroaderDefinitionOptions options = new RailroaderDefinitionOptions();
        private readonly Dictionary<string, RailroaderDefinitionCopier.RoadNumberOverrides> roadOverrides =
            new Dictionary<string, RailroaderDefinitionCopier.RoadNumberOverrides>(StringComparer.OrdinalIgnoreCase);

        [MenuItem("Railroader/Definition Starter")]
        public static void Open()
        {
            GetWindow<RailroaderDefinitionStarter>("Definition Starter");
        }

        private void OnEnable() => pack = EditorPrefs.GetString("RailroaderTools.SelectedPack", "");

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
            EditorGUILayout.LabelField("Railroader Definition Starter v3.0 Beta 1.5.2", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Creates a starter definition for an existing root prefab. " +
                "The current 0.6 game expects a JSON object containing an 'objects' array, " +
                "despite the published docs showing a top-level array. " +
                "Use Convert Array to Container to repair files created by v2.8.", MessageType.Info);
            pack = RailroaderAssetPackPicker.Draw("Asset Pack", pack);
            if (!RailroaderAssetPackTools.ValidPack(pack))
            {
                return;
            }
            var prefabs = RailroaderAssetPackTools.RootPrefabs(pack);
            if (prefabs.Count == 0)
            {
                EditorGUILayout.HelpBox("Import a model first; this pack has no root prefabs.", MessageType.Warning);
                return;
            }
            prefabIndex = Mathf.Clamp(prefabIndex, 0, prefabs.Count - 1);
            var names = prefabs.Select(Path.GetFileNameWithoutExtension).ToArray();
            prefabIndex = EditorGUILayout.Popup("Root Prefab", prefabIndex, names);
            string chosen = names[prefabIndex];
            if (!string.Equals(lastSelected, pack + "/" + chosen, StringComparison.Ordinal))
            {
                lastSelected = pack + "/" + chosen;
                displayName = RailroaderStarterDefinitions.DisplayName(chosen);
                options.WheelPaths = "";
                options.BrakeClip = "";
            }
            EditorGUILayout.LabelField("Definition ID", RailroaderStarterDefinitions.DefinitionId(pack, chosen));
            EditorGUILayout.LabelField("Model Identifier", "./" + chosen);
            kind = (RailroaderStarterDefinitionKind)EditorGUILayout.EnumPopup("Kind", kind);
            displayName = EditorGUILayout.TextField("Display Name", displayName);
            RailroaderDefinitionOptionsUI.Draw(kind, options, chosen, pack + "/" + chosen + ".prefab");
            EditorGUILayout.HelpBox("A scrollable JSON review opens before creating a new definition. " +
                "Existing entries remain unchanged.", MessageType.Info);
            if (GUILayout.Button("Convert Array to Container (v2.8 repair)"))
            {
                try
                {
                    status = RailroaderStarterDefinitions.ConvertArrayToContainer(pack);
                }
                catch (Exception ex)
                {
                    status = "Failed: " + ex.Message;
                    RailroaderEditorFeedback.ShowError("Definition Starter", ex);
                }
            }
            if (GUILayout.Button("Create Definition (if missing)", GUILayout.Height(30)))
            {
                try
                {
                    status = RailroaderStarterDefinitions.CreateIfMissing(pack, chosen, kind, displayName, options);
                }
                catch (Exception ex)
                {
                    status = "Failed: " + ex.Message;
                    RailroaderEditorFeedback.ShowError("Definition Starter", ex);
                }
            }
            if (GUILayout.Button("Fix Existing Model Identifier"))
            {
                try
                {
                    status = RailroaderStarterDefinitions.FixModelIdentifier(pack, chosen);
                }
                catch (Exception ex)
                {
                    status = "Failed: " + ex.Message;
                    RailroaderEditorFeedback.ShowError("Definition Starter", ex);
                }
            }
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Copy Configuration to Other Models", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Uses the first definition as the default template. Copies its " +
                "configuration to other root-prefab definitions of the SAME kind, preserving " +
                "each destination's identifier, name and model reference. " +
                "Baked Colorizer components and styles stay with their original models. " +
                "Existing destination settings will be replaced; a backup is created.", MessageType.Warning);
            try
            {
                var sources = RailroaderDefinitionCopier.Sources(pack);
                if (sources.Count > 0)
                {
                    templateSourceIndex = Mathf.Clamp(templateSourceIndex, 0, sources.Count - 1);
                    templateSourceIndex = EditorGUILayout.Popup("Source definition", templateSourceIndex,
                        sources.Select(x => x.Label).ToArray());
                    var chosenSource = sources[templateSourceIndex];
                    var targets = sources.Where(x =>
                        !string.Equals(x.Id, chosenSource.Id, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(x.Kind, chosenSource.Kind, StringComparison.OrdinalIgnoreCase)).ToList();
                    EditorGUILayout.LabelField("Compatible targets", targets.Count.ToString());
                    bool hasStyles = string.Equals(chosenSource.Kind, "Car", StringComparison.OrdinalIgnoreCase) &&
                        RailroaderDefinitionCopier.HasLinkedStyles(pack, chosenSource.Id);
                    if (string.Equals(chosenSource.Kind, "Car", StringComparison.OrdinalIgnoreCase))
                    {
                        copyRelatedCarStyles = EditorGUILayout.ToggleLeft(
                            "Copy / create CarStyles and ReportingMarks", copyRelatedCarStyles);
                        if (copyRelatedCarStyles)
                            EditorGUILayout.HelpBox(hasStyles
                                ? "Existing livery styles keep their own marks and numbering series. " +
                                  "Enter a destination mark for new styles; source style settings " +
                                  "are copied as a starting point."
                                : "The source has no linked CarStyle. Enter a reporting mark for " +
                                  "any destination that needs a new CarStyle and ReportingMark. " +
                                  "Leave it blank to copy only the car definition.", MessageType.Info);
                    }
                    bool marksValid = true;
                    var related = new RailroaderDefinitionCopier.RelatedCopyOptions {
                        IncludeCarStyles = string.Equals(chosenSource.Kind, "Car", StringComparison.OrdinalIgnoreCase) &&
                            copyRelatedCarStyles
                    };
                    bool needsStyleMarkInputs = related.IncludeCarStyles || targets.Any(target =>
                        roadOverrides.TryGetValue(target.Id, out var numbers) &&
                        (numbers.HasRange || numbers.OverrideDecalAppearance));
                    if (needsStyleMarkInputs)
                    {
                        foreach (var target in targets)
                        {
                            string currentMark = RailroaderDefinitionCopier.ExistingReportingMark(pack, target.Id);
                            if (!string.IsNullOrWhiteSpace(currentMark))
                            {
                                EditorGUILayout.LabelField(target.Prefab + " (existing reporting mark)", currentMark);
                                related.TargetMarks[target.Id] = currentMark;
                            }
                            else
                            {
                                if (!targetReportingMarks.TryGetValue(target.Id, out string entry))
                                {
                                    entry = "";
                                }
                                entry = EditorGUILayout.TextField("Mark for " + target.Prefab, entry);
                                targetReportingMarks[target.Id] = entry;
                                related.TargetMarks[target.Id] = entry;
                                // A mark is required for cloning an existing source style, or
                                // for numbering/decal overrides that need a destination style.
                                // If the source has no style, an optional supplied mark alone
                                // is enough to create a minimal CarStyle and ReportingMark.
                                bool needsMark = (related.IncludeCarStyles && hasStyles) ||
                                    (roadOverrides.TryGetValue(target.Id, out var road) &&
                                        (road.HasRange || road.OverrideDecalAppearance));
                                if (string.IsNullOrWhiteSpace(entry) && needsMark)
                                {
                                    marksValid = false;
                                }
                            }
                        }
                        if (!marksValid)
                            EditorGUILayout.HelpBox("Enter a reporting mark for each new livery style " +
                                "that needs copying or a numbering/decal override.", MessageType.Warning);
                    }
                    if (chosenSource.Kind == "Car" || chosenSource.Kind == "DieselLocomotive")
                    {
                        EditorGUILayout.Space();
                        EditorGUILayout.LabelField("Optional numbering and decal overrides (per destination)",
                            EditorStyles.boldLabel);
                        EditorGUILayout.HelpBox("Leave blank to inherit the copied value. Numbering series " +
                            "keys belong to CarStyle.carSeries, not the car's reporting mark. " +
                            "Provide a key and start/end to create a new series.", MessageType.None);
                        foreach (var target in targets)
                        {
                            if (!roadOverrides.TryGetValue(target.Id, out var numbers))
                            {
                                numbers = new RailroaderDefinitionCopier.RoadNumberOverrides();
                                roadOverrides[target.Id] = numbers;
                            }
                            EditorGUILayout.LabelField(target.Prefab, EditorStyles.miniBoldLabel);
                            numbers.BaseRoadNumber = EditorGUILayout.TextField("Base road number", numbers.BaseRoadNumber);
                            if (chosenSource.Kind == "Car")
                            {
                                numbers.SeriesName = EditorGUILayout.TextField("Numbering series key", numbers.SeriesName);
                                numbers.SeriesStart = EditorGUILayout.TextField("Range start", numbers.SeriesStart);
                                numbers.SeriesEnd = EditorGUILayout.TextField("Range end", numbers.SeriesEnd);
                                RailroaderDefinitionOptionsUI.DrawDecalFinish(numbers);
                            }
                            related.RoadNumbers[target.Id] = numbers;
                        }
                    }
                    using (new EditorGUI.DisabledScope(targets.Count == 0 || !marksValid))
                    {
                        if (GUILayout.Button("Copy Settings to Other Models of This Kind", GUILayout.Height(28)))
                        {
                            status = RailroaderDefinitionCopier.CopyToTargets(pack,
                                chosenSource.Id, targets.Select(x => x.Id), true, related);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                status = "Definition copy failed: " + ex.Message;
                RailroaderEditorFeedback.ShowError("Definition Copy", ex);
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                EditorGUILayout.HelpBox(status, MessageType.Info);
            }
        }
    }
}
#endif
