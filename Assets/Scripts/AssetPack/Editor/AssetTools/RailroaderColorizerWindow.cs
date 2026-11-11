#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Railroader.AssetTools.Colorizer;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Railroader.AssetTools
{
    /// <summary>
    /// ADK-native colorizer authoring and baking UI. No standalone package required.
    /// Only existing packs are selectable; no pack creation or runtime components.
    /// </summary>
    public sealed class RailroaderColorizerWindow : EditorWindow
    {
        private string pack;
        private string carIdentifier = "";
        private GameObject authoringPrefab;
        private int rootIndex;
        private string outputPrefabName = "";
        private string result;
        private bool generateSelectableStyle = true;
        private bool showParts = true;
        private Vector2 windowScroll;
        private bool canBake;
        private string bakeAuthorPath = "";
        private string bakeRootPath = "";
        private string inspectedPrefab = "";
        private List<ColorizerMarkerBatch.Slot> partSlots = new List<ColorizerMarkerBatch.Slot>();
        private string sharedStyleClass = "body";
        private string sharedControlLabel = "Body";
        private Color commonDefaultColor = Color.white;
        private float commonColorGamma = 1f;
        private string blacklistLoadedPack = "";
        private string blacklistText = "";
        private bool showBlacklist = true;

        [MenuItem("Railroader/Colorizer Baker")]
        public static void Open()
        {
            GetWindow<RailroaderColorizerWindow>("Colorizer Baker");
        }

        private void OnEnable()
        {
            pack = EditorPrefs.GetString("RailroaderTools.SelectedPack", "");
        }

        private void OnGUI()
        {
            canBake = false;
            // Scroll the entire configuration area, not just the parts list.
            // Bake remains outside the scroll view so it is ALWAYS reachable.
            using (var scroll = new EditorGUILayout.ScrollViewScope(windowScroll, GUILayout.ExpandHeight(true)))
            {
                windowScroll = scroll.scrollPosition;
                DrawConfiguration();
                if (!string.IsNullOrWhiteSpace(result))
                {
                    EditorGUILayout.HelpBox(result, MessageType.Info);
                }
            }

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(!canBake))
            {
                if (GUILayout.Button("Bake Colorizers", GUILayout.Height(33)))
                {
                    bool confirm = EditorUtility.DisplayDialog("Update existing root prefab?",
                        "This will update the Colorizer materials and MaterialMap on\n" + bakeRootPath +
                        "\n\nThe existing game prefab hierarchy, colliders and other components " +
                        "will be preserved and checked after saving. The Authoring/ copy remains untouched.\n\n" +
                        "Generated materials and " + pack + "/Definitions.json will also be updated. " +
                        "File backups are created first.\n\n" +
                        "Pull any changes made in the in-game Definition Editor before proceeding.",
                        "Bake / Update", "Cancel");
                    if (confirm)
                    {
                        try
                        {
                            result = Bake(pack, carIdentifier, bakeAuthorPath, outputPrefabName, generateSelectableStyle);
                        }
                        catch (Exception ex)
                        {
                            Error(ex);
                        }
                    }
                }
            }
        }

        private void DrawConfiguration()
        {
            EditorGUILayout.LabelField("Railroader Colorizer Baker", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Create the pack and car definition with the ADK first. This tool keeps " +
                "your editable prefab under Authoring/ and updates only the Colorizer materials and mappings " +
                "on the EXISTING root prefab (preserving colliders and other custom components). " +
                "Only one game-facing prefab is produced. Pull any in-game Definitions edits before baking.", MessageType.Info);
            string previousPack = pack;
            pack = RailroaderAssetPackPicker.Draw("Asset Pack", pack);
            if (pack != previousPack)
            {
                authoringPrefab = null;
                carIdentifier = "";
                rootIndex = 0;
                outputPrefabName = "";
                inspectedPrefab = "";
                blacklistLoadedPack = "";
                blacklistText = "";
            }
            if (!RailroaderAssetPackTools.ValidPack(pack))
            {
                return;
            }

            var cars = ReadCars(pack);
            if (cars.Count == 0)
            {
                EditorGUILayout.HelpBox("No car/locomotive definitions found in this pack's Definitions.json. " +
                    "Create the definition through the Railroader editor, then Pull it into the ADK.", MessageType.Warning);
            }
            else
            {
                string[] options = cars.ToArray();
                int current = Math.Max(0, cars.IndexOf(carIdentifier));
                int selected = EditorGUILayout.Popup("Car Definition", current, options);
                carIdentifier = cars[selected];
            }
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("1. Prepare an authoring prefab", EditorStyles.boldLabel);
            var rootPrefabs = RailroaderAssetPackTools.RootPrefabs(pack)
                .Where(path => !IsGenerated(path))
                .ToList();
            if (rootPrefabs.Count == 0)
            {
                EditorGUILayout.HelpBox("There are no ungenerated root prefabs. Use Asset Importer first, " +
                    "or select an existing prefab under Authoring/ below.", MessageType.Info);
            }
            else
            {
                rootIndex = Mathf.Clamp(rootIndex, 0, rootPrefabs.Count - 1);
                string[] names = rootPrefabs.Select(Path.GetFileNameWithoutExtension).ToArray();
                int nextIndex = EditorGUILayout.Popup("Source root prefab", rootIndex, names);
                if (nextIndex != rootIndex)
                {
                    rootIndex = nextIndex;
                    authoringPrefab = null;
                    outputPrefabName = "";
                    inspectedPrefab = "";
                }
                if (string.IsNullOrWhiteSpace(outputPrefabName))
                {
                    outputPrefabName = Path.GetFileNameWithoutExtension(rootPrefabs[rootIndex]);
                }
                if (GUILayout.Button("Create Authoring Copy in Pack/Authoring"))
                {
                        try
                        {
                            string created = CreateAuthoringCopy(pack, rootPrefabs[rootIndex]);
                            authoringPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(created);
                            outputPrefabName = authoringPrefab.name;
                            result = "Created " + created + ". Open it and attach Custom Colorizer markers to the mesh renderers.";
                            Selection.activeObject = authoringPrefab;
                            RefreshMarkerList(created);
                        }
                        catch (Exception ex)
                        {
                            Error(ex);
                        }
                }
            }
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("2. Edit and bake", EditorStyles.boldLabel);
            var previousAuthoring = authoringPrefab;
            authoringPrefab = (GameObject)EditorGUILayout.ObjectField("Authoring prefab", authoringPrefab,
                typeof(GameObject), false);
            if (authoringPrefab != previousAuthoring)
            {
                inspectedPrefab = "";
                if (authoringPrefab != null)
                {
                    outputPrefabName = authoringPrefab.name;
                }
            }
            // The authoring copy uses the SAME filename as the existing root
            // prefab, but lives under Authoring/. No second root asset is made.
            if (authoringPrefab != null)
            {
                outputPrefabName = authoringPrefab.name;
            }
            string rootPrefabPath = string.IsNullOrEmpty(outputPrefabName) ? "" :
                pack + "/" + outputPrefabName + ".prefab";
            var rootPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(rootPrefabPath);
            EditorGUILayout.ObjectField("Game-ready root prefab", rootPrefab, typeof(GameObject), false);
            string authorPath = authoringPrefab ? AssetDatabase.GetAssetPath(authoringPrefab) : "";
            bool validAuthoring = IsPackSubfolderPrefab(pack, authorPath);
            if (authoringPrefab != null && !validAuthoring)
                EditorGUILayout.HelpBox("Select a prefab inside this pack's Authoring/ subfolder. " +
                    "Do not use the pack's root game-ready prefab as the source.", MessageType.Error);
            if (validAuthoring && rootPrefab == null)
                EditorGUILayout.HelpBox("Cannot find the matching game-ready root prefab at " + rootPrefabPath +
                    ". The authoring prefab name must match an existing root prefab.", MessageType.Error);
            if (validAuthoring)
            {
                if (GUILayout.Button("Open Authoring Prefab for Editing"))
                {
                    AssetDatabase.OpenAsset(authoringPrefab);
                }
                EditorGUILayout.HelpBox("Add markers in bulk below, or edit individual Colorizer Markers in the authoring prefab. " +
                    "Save any open Prefab Mode edits before using bulk actions. Your FBX and source materials are not modified.", MessageType.None);
                DrawBulkMarkerEditor(authorPath);
            }
            generateSelectableStyle = EditorGUILayout.ToggleLeft("Create selectable Custom Paint car style", generateSelectableStyle);
            EditorGUILayout.HelpBox(generateSelectableStyle
                ? "Creates a named CarStyle paint scheme, in addition to individual per-zone color controls."
                : "Only adds color controls to the car definition; no separate selectable Custom Paint scheme.", MessageType.None);
            canBake = validAuthoring && rootPrefab != null && cars.Count != 0;
            bakeAuthorPath = authorPath;
            bakeRootPath = rootPrefabPath;
        }

        private void RefreshMarkerList(string path)
        {
            partSlots = ColorizerMarkerBatch.Inspect(path);
            inspectedPrefab = path;
        }

        private void DrawBulkMarkerEditor(string authorPath)
        {
            EditorGUILayout.Space(8);
            showParts = EditorGUILayout.Foldout(showParts, "3. Colorizer Markers", true);
            if (!showParts)
            {
                return;
            }
            if (inspectedPrefab != authorPath)
            {
                try
                {
                    RefreshMarkerList(authorPath);
                }
                catch (Exception ex)
                {
                    Error(ex); return;
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                int meshes = partSlots.Select(p => p.RendererId).Distinct(StringComparer.Ordinal).Count();
                int marked = partSlots.Count(p => p.HasMarker);
                EditorGUILayout.LabelField($"{meshes} mesh parts / {partSlots.Count} slots / {marked} marked", EditorStyles.miniLabel);
                if (GUILayout.Button("Refresh", GUILayout.Width(75)))
                {
                    try
                    {
                        RefreshMarkerList(authorPath);
                    }
                    catch (Exception ex)
                    {
                        Error(ex);
                    }
                }
            }

            DrawMaterialBlacklist(authorPath);

            if (partSlots.Count == 0)
            {
                EditorGUILayout.HelpBox("No eligible material slots found. Repair missing materials first if needed.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Group by Source Material", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Auto-Assign adds markers only where missing. Reset overwrites " +
                    "existing groups. Both skip materials matching your blacklist.", MessageType.None);
                if (GUILayout.Button("Auto-Assign by Material (Unmarked Only)", GUILayout.Height(27)))
                {
                    ApplyMaterialGroups(authorPath, false);
                }
                if (GUILayout.Button("Reset ALL to Material Groups", GUILayout.Height(27)))
                {
                    ApplyMaterialGroups(authorPath, true);
                }

                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Alternative: One Common Group", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Apply one group to all non-blacklisted parts, then Ctrl-select exceptions " +
                    "in the authoring prefab and edit their markers together in the Inspector.", MessageType.None);
                sharedStyleClass = EditorGUILayout.TextField("Common Style Class", sharedStyleClass);
                sharedControlLabel = EditorGUILayout.TextField("Common Control Label", sharedControlLabel);
                commonDefaultColor = EditorGUILayout.ColorField("Default Color", commonDefaultColor);
                commonColorGamma = EditorGUILayout.FloatField("Color Gamma", commonColorGamma);
                if (GUILayout.Button("Apply Common Group to ALL Parts", GUILayout.Height(27)))
                {
                    ApplyCommonGroupToAll(authorPath);
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Marker Cleanup", EditorStyles.boldLabel);
            if (GUILayout.Button("Remove Blacklisted Markers", GUILayout.Height(27)))
            {
                RemoveBlacklistedMarkers(authorPath);
            }
            if (GUILayout.Button("Remove ALL Colorizer Markers from Authoring Prefab"))
            {
                RemoveAllMarkers(authorPath);
            }
            EditorGUILayout.LabelField("Individual markers can also be removed from selected parts in the Inspector.",
                EditorStyles.miniLabel);
            EditorGUILayout.Space(6);
        }

        private List<string> BlacklistTerms() => ColorizerMaterialBlacklist.Parse(blacklistText);

        private void EnsureBlacklistLoaded()
        {
            if (blacklistLoadedPack == pack)
            {
                return;
            }
            blacklistText = string.Join("\n", ColorizerMaterialBlacklist.Load(pack));
            blacklistLoadedPack = pack;
        }

        private void SaveBlacklist()
        {
            ColorizerMaterialBlacklist.Save(pack, BlacklistTerms());
            blacklistText = string.Join("\n", BlacklistTerms());
        }

        private void DrawMaterialBlacklist(string authorPath)
        {
            try
            {
                EnsureBlacklistLoaded();
            }
            catch (Exception ex)
            {
                Error(ex); return;
            }
            EditorGUILayout.Space(6);
            showBlacklist = EditorGUILayout.Foldout(showBlacklist, "Material Blacklist", true);
            if (!showBlacklist)
            {
                return;
            }
            EditorGUILayout.HelpBox("One material-name fragment per line (case-insensitive contains match). " +
                "Glass, Window, or Mirror materials can be excluded from bulk assignment. " +
                "Manual marker editing is still permitted. Blacklist is saved per asset pack in Authoring/.",
                MessageType.None);
            blacklistText = EditorGUILayout.TextArea(blacklistText, GUILayout.MinHeight(75));
            var terms = BlacklistTerms();
            int skipped = partSlots.Count(p => ColorizerMaterialBlacklist.Matches(p.MaterialName, terms));
            EditorGUILayout.LabelField($"{skipped} of {partSlots.Count} eligible material slots excluded", EditorStyles.miniLabel);
            if (GUILayout.Button("Save Material Blacklist"))
            {
                try
                {
                    SaveBlacklist();
                    result = "Saved material blacklist for " + RailroaderAssetPackTools.PackIdentifier(pack) + ".";
                }
                catch (Exception ex)
                {
                    Error(ex);
                }
            }
        }

        private void RemoveBlacklistedMarkers(string authorPath)
        {
            try
            {
                EnsureAuthoringPrefabClosed(authorPath);
                var terms = BlacklistTerms();
                if (terms.Count == 0)
                {
                    result = "Material blacklist is empty. Add material-name fragments first.";
                    return;
                }
                int count = ColorizerMarkerBatch.CountBlacklisted(authorPath, terms);
                if (count == 0)
                {
                    SaveBlacklist();
                    result = "No existing Colorizer Markers match the material blacklist.";
                    return;
                }
                if (!EditorUtility.DisplayDialog("Remove blacklisted Colorizer Markers?",
                    "Remove " + count + " marker component(s) on material slots matching the blacklist?" +
                    "\n\nOther slots and components are preserved. This only edits the authoring prefab; " +
                    "it does not reverse a previous bake. Rebake afterward to update the game prefab.",
                    "Remove Blacklisted", "Cancel")) return;
                SaveBlacklist();
                result = ColorizerMarkerBatch.RemoveBlacklisted(authorPath, terms);
                RefreshMarkerList(authorPath);
            }
            catch (Exception ex)
            {
                Error(ex);
            }
        }

        private void EnsureAuthoringPrefabClosed(string authorPath)
        {
            // Batch editing a second prefab copy while Prefab Mode is open risks
            // overwriting its unsaved changes with older asset data.
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && string.Equals(stage.assetPath, authorPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save and close the authoring Prefab Mode tab before " +
                    "bulk editing markers, then reopen it to inspect the results.");
        }

        private List<ColorizerMarkerBatch.Request> MaterialGroupRequests(bool resetAll)
        {
            // Group by actual Unity material asset, not renderer name. A reused
            // source material produces one shared control, while different materials
            // are guaranteed distinct class names even when filenames collide.
            var eligible = partSlots.Where(p =>
                !ColorizerMaterialBlacklist.Matches(p.MaterialName, BlacklistTerms())).ToList();
            HashSet<string> usedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> byMaterial = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in eligible.GroupBy(p => p.MaterialId)
                .OrderBy(g => ColorizerMarkerBatch.SuggestedClass(g.First().MaterialName), StringComparer.Ordinal)
                .ThenBy(g => g.First().MaterialName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Key, StringComparer.Ordinal))
            {
                string baseClass = ColorizerMarkerBatch.SuggestedClass(group.First().MaterialName);
                string uniqueClass = baseClass;
                int suffix = 2;
                while (!usedClasses.Add(uniqueClass))
                {
                    uniqueClass = baseClass + "-" + suffix++;
                }
                byMaterial[group.Key] = uniqueClass;
            }
            // A shared group also needs consistent default color/gamma. Otherwise
            // the baker correctly rejects conflicting values for one control.
            // When resetting, keep the first existing material-group default and
            // align the other markers. For unmarked slots, only inherit the group's
            // defaults when an existing marker already uses that same control.
            Dictionary<string, ColorizerMarkerBatch.Slot> defaultsByMaterial = new Dictionary<string, ColorizerMarkerBatch.Slot>(StringComparer.Ordinal);
            foreach (var group in eligible.GroupBy(p => p.MaterialId))
            {
                string style = byMaterial[group.Key];
                string label = ColorizerMarkerBatch.SuggestedLabel(style);
                var source = group.FirstOrDefault(p => p.HasMarker &&
                    string.Equals(p.ExistingStyleClass, style, StringComparison.Ordinal) &&
                    string.Equals(p.ExistingLabel, label, StringComparison.Ordinal));
                if (resetAll && source == null)
                {
                    source = group.FirstOrDefault(p => p.HasMarker);
                }
                if (source != null)
                {
                    defaultsByMaterial[group.Key] = source;
                }
            }
            return eligible.Where(p => resetAll || !p.HasMarker)
                .Select(p =>
                {
                    defaultsByMaterial.TryGetValue(p.MaterialId, out var source);
                    return new ColorizerMarkerBatch.Request
                    {
                        Id = p.Id,
                        StyleClass = byMaterial[p.MaterialId],
                        Label = ColorizerMarkerBatch.SuggestedLabel(byMaterial[p.MaterialId]),
                        NormalizeDefaults = resetAll || source != null,
                        DefaultColor = source == null ? Color.white : source.ExistingDefaultColor,
                        DefaultGamma = source == null ? 1f : source.ExistingGamma
                    };
                }).ToList();
        }

        private void ApplyMaterialGroups(string authorPath, bool resetAll)
        {
            try
            {
                EnsureAuthoringPrefabClosed(authorPath);
                var requests = MaterialGroupRequests(resetAll);
                if (requests.Count == 0)
                {
                    result = "No unmarked, non-blacklisted parts found. Reset ALL can update existing eligible groups.";
                    return;
                }
                if (resetAll && !EditorUtility.DisplayDialog("Reset material groups?",
                    "Reassign all " + requests.Count + " eligible material slots based on their source materials?" +
                    "\n\nManual style classes and control labels will be overwritten. " +
                    "Default colors and gamma are aligned per material group using the first " +
                    "existing marker as the starting value. This only edits the authoring prefab.",
                    "Reset Groups", "Cancel")) return;
                SaveBlacklist();
                result = ColorizerMarkerBatch.Apply(authorPath, requests, resetAll, false);
                RefreshMarkerList(authorPath);
            }
            catch (Exception ex)
            {
                Error(ex);
            }
        }

        private void RemoveAllMarkers(string authorPath)
        {
            try
            {
                EnsureAuthoringPrefabClosed(authorPath);
                int count = ColorizerMarkerBatch.CountAll(authorPath);
                if (count == 0)
                {
                    result = "No Colorizer Markers found in the authoring prefab.";
                    return;
                }
                if (!EditorUtility.DisplayDialog("Remove all Colorizer Markers?",
                    "Remove " + count + " Colorizer Marker component(s) from the authoring prefab?" +
                    "\n\nThis does not delete the prefab, source FBX, meshes, materials, animations, " +
                    "or other components. It also does not undo an earlier bake.",
                    "Remove Markers", "Cancel")) return;
                result = ColorizerMarkerBatch.RemoveAll(authorPath);
                RefreshMarkerList(authorPath);
            }
            catch (Exception ex)
            {
                Error(ex);
            }
        }

        private void ApplyCommonGroupToAll(string authorPath)
        {
            try
            {
                if (!ColorizerMarkerBatch.IsValidStyleClass(sharedStyleClass) ||
                    string.IsNullOrWhiteSpace(sharedControlLabel))
                    throw new InvalidOperationException("Enter a valid lowercase Style Class and a nonempty Control Label.");
                if (float.IsNaN(commonColorGamma) || float.IsInfinity(commonColorGamma) || commonColorGamma <= 0f)
                {
                    throw new InvalidOperationException("Color Gamma must be greater than zero.");
                }

                EnsureAuthoringPrefabClosed(authorPath);

                var eligible = partSlots.Where(p =>
                    !ColorizerMaterialBlacklist.Matches(p.MaterialName, BlacklistTerms())).ToList();
                if (eligible.Count == 0)
                {
                    result = "No non-blacklisted material slots found.";
                    return;
                }
                int marked = eligible.Count(p => p.HasMarker);
                string message = "Apply '" + sharedStyleClass + "' / '" + sharedControlLabel +
                    "' to all " + eligible.Count + " non-blacklisted material slots?" +
                    "\n\nThis writes to the authoring prefab only; it does not bake the game prefab.";
                if (marked != 0)
                {
                    message += "\n\n" + marked + " existing marker(s) will be updated, including their default color and gamma.";
                }
                if (!EditorUtility.DisplayDialog("Apply common Colorizer group", message, "Apply to All", "Cancel"))
                {
                    return;
                }

                var requests = eligible.Select(p => new ColorizerMarkerBatch.Request
                {
                    Id = p.Id,
                    StyleClass = sharedStyleClass.Trim(),
                    Label = sharedControlLabel.Trim(),
                    NormalizeDefaults = true,
                    DefaultColor = commonDefaultColor,
                    DefaultGamma = commonColorGamma
                }).ToList();
                SaveBlacklist();
                result = ColorizerMarkerBatch.Apply(authorPath, requests, true, false);
                RefreshMarkerList(authorPath);
            }
            catch (Exception ex)
            {
                Error(ex);
            }
        }

        private void Error(Exception ex)
        {
            result = "Failed: " + ex.Message;
            RailroaderEditorFeedback.ShowError("Colorizer Baker", ex);
        }

        private static bool IsGenerated(string path)
        {
            var importer = AssetImporter.GetAtPath(path);
            return importer != null && (importer.userData ?? "").StartsWith("RailroaderAssetTools.Colorizer:", StringComparison.Ordinal);
        }

        private static bool IsPackSubfolderPrefab(string pack, string path)
        {
            if (!RailroaderAssetPackTools.ValidPack(pack) ||
                string.IsNullOrWhiteSpace(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                !path.StartsWith(pack + "/", StringComparison.OrdinalIgnoreCase))
                return false;
            string folder = pack.TrimEnd('/') + "/Authoring/";
            return path.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> ReadCars(string pack)
        {
            string definitions = RailroaderAssetPackTools.Absolute(pack + "/Definitions.json");
            if (!File.Exists(definitions))
            {
                return new List<string>();
            }
            try
            {
                var root = JToken.Parse(File.ReadAllText(definitions));
                var entries = root as JArray ?? (root as JObject)?["objects"] as JArray;
                if (entries == null)
                {
                    throw new InvalidOperationException("Expected a top-level array of definitions.");
                }
                return entries.OfType<JObject>()
                    .Where(o => IsCar((string)o["definition"]?["kind"]))
                    .Select(o => (string)o["identifier"])
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Railroader Colorizer] Cannot read Definitions.json: " + ex.Message);
                return new List<string>();
            }
        }

        private static bool IsCar(string kind) => kind == "Car" || kind == "DieselLocomotive" ||
            kind == "SteamLocomotive" || kind == "ArticulatedSteamLocomotive";

        private static string CreateAuthoringCopy(string pack, string original)
        {
            string folder = pack + "/Authoring";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(pack, "Authoring");
            }
            string target = folder + "/" + Path.GetFileName(original);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(target) != null)
            {
                throw new InvalidOperationException("Authoring copy already exists: " + target + ". Select it instead of overwriting.");
            }
            if (!AssetDatabase.CopyAsset(original, target))
            {
                throw new InvalidOperationException("Unity could not copy prefab to " + target);
            }
            AssetDatabase.SaveAssets();
            return target;
        }

        private static string Bake(string pack, string car, string authorPath, string outputPrefabName,
            bool generateSelectableStyle)
        {
            if (!IsPackSubfolderPrefab(pack, authorPath))
            {
                throw new InvalidOperationException("Authoring prefab must be inside a subfolder of the pack.");
            }
            var root = PrefabUtility.LoadPrefabContents(authorPath);
            try
            {
                if (string.IsNullOrWhiteSpace(outputPrefabName))
                {
                    throw new InvalidOperationException("Choose an authoring prefab matching an existing root prefab.");
                }
                string output = ColorizerBaker.Bake(root, authorPath, pack, car, outputPrefabName, generateSelectableStyle);
                // The authoring prefab is never overwritten by the bake.
                // Material/control identifiers are deterministic, so no marker GUID
                // modifications have to be written back to the source.
                AssetDatabase.SaveAssets();
                return output;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
#endif
