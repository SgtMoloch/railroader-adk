#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Railroader.AssetTools
{
    /// <summary>Display expected authoring mistakes as dialogs, keeping unexpected failures in Console.</summary>
    internal static class RailroaderEditorFeedback
    {
        internal static void ShowError(string action, Exception exception)
        {
            bool expected = exception is InvalidOperationException ||
                exception is ArgumentException ||
                exception is FileNotFoundException ||
                exception is DirectoryNotFoundException;

            string message = exception.Message;
            if (!expected)
            {
                Debug.LogException(exception);
                message += "\n\nThis was an unexpected error. See the Unity Console for details.";
            }

            EditorUtility.DisplayDialog(action + " — " + (expected ? "Action not completed" : "Error"),
                message, "OK");
        }
    }
}
#endif
