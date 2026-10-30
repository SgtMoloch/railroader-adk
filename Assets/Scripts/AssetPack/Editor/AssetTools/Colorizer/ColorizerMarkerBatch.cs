using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Railroader.AssetTools.Colorizer
{
    /// <summary>
    /// Bulk authoring operates only on an existing Authoring/ prefab; it never edits
    /// the imported FBX, its source materials or the game-facing root prefab.
    /// </summary>
    public static class ColorizerMarkerBatch
    {
        public sealed class Slot
        {
            public string Id;
            public string MaterialId;
            public string MaterialName;
            public string RendererId;
            public string RendererPath;
            public int MaterialSlot;
            public bool HasMarker;
            public string ExistingStyleClass;
            public string ExistingLabel;
            public Color ExistingDefaultColor;
            public float ExistingGamma;
        }

        public sealed class Request
        {
            public string Id;
            public string StyleClass;
            public string Label;
            // Optional. Only the explicit default-all operation resets color and
            // gamma. Normal per-slot batch edits retain existing color settings.
            public bool NormalizeDefaults;
            public Color DefaultColor;
            public float DefaultGamma;
        }

        public static List<Slot> Inspect(string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                List<Slot> list = new List<Slot>();
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                    {
                        continue;
                    }
                    string path = IndexPath(root.transform, renderer.transform);
                    string prettyPath = NamedPath(root.transform, renderer.transform);
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var material = materials[i];
                        if (material == null || !AssetDatabase.Contains(material) ||
                            (!material.HasProperty("_BaseColor") && !material.HasProperty("_Color"))) continue;
                        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string guid, out long localId))
                        {
                            continue;
                        }
                        var marker = renderer.GetComponents<CustomColorizer>().FirstOrDefault(m => m.materialSlot == i);
                        list.Add(new Slot
                        {
                            Id = path + ":" + i,
                            RendererId = path,
                            MaterialId = guid + ":" + localId,
                            MaterialName = material.name,
                            RendererPath = prettyPath,
                            MaterialSlot = i,
                            HasMarker = marker != null,
                            ExistingStyleClass = marker == null ? "" : marker.styleClass,
                            ExistingLabel = marker == null ? "" : marker.controlLabel,
                            ExistingDefaultColor = marker == null ? Color.white : marker.defaultColor,
                            ExistingGamma = marker == null ? 1f : marker.colorGamma
                        });
                    }
                }
                return list.OrderBy(s => s.RendererPath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(s => s.RendererId, StringComparer.Ordinal)
                    .ThenBy(s => s.MaterialSlot).ToList();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Part-first default: the renderer name determines the control, while
        // generic imported renderer names fall back to the material name.
        public static string SuggestedPartClass(string rendererPath, string materialName)
        {
            string leaf = (rendererPath ?? "").Split('/').LastOrDefault() ?? "";
            if (leaf == "(root)")
            {
                leaf = "";
            }
            string normalized = Regex.Replace(leaf.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            if (normalized == "default" || normalized == "mesh" || normalized == "object" ||
                normalized == "geometry" || normalized == "")
                return SuggestedClass(materialName);
            return SuggestedClass(normalized);
        }

        public static bool IsValidStyleClass(string value)
        {
            return Regex.IsMatch(value ?? "", "^[a-z][a-z0-9_-]*$");
        }

        public static string SuggestedClass(string materialName)
        {
            var result = Regex.Replace((materialName ?? "").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            // The ADK's standard 'body' class lets related body materials share one color.
            if (result == "mainbody" || result == "main-body" || result == "body")
            {
                return "body";
            }
            if (result.Length > 38)
            {
                result = result.Substring(0, 38).Trim('-');
            }
            return string.IsNullOrEmpty(result) ? "body" : result;
        }

        public static string SuggestedLabel(string styleClass)
        {
            if (styleClass == "body")
            {
                return "Body";
            }
            var parts = Regex.Split(styleClass ?? "", "[-_\\s]+").Where(p => p.Length != 0);
            return string.Join(" ", parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
        }

        /// <summary>
        /// Removes every Colorizer Marker from the authoring prefab, including
        /// markers on missing/unsupported materials omitted by Inspect().
        /// No FBX, renderer, material asset, or game-facing prefab is edited.
        /// </summary>
        public static string RemoveAll(string prefabPath)
        {
            ValidateAuthoringPath(prefabPath);
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var markers = root.GetComponentsInChildren<CustomColorizer>(true);
                if (markers.Length == 0)
                {
                    return "No Colorizer Markers found in the authoring prefab.";
                }
                foreach (var marker in markers)
                {
                    UnityEngine.Object.DestroyImmediate(marker);
                }
                if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null)
                {
                    throw new InvalidOperationException("Could not save authoring prefab: " + prefabPath);
                }
                AssetDatabase.SaveAssets();
                return $"Removed {markers.Length} Colorizer Marker(s) from the authoring prefab.";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Counts markers on blacklisted material slots, including markers whose
        /// materials don't appear in Inspect() (e.g. glass shaders without a color
        /// property). A multi-material Renderer is handled per materialSlot.
        /// </summary>
        public static int CountBlacklisted(string prefabPath, IList<string> materialNameTerms)
        {
            ValidateAuthoringPath(prefabPath);
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                return FindBlacklisted(root, materialNameTerms).Count;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Removes ONLY marker components attached to blacklisted material slots
        /// in the editable authoring prefab. Existing meshes, materials, other
        /// components, and the game-facing prefab are never modified.
        /// Does not retroactively undo a previous bake.
        /// </summary>
        public static string RemoveBlacklisted(string prefabPath, IList<string> materialNameTerms)
        {
            ValidateAuthoringPath(prefabPath);
            if (materialNameTerms == null || materialNameTerms.Count == 0)
                return "Blacklist is empty; no markers were removed.";
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var targets = FindBlacklisted(root, materialNameTerms);
                if (targets.Count == 0)
                {
                    return "No Colorizer Markers match the material blacklist.";
                }
                foreach (var marker in targets)
                {
                    UnityEngine.Object.DestroyImmediate(marker);
                }
                if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null)
                {
                    throw new InvalidOperationException("Could not save authoring prefab: " + prefabPath);
                }
                AssetDatabase.SaveAssets();
                return $"Removed {targets.Count} blacklisted Colorizer Marker(s) from the authoring prefab.";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static List<CustomColorizer> FindBlacklisted(GameObject root, IList<string> terms)
        {
            List<CustomColorizer> matches = new List<CustomColorizer>();
            if (terms == null || terms.Count == 0)
            {
                return matches;
            }
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                {
                    continue;
                }
                var materials = renderer.sharedMaterials;
                foreach (var marker in renderer.GetComponents<CustomColorizer>())
                {
                    int slot = marker.materialSlot;
                    if (slot < 0 || slot >= materials.Length)
                    {
                        continue;
                    }
                    var material = materials[slot];
                    if (material != null && ColorizerMaterialBlacklist.Matches(material.name, terms))
                    {
                        matches.Add(marker);
                    }
                }
            }
            return matches;
        }

        public static int CountAll(string prefabPath)
        {
            ValidateAuthoringPath(prefabPath);
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                return root.GetComponentsInChildren<CustomColorizer>(true).Length;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ValidateAuthoringPath(string prefabPath)
        {
            if (string.IsNullOrWhiteSpace(prefabPath) ||
                !prefabPath.Replace('\\', '/').Contains("/Authoring/") ||
                !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                throw new InvalidOperationException("Only an existing Authoring/ prefab asset may be edited in bulk.");
        }

        public static string Apply(string prefabPath, IList<Request> requests, bool updateExisting, bool remove)
        {
            if (requests == null || requests.Count == 0)
            {
                return "No material slots selected.";
            }
            ValidateAuthoringPath(prefabPath);
            foreach (var req in requests)
            {
                if (string.IsNullOrWhiteSpace(req.Id))
                {
                    throw new InvalidOperationException("Missing slot identity.");
                }
                if (!remove && !Regex.IsMatch(req.StyleClass ?? "", "^[a-z][a-z0-9_-]*$"))
                {
                    throw new InvalidOperationException("Style Class must start with a lowercase letter and contain only lowercase letters, numbers, _ or -: " + req.StyleClass);
                }
                if (!remove && string.IsNullOrWhiteSpace(req.Label))
                {
                    throw new InvalidOperationException("A control label is required for " + req.Id);
                }
                if (!remove && req.NormalizeDefaults &&
                    (float.IsNaN(req.DefaultGamma) || float.IsInfinity(req.DefaultGamma) || req.DefaultGamma <= 0f))
                    throw new InvalidOperationException("Default Gamma must be greater than zero for " + req.Id);
            }
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            int added = 0, updated = 0, removed = 0, skipped = 0;
            try
            {
                foreach (var req in requests)
                {
                    int colon = req.Id.LastIndexOf(':');
                    if (colon < 0 || !int.TryParse(req.Id.Substring(colon + 1), out int slot))
                    {
                        throw new InvalidOperationException("Invalid slot identity: " + req.Id);
                    }
                    var t = Resolve(root.transform, req.Id.Substring(0, colon));
                    var renderer = t.GetComponent<Renderer>();
                    if (renderer == null || slot < 0 || slot >= renderer.sharedMaterials.Length)
                    {
                        throw new InvalidOperationException("Renderer or slot changed since last refresh: " + req.Id);
                    }
                    var markers = renderer.GetComponents<CustomColorizer>().Where(m => m.materialSlot == slot).ToArray();
                    if (remove)
                    {
                        foreach (var marker in markers)
                        {
                            UnityEngine.Object.DestroyImmediate(marker); removed++;
                        }
                        continue;
                    }
                    var material = renderer.sharedMaterials[slot];
                    if (material == null || !AssetDatabase.Contains(material) ||
                        (!material.HasProperty("_BaseColor") && !material.HasProperty("_Color")))
                        throw new InvalidOperationException("Invalid or non-colorable material in slot " + req.Id);
                    if (markers.Length > 1)
                    {
                        throw new InvalidOperationException("Multiple markers on one slot: " + req.Id + ". Remove duplicates first.");
                    }
                    var existing = markers.FirstOrDefault();
                    if (existing != null && !updateExisting)
                    {
                        skipped++; continue;
                    }
                    var target = existing ?? renderer.gameObject.AddComponent<CustomColorizer>();
                    target.materialSlot = slot;
                    target.styleClass = req.StyleClass;
                    target.controlLabel = req.Label;
                    target.showInCustomize = true;
                    if (req.NormalizeDefaults)
                    {
                        target.defaultColor = req.DefaultColor;
                        target.colorGamma = req.DefaultGamma;
                    }
                    if (existing == null)
                    {
                        added++;
                    }
                    else
                    {
                        updated++;
                    }
                }
                if (added + updated + removed > 0)
                {
                    if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null)
                    {
                        throw new InvalidOperationException("Could not save authoring prefab: " + prefabPath);
                    }
                    AssetDatabase.SaveAssets();
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return $"Markers: {added} added, {updated} updated, {removed} removed, {skipped} existing skipped.";
        }

        private static Transform Resolve(Transform root, string id)
        {
            var t = root;
            if (string.IsNullOrEmpty(id))
            {
                return t;
            }
            foreach (var part in id.Split('.'))
            {
                if (!int.TryParse(part, out int index) || index < 0 || index >= t.childCount)
                {
                    throw new InvalidOperationException("Prefab hierarchy changed. Refresh parts before applying.");
                }
                t = t.GetChild(index);
            }
            return t;
        }

        private static string IndexPath(Transform root, Transform child)
        {
            List<int> indices = new List<int>();
            while (child != root)
            {
                indices.Add(child.GetSiblingIndex());
                child = child.parent;
                if (child == null)
                {
                    throw new InvalidOperationException("Renderer not part of authoring prefab.");
                }
            }
            indices.Reverse();
            return string.Join(".", indices.Select(i => i.ToString()));
        }

        private static string NamedPath(Transform root, Transform child)
        {
            List<string> names = new List<string>();
            while (child != root)
            {
                names.Add(child.name);
                child = child.parent;
                if (child == null)
                {
                    throw new InvalidOperationException("Renderer not part of authoring prefab.");
                }
            }
            names.Reverse();
            return names.Count == 0 ? "(root)" : string.Join("/", names);
        }
    }
}
