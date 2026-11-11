#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Railroader.AssetTools
{
    /// <summary>
    /// Copies collision components and GameObject layers onto a NEW FBX-based livery variant.
    /// Does not instantiate the source prefab as the livery's parent: that would also inherit
    /// materials, Colorizer data, animations, and other source-livery modifications.
    /// </summary>
    internal static class RailroaderLiveryCollisionCopier
    {
        internal static string Copy(string sourcePath, string targetPath, string modelPath)
        {
            if (string.IsNullOrEmpty(sourcePath) || sourcePath == targetPath)
                throw new InvalidOperationException("Choose a different source prefab for collider copying.");
            GameObject sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            GameObject targetAsset = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            if (sourceAsset == null || targetAsset == null)
                throw new InvalidOperationException("The source or destination livery prefab is missing.");
            if (PrefabUtility.GetPrefabAssetType(sourceAsset) != PrefabAssetType.Variant)
                throw new InvalidOperationException("The source prefab must be an FBX-based Prefab Variant. " +
                    "Legacy prefabs with an extra nested-FBX root may have incompatible collider paths; " +
                    "choose the original model variant instead.");
            if (!RailroaderAssetPackTools.PrefabReferencesModel(sourceAsset, modelPath) ||
                !RailroaderAssetPackTools.PrefabReferencesModel(targetAsset, modelPath))
                throw new InvalidOperationException("Both prefabs must reference the same imported FBX/OBJ.");

            GameObject source = null;
            GameObject target = null;
            int copiedComponents = 0;
            int createdObjects = 0;
            int copiedLayers = 0;
            int expectedColliders = 0;
            int rootLayer = 0;
            try
            {
                source = PrefabUtility.LoadPrefabContents(sourcePath);
                target = PrefabUtility.LoadPrefabContents(targetPath);
                if (source == null || target == null)
                    throw new InvalidOperationException("Cannot open source or target prefab contents.");

                rootLayer = source.layer;
                // Work from the source hierarchy; shared FBX nodes already exist on the
                // destination, but manually created collider holders may not.
                foreach (Transform sourceNode in source.GetComponentsInChildren<Transform>(true))
                {
                    Collider[] colliders = sourceNode.GetComponents<Collider>();
                    bool required = sourceNode == source.transform || colliders.Length > 0 ||
                        sourceNode.gameObject.layer != 0;
                    Transform targetNode = MatchNode(source.transform, target.transform,
                        sourceNode, required, ref createdObjects);
                    if (targetNode == null)
                        continue; // No corresponding object and no collision/layer data to copy.

                    if (targetNode.gameObject.layer != sourceNode.gameObject.layer)
                    {
                        targetNode.gameObject.layer = sourceNode.gameObject.layer;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(targetNode.gameObject);
                        copiedLayers++;
                    }
                    CopyColliders(colliders, targetNode.gameObject, ref copiedComponents);
                }

                expectedColliders = target.GetComponentsInChildren<Collider>(true).Length;
                if (PrefabUtility.SaveAsPrefabAsset(target, targetPath) == null)
                    throw new InvalidOperationException("Could not save the livery prefab after collider copying.");
            }
            finally
            {
                if (target != null)
                    PrefabUtility.UnloadPrefabContents(target);
                if (source != null)
                    PrefabUtility.UnloadPrefabContents(source);
            }

            // Validate the serialized prefab, not merely the in-memory object we edited.
            GameObject verification = PrefabUtility.LoadPrefabContents(targetPath);
            try
            {
                if (verification == null ||
                    verification.GetComponentsInChildren<Collider>(true).Length != expectedColliders ||
                    verification.layer != rootLayer)
                {
                    throw new InvalidOperationException("Livery prefab did not retain the copied colliders " +
                        "or root Clickable layer after saving. Inspect it before using it in-game.");
                }
            }
            finally
            {
                if (verification != null)
                    PrefabUtility.UnloadPrefabContents(verification);
            }
            return "Copied/synchronized " + copiedComponents + " collider components from " +
                sourcePath + "; created " + createdObjects + " collider-holder objects; " +
                "updated " + copiedLayers + " layers (root layer: " +
                LayerMask.LayerToName(rootLayer) + "); " + expectedColliders + " total colliders verified";
        }

        private static Transform MatchNode(Transform sourceRoot, Transform targetRoot,
            Transform sourceNode, bool createMissing, ref int createdObjects)
        {
            if (sourceNode == sourceRoot)
                return targetRoot;

            Transform parent = MatchNode(sourceRoot, targetRoot, sourceNode.parent,
                createMissing, ref createdObjects);
            if (parent == null)
                return null;

            // Same-name sibling ordinal remains stable even when independent collider
            // holder GameObjects have been added alongside imported FBX children.
            int ordinal = 0;
            for (int i = 0; i < sourceNode.GetSiblingIndex(); i++)
            {
                if (string.Equals(sourceNode.parent.GetChild(i).name,
                    sourceNode.name, StringComparison.Ordinal))
                    ordinal++;
            }
            int seen = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform candidate = parent.GetChild(i);
                if (!string.Equals(candidate.name, sourceNode.name, StringComparison.Ordinal))
                    continue;
                if (seen++ == ordinal)
                    return candidate;
            }
            if (!createMissing)
                return null;

            GameObject holder = new GameObject(sourceNode.name);
            Transform result = holder.transform;
            result.SetParent(parent, false);
            result.localPosition = sourceNode.localPosition;
            result.localRotation = sourceNode.localRotation;
            result.localScale = sourceNode.localScale;
            holder.SetActive(sourceNode.gameObject.activeSelf);
            holder.layer = sourceNode.gameObject.layer;
            holder.tag = sourceNode.gameObject.tag;
            createdObjects++;
            return result;
        }

        private static void CopyColliders(Collider[] sourceColliders,
            GameObject target, ref int copiedComponents)
        {
            // Match by exact component type and ordinal to preserve multiple boxes on
            // one GameObject. Copy to existing imported FBX components as overrides,
            // otherwise add the missing components. This code runs only on first import.
            Dictionary<Type, int> ordinalByType = new Dictionary<Type, int>();
            foreach (Collider source in sourceColliders)
            {
                Type type = source.GetType();
                int ordinal = ordinalByType.TryGetValue(type, out int previous) ? previous : 0;
                ordinalByType[type] = ordinal + 1;
                Component[] existing = target.GetComponents(type);
                Component destination = ordinal < existing.Length
                    ? existing[ordinal] : target.AddComponent(type);
                if (destination == null)
                    throw new InvalidOperationException("Could not add " + type.Name + " on " + target.name);
                // Preserves BoxCollider size/center, MeshCollider mesh/material, triggers,
                // enabled state, and other serialized properties without copying renderers.
                EditorUtility.CopySerialized(source, destination);
                // Imported FBX nodes are nested prefab instances. Without recording
                // their overrides, SaveAsPrefabAsset can discard changed collider fields.
                PrefabUtility.RecordPrefabInstancePropertyModifications(destination);
                copiedComponents++;
            }
        }
    }
}
#endif
