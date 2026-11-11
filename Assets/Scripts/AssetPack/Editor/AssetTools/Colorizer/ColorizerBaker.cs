using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AssetPack.Common.Components;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Railroader.AssetTools.Colorizer
{
    public static class ColorizerBaker
    {
        private sealed class Assignment
        {
            // Authoring owns the marker and the source material. The renderer
            // receiving the baked material belongs to the EXISTING game prefab.
            public Renderer Renderer;
            public Renderer OutputRenderer;
            public int Slot;
        }

        // One Target represents one logical control on one source material.
        // Many renderer slots can point at the same generated material.
        private sealed class Target
        {
            public Material Source;
            public string Path;
            public ColorZone Zone;
            public readonly List<Assignment> Assignments = new List<Assignment>();
        }

        public static string Bake(GameObject source, string sourcePath, string pack, string carIdentifier, string prefabName, bool generateSelectableStyle = true)
        {
            ColorizerPreview.ClearAll();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Bake in Edit Mode.");
            }
            pack = (pack ?? "").Replace('\\', '/').TrimEnd('/');
            if (!Regex.IsMatch(pack, @"^Assets/AssetPacks/[a-zA-Z0-9_. -]+$") || pack.EndsWith("/.") || pack.EndsWith("/.."))
            {
                throw new InvalidOperationException("Choose an existing direct child of Assets/AssetPacks, such as Assets/AssetPacks/my-pack.");
            }
            if (!Regex.IsMatch(prefabName ?? "", @"^[a-z][a-z0-9-]*$"))
            {
                throw new InvalidOperationException("Prefab Name must start with a lowercase letter and contain only lowercase letters, digits and hyphens.");
            }
            sourcePath = (sourcePath ?? "").Replace('\\', '/');
            if (source == null || string.IsNullOrWhiteSpace(sourcePath) ||
                !sourcePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Bake from an authoring prefab asset, not a scene object.");
            }
            if (!sourcePath.StartsWith(pack + "/Authoring/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The authoring prefab must be in the selected asset pack's Authoring/ folder.");
            }
            string infoPath = pack + "/Info.json";
            string definitionsPath = pack + "/Definitions.json";
            if (!File.Exists(infoPath) || !File.Exists(definitionsPath))
            {
                throw new InvalidOperationException("Create the pack with Railroader > Asset Packs and add its car definition first. Info.json and Definitions.json are required.");
            }
            var info = JObject.Parse(File.ReadAllText(infoPath));
            if (!Regex.IsMatch((string)info["identifier"] ?? "", @"^[a-z][a-z0-9.-]*/[a-z][a-z0-9.-]*$"))
            {
                throw new InvalidOperationException("Info.json is not a native 0.6 pack manifest. A legacy UMM mod folder cannot be baked directly.");
            }

            // The root game prefab is authoritative for its hierarchy, colliders,
            // animations and all non-Colorizer components. NEVER replace it with
            // a clone of Authoring/: the copies can diverge after the first bake.
            string prefabPath = pack + "/" + prefabName + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                throw new InvalidOperationException("Root prefab does not exist: " + prefabPath +
                    ". Import the model first, then create its Authoring copy.");
            }
            string materialsFolder = pack + "/ColorizerMaterials-" + prefabName;
            string owner = "RailroaderAssetTools.Colorizer:" + carIdentifier;
            var changed = new List<string>();
            string backupFolder = null;
            GameObject output = null;
            try
            {
                // Load the *existing* prefab rather than starting from Authoring/.
                // LoadPrefabContents retains nested FBX links and added overrides.
                output = PrefabUtility.LoadPrefabContents(prefabPath);
                if (output == null)
                {
                    throw new InvalidOperationException("Could not load the game prefab: " + prefabPath);
                }
                Dictionary<string, string> originalColliders = CaptureColliders(output);
                foreach (var transform in source.GetComponentsInChildren<Transform>(true))
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                    {
                        throw new InvalidOperationException("Missing scripts on " + transform.name +
                            ". Repair them in the authoring prefab before baking.");
                    }
                }

                var targets = GatherTargets(source, materialsFolder);
                if (targets.Count == 0)
                {
                    throw new InvalidOperationException("No enabled Custom Colorizer components found. Attach one to a mesh Renderer first.");
                }
                // Resolve *all* authoring marker destinations before touching any
                // assets. If a model's structure has changed, refuse the bake rather
                // than assigning a material to an unrelated mesh by accident.
                foreach (var target in targets)
                {
                    foreach (var assignment in target.Assignments)
                    {
                        assignment.OutputRenderer = ResolveOutputRenderer(source.transform,
                            output.transform, assignment.Renderer, assignment.Slot);
                    }
                }

                var maps = output.GetComponentsInChildren<MaterialMap>(true);
                if (maps.Length > 1)
                {
                    throw new InvalidOperationException("Use only one MaterialMap in the game prefab hierarchy. Found " + maps.Length + ".");
                }
                var map = maps.Length == 0 ? output.AddComponent<MaterialMap>() : maps[0];
                if (map.entries == null)
                {
                    map.entries = new List<MaterialMap.MapEntry>();
                }
                // A repeated bake must replace its old generated map entries,
                // not append duplicates. Keep manually authored entries intact.
                map.entries = map.entries.Where(entry =>
                    entry.material == null || !IsOwnedBy(entry.material, owner)).ToList();
                HashSet<string> mapNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in map.entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.name) || entry.material == null || !mapNames.Add(entry.name))
                    {
                        throw new InvalidOperationException("Existing MaterialMap entries must have unique names and assigned materials.");
                    }
                }
                HashSet<string> outputMaterials = new HashSet<string>(targets.Select(t => t.Path), StringComparer.OrdinalIgnoreCase);
                var sourceMaterials = source.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                    .Concat(source.GetComponentsInChildren<MaterialMap>(true)
                        .SelectMany(m => m.entries ?? new List<MaterialMap.MapEntry>())
                        .Select(e => e.material));
                foreach (var sourceMaterial in sourceMaterials)
                {
                    if (sourceMaterial != null && outputMaterials.Contains(AssetDatabase.GetAssetPath(sourceMaterial)))
                    {
                        throw new InvalidOperationException("The authoring model references a generated output material: " + sourceMaterial.name +
                            ". Use the original source material, or duplicate it outside the generated material folder before baking.");
                    }
                }
                foreach (var target in targets)
                {
                    EnsureOwnedOrNew(target.Path, owner);
                    if (!mapNames.Add(target.Zone.MaterialName))
                    {
                        throw new InvalidOperationException("A generated material-map name already exists: " + target.Zone.MaterialName);
                    }
                }
                // Validate the definition merge before writing any project files.
                string merged = ColorizerDefinitionWriter.Merge(File.ReadAllText(definitionsPath),
                    carIdentifier, prefabName, targets.Select(t => t.Zone).ToArray(), generateSelectableStyle);

                changed.Add(definitionsPath);
                changed.Add(prefabPath);
                changed.AddRange(targets.Select(t => t.Path));
                backupFolder = Backup(changed);
                if (!AssetDatabase.IsValidFolder(materialsFolder))
                {
                    AssetDatabase.CreateFolder(pack, "ColorizerMaterials-" + prefabName);
                }
                foreach (var target in targets)
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(target.Path);
                    if (material == null)
                    {
                        material = new Material(target.Source);
                        AssetDatabase.CreateAsset(material, target.Path);
                    }
                    else
                    {
                        EditorUtility.CopySerialized(target.Source, material);
                    }
                    material.name = target.Zone.MaterialName;
                    if (!ColorUtility.TryParseHtmlString("#" + target.Zone.ColorHex, out var defaultColor))
                    {
                        throw new InvalidOperationException("Invalid default color: " + target.Zone.ColorHex);
                    }
                    string colorProperty = material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                    material.SetColor(colorProperty, ColorizerPreview.Tint(defaultColor, target.Zone.Gamma));
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssetIfDirty(material);
                    SetOwner(target.Path, owner);
                    material = AssetDatabase.LoadAssetAtPath<Material>(target.Path);
                    foreach (var assignment in target.Assignments)
                    {
                        var slots = assignment.OutputRenderer.sharedMaterials;
                        slots[assignment.Slot] = material;
                        assignment.OutputRenderer.sharedMaterials = slots;
                        // The renderer can be a child of a nested FBX prefab.
                        // Persist the material override rather than losing it on save.
                        if (PrefabUtility.IsPartOfPrefabInstance(assignment.OutputRenderer))
                        {
                            PrefabUtility.RecordPrefabInstancePropertyModifications(assignment.OutputRenderer);
                        }
                    }
                    map.entries.Add(new MaterialMap.MapEntry { name = target.Zone.MaterialName, material = material });
                }
                if (PrefabUtility.IsPartOfPrefabInstance(map))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(map);
                }
                // Game-facing prefabs should never contain editor authoring markers.
                // Do not remove anything else, including collider GameObjects.
                foreach (var marker in output.GetComponentsInChildren<CustomColorizer>(true))
                {
                    Object.DestroyImmediate(marker);
                }
                VerifyColliders(originalColliders, CaptureColliders(output));
                var saved = PrefabUtility.SaveAsPrefabAsset(output, prefabPath);
                if (saved == null)
                {
                    throw new InvalidOperationException("Unity could not save the baked prefab.");
                }
                PrefabUtility.UnloadPrefabContents(output);
                output = null;

                // Detect Unity prefab remapping issues after serialization too.
                // An unexpected collider loss automatically restores the backup.
                var verification = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    VerifyColliders(originalColliders, CaptureColliders(verification));
                    VerifyBakedMaterials(source.transform, verification.transform, targets);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(verification);
                }
                File.WriteAllText(definitionsPath, merged, new UTF8Encoding(false));
                AssetDatabase.Refresh();
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                return "Baked " + targets.Sum(t => t.Assignments.Count) + " mesh slot(s) using " +
                    targets.Count + " material/control group(s).\n\nUpdated existing prefab: " + prefabPath +
                    "\nPreserved and checked " + originalColliders.Count + " collider(s)." +
                    "\nUpdated: " + definitionsPath + "\n\nBuild this pack with Railroader > Asset Packs." +
                    (generateSelectableStyle ? " Select Custom Paint in the car's Customize window." : " Per-zone color controls remain available without a separate paint scheme.") +
                    "\n\nBackup: " + backupFolder;
            }
            catch
            {
                if (output != null)
                {
                    PrefabUtility.UnloadPrefabContents(output);
                }
                if (backupFolder != null)
                {
                    Restore(changed, backupFolder);
                }
                throw;
            }
        }

        private static void VerifyBakedMaterials(Transform sourceRoot, Transform outputRoot,
            List<Target> targets)
        {
            var maps = outputRoot.GetComponentsInChildren<MaterialMap>(true);
            if (maps.Length != 1 || maps[0].entries == null)
            {
                throw new InvalidOperationException("Baked MaterialMap was not saved correctly.");
            }
            foreach (var target in targets)
            {
                var matches = maps[0].entries.Where(e => e.name == target.Zone.MaterialName &&
                    e.material != null && string.Equals(AssetDatabase.GetAssetPath(e.material),
                        target.Path, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count != 1)
                {
                    throw new InvalidOperationException("Baked MaterialMap entry missing or duplicated: " +
                        target.Zone.MaterialName);
                }
                foreach (var assignment in target.Assignments)
                {
                    var renderer = ResolveOutputRenderer(sourceRoot, outputRoot,
                        assignment.Renderer, assignment.Slot);
                    string actual = AssetDatabase.GetAssetPath(renderer.sharedMaterials[assignment.Slot]);
                    if (!string.Equals(actual, target.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Baked material override failed to save on '" +
                            renderer.name + "' slot " + assignment.Slot + ".");
                    }
                }
            }
        }

        private static bool IsOwnedBy(Material material, string owner)
        {
            string path = AssetDatabase.GetAssetPath(material);
            var importer = string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path);
            return importer != null && importer.userData == owner;
        }

        // Match by name and occurrence among same-named siblings, not absolute
        // sibling index. Extra collider objects added to the game prefab therefore
        // cannot shift the renderer mapping. Missing or changed meshes fail safely.
        private static Renderer ResolveOutputRenderer(Transform sourceRoot, Transform outputRoot,
            Renderer sourceRenderer, int slot)
        {
            var path = new Stack<KeyValuePair<string, int>>();
            Transform current = sourceRenderer.transform;
            while (current != sourceRoot)
            {
                if (current.parent == null)
                {
                    throw new InvalidOperationException("Colorizer renderer is outside the authoring root: " + sourceRenderer.name);
                }
                int occurrence = 0;
                for (int i = 0; i < current.GetSiblingIndex(); i++)
                {
                    if (current.parent.GetChild(i).name == current.name)
                    {
                        occurrence++;
                    }
                }
                path.Push(new KeyValuePair<string, int>(current.name, occurrence));
                current = current.parent;
            }
            Transform destination = outputRoot;
            foreach (var step in path)
            {
                int occurrence = 0;
                Transform matching = null;
                for (int i = 0; i < destination.childCount; i++)
                {
                    var child = destination.GetChild(i);
                    if (child.name == step.Key && occurrence++ == step.Value)
                    {
                        matching = child;
                        break;
                    }
                }
                if (matching == null)
                {
                    throw new InvalidOperationException("The game prefab no longer matches the authoring renderer hierarchy at '" +
                        sourceRenderer.name + "'. Reconcile the model hierarchy before baking.");
                }
                destination = matching;
            }
            var sourceComponents = sourceRenderer.GetComponents<Renderer>()
                .Where(r => r.GetType() == sourceRenderer.GetType()).ToList();
            int rendererIndex = sourceComponents.IndexOf(sourceRenderer);
            var outputComponents = destination.GetComponents<Renderer>()
                .Where(r => r.GetType() == sourceRenderer.GetType()).ToList();
            if (rendererIndex < 0 || rendererIndex >= outputComponents.Count)
            {
                throw new InvalidOperationException("Cannot match a Colorizer renderer on '" +
                    sourceRenderer.name + "' in the existing game prefab.");
            }
            Renderer target = outputComponents[rendererIndex];
            if (slot >= target.sharedMaterials.Length)
            {
                throw new InvalidOperationException("Material slot " + slot + " is missing from game renderer '" + target.name + "'.");
            }
            Mesh sourceMesh = GetMesh(sourceRenderer);
            Mesh targetMesh = GetMesh(target);
            if (sourceMesh != targetMesh)
            {
                throw new InvalidOperationException("Authoring and game prefab have different meshes at '" +
                    sourceRenderer.name + "'. Update the authoring copy / FBX before baking.");
            }
            return target;
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }
            var filter = renderer.GetComponent<MeshFilter>();
            return filter == null ? null : filter.sharedMesh;
        }

        private static Dictionary<string, string> CaptureColliders(GameObject root)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                string path = ColliderPath(root.transform, transform);
                var colliders = transform.GetComponents<Collider>();
                for (int i = 0; i < colliders.Length; i++)
                {
                    var collider = colliders[i];
                    string key = path + ":" + collider.GetType().FullName + ":" + i;
                    // Collider data should not change during a material-only bake.
                    string state = collider.enabled + ":" + collider.isTrigger + ":" +
                        AssetDatabase.GetAssetPath(collider.sharedMaterial);
                    if (collider is BoxCollider box)
                    {
                        state += ":" + FormatVector(box.center) + ":" + FormatVector(box.size);
                    }
                    else if (collider is SphereCollider sphere)
                    {
                        state += ":" + FormatVector(sphere.center) + ":" + FormatFloat(sphere.radius);
                    }
                    else if (collider is CapsuleCollider capsule)
                    {
                        state += ":" + FormatVector(capsule.center) + ":" + FormatFloat(capsule.radius) + ":" +
                            FormatFloat(capsule.height) + ":" + capsule.direction;
                    }
                    else if (collider is MeshCollider mesh)
                    {
                        state += ":" + mesh.convex + ":" + AssetDatabase.GetAssetPath(mesh.sharedMesh) +
                            ":" + (mesh.sharedMesh == null ? "" : mesh.sharedMesh.name);
                    }
                    result.Add(key, state);
                }
            }
            return result;
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatVector(Vector3 value)
        {
            return FormatFloat(value.x) + "," + FormatFloat(value.y) + "," + FormatFloat(value.z);
        }

        private static string ColliderPath(Transform root, Transform target)
        {
            var segments = new Stack<string>();
            while (target != root)
            {
                if (target.parent == null)
                {
                    throw new InvalidOperationException("Collider is outside the game prefab root.");
                }
                int occurrence = 0;
                for (int i = 0; i < target.GetSiblingIndex(); i++)
                {
                    if (target.parent.GetChild(i).name == target.name)
                    {
                        occurrence++;
                    }
                }
                segments.Push(target.name + "[" + occurrence + "]");
                target = target.parent;
            }
            return "root/" + string.Join("/", segments.ToArray());
        }

        private static void VerifyColliders(Dictionary<string, string> before, Dictionary<string, string> after)
        {
            if (before.Count == after.Count && before.All(pair =>
                after.TryGetValue(pair.Key, out var state) && state == pair.Value))
            {
                return;
            }
            string missing = string.Join("\n", before.Keys.Except(after.Keys).Take(12));
            string changed = string.Join("\n", before.Keys.Intersect(after.Keys)
                .Where(key => before[key] != after[key]).Take(12));
            string added = string.Join("\n", after.Keys.Except(before.Keys).Take(12));
            throw new InvalidOperationException("Colorizer bake would change the game's colliders (" +
                before.Count + " before, " + after.Count + " after). Changes have been rolled back." +
                (missing.Length == 0 ? "" : "\nMissing: " + missing) +
                (changed.Length == 0 ? "" : "\nModified: " + changed) +
                (added.Length == 0 ? "" : "\nAdded: " + added));
        }

        private static List<Target> GatherTargets(GameObject root, string materialsFolder)
        {
            Dictionary<string, Target> grouped = new Dictionary<string, Target>(StringComparer.Ordinal);
            HashSet<string> claimedSlots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var marker in root.GetComponentsInChildren<CustomColorizer>(true))
            {
                if (!marker.enabled)
                {
                    continue;
                }
                var renderer = marker.GetComponent<Renderer>();
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                {
                    throw new InvalidOperationException(marker.name + ": Colorizer Marker requires a MeshRenderer or SkinnedMeshRenderer on the same object.");
                }
                var slots = renderer.sharedMaterials;
                if (marker.materialSlot < 0 || marker.materialSlot >= slots.Length || slots[marker.materialSlot] == null)
                {
                    throw new InvalidOperationException(marker.name + ": material slot is empty or out of range.");
                }
                string slotKey = HierarchyKey(root.transform, marker.transform) + ":" + marker.materialSlot;
                if (!claimedSlots.Add(slotKey))
                {
                    throw new InvalidOperationException(marker.name + ": more than one colorizer claims material slot " + marker.materialSlot + ".");
                }
                var source = slots[marker.materialSlot];
                if (!AssetDatabase.Contains(source))
                {
                    throw new InvalidOperationException(marker.name + ": save the source material as an asset first.");
                }
                if (!source.HasProperty("_BaseColor") && !source.HasProperty("_Color"))
                {
                    throw new InvalidOperationException(source.name + ": shader has no _BaseColor or _Color property. Use a shared Railroader car shader.");
                }
                if (string.IsNullOrWhiteSpace(marker.styleClass))
                {
                    throw new InvalidOperationException(marker.name + ": Style Class is required.");
                }
                if (marker.showInCustomize && string.IsNullOrWhiteSpace(marker.controlLabel))
                {
                    throw new InvalidOperationException(marker.name + ": Control Label is required for customizable colors.");
                }
                if (float.IsNaN(marker.colorGamma) || float.IsInfinity(marker.colorGamma) || marker.colorGamma <= 0f)
                {
                    throw new InvalidOperationException(marker.name + ": Color Gamma must be finite and positive.");
                }

                // Unity's local file identifier distinguishes distinct embedded FBX
                // materials even when they share the same FBX asset path / GUID.
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId))
                {
                    throw new InvalidOperationException(source.name + ": Unity could not identify the material asset.");
                }
                string label = marker.controlLabel == null ? "" : marker.controlLabel.Trim();
                string groupKey = guid + ":" + localId + "|" + marker.styleClass.Trim() + "|" + label;
                string colorHex = ColorUtility.ToHtmlStringRGB(marker.defaultColor);
                if (!grouped.TryGetValue(groupKey, out var group))
                {
                    group = new Target
                    {
                        Source = source,
                        Zone = new ColorZone
                        {
                            // Deterministic identifier: stable across marker and FBX hierarchy order.
                            Key = null, // Assigned after readable generated material names have been resolved.
                            StyleClass = marker.styleClass.Trim(),
                            ColorHex = colorHex,
                            Gamma = marker.colorGamma,
                            Customize = marker.showInCustomize,
                            Label = label
                        }
                    };
                    grouped.Add(groupKey, group);
                }
                else if (group.Zone.ColorHex != colorHex ||
                    !Mathf.Approximately(group.Zone.Gamma, marker.colorGamma) ||
                    group.Zone.Customize != marker.showInCustomize)
                {
                    throw new InvalidOperationException(
                        "Colorizers for material '" + source.name + "' with style '" + marker.styleClass +
                        "' and label '" + label + "' have different default color, gamma or customization settings. " +
                        "Make their settings identical or use distinct style classes/labels.");
                }
                group.Assignments.Add(new Assignment { Renderer = renderer, Slot = marker.materialSlot });
            }

            // Use the Style Class itself as the generated material name (body.mat,
            // door.mat, etc.). Different source materials can share a Style Class
            // but still need separate material assets / MaterialMap entries.
            // Sort by stable source identity so repeated bakes choose the same names.
            var ordered = grouped.OrderBy(p => Slug(p.Value.Zone.StyleClass), StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Value.Source.name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Value.Zone.Label, StringComparer.Ordinal)
                .ThenBy(p => p.Key, StringComparer.Ordinal)
                .ToList();
            HashSet<string> reservedStyleNames = new HashSet<string>(
                ordered.Select(p => Slug(p.Value.Zone.StyleClass)), StringComparer.OrdinalIgnoreCase);
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in ordered)
            {
                var group = pair.Value;
                string baseName = Slug(group.Zone.StyleClass);
                string materialName = baseName;
                if (!names.Add(materialName))
                {
                    // Reserve unsuffixed class names for their actual classes.
                    // Short, deterministic numeric suffixes distinguish groups
                    // that share a class but cannot share one material asset.
                    int suffix = 2;
                    do
                    {
                        materialName = baseName + "-" + suffix++;
                    }
                    while (reservedStyleNames.Contains(materialName) || !names.Add(materialName));
                }
                group.Zone.MaterialName = materialName;
                group.Zone.Key = materialName;
                group.Path = materialsFolder + "/" + materialName + ".mat";
            }
            return grouped.Values.OrderBy(t => t.Zone.MaterialName, StringComparer.Ordinal).ToList();
        }

        private static string Slug(string value)
        {
            string result = Regex.Replace((value ?? "").Trim().ToLowerInvariant(), @"[^a-z0-9_-]+", "-").Trim('-');
            if (result.Length > 42)
            {
                result = result.Substring(0, 42).Trim('-');
            }
            return result.Length == 0 ? "color" : result;
        }

        private static string HierarchyKey(Transform root, Transform target)
        {
            List<string> parts = new List<string>();
            while (target != root)
            {
                parts.Add(target.GetSiblingIndex().ToString());
                target = target.parent;
                if (target == null)
                {
                    throw new InvalidOperationException("Colorizer lies outside the export root.");
                }
            }
            parts.Reverse();
            return "root/" + string.Join("/", parts);
        }


        private static void EnsureOwnedOrNew(string path, string owner)
        {
            if (!File.Exists(path))
            {
                return;
            }
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null || importer.userData != owner)
            {
                throw new InvalidOperationException("Refusing to replace an asset not created by this exporter: " + path + ". Choose a new Prefab Name.");
            }
        }

        private static void SetOwner(string path, string owner)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                throw new InvalidOperationException("Unity did not import " + path);
            }
            importer.userData = owner;
            importer.SaveAndReimport();
        }

        private static string Backup(IEnumerable<string> paths)
        {
            string folder = "Library/RailroaderAssetTools/ColorizerBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(folder);
            foreach (var path in paths.SelectMany(p => new[] { p, p + ".meta" }))
            {
                if (!File.Exists(path))
                {
                    continue;
                }
                string destination = Path.Combine(folder, path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(path, destination, false);
            }
            return folder;
        }

        private static void Restore(IEnumerable<string> paths, string backup)
        {
            // Only paths prevalidated and explicitly touched by this bake are restored/deleted.
            foreach (var path in paths)
            {
                string saved = Path.Combine(backup, path);
                if (!File.Exists(saved))
                {
                    AssetDatabase.DeleteAsset(path);
                }
                else
                {
                    File.Copy(saved, path, true);
                    string meta = saved + ".meta";
                    if (File.Exists(meta))
                    {
                        File.Copy(meta, path + ".meta", true);
                    }
                }
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Debug.LogWarning("Colorizer bake rolled back. Backup remains at " + backup);
        }
    }
}
