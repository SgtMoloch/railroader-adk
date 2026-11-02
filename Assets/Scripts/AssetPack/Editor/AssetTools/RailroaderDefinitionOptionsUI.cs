#if UNITY_EDITOR
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Railroader.AssetTools
{
    internal static class RailroaderDefinitionOptionsUI
    {
        /// <summary>
        /// A CarStyle's Decal class controls road number/reporting mark decal appearance.
        /// It does not change material textures or add a gameplay component.
        /// </summary>
        internal static void DrawDecalFinish(RailroaderDefinitionCopier.RoadNumberOverrides numbers)
        {
            numbers.OverrideDecalAppearance = EditorGUILayout.ToggleLeft(
                "Override number / reporting mark decal finish", numbers.OverrideDecalAppearance);
            if (!numbers.OverrideDecalAppearance)
            {
                return;
            }

            numbers.DecalColorHex = EditorGUILayout.TextField("Decal color (#RRGGBB)", numbers.DecalColorHex);
            if (Regex.IsMatch(numbers.DecalColorHex ?? "", @"^#[0-9a-fA-F]{6}$") &&
                ColorUtility.TryParseHtmlString(numbers.DecalColorHex, out Color color))
            {
                Color newColor = EditorGUILayout.ColorField("Color picker", color);
                if (newColor != color)
                {
                    numbers.DecalColorHex = "#" + ColorUtility.ToHtmlStringRGB(newColor);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Enter a six-digit hex color, such as #222222.", MessageType.Warning);
            }

            numbers.DecalShininess = EditorGUILayout.Slider("Shininess", numbers.DecalShininess, 0.0f, 1.0f);
            EditorGUILayout.LabelField("Definition value", "smooth(" + numbers.DecalColorHex + ", " +
                numbers.DecalShininess.ToString("0.0###", CultureInfo.InvariantCulture) + ")");
            EditorGUILayout.HelpBox("This changes CarStyle.styles.Decal.color (all decals using the " +
                "Decal style class). Uncheck to inherit the source style's color and finish.", MessageType.None);
        }

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
