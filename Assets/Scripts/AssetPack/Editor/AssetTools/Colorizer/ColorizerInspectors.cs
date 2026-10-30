using System;
using UnityEditor;
using UnityEngine;

namespace Railroader.AssetTools.Colorizer
{
    // Unity's regular Inspector supports Ctrl/Shift selection only if the custom
    // editor opts in to editing multiple component targets.
    [CustomEditor(typeof(CustomColorizer))]
    [CanEditMultipleObjects]
    public sealed class CustomColorizerInspector : UnityEditor.Editor
    {
        private readonly ColorizerPreview preview = new ColorizerPreview();
        private bool showPreview = true;

        private void OnDisable()
        {
            preview.Clear();
        }

        public override void OnInspectorGUI()
        {
            bool multiple = targets.Length > 1;
            serializedObject.Update();
            var materialSlot = serializedObject.FindProperty("materialSlot");

            if (multiple)
            {
                // A per-object popup would be incorrect when selected renderers
                // have different material layouts. Use Unity's mixed-value field.
                preview.Clear();
                EditorGUILayout.HelpBox(
                    $"Editing {targets.Length} Colorizer Markers. Changing Style Class or " +
                    "Control Label applies to all selected markers. Mixed values are supported. " +
                    "Use the batch editor for initial marker attachment.", MessageType.Info);
                EditorGUILayout.PropertyField(materialSlot, new GUIContent("Material Slot"));
            }
            else
            {
                var marker = (CustomColorizer)target;
                var renderer = marker.GetComponent<Renderer>();
                if (renderer == null)
                {
                    EditorGUILayout.HelpBox("Attach Custom Colorizer to the mesh object that has a Renderer.", MessageType.Error);
                }
                else
                {
                    var slots = renderer.sharedMaterials;
                    var labels = new string[slots.Length + 1];
                    labels[0] = "Choose material slot";
                    for (int i = 0; i < slots.Length; i++)
                    {
                        labels[i + 1] = i + ": " + (slots[i] == null ? "(empty)" : slots[i].name);
                    }
                    int current = materialSlot.intValue >= 0 && materialSlot.intValue < slots.Length
                        ? materialSlot.intValue + 1 : 0;
                    int selected = EditorGUILayout.Popup("Material to color", current, labels);
                    if (selected != current)
                    {
                        materialSlot.intValue = selected - 1;
                    }
                }
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("styleClass"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultColor"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("colorGamma"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("showInCustomize"));
            // Keep the label visible for multi-edit even if targets disagree on
            // showInCustomize. Otherwise the user can't fix mixed labels in one go.
            var show = serializedObject.FindProperty("showInCustomize");
            if (multiple || show.hasMultipleDifferentValues || show.boolValue)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("controlLabel"));
            }

            bool changed = serializedObject.ApplyModifiedProperties();
            if (!multiple)
            {
                bool nextPreview = EditorGUILayout.Toggle("Preview while selected", showPreview);
                if (nextPreview != showPreview)
                {
                    changed = true;
                }
                showPreview = nextPreview;
                if (showPreview)
                {
                    EditorGUILayout.HelpBox(preview.Apply((CustomColorizer)target), MessageType.Info);
                }
                else
                {
                    preview.Clear();
                }
            }
            if (changed)
            {
                SceneView.RepaintAll();
            }
            EditorGUILayout.HelpBox(
                "Give several markers the same Style Class and Control Label to share one in-game color. " +
                "For a default-first workflow, use 'Apply Common Group to ALL Parts' in the Colorizer Baker, " +
                "then open the authoring prefab and Ctrl-select the exceptions to change them together. " +
                "Save the authoring prefab before baking.", MessageType.Info);
        }
    }
}
