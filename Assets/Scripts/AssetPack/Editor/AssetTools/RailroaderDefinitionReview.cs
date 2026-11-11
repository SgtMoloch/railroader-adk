#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Railroader.AssetTools
{
    /// <summary>Scrollable JSON preview; confirmation does not edit the previewed data.</summary>
    internal sealed class RailroaderDefinitionReview : EditorWindow
    {
        private string json, notes;
        private Vector2 scroll;
        private bool accepted;

        internal static bool Confirm(string json, string notes)
        {
            var window = CreateInstance<RailroaderDefinitionReview>();
            window.titleContent = new GUIContent("Review Definition");
            window.accepted = false;
            window.json = json;
            window.notes = notes;
            window.minSize = new Vector2(560, 430);
            window.maxSize = new Vector2(1100, 1000);
            window.position = new Rect((Screen.currentResolution.width - 700) / 2f,
                (Screen.currentResolution.height - 640) / 2f, 700, 640);
            window.ShowModalUtility();
            return window.accepted;
        }

        private void OnGUI()
        {
            RailroaderADKToolsVersion.DrawHeader("Definition Review");
            EditorGUILayout.HelpBox("Nothing in Definitions.json will be written until you select Create. " +
                "Existing definitions are never overwritten by the starter.", MessageType.Info);
            if (!string.IsNullOrWhiteSpace(notes))
            {
                EditorGUILayout.HelpBox(notes, MessageType.Warning);
            }
            using (var view = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.ExpandHeight(true)))
            {
                scroll = view.scrollPosition;
                EditorGUILayout.TextArea(json, GUILayout.ExpandHeight(true));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel", GUILayout.Height(32)))
                {
                    accepted = false; Close();
                }
                if (GUILayout.Button("Create Definition", GUILayout.Height(32)))
                {
                    accepted = true; Close();
                }
            }
        }
    }
}
#endif
