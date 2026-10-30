using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Railroader.AssetTools.Colorizer
{
    [Serializable]
    public sealed class ColorZone
    {
        public string Key;
        public string StyleClass;
        public string MaterialName;
        public string ColorHex;
        public string Label;
        public float Gamma = 1f;
        public bool Customize = true;
    }

    /// <summary>Merges authoring output into a Railroader 0.6 Definitions.json.</summary>
    public static class ColorizerDefinitionWriter
    {
        public const string ComponentPrefix = "assettools-colorizer-";
        private const string PaintName = "Custom Paint";
        private static readonly Regex SafeToken = new Regex("^[a-z][a-z0-9_-]*$", RegexOptions.CultureInvariant);
        private static readonly Regex Identifier = new Regex("^[a-z0-9][a-z0-9.-]*/[a-z0-9][a-z0-9.-]*$", RegexOptions.CultureInvariant);
        private static readonly Regex Hex = new Regex("^#?[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant);

        public static string Merge(string definitionsJson, string carIdentifier, string prefabName, IReadOnlyList<ColorZone> zones, bool generateSelectableStyle = true)
        {
            if (string.IsNullOrWhiteSpace(carIdentifier) || !Identifier.IsMatch(carIdentifier) || carIdentifier.Contains(".."))
            {
                throw new ArgumentException("Car identifier must be a lowercase domain/id, for example mydomain/gp60.", nameof(carIdentifier));
            }
            RequireToken(prefabName, "Prefab name");
            if (zones == null || zones.Count == 0)
            {
                throw new ArgumentException("Select at least one paint zone.", nameof(zones));
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> materials = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, ColorZone> grouped = new Dictionary<string, ColorZone>(StringComparer.Ordinal);
            foreach (var zone in zones)
            {
                if (zone == null)
                {
                    throw new ArgumentException("A paint zone is null.", nameof(zones));
                }
                RequireToken(zone.Key, "Zone key");
                RequireToken(zone.StyleClass, "Style class");
                if (!keys.Add(zone.Key))
                {
                    throw new ArgumentException("Duplicate zone key: " + zone.Key);
                }
                if (string.IsNullOrWhiteSpace(zone.MaterialName) || zone.MaterialName != zone.MaterialName.Trim())
                {
                    throw new ArgumentException("Material names must be nonempty and have no leading or trailing spaces.");
                }
                if (!materials.Add(zone.MaterialName))
                {
                    throw new ArgumentException("More than one zone targets material: " + zone.MaterialName);
                }
                if (zone.ColorHex == null || !Hex.IsMatch(zone.ColorHex))
                {
                    throw new ArgumentException("Color must contain six hexadecimal digits: " + zone.Key);
                }
                if (float.IsNaN(zone.Gamma) || float.IsInfinity(zone.Gamma) || zone.Gamma <= 0)
                {
                    throw new ArgumentException("Color gamma must be finite and greater than zero: " + zone.Key);
                }
                if (zone.Customize && string.IsNullOrWhiteSpace(zone.Label))
                {
                    throw new ArgumentException("A player-editable zone requires a label: " + zone.Key);
                }
                ColorZone earlier;
                if (grouped.TryGetValue(zone.StyleClass, out earlier))
                {
                    if (Color(earlier) != Color(zone) || earlier.Customize != zone.Customize ||
                        (zone.Customize && !string.Equals(earlier.Label, zone.Label, StringComparison.Ordinal)))
                        throw new ArgumentException("Zones sharing style class '" + zone.StyleClass + "' must use the same color and customization settings. Gamma may differ per material group.");
                }
                else
                {
                    grouped.Add(zone.StyleClass, zone);
                }
            }

            JToken root;
            try
            {
                root = JToken.Parse(definitionsJson, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            }
            catch (Exception ex) when (ex is JsonException || ex is ArgumentException)
            {
                throw new ArgumentException("Definitions.json must be valid JSON without duplicate property names.", nameof(definitionsJson), ex);
            }

            // The current ADK writes a top-level array. Accept the earlier
            // Game-compatible { "objects": [...] } container, plus earlier array
            // files for editor-side compatibility (do not change outer shape here).
            var objects = root as JArray ?? (root as JObject)?["objects"] as JArray;
            if (objects == null)
            {
                throw new ArgumentException("Definitions.json must be an array (or legacy object with an objects array).");
            }
            var matches = objects.OfType<JObject>().Where(o => (string)o["identifier"] == carIdentifier).ToList();
            if (matches.Count != 1)
            {
                throw new ArgumentException("Expected exactly one car definition with identifier '" + carIdentifier + "'.");
            }
            var car = matches[0]["definition"] as JObject;
            if (car == null || !IsCarKind((string)car["kind"]))
            {
                throw new ArgumentException("The selected identifier must describe a Car, SteamLocomotive, DieselLocomotive, or ArticulatedSteamLocomotive.");
            }

            var components = ComponentCollection.From(car["components"]);
            var styles = OptionalObject(car, "styles");
            HashSet<string> previouslyManagedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in components.All())
            {
                string id = existing.Key;
                JObject component = existing.Value;
                if (id.StartsWith(ComponentPrefix, StringComparison.Ordinal))
                {
                    if (component == null || (string)component["kind"] != "RendererGroup")
                    {
                        throw new ArgumentException("Reserved component id is occupied by a different component: " + id);
                    }
                    foreach (var oldClass in SplitStyleClasses((string)component["styleClass"]))
                    {
                        previouslyManagedClasses.Add(oldClass);
                    }
                    continue;
                }
                if (component == null || !string.Equals((string)component["kind"], "RendererGroup", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var materialReference = component["material"] as JObject;
                var materialName = materialReference == null ? null : (string)materialReference["materialName"];
                var styleClasses = SplitStyleClasses((string)component["styleClass"]);
                if ((materialName != null && materials.Contains(materialName)) ||
                    grouped.Keys.Any(c => styleClasses.Any(s => string.Equals(c, s, StringComparison.OrdinalIgnoreCase))))
                    throw new ArgumentException("Existing RendererGroup '" + id + "' already claims a requested material or style class. Resolve that group before baking.");
            }

            foreach (var pair in grouped)
            {
                string selector = "Renderer." + pair.Key;
                // Selectors are case-insensitive in Railroader. Keep the existing spelling.
                var matchingRules = styles.Properties().Where(p => string.Equals(p.Name, selector, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matchingRules.Count > 1)
                {
                    throw new ArgumentException("Duplicate equivalent style selectors: " + selector);
                }
                var existingRule = matchingRules.Count == 0 ? null : matchingRules[0].Value as JObject;
                if (matchingRules.Count != 0 && existingRule == null)
                {
                    throw new ArgumentException("Style rule must be an object: " + selector);
                }
                if (!previouslyManagedClasses.Contains(pair.Key) && existingRule != null)
                {
                    CheckUnmanagedRule(existingRule, pair.Value, selector);
                }
            }

            string paintIdentifier = carIdentifier + "-custom-paint";
            var paintMatches = objects.OfType<JObject>().Where(o => (string)o["identifier"] == paintIdentifier).ToList();
            if (paintMatches.Count > 1)
            {
                throw new ArgumentException("Duplicate generated paint definition: " + paintIdentifier);
            }
            JObject paintObject = paintMatches.Count == 0 ? null : paintMatches[0];
            JObject paint = paintObject == null ? null : paintObject["definition"] as JObject;
            if (paintObject != null)
            {
                var targets = paint == null ? null : paint["carIdentifiers"] as JArray;
                if (paint == null || (string)paint["kind"] != "CarStyle" || (string)paint["name"] != PaintName ||
                    targets == null || targets.Count != 1 || (string)targets[0] != carIdentifier)
                    throw new ArgumentException("Generated paint identifier is already used by another definition: " + paintIdentifier);
            }

            // Mutation is local to this parsed document; callers only receive a result on success.
            components.RemoveManaged(ComponentPrefix);
            foreach (var zone in zones)
                components.Add(ComponentPrefix + zone.Key, new JObject
                {
                    ["kind"] = "RendererGroup",
                    ["styleClass"] = zone.StyleClass,
                    ["material"] = new JObject { ["materialName"] = zone.MaterialName },
                    ["colorGamma"] = zone.Gamma
                });
            car["components"] = components.Data;
            car["modelIdentifier"] = "./" + prefabName;
            // The old baker left retired control selectors behind forever.
            // Remove only selectors previously owned by generated RendererGroups.
            RemoveRetiredRules(styles, previouslyManagedClasses, grouped.Keys);
            MergeRules(styles, grouped, true);
            car["styles"] = styles;

            if (!generateSelectableStyle)
            {
                // Only remove our own generated style, never unrelated paint definitions.
                if (paintObject != null)
                {
                    paintObject.Remove();
                }
                return root.ToString(Formatting.Indented) + Environment.NewLine;
            }
            if (paintObject == null)
            {
                paint = new JObject
                {
                    ["kind"] = "CarStyle",
                    ["name"] = PaintName,
                    ["carIdentifiers"] = new JArray(carIdentifier)
                };
                paintObject = new JObject
                {
                    ["identifier"] = paintIdentifier,
                    ["metadata"] = new JObject { ["name"] = PaintName },
                    ["definition"] = paint
                };
                objects.Add(paintObject);
            }
            var paintStyles = OptionalObject(paint, "styles");
            RemoveRetiredRules(paintStyles, previouslyManagedClasses, grouped.Keys);
            // Keep player customization labels on the selected CarStyle too.
            // The car definition and the selectable style should agree.
            MergeRules(paintStyles, grouped, true);
            paint["styles"] = paintStyles;
            return root.ToString(Formatting.Indented) + Environment.NewLine;
        }

        /// <summary>
        /// Supports the documented component array and older component dictionaries.
        /// Existing JSON shape and unrelated components are preserved unchanged.
        /// </summary>
        private sealed class ComponentCollection
        {
            private readonly JObject map;
            private readonly JArray list;
            internal JToken Data => (JToken)map ?? list;

            private ComponentCollection(JObject asMap, JArray asList)
            {
                map = asMap;
                list = asList;
            }
            internal static ComponentCollection From(JToken token)
            {
                if (token == null || token.Type == JTokenType.Null)
                {
                    return new ComponentCollection(null, new JArray());
                }
                if (token is JObject o)
                {
                    return new ComponentCollection(o, null);
                }
                if (token is JArray a)
                {
                    return new ComponentCollection(null, a);
                }
                throw new ArgumentException("components must be an array or an object map.");
            }
            internal IEnumerable<KeyValuePair<string, JObject>> All()
            {
                if (map != null)
                {
                    foreach (var entry in map.Properties())
                    {
                        yield return new KeyValuePair<string, JObject>(entry.Name, entry.Value as JObject);
                    }
                }
                else
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var component = list[i] as JObject;
                        string name = component == null ? "" : ((string)component["id"] ??
                            (string)component["name"] ?? "");
                        yield return new KeyValuePair<string, JObject>(name, component);
                    }
                }
            }
            internal void RemoveManaged(string prefix)
            {
                if (map != null)
                {
                    foreach (var entry in map.Properties()
                        .Where(p => p.Name.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                        entry.Remove();
                }
                else
                {
                    foreach (var component in list.OfType<JObject>()
                        .Where(o => ((string)o["id"] ?? (string)o["name"] ?? "")
                        .StartsWith(prefix, StringComparison.Ordinal)).ToList())
                        component.Remove();
                }
            }
            internal void Add(string id, JObject component)
            {
                if (map != null)
                {
                    map.Add(id, component);
                }
                else
                {
                    // The documented array form stores component identity as name.
                    component["name"] = id;
                    list.Add(component);
                }
            }
        }

        private static void RemoveRetiredRules(JObject styles, IEnumerable<string> previousClasses,
            IEnumerable<string> currentClasses)
        {
            HashSet<string> current = new HashSet<string>(currentClasses, StringComparer.OrdinalIgnoreCase);
            foreach (string oldClass in previousClasses)
            {
                if (current.Contains(oldClass))
                {
                    continue;
                }
                string selector = "Renderer." + oldClass;
                foreach (var property in styles.Properties()
                    .Where(p => string.Equals(p.Name, selector, StringComparison.OrdinalIgnoreCase)).ToList())
                    property.Remove();
            }
        }

        private static void MergeRules(JObject styles, Dictionary<string, ColorZone> grouped, bool includeCustomization)
        {
            foreach (var pair in grouped)
            {
                string selector = "Renderer." + pair.Key;
                var matches = styles.Properties().Where(p => string.Equals(p.Name, selector, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count > 1)
                {
                    throw new ArgumentException("Duplicate equivalent style selectors: " + selector);
                }
                var property = matches.Count == 0 ? null : matches[0];
                var rule = property == null ? new JObject() : property.Value as JObject;
                if (rule == null)
                {
                    throw new ArgumentException("Style rule must be an object: " + selector);
                }
                rule["color"] = Color(pair.Value);
                if (includeCustomization)
                {
                    if (pair.Value.Customize)
                    {
                        rule["customize-color"] = pair.Value.Label;
                    }
                    else
                    {
                        rule.Remove("customize-color");
                    }
                }
                if (property == null)
                {
                    styles.Add(selector, rule);
                }
            }
        }

        private static void CheckUnmanagedRule(JObject rule, ColorZone zone, string selector)
        {
            var color = rule["color"];
            if (color != null && (color.Type != JTokenType.String || !Hex.IsMatch((string)color) ||
                ((string)color).TrimStart('#').ToUpperInvariant() != Color(zone)))
                throw new ArgumentException("Existing color conflicts with requested paint: " + selector);
            var label = rule["customize-color"];
            if (label != null && (!zone.Customize || label.Type != JTokenType.String || (string)label != zone.Label))
            {
                throw new ArgumentException("Existing customization label conflicts with requested paint: " + selector);
            }
        }

        private static JObject OptionalObject(JObject parent, string key)
        {
            var token = parent[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                return new JObject();
            }
            var value = token as JObject;
            if (value == null)
            {
                throw new ArgumentException(key + " must be a Railroader 0.6 object map. Legacy component arrays require migration first.");
            }
            return value;
        }

        private static string Color(ColorZone zone)
        {
            return zone.ColorHex.TrimStart('#').ToUpperInvariant();
        }

        private static IEnumerable<string> SplitStyleClasses(string value)
        {
            return Regex.Split(value ?? "", "[\\s,]+").Where(s => s.Length != 0);
        }

        private static void RequireToken(string value, string label)
        {
            if (string.IsNullOrEmpty(value) || !SafeToken.IsMatch(value))
            {
                throw new ArgumentException(label + " must start with a lowercase letter and contain only lowercase letters, numbers, underscores, and hyphens.");
            }
        }

        private static bool IsCarKind(string kind)
        {
            return kind == "Car" || kind == "SteamLocomotive" || kind == "DieselLocomotive" || kind == "ArticulatedSteamLocomotive";
        }
    }
}
