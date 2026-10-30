using System;
using System.Collections.Generic;
using System.IO;
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
            public Renderer Renderer;
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
                throw new InvalidOperationException("Bake from an authoring prefab asset, not a scene object.");
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

            // Bake IN PLACE to the pack's existing root prefab. Authoring/ is a
            // private editable backup; a second root-level prefab would appear
            // as an extra model in Railroader's asset selection UI.
            string prefabPath = pack + "/" + prefabName + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                throw new InvalidOperationException("Root prefab does not exist: " + prefabPath +
                    ". Import the model first, then create its Authoring copy.");
            string materialsFolder = pack + "/ColorizerMaterials-" + prefabName;
            string owner = "RailroaderAssetTools.Colorizer:" + carIdentifier;
            var clone = Object.Instantiate(source);
            // If we cloned a prefab ASSET, unpack only its outer wrapper, not the
            // nested imported FBX. This avoids creating a variant of the authoring
            // prefab while keeping the Blender -> FBX link on the child objects.
            if (PrefabUtility.IsAnyPrefabInstanceRoot(clone))
                PrefabUtility.UnpackPrefabInstance(clone, PrefabUnpackMode.OutermostRoot,
                    InteractionMode.AutomatedAction);
            clone.name = prefabName;
            clone.transform.position = Vector3.zero;
            clone.transform.rotation = Quaternion.identity;
            List<string> changed = new List<string>();
            string backupFolder = null;
            try
            {
                // Preserve the nested FBX prefab connection. The authoring markers
                // are added overrides and are removed from this clone before saving.
                // Do NOT UnpackAll: that severs the Blender reimport relationship.
                foreach (var transform in clone.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                    {
                        throw new InvalidOperationException("Missing scripts on " + transform.name + ". Repair them in the authoring prefab before baking.");
                    }
                var maps = clone.GetComponentsInChildren<MaterialMap>(true);
                if (maps.Length > 1)
                {
                    throw new InvalidOperationException("Use only one MaterialMap in the prefab hierarchy. Found " + maps.Length + ".");
                }
                var map = maps.Length == 0 ? clone.AddComponent<MaterialMap>() : maps[0];
                if (map.entries == null)
                {
                    map.entries = new List<MaterialMap.MapEntry>();
                }
                HashSet<string> mapNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in map.entries)
                    if (string.IsNullOrWhiteSpace(entry.name) || entry.material == null || !mapNames.Add(entry.name))
                    {
                        throw new InvalidOperationException("Existing MaterialMap entries must have unique names and assigned materials.");
                    }

                var targets = GatherTargets(clone, materialsFolder);
                if (targets.Count == 0)
                {
                    throw new InvalidOperationException("No enabled Custom Colorizer components found. Attach one to a mesh Renderer first.");
                }
                HashSet<string> outputMaterials = new HashSet<string>(targets.Select(t => t.Path), StringComparer.OrdinalIgnoreCase);
                var sourceMaterials = clone.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                    .Concat(map.entries.Select(e => e.material));
                foreach (var sourceMaterial in sourceMaterials)
                    if (sourceMaterial != null && outputMaterials.Contains(AssetDatabase.GetAssetPath(sourceMaterial)))
                        throw new InvalidOperationException("The authoring model references a generated output material: " + sourceMaterial.name +
                            ". Use the original source material, or duplicate it outside the generated material folder before baking.");
                foreach (var target in targets)
                {
                    EnsureOwnedOrNew(target.Path, owner);
                    if (!mapNames.Add(target.Zone.MaterialName))
                    {
                        throw new InvalidOperationException("A generated material-map name already exists: " + target.Zone.MaterialName);
                    }
                }
                // Merge validates the native schema, duplicate slots/classes and existing ownership
                // before any project file is changed.
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
                    // Show the authored default in Unity too. Native RendererGroup replaces this
                    // tint when the game applies styles, so this does not multiply the color twice.
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
                        var slots = assignment.Renderer.sharedMaterials;
                        slots[assignment.Slot] = material;
                        assignment.Renderer.sharedMaterials = slots;
                    }
                    // One MaterialMap entry / RendererGroup per logical material,
                    // regardless of how many mesh slots share it.
                    map.entries.Add(new MaterialMap.MapEntry { name = target.Zone.MaterialName, material = material });
                }
                foreach (var marker in clone.GetComponentsInChildren<CustomColorizer>(true))
                {
                    Object.DestroyImmediate(marker);
                }
                var saved = PrefabUtility.SaveAsPrefabAsset(clone, prefabPath);
                if (saved == null)
                {
                    throw new InvalidOperationException("Unity could not save the baked prefab.");
                }
                // The root prefab is an ADK asset, not a generated output owned
                // by this tool. Preserve its existing importer metadata.
                File.WriteAllText(definitionsPath, merged, new UTF8Encoding(false));
                AssetDatabase.Refresh();
                Selection.activeObject = saved;
                return "Baked " + targets.Sum(t => t.Assignments.Count) + " mesh slot(s) using " +
                    targets.Count + " material/control group(s).\n\nUpdated existing prefab: " + prefabPath +
                    "\nUpdated: " + definitionsPath + "\n\nBuild this pack with Railroader > Asset Packs." +
                    (generateSelectableStyle ? " Select Custom Paint in the car's Customize window." : " Per-zone color controls remain available without a separate paint scheme.") +
                    "\n\nBackup: " + backupFolder;
            }
            catch
            {
                if (backupFolder != null)
                {
                    Restore(changed, backupFolder);
                }
                throw;
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }
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
