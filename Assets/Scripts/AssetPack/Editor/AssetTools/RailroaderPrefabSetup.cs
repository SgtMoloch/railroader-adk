#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using AssetPack.Common.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Railroader.AssetTools
{
    /// <summary>Non-destructive prefab setup. Edits the root prefab variant, never the shared FBX importer.</summary>
    internal static class RailroaderPrefabSetup
    {
        internal static string[] WheelPaths(string prefabPath)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null)
            {
                return new string[0];
            }
            // No need to edit the prefab to discover paths; nested transforms are visible on the asset.
            List<string> found = new List<string>();
            foreach (Transform t in asset.GetComponentsInChildren<Transform>(true))
            {
                if (t == asset.transform || !t.name.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                // Avoid writing wheelset/group parents; only leaf wheel objects should be spun.
                if (t.GetComponentsInChildren<Renderer>(true).Length == 0 && t.childCount > 0)
                {
                    continue;
                }
                List<string> parts = new List<string>();
                Transform current = t;
                while (current != null && current != asset.transform)
                {
                    parts.Add(current.name);
                    current = current.parent;
                }
                if (current != asset.transform)
                {
                    continue;
                }
                parts.Reverse();
                found.Add(String.Join("/", parts));
            }
            return found.Distinct(StringComparer.Ordinal).ToArray();
        }

        internal static Material TruckMaterial(string prefabPath)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null)
            {
                return null;
            }
            var materials = asset.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials ?? new Material[0])
                .Where(m => m != null).Distinct().ToArray();
            return materials.FirstOrDefault(m => string.Equals(m.name, "Truck", StringComparison.OrdinalIgnoreCase))
                ?? materials.FirstOrDefault(m => m.name.IndexOf("truck", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static bool HasClickableLayer => LayerMask.NameToLayer("Clickable") >= 0;

        /// <summary>Updates only the requested features. Never replaces an existing collider or map entry.</summary>
        internal static string Configure(string prefabPath, bool collider, bool truckMap)
        {
            if (!collider && !truckMap)
            {
                return "No prefab setup requested";
            }
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            if (contents == null)
            {
                throw new InvalidOperationException("Cannot open prefab: " + prefabPath);
            }
            List<string> notes = new List<string>();
            bool changed = false;
            try
            {
                if (collider)
                {
                    int clickable = LayerMask.NameToLayer("Clickable");
                    if (clickable < 0)
                        notes.Add("Clickable layer is missing from Unity Tags & Layers; collider setup skipped");
                    else
                    {
                        var existing = contents.GetComponent<BoxCollider>();
                        if (existing == null)
                        {
                            Bounds bounds;
                            if (TryLocalMeshBounds(contents, out bounds))
                            {
                                var box = contents.AddComponent<BoxCollider>();
                                box.center = bounds.center;
                                box.size = bounds.size;
                                notes.Add("Added BoxCollider from mesh bounds");
                                changed = true;
                            }
                            else
                            {
                                notes.Add("No mesh bounds found; collider skipped");
                            }
                        }
                        else
                        {
                            notes.Add("Existing root BoxCollider preserved");
                        }
                        if (contents.layer != clickable)
                        {
                            contents.layer = clickable;
                            changed = true;
                        }
                        notes.Add("Root layer: Clickable");
                    }
                }
                if (truckMap)
                {
                    Material material = contents.GetComponentsInChildren<Renderer>(true)
                        .SelectMany(r => r.sharedMaterials ?? new Material[0])
                        .Where(m => m != null)
                        .Distinct()
                        .FirstOrDefault(m => string.Equals(m.name, "Truck", StringComparison.OrdinalIgnoreCase));
                    if (material == null)
                        material = contents.GetComponentsInChildren<Renderer>(true)
                            .SelectMany(r => r.sharedMaterials ?? new Material[0])
                            .FirstOrDefault(m => m != null && m.name.IndexOf("truck", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (material == null)
                        notes.Add("No material named Truck found; MaterialMap/RendererGroup not created");
                    else
                    {
                        var maps = contents.GetComponentsInChildren<MaterialMap>(true);
                        if (maps.Length > 1)
                        {
                            throw new InvalidOperationException("More than one MaterialMap exists on " + prefabPath);
                        }
                        MaterialMap map = maps.Length > 0 ? maps[0] : contents.AddComponent<MaterialMap>();
                        if (maps.Length == 0)
                        {
                            changed = true;
                        }
                        if (map.entries == null)
                        {
                            map.entries = new List<MaterialMap.MapEntry>(); changed = true;
                        }
                        int mappedIndex = map.entries.FindIndex(e =>
                            string.Equals(e.name, "Truck", StringComparison.OrdinalIgnoreCase));
                        if (mappedIndex >= 0)
                        {
                            // Preserve valid user-defined maps; repair an unassigned Truck slot.
                            var entry = map.entries[mappedIndex];
                            if (entry.material == null)
                            {
                                entry.material = material;
                                map.entries[mappedIndex] = entry;
                                changed = true;
                                notes.Add("Filled empty Truck MaterialMap entry");
                            }
                            else
                            {
                                notes.Add("Existing Truck MaterialMap entry preserved");
                            }
                        }
                        else
                        {
                            map.entries.Add(new MaterialMap.MapEntry { name = "Truck", material = material });
                            changed = true;
                            notes.Add("MaterialMap: Truck -> " + material.name);
                        }
                    }
                }
                if (changed && PrefabUtility.SaveAsPrefabAsset(contents, prefabPath) == null)
                {
                    throw new InvalidOperationException("Unity failed saving configured prefab: " + prefabPath);
                }
                return String.Join("; ", notes);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // Mesh bounds are in mesh-local space, not world-space Renderer.bounds. This
        // keeps nested/scaled FBX transforms and prefab variants correct.
        private static bool TryLocalMeshBounds(GameObject root, out Bounds total)
        {
            total = new Bounds();
            bool any = false;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null)
                {
                    AddBounds(filter.sharedMesh.bounds, filter.transform, root.transform, ref total, ref any);
                }
            foreach (SkinnedMeshRenderer skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skinned.sharedMesh != null)
                {
                    AddBounds(skinned.localBounds, skinned.transform, root.transform, ref total, ref any);
                }
            return any;
        }

        private static void AddBounds(Bounds mesh, Transform from, Transform root, ref Bounds total, ref bool any)
        {
            Vector3 center = mesh.center, extents = mesh.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                Vector3 inRoot = root.InverseTransformPoint(from.TransformPoint(corner));
                if (!any)
                {
                    total = new Bounds(inRoot, Vector3.zero); any = true;
                }
                else
                {
                    total.Encapsulate(inRoot);
                }
            }
        }
    }
}
#endif
