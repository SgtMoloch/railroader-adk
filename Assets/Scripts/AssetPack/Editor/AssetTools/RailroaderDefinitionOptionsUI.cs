#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Railroader.AssetTools
{
    internal static class RailroaderDefinitionOptionsUI
    {
        internal static void Draw(RailroaderStarterDefinitionKind kind, RailroaderDefinitionOptions o,
            string prefabName, string prefabPath = null)
        {
            if (kind == RailroaderStarterDefinitionKind.Truck)
            {
                EditorGUILayout.LabelField("Truck defaults", EditorStyles.boldLabel);
                o.Axles = EditorGUILayout.IntField("Number of axles", o.Axles);
                o.WheelDiameterMeters = EditorGUILayout.FloatField("Wheel diameter (m)", o.WheelDiameterMeters);
                o.TruckWheelbaseMeters = EditorGUILayout.FloatField("Wheelbase (m)", o.TruckWheelbaseMeters);
                EditorGUILayout.LabelField("Wheel transform paths (one per line; / between hierarchy levels)");
                o.WheelPaths = EditorGUILayout.TextArea(o.WheelPaths, GUILayout.MinHeight(48));
                if (GUILayout.Button("Detect Wheel Paths from Prefab") && !string.IsNullOrEmpty(prefabPath))
                {
                    o.WheelPaths = string.Join("\n", RailroaderPrefabSetup.WheelPaths(prefabPath));
                }
                EditorGUILayout.HelpBox("Leave paths blank to auto-detect names beginning with Wheel. " +
                    "Check the JSON preview; if detection finds none, fill these in manually.", MessageType.None);
                o.BrakeAnimation = EditorGUILayout.ToggleLeft("Reference brake animation", o.BrakeAnimation);
                if (o.BrakeAnimation)
                {
                    o.BrakeClip = EditorGUILayout.TextField("Brake clip (blank = FBX name)", o.BrakeClip);
                }
                o.TruckColor = EditorGUILayout.ToggleLeft("Add Truck RendererGroup + MaterialMap", o.TruckColor);
            }
            else if (kind == RailroaderStarterDefinitionKind.DieselLocomotive)
            {
                EditorGUILayout.LabelField("Diesel defaults", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Car type", "LD");
                EditorGUILayout.LabelField("Fuel load", "rr/diesel-fuel (Gallons)");
                o.DieselFuelGallons = EditorGUILayout.IntField("Fuel capacity (gal)", o.DieselFuelGallons);
            }
            if (kind == RailroaderStarterDefinitionKind.Car ||
                kind == RailroaderStarterDefinitionKind.DieselLocomotive)
            {
                EditorGUILayout.Space();
                o.BaseRoadNumber = EditorGUILayout.TextField("Base road number (optional)", o.BaseRoadNumber);
                o.ClickableCollider = EditorGUILayout.ToggleLeft(
                    "Add model-sized BoxCollider + Clickable layer", o.ClickableCollider);
                if (!RailroaderPrefabSetup.HasClickableLayer)
                {
                    EditorGUILayout.HelpBox("Clickable layer not found. Collider setup will be skipped.", MessageType.Warning);
                }
            }
            if (kind == RailroaderStarterDefinitionKind.SteamLocomotive)
            {
                EditorGUILayout.HelpBox("Steam-specific wheelsets, tenders and boiler values are deferred.", MessageType.Info);
            }
        }
    }
}
#endif
