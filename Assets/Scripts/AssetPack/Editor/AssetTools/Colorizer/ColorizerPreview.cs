using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Railroader.AssetTools.Colorizer
{
    // A preview belongs to an Inspector, never to the material asset or exported prefab.
    public sealed class ColorizerPreview : IDisposable
    {
        private static readonly List<ColorizerPreview> Active = new List<ColorizerPreview>();
        private Renderer renderer;
        private Material material;
        private int slot;
        private MaterialPropertyBlock original;
        private MaterialPropertyBlock working;

        static ColorizerPreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ClearAll;
            EditorApplication.quitting += ClearAll;
            EditorApplication.playModeStateChanged += _ => ClearAll();
            Undo.undoRedoPerformed += ClearAll;
            Selection.selectionChanged += ClearAll;
        }

        public string Apply(CustomColorizer marker)
        {
            if (!marker.enabled || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Clear();
                return "Preview is available in Edit Mode while this component is enabled.";
            }
            if (EditorUtility.IsPersistent(marker))
            {
                Clear();
                return "Open the prefab in Prefab Mode, or select its instance in the Hierarchy, to see the preview.";
            }
            if (!Selection.gameObjects.Contains(marker.gameObject))
            {
                Clear();
                return "Select this mesh in the Hierarchy to preview its color.";
            }
            var target = marker.GetComponent<Renderer>();
            var materials = target == null ? null : target.sharedMaterials;
            if (materials == null || marker.materialSlot < 0 || marker.materialSlot >= materials.Length || materials[marker.materialSlot] == null)
            {
                Clear();
                return "Choose a populated material slot on this mesh's Renderer.";
            }
            var source = materials[marker.materialSlot];
            if (!source.HasProperty("_BaseColor") && !source.HasProperty("_Color"))
            {
                Clear();
                return "This material's shader has no _BaseColor or _Color. Assign an ADK shared car shader first.";
            }
            if (float.IsNaN(marker.colorGamma) || float.IsInfinity(marker.colorGamma) || marker.colorGamma <= 0)
            {
                Clear();
                return "Color Gamma must be finite and greater than zero.";
            }
            if (renderer != target || slot != marker.materialSlot || material != source)
            {
                Clear();
            }
            // A locked second Inspector must not snapshot a first Inspector's preview.
            if (Active.Any(p => p != this && p.renderer == target && p.slot == marker.materialSlot))
            {
                return "This slot is already being previewed by another Inspector.";
            }
            if (renderer == null)
            {
                renderer = target;
                material = source;
                slot = marker.materialSlot;
                original = new MaterialPropertyBlock();
                working = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original, slot);
                Active.Add(this);
            }
            renderer.GetPropertyBlock(working, slot);
            if (original.isEmpty)
                renderer.GetPropertyBlock(working); // A slot override replaces the renderer-wide block.
            working.SetColor(source.HasProperty("_BaseColor") ? "_BaseColor" : "_Color",
                Tint(marker.defaultColor, marker.colorGamma));
            renderer.SetPropertyBlock(working, slot);
            return "Previewing this slot while selected. Deselecting restores the source appearance.";
        }

        public static Color Tint(Color color, float gamma)
        {
            float exponent = 1f / gamma;
            return new Color(Mathf.Pow(Mathf.Clamp01(color.r), exponent),
                Mathf.Pow(Mathf.Clamp01(color.g), exponent), Mathf.Pow(Mathf.Clamp01(color.b), exponent), 1f);
        }

        public void Clear()
        {
            if (renderer != null && original != null && slot >= 0 && slot < renderer.sharedMaterials.Length)
            {
                renderer.SetPropertyBlock(original.isEmpty ? null : original, slot);
            }
            bool wasActive = Active.Remove(this);
            renderer = null;
            material = null;
            original = null;
            working = null;
            if (wasActive)
            {
                SceneView.RepaintAll();
            }
        }

        public static void ClearAll()
        {
            foreach (var preview in Active.ToArray())
            {
                preview.Clear();
            }
        }

        public void Dispose()
        {
            Clear();
        }
    }
}
