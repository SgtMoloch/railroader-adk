#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AssetPack.Common.Components;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Railroader.AssetTools
{
    /// <summary>
    /// Animation asset discovery / AnimationMap setup for Railroader's ADK.
    /// Does not create definitions, gameplay components, animation curves or controllers.
    /// Never edits the imported FBX or overwrites populated user mappings.
    /// </summary>
    internal static class RailroaderAnimationService
    {
        internal sealed class Proposal
        {
            internal AnimationClip Clip;
            internal string Name;
            internal bool Include = true;
        }

        internal sealed class Snapshot
        {
            internal string PrefabPath;
            internal string ModelPath;
            internal string ModelRootPath;
            internal readonly List<Proposal> Suggestions = new List<Proposal>();
            internal readonly List<string> Existing = new List<string>();
            internal readonly List<string> Warnings = new List<string>();
        }

        // Do not hard-code AnimationMap.MapEntry's serialized field names; ADK versions may
        // expose either `clip` or `animation`. Validate the installed ADK's actual structure.
        private sealed class MapLayout
        {
            internal string Entries;
            internal string Key;
            internal string Clip;
        }

        private static MapLayout Layout(AnimationMap map)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (FieldInfo field in map.GetType().GetFields(flags)
                .OrderBy(f => string.Equals(f.Name, "entries", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            {
                if (field.IsStatic || field.IsNotSerialized)
                {
                    continue;
                }
                if (!field.IsPublic && !Attribute.IsDefined(field, typeof(SerializeField)))
                {
                    continue;
                }
                Type element = null;
                if (field.FieldType.IsArray)
                {
                    element = field.FieldType.GetElementType();
                }
                else if (field.FieldType.IsGenericType &&
                    field.FieldType.GetGenericTypeDefinition() == typeof(List<>))
                    element = field.FieldType.GetGenericArguments()[0];
                if (element == null)
                {
                    continue;
                }
                var fields = element.GetFields(flags)
                    .Where(f => !f.IsStatic && !f.IsNotSerialized &&
                        (f.IsPublic || Attribute.IsDefined(f, typeof(SerializeField)))).ToArray();
                var key = fields.FirstOrDefault(f => f.FieldType == typeof(string) &&
                    string.Equals(f.Name, "name", StringComparison.OrdinalIgnoreCase))
                    ?? fields.FirstOrDefault(f => f.FieldType == typeof(string));
                var clip = fields.FirstOrDefault(f => typeof(AnimationClip).IsAssignableFrom(f.FieldType));
                if (key == null || clip == null)
                {
                    continue;
                }
                SerializedProperty array = new SerializedObject(map).FindProperty(field.Name);
                if (array != null && array.isArray)
                {
                    return new MapLayout { Entries = field.Name, Key = key.Name, Clip = clip.Name };
                }
            }
            throw new InvalidOperationException("Could not identify AnimationMap entries in this ADK. " +
                "Expected a serialized list containing a string name and an AnimationClip. " +
                "No prefab changes were saved. Please share the AnimationMap C# definition.");
        }

        private sealed class MapEntry
        {
            internal string Name;
            internal AnimationClip Clip;
        }

        private static List<MapEntry> Read(AnimationMap map)
        {
            var layout = Layout(map);
            SerializedObject serialized = new SerializedObject(map);
            SerializedProperty array = serialized.FindProperty(layout.Entries);
            List<MapEntry> entries = new List<MapEntry>();
            for (int i = 0; i < array.arraySize; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                SerializedProperty key = element.FindPropertyRelative(layout.Key);
                SerializedProperty clip = element.FindPropertyRelative(layout.Clip);
                if (key == null || clip == null || key.propertyType != SerializedPropertyType.String ||
                    clip.propertyType != SerializedPropertyType.ObjectReference)
                    throw new InvalidOperationException("Unexpected serialized AnimationMap entry shape in this ADK.");
                entries.Add(new MapEntry { Name = key.stringValue, Clip = clip.objectReferenceValue as AnimationClip });
            }
            return entries;
        }

        private static Transform AnimatedRoot(GameObject root, out string modelPath)
        {
            // FBX-based prefab variants use the imported FBX as their root. The old
            // wrapper-prefab workflow puts the imported FBX in a child instead.
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject);
                if (source == null)
                {
                    source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                }
                string path = source == null ? null : AssetDatabase.GetAssetPath(source);
                if (!string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is ModelImporter)
                {
                    modelPath = path;
                    return t;
                }
            }
            modelPath = null;
            return root.transform;
        }

        private static string PathFrom(Transform root, Transform t)
        {
            if (root == t)
            {
                return "(root)";
            }
            List<string> parts = new List<string>();
            for (Transform p = t; p != null && p != root; p = p.parent)
            {
                parts.Add(p.name);
            }
            parts.Reverse();
            return String.Join("/", parts);
        }

        internal static List<AnimationClip> Clips(string modelPath)
        {
            if (string.IsNullOrEmpty(modelPath))
            {
                return new List<AnimationClip>();
            }
            return AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
                .Where(c => c != null && !c.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                .GroupBy(c => c.GetInstanceID()).Select(g => g.First())
                .OrderBy(c => c.name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static Snapshot Inspect(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException("Select a valid root-level prefab.");
            }
            var s = new Snapshot { PrefabPath = prefabPath };
            var modelRoot = AnimatedRoot(prefab, out string model);
            s.ModelPath = model;
            s.ModelRootPath = PathFrom(prefab.transform, modelRoot);
            AnimationMap[] maps = prefab.GetComponentsInChildren<AnimationMap>(true);
            if (maps.Length > 1)
                s.Warnings.Add("Multiple AnimationMaps found. Railroader only uses the first; remove duplicates manually.");
            AnimationMap map = maps.FirstOrDefault();
            if (map != null && map.transform != modelRoot)
                s.Warnings.Add("AnimationMap is on '" + PathFrom(prefab.transform, map.transform) +
                    "', not the animated model root '" + s.ModelRootPath +
                    "'. Move it manually before rebuilding; this tool won't move populated maps.");
            if (map != null && map.GetComponent<Animator>() == null)
            {
                s.Warnings.Add("AnimationMap has no Animator on the same GameObject.");
            }
            var entries = map == null ? new List<MapEntry>() : Read(map);
            foreach (var e in entries)
            {
                s.Existing.Add((string.IsNullOrEmpty(e.Name) ? "<unnamed>" : e.Name) + " -> " +
                    (e.Clip == null ? "<unassigned>" : e.Clip.name));
                if (e.Clip == null)
                {
                    s.Warnings.Add("Existing map entry '" + e.Name + "' has no clip.");
                }
            }
            if (model == null)
                s.Warnings.Add("Could not locate an imported FBX/OBJ in this prefab. " +
                    "You can still map a manually selected .anim asset.");
            HashSet<string> names = new HashSet<string>(entries.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
            foreach (AnimationClip clip in Clips(model))
            {
                if (clip.legacy) s.Warnings.Add("Clip '" + clip.name + "' uses Legacy animation; " +
                    "Railroader's Animator requires Generic/non-Legacy clips.");
                if (entries.Any(e => e.Clip == clip))
                {
                    continue;
                }
                string suggestion = clip.name;
                int n = 2;
                while (names.Contains(suggestion))
                {
                    suggestion = clip.name + " (" + n++ + ")";
                }
                names.Add(suggestion);
                s.Suggestions.Add(new Proposal { Name = suggestion, Clip = clip,
                    Include = !clip.legacy });
            }
            if (map == null)
            {
                s.Warnings.Add("No AnimationMap found (it will be created when mappings are applied).");
            }
            if (s.Suggestions.Count == 0 && entries.Count == 0)
            {
                s.Warnings.Add("No animation clips were found. Check the FBX Animation import tab / Blender NLA export.");
            }
            return s;
        }

        // An animation map must be colocated with the Animator. Preserve any existing
        // mapping and append new clips only, never silently remap an existing name.
        internal static string Apply(string prefabPath, IEnumerable<Proposal> proposals, bool createWhenEmpty = false)
        {
            var selected = proposals.Where(p => p != null && p.Include && p.Clip != null)
                .ToList();
            if (selected.Any(p => string.IsNullOrWhiteSpace(p.Name)))
            {
                throw new InvalidOperationException("Map names cannot be empty.");
            }
            if (selected.Any(p => p.Clip.legacy))
                throw new InvalidOperationException("Legacy clips cannot be used by Railroader's Animator. " +
                    "Switch the FBX Rig import to Generic and reimport first.");
            if (selected.GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            {
                throw new InvalidOperationException("Two selected animations have the same map name.");
            }
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                throw new InvalidOperationException("Save and close Prefab Mode before applying AnimationMap changes.");
            }
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                throw new InvalidOperationException("Could not load " + prefabPath);
            }
            try
            {
                Transform modelRoot = AnimatedRoot(root, out _);
                var maps = root.GetComponentsInChildren<AnimationMap>(true);
                if (maps.Length > 1)
                {
                    throw new InvalidOperationException("Multiple AnimationMaps found. Remove extras manually first.");
                }
                if (maps.Length == 1 && maps[0].transform != modelRoot)
                    throw new InvalidOperationException("Existing AnimationMap is not on the model root. " +
                        "Move it to the FBX root manually to preserve mappings before applying.");
                if (maps.Length == 0 && selected.Count == 0 && !createWhenEmpty)
                    return "No clips selected; no AnimationMap created.";
                AnimationMap map = maps.Length != 0 ? maps[0] : modelRoot.gameObject.AddComponent<AnimationMap>();
                var layout = Layout(map); // fail safely if installed ADK's map schema differs
                var existing = Read(map);
                HashSet<string> names = new HashSet<string>(existing.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
                HashSet<AnimationClip> mappedClips = new HashSet<AnimationClip>(existing.Where(e => e.Clip != null).Select(e => e.Clip));
                foreach (Proposal p in selected)
                {
                    if (mappedClips.Contains(p.Clip))
                    {
                        continue;
                    }
                    MapEntry matching = existing.FirstOrDefault(e =>
                        string.Equals(e.Name, p.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (matching != null && matching.Clip != null)
                        throw new InvalidOperationException("Mapping name '" + p.Name +
                            "' is already assigned. Existing mappings are preserved; choose another name.");
                    names.Add(p.Name.Trim());
                    mappedClips.Add(p.Clip);
                }
                if (map.GetComponent<Animator>() == null)
                {
                    modelRoot.gameObject.AddComponent<Animator>();
                }
                SerializedObject serialized = new SerializedObject(map);
                SerializedProperty array = serialized.FindProperty(layout.Entries);
                int added = 0;
                HashSet<AnimationClip> covered = new HashSet<AnimationClip>(existing.Where(e => e.Clip != null).Select(e => e.Clip));
                int repaired = 0;
                foreach (Proposal p in selected)
                {
                    if (!covered.Add(p.Clip))
                    {
                        continue;
                    }
                    int empty = existing.FindIndex(e => e.Clip == null &&
                        string.Equals(e.Name, p.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (empty >= 0)
                    {
                        array.GetArrayElementAtIndex(empty).FindPropertyRelative(layout.Clip)
                            .objectReferenceValue = p.Clip;
                        repaired++;
                    }
                    else
                    {
                        int i = array.arraySize;
                        array.InsertArrayElementAtIndex(i);
                        var element = array.GetArrayElementAtIndex(i);
                        element.FindPropertyRelative(layout.Key).stringValue = p.Name.Trim();
                        element.FindPropertyRelative(layout.Clip).objectReferenceValue = p.Clip;
                        added++;
                    }
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null)
                {
                    throw new InvalidOperationException("Unity failed saving " + prefabPath);
                }
                return "AnimationMap ready on model root; " + added + " new clip(s) added, " +
                    repaired + " empty slot(s) repaired, " + existing.Count +
                    " existing mapping(s) otherwise preserved. Animator ensured.";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        internal static string AutoSetup(string prefabPath, bool ensureEmptyMapForTruck)
        {
            Snapshot snapshot = Inspect(prefabPath);
            if (snapshot.Warnings.Any(w => w.StartsWith("Multiple AnimationMaps") ||
                                           w.StartsWith("AnimationMap is on '")))
                return "Skipped: existing AnimationMap placement needs manual review";
            return Apply(prefabPath, snapshot.Suggestions, ensureEmptyMapForTruck);
        }

        internal static List<string> Validate(string pack, string prefabPath)
        {
            List<string> issues = new List<string>();
            Snapshot snapshot = Inspect(prefabPath);
            issues.AddRange(snapshot.Warnings);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            AnimationMap[] maps = prefab.GetComponentsInChildren<AnimationMap>(true);
            if (maps.Length == 0)
            {
                return issues;
            }
            var entries = Read(maps[0]);
            HashSet<string> names = new HashSet<string>(entries.Where(e => e.Clip != null).Select(e => e.Name),
                StringComparer.OrdinalIgnoreCase);
            foreach (var duplicate in entries.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1))
                issues.Add("Duplicate AnimationMap name: '" + duplicate.Key + "'.");

            string definitionsPath = RailroaderAssetPackTools.Absolute(pack + "/Definitions.json");
            if (!File.Exists(definitionsPath))
            {
                issues.Add("No Definitions.json in this pack; definition reference validation skipped.");
                return issues;
            }
            try
            {
                JToken doc = JToken.Parse(File.ReadAllText(definitionsPath));
                JArray objects = (doc as JObject)?["objects"] as JArray;
                if (objects == null)
                {
                    throw new InvalidOperationException("Expected { objects: [...] }.");
                }
                string name = Path.GetFileNameWithoutExtension(prefabPath);
                string domain = RailroaderAssetPackTools.PackIdentifier(pack).Split('/')[0];
                var matching = objects.OfType<JObject>().Where(o =>
                {
                    string model = (string)o["definition"]?["modelIdentifier"];
                    return string.Equals(model, "./" + name, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(model, domain + "/" + name, StringComparison.OrdinalIgnoreCase);
                }).ToList();
                if (matching.Count == 0)
                {
                    issues.Add("No definition in this pack references model './" + name +
                        "'. Animation references cannot be checked yet.");
                    return issues;
                }
                var refs = matching.SelectMany(o => (o["definition"] as JContainer)?.Descendants().OfType<JProperty>()
                        .Where(p => p.Name == "clipName") ?? Enumerable.Empty<JProperty>())
                    .Select(p => (string)p.Value).Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                foreach (string reference in refs)
                    if (!names.Contains(reference)) issues.Add("Definition references '" + reference +
                        "' but AnimationMap has no assigned clip by that name.");
                foreach (MapEntry entry in entries.Where(e => e.Clip != null))
                    if (!refs.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                        issues.Add("Info: '" + entry.Name +
                            "' is mapped but not referenced by the selected model definition (may be intentional).");
                if (refs.Count > 0 && refs.All(names.Contains))
                {
                    issues.Add("All " + refs.Count + " definition clip reference(s) resolve.");
                }
            }
            catch (Exception e)
            {
                issues.Add("Could not validate Definitions.json: " + e.Message);
            }
            return issues;
        }
    }

    /// <summary>Independent, scrollable editor for mapping imported FBX clips and checking definitions.</summary>
    public sealed class RailroaderAnimationBuilder : EditorWindow
    {
        private string pack;
        private int prefabIndex;
        private string loadedPrefab;
        private RailroaderAnimationService.Snapshot snapshot;
        private AnimationClip manualClip;
        private string status;
        private Vector2 scroll;

        [MenuItem("Railroader/Animation Builder")]
        public static void Open()
        {
            GetWindow<RailroaderAnimationBuilder>("Animation Builder");
        }

        private void OnEnable()
        {
            pack = EditorPrefs.GetString("RailroaderTools.SelectedPack", "");
        }

        private void OnGUI()
        {
            using (var view = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.ExpandHeight(true)))
            {
                scroll = view.scrollPosition;
                EditorGUILayout.LabelField("Railroader Animation Builder v3.0 Beta 1.2", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Detect FBX clips and set up AnimationMap + Animator on the " +
                    "animated model root. Existing mappings are retained. This does not create definition " +
                    "components, Animator Controllers or animation curves.", MessageType.Info);
                string priorPack = pack;
                pack = RailroaderAssetPackPicker.Draw("Asset Pack", pack);
                if (pack != priorPack)
                {
                    loadedPrefab = null; prefabIndex = 0;
                }
                if (!RailroaderAssetPackTools.ValidPack(pack))
                {
                    return;
                }
                List<string> prefabs = RailroaderAssetPackTools.RootPrefabs(pack);
                if (prefabs.Count == 0)
                {
                    EditorGUILayout.HelpBox("No root prefabs in this pack.", MessageType.Warning);
                    return;
                }
                prefabIndex = Mathf.Clamp(prefabIndex, 0, prefabs.Count - 1);
                prefabIndex = EditorGUILayout.Popup("Root Prefab", prefabIndex,
                    prefabs.Select(Path.GetFileNameWithoutExtension).ToArray());
                string prefab = prefabs[prefabIndex];
                if (prefab != loadedPrefab || snapshot == null)
                {
                    Refresh(prefab);
                }
                if (snapshot == null)
                {
                    EditorGUILayout.HelpBox(status, MessageType.Error); return;
                }
                EditorGUILayout.LabelField("Animated root", snapshot.ModelRootPath);
                EditorGUILayout.LabelField("Source model", snapshot.ModelPath ?? "(none detected)");
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Already mapped (preserved)", EditorStyles.boldLabel);
                if (snapshot.Existing.Count == 0)
                {
                    EditorGUILayout.LabelField("None");
                }
                foreach (string name in snapshot.Existing)
                {
                    EditorGUILayout.LabelField("• " + name);
                }
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("New clips (select and rename)", EditorStyles.boldLabel);
                if (snapshot.Suggestions.Count == 0)
                {
                    EditorGUILayout.LabelField("No unmapped FBX clips detected.");
                }
                foreach (var proposal in snapshot.Suggestions)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        proposal.Include = EditorGUILayout.Toggle(proposal.Include, GUILayout.Width(22));
                        EditorGUILayout.ObjectField(proposal.Clip, typeof(AnimationClip), false, GUILayout.Width(180));
                        proposal.Name = EditorGUILayout.TextField(proposal.Name);
                    }
                }
                EditorGUILayout.Space();
                manualClip = (AnimationClip)EditorGUILayout.ObjectField("Add external clip", manualClip,
                    typeof(AnimationClip), false);
                using (new EditorGUI.DisabledScope(manualClip == null))
                    if (GUILayout.Button("Add Selected Clip to List"))
                    {
                        if (snapshot.Suggestions.Any(p => p.Clip == manualClip))
                        {
                            status = "That clip is already in the list.";
                        }
                        else
                        {
                            HashSet<string> names = new HashSet<string>(snapshot.Suggestions.Select(p => p.Name),
                                StringComparer.OrdinalIgnoreCase);
                            string name = manualClip.name;
                            int suffix = 2;
                            while (names.Contains(name))
                            {
                                name = manualClip.name + " (" + suffix++ + ")";
                            }
                            snapshot.Suggestions.Add(new RailroaderAnimationService.Proposal
                                { Clip = manualClip, Name = name });
                            manualClip = null;
                        }
                    }
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Current checks", EditorStyles.boldLabel);
                foreach (string warning in snapshot.Warnings)
                {
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
                }
                if (!string.IsNullOrEmpty(status))
                {
                    EditorGUILayout.HelpBox(status, MessageType.Info);
                }
            }
            // Keep Apply reachable even with a large number of clips.
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", GUILayout.Height(27)))
                {
                    string prefab = loadedPrefab;
                    Refresh(prefab);
                }
                using (new EditorGUI.DisabledScope(snapshot == null))
                if (GUILayout.Button("Validate", GUILayout.Height(27)))
                {
                    try
                    {
                        status = String.Join("\n", RailroaderAnimationService.Validate(pack, loadedPrefab));
                    }
                    catch (Exception e)
                    {
                        status = e.Message; RailroaderEditorFeedback.ShowError("Animation Builder", e);
                    }
                }
                using (new EditorGUI.DisabledScope(snapshot == null))
                {
                    if (GUILayout.Button("Apply AnimationMap", GUILayout.Height(27)))
                    {
                        try
                        {
                            string result = RailroaderAnimationService.Apply(loadedPrefab, snapshot.Suggestions);
                            Refresh(loadedPrefab);
                            status = result + "\n" + String.Join("\n",
                                RailroaderAnimationService.Validate(pack, loadedPrefab));
                        }
                        catch (Exception e)
                        {
                            status = e.Message;
                            RailroaderEditorFeedback.ShowError("Animation Builder", e);
                        }
                    }
                }
            }
        }

        private void Refresh(string prefab)
        {
            loadedPrefab = prefab;
            snapshot = null;
            status = null;
            if (string.IsNullOrEmpty(prefab))
            {
                return;
            }
            try
            {
                snapshot = RailroaderAnimationService.Inspect(prefab);
            }
            catch (Exception e)
            {
                status = e.Message; RailroaderEditorFeedback.ShowError("Animation Builder", e);
            }
        }
    }
}
#endif
