#if UNITY_EDITOR
using UnityEditor;

namespace Railroader.AssetTools
{
    /// <summary>One shared version label for all Railroader ADK Tools editor windows.</summary>
    internal static class RailroaderADKToolsVersion
    {
        internal const string Current = "v3.0 Beta 1.5.9";

        internal static void DrawHeader(string windowName)
        {
            EditorGUILayout.LabelField("Railroader " + windowName + " " + Current,
                EditorStyles.boldLabel);
        }
    }
}
#endif
