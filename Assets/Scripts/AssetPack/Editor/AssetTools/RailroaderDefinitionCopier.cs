#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Railroader.AssetTools
{
    /// <summary>
    /// Copies a fully configured definition onto another root prefab definition without
    /// copying the destination's auto-generated identity or the Colorizer's baked data.
    /// Optionally copies related CarStyle and ReportingMark entries (2.9.1).
    /// </summary>
    internal static class RailroaderDefinitionCopier
    {
        private const string GeneratedComponentPrefix = "assettools-colorizer-";

        internal sealed class Choice
        {
            internal string Id;
            internal string Kind;
            internal string Prefab;
            internal string Label => Id + " (" + Kind + ")";
        }

        private static string DefinitionsPath(string pack) => pack + "/Definitions.json";

        private static JArray Read(string pack, out JToken root)
        {
            if (!RailroaderAssetPackTools.ValidPack(pack))
            {
                throw new InvalidOperationException("Select an existing ADK asset pack first.");
            }
            string path = RailroaderAssetPackTools.Absolute(DefinitionsPath(pack));
            if (!File.Exists(path))
            {
                throw new InvalidOperationException("This pack has no Definitions.json. Create a first definition before copying.");
            }
            root = JToken.Parse(File.ReadAllText(path), new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
            if (!(root is JObject obj) || !(obj["objects"] is JArray entries))
            {
                throw new InvalidOperationException("Definitions.json must use the current { \"objects\": [...] } container format.");
            }
            if (entries.Any(x => !(x is JObject)))
            {
                throw new InvalidOperationException("Definitions.json contains an entry that is not an object.");
            }
            return entries;
        }

        internal static List<Choice> Sources(string pack, string requireKind = null)
        {
            List<Choice> result = new List<Choice>();
            if (!RailroaderAssetPackTools.ValidPack(pack) ||
                !File.Exists(RailroaderAssetPackTools.Absolute(DefinitionsPath(pack)))) return result;
            JArray objects = Read(pack, out _);
            foreach (var item in objects.OfType<JObject>())
            {
                var definition = item["definition"] as JObject;
                string kind = (string)definition?["kind"];
                string id = (string)item["identifier"];
                string modelIdentifier = (string)definition?["modelIdentifier"];
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(kind) ||
                    string.Equals(kind, "CarStyle", StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(requireKind) && !string.Equals(kind, requireKind, StringComparison.OrdinalIgnoreCase)))
                    continue;
                // Only definitions for actual root prefabs in THIS asset pack.
                if (string.IsNullOrEmpty(modelIdentifier) || !modelIdentifier.StartsWith("./", StringComparison.Ordinal) ||
                    !RailroaderAssetPackTools.ValidAssetName(modelIdentifier.Substring(2))) continue;
                string prefab = modelIdentifier.Substring(2);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(pack + "/" + prefab + ".prefab") == null)
                {
                    continue;
                }
                result.Add(new Choice { Id = id, Kind = kind, Prefab = prefab });
            }
            if (result.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            {
                throw new InvalidOperationException("Definitions.json contains duplicate definition identifiers.");
            }
            return result; // Preserve source file order: first definition is the default template.
        }

        internal static bool Exists(string pack, string identifier)
        {
            if (!File.Exists(RailroaderAssetPackTools.Absolute(DefinitionsPath(pack))))
            {
                return false;
            }
            JArray entries = Read(pack, out _);
            return entries.OfType<JObject>().Any(o =>
                string.Equals((string)o["identifier"], identifier, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Pure model definition transform: preserve destination kind, modelIdentifier,
        /// identifier, display name and generated Colorizer components/styles.
        /// Separate CarStyle and ReportingMark entries are handled at container level.
        /// </summary>
        internal static JObject CopyDefinition(JObject source, JObject destination)
        {
            if (source == null || destination == null)
            {
                throw new ArgumentNullException(source == null ? "source" : "destination");
            }
            JObject src = source["definition"] as JObject;
            JObject dst = destination["definition"] as JObject;
            if (src == null || dst == null)
            {
                throw new InvalidOperationException("Both entries must have a definition object.");
            }
            string srcKind = (string)src["kind"];
            string dstKind = (string)dst["kind"];
            if (string.IsNullOrWhiteSpace(srcKind) || !string.Equals(srcKind, dstKind, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Definition kinds differ: " + srcKind + " vs " + dstKind +
                    ". Copying across types could produce invalid car/truck definitions.");
            if (src["modelIdentifier"]?.Type != JTokenType.String || dst["modelIdentifier"]?.Type != JTokenType.String)
            {
                throw new InvalidOperationException("Both definitions must have a modelIdentifier string.");
            }

            JObject result = (JObject)destination.DeepClone();
            JObject copied = (JObject)src.DeepClone();
            copied["kind"] = dst["kind"].DeepClone();
            copied["modelIdentifier"] = dst["modelIdentifier"].DeepClone();
            CopyComponentsExcludingGenerated(src, dst, copied);
            CopyStylesExcludingGenerated(src, dst, copied);
            result["definition"] = copied;

            // Copy other source metadata (e.g. artist tags), but not the destination's
            // generated display name. Leave other destination top-level keys intact.
            var srcMetadata = source["metadata"] as JObject;
            var dstMetadata = destination["metadata"] as JObject;
            if (srcMetadata != null)
            {
                JObject metadata = (JObject)srcMetadata.DeepClone();
                if (dstMetadata?["name"] != null)
                {
                    metadata["name"] = dstMetadata["name"].DeepClone();
                }
                else
                {
                    metadata.Remove("name");
                }
                // Livery descriptions often contain the railroad name (e.g. "ATSF").
                // Never replace a destination label with the source railroad's label.
                if (dstMetadata?.Property("description") != null)
                {
                    metadata["description"] = dstMetadata["description"].DeepClone();
                }
                else
                {
                    metadata.Remove("description");
                }
                result["metadata"] = metadata;
            }
            return result;
        }

        private static bool Generated(string name) =>
            !string.IsNullOrEmpty(name) && name.StartsWith(GeneratedComponentPrefix, StringComparison.Ordinal);

        private static IEnumerable<KeyValuePair<string, JObject>> Components(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                yield break;
            }
            if (token is JObject map)
            {
                foreach (var p in map.Properties())
                {
                    if (!(p.Value is JObject o))
                    {
                        throw new InvalidOperationException("Component '" + p.Name + "' must be an object.");
                    }
                    yield return new KeyValuePair<string, JObject>(p.Name, o);
                }
            }
            else if (token is JArray array)
            {
                foreach (JToken item in array)
                {
                    if (!(item is JObject o))
                    {
                        throw new InvalidOperationException("A component entry must be an object.");
                    }
                    string name = (string)o["name"] ?? (string)o["id"];
                    yield return new KeyValuePair<string, JObject>(name, o);
                }
            }
            else
            {
                throw new InvalidOperationException("components must be an object or an array.");
            }
        }

        private static HashSet<string> GeneratedClasses(JObject definition)
        {
            HashSet<string> classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Components(definition["components"]))
            {
                if (!Generated(entry.Key) ||
                    !string.Equals((string)entry.Value["kind"], "RendererGroup", StringComparison.OrdinalIgnoreCase)) continue;
                string styleClass = (string)entry.Value["styleClass"] ?? "";
                foreach (string s in System.Text.RegularExpressions.Regex.Split(styleClass, "[\\s,]+"))
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        classes.Add("Renderer." + s);
                    }
            }
            return classes;
        }

        private static void CopyComponentsExcludingGenerated(JObject src, JObject dst, JObject copied)
        {
            JToken srcToken = src["components"];
            JToken dstToken = dst["components"];
            var retained = Components(dstToken).Where(x => Generated(x.Key)).ToList();
            if (srcToken == null || srcToken.Type == JTokenType.Null)
            {
                // A missing source components collection means: copy NO ordinary
                // components, but retain the destination's own generated groups.
                if (retained.Count == 0)
                {
                    copied.Remove("components");
                }
                else if (dstToken is JObject)
                {
                    JObject generatedOnly = new JObject();
                    foreach (var entry in retained)
                    {
                        generatedOnly.Add(entry.Key, entry.Value.DeepClone());
                    }
                    copied["components"] = generatedOnly;
                }
                else
                {
                    JArray generatedOnly = new JArray();
                    foreach (var entry in retained)
                    {
                        generatedOnly.Add(entry.Value.DeepClone());
                    }
                    copied["components"] = generatedOnly;
                }
                return;
            }
            bool srcMap = srcToken is JObject;
            if (!(srcMap || srcToken is JArray))
            {
                throw new InvalidOperationException("Invalid source components type.");
            }
            if (retained.Count != 0 && ((dstToken is JObject) != srcMap))
                throw new InvalidOperationException("Source and destination have different component formats. " +
                    "Resolve those before copying Colorizer-managed entries.");
            if (srcMap)
            {
                JObject clean = new JObject();
                foreach (var item in Components(srcToken).Where(x => !Generated(x.Key)))
                {
                    clean.Add(item.Key, item.Value.DeepClone());
                }
                foreach (var item in retained)
                {
                    if (clean.Property(item.Key) != null)
                    {
                        throw new InvalidOperationException("Generated component name conflicts with copied component: " + item.Key);
                    }
                    clean.Add(item.Key, item.Value.DeepClone());
                }
                copied["components"] = clean;
            }
            else
            {
                JArray clean = new JArray();
                foreach (var item in Components(srcToken).Where(x => !Generated(x.Key)))
                {
                    clean.Add(item.Value.DeepClone());
                }
                foreach (var item in retained)
                {
                    clean.Add(item.Value.DeepClone());
                }
                copied["components"] = clean;
            }
        }

        private static void CopyStylesExcludingGenerated(JObject src, JObject dst, JObject copied)
        {
            var srcManaged = GeneratedClasses(src);
            var dstManaged = GeneratedClasses(dst);
            if (src["styles"] != null && !(src["styles"] is JObject))
            {
                throw new InvalidOperationException("Source styles must be an object.");
            }
            if (dst["styles"] != null && !(dst["styles"] is JObject))
            {
                throw new InvalidOperationException("Destination styles must be an object.");
            }
            JObject result = new JObject();
            if (src["styles"] is JObject srcStyles)
                foreach (var p in srcStyles.Properties())
                    if (!srcManaged.Contains(p.Name) && !dstManaged.Contains(p.Name))
                    {
                        result.Add(p.Name, p.Value.DeepClone());
                    }
            if (dst["styles"] is JObject dstStyles)
                foreach (var p in dstStyles.Properties())
                    if (dstManaged.Contains(p.Name))
                    {
                        result[p.Name] = p.Value.DeepClone();
                    }
            if (result.Count != 0 || src["styles"] != null || dstManaged.Count != 0)
            {
                copied["styles"] = result;
            }
            else
            {
                copied.Remove("styles");
            }
        }

        /// <summary>
        /// Optional companion copy for Car definitions. Reporting marks and CarStyles are
        /// separate entries in the same objects array, not children of the Car definition.
        /// A target mark is mandatory for an unstyled destination, so a new repaint isn't
        /// silently registered under the source railroad's reporting mark.
        /// </summary>
        internal sealed class RelatedCopyOptions
        {
            internal bool IncludeCarStyles;
            internal readonly Dictionary<string, string> TargetMarks =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, RoadNumberOverrides> RoadNumbers =
                new Dictionary<string, RoadNumberOverrides>(StringComparer.OrdinalIgnoreCase);
        }

        internal sealed class RoadNumberOverrides
        {
            internal string BaseRoadNumber = "";
            internal string SeriesName = "";
            internal string SeriesStart = "";
            internal string SeriesEnd = "";

            internal bool HasRange => !string.IsNullOrWhiteSpace(SeriesName) ||
                !string.IsNullOrWhiteSpace(SeriesStart) || !string.IsNullOrWhiteSpace(SeriesEnd);
        }

        private static bool KindIs(JObject entry, string kind) =>
            string.Equals((string)(entry["definition"] as JObject)?["kind"], kind,
                StringComparison.OrdinalIgnoreCase);

        private static List<JObject> LinkedStyles(JArray objects, string carId) =>
            objects.OfType<JObject>().Where(o => KindIs(o, "CarStyle") &&
                ((o["definition"] as JObject)?["carIdentifiers"] as JArray)?
                    .Any(i => string.Equals((string)i, carId, StringComparison.OrdinalIgnoreCase)) == true).ToList();

        private static bool HasLinkedStylesInArray(JArray objects, string sourceId)
        {
            return LinkedStyles(objects, sourceId).Count != 0;
        }

        internal static bool HasLinkedStyles(string pack, string sourceId)
        {
            if (!RailroaderAssetPackTools.ValidPack(pack))
            {
                return false;
            }
            if (!File.Exists(RailroaderAssetPackTools.Absolute(DefinitionsPath(pack))))
            {
                return false;
            }
            return LinkedStyles(Read(pack, out _), sourceId).Count != 0;
        }

        internal static string ExistingReportingMark(string pack, string targetId)
        {
            if (!RailroaderAssetPackTools.ValidPack(pack))
            {
                return null;
            }
            if (!File.Exists(RailroaderAssetPackTools.Absolute(DefinitionsPath(pack))))
            {
                return null;
            }
            var marks = LinkedStyles(Read(pack, out _), targetId)
                .Select(x => (string)(x["definition"] as JObject)?["reportingMark"])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return marks.Count == 1 ? marks[0] : null;
        }

        private static string ValidateReportingMark(string value, string target)
        {
            value = (value ?? "").Trim().ToUpperInvariant();
            if (value.Length < 1 || value.Length > 12 || !value.All(c =>
                (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
                throw new InvalidOperationException("Reporting mark for " + target +
                    " must be 1–12 letters/digits. Enter the correct mark before copying linked styles.");
            return value;
        }

        private static string NamespaceFor(string id)
        {
            int slash = (id ?? "").IndexOf('/');
            if (slash <= 0)
            {
                throw new InvalidOperationException("Invalid namespaced definition ID: " + id);
            }
            return id.Substring(0, slash);
        }

        private static bool IdExists(JArray entries, string id) => entries.OfType<JObject>()
            .Any(o => string.Equals((string)o["identifier"], id, StringComparison.OrdinalIgnoreCase));

        private static string UniqueStyleId(JArray entries, string targetId, string mark)
        {
            string ns = NamespaceFor(targetId);
            string suffix = new string(mark.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            string original = ns + "/style." + suffix;
            if (!IdExists(entries, original))
            {
                return original;
            }
            string tail = targetId.Substring(targetId.IndexOf('/') + 1).ToLowerInvariant();
            tail = new string(tail.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
            if (tail.Length == 0)
            {
                tail = "model";
            }
            string baseId = original + "-" + tail;
            string candidate = baseId;
            int n = 2;
            while (IdExists(entries, candidate))
            {
                candidate = baseId + "-" + n++;
            }
            return candidate;
        }

        private static void EnsureReportingMark(JArray objects, JObject sourceMarkObject,
            string targetId, string mark)
        {
            var existing = objects.OfType<JObject>().Where(o => KindIs(o, "ReportingMark") &&
                string.Equals((string)(o["definition"] as JObject)?["mark"], mark,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NamespaceFor((string)o["identifier"]), NamespaceFor(targetId),
                    StringComparison.OrdinalIgnoreCase)).ToList();
            if (existing.Count > 1)
            {
                throw new InvalidOperationException("Duplicate ReportingMark definitions for " + mark);
            }
            if (existing.Count == 1) return; // Shared marks are reused, not overwritten.
            string newId = NamespaceFor(targetId) + "/" + mark;
            if (IdExists(objects, newId))
            {
                throw new InvalidOperationException("ReportingMark ID is already used by a different entry: " + newId);
            }
            JObject created = sourceMarkObject != null ? (JObject)sourceMarkObject.DeepClone() :
                new JObject { ["metadata"] = JValue.CreateNull(), ["definition"] = new JObject {
                    ["kind"] = "ReportingMark", ["components"] = new JObject() } };
            created["identifier"] = newId;
            ((JObject)created["definition"])["mark"] = mark;
            objects.Add(created);
        }

        private static JObject FindMarkTemplate(JArray objects, string sourceMark)
        {
            return objects.OfType<JObject>().FirstOrDefault(o => KindIs(o, "ReportingMark") &&
                string.Equals((string)(o["definition"] as JObject)?["mark"], sourceMark,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static void UpdateExistingStyle(JObject source, JObject destination,
            string destinationId, string targetMark)
        {
            JObject src = (JObject)source["definition"];
            JObject dst = (JObject)destination["definition"];
            // Keep destination style identity, series and mark. Copy its presentation
            // settings from source; preserve any existing target-specific Renderer classes.
            JObject styles = src["styles"] as JObject;
            JObject old = dst["styles"] as JObject;
            if (styles != null)
            {
                JObject merged = (JObject)styles.DeepClone();
                if (old != null)
                    foreach (var p in old.Properties())
                        if (p.Name.StartsWith("Renderer.", StringComparison.OrdinalIgnoreCase))
                        {
                            merged[p.Name] = p.Value.DeepClone();
                        }
                dst["styles"] = merged;
            }
            else
            {
                dst.Remove("styles");
            }
            // Keep a target's own numbering ranges; if it has none, inherit the
            // source's range (the editable override can still replace it later).
            JObject targetSeries = dst["carSeries"] as JObject;
            if ((targetSeries == null || targetSeries.Count == 0) && src["carSeries"] != null)
            {
                dst["carSeries"] = src["carSeries"].DeepClone();
            }
            if (src["components"] != null && dst["components"] == null)
            {
                dst["components"] = src["components"].DeepClone();
            }
            dst["reportingMark"] = targetMark;
            dst["carIdentifiers"] = new JArray(destinationId);
        }

        private static void CopyLinkedCarStyles(JArray objects, string sourceId,
            string destinationId, string requestedMark)
        {
            List<JObject> sourceStyles = LinkedStyles(objects, sourceId);
            if (sourceStyles.Count == 0)
            {
                return;
            }
            var sourceMarks = sourceStyles.Select(s => (string)(s["definition"] as JObject)?["reportingMark"])
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (sourceMarks.Count != 1)
                throw new InvalidOperationException(sourceId + " has multiple or missing source reporting marks. " +
                    "Map these styles individually in the definition editor instead of bulk copying.");
            List<JObject> targetStyles = LinkedStyles(objects, destinationId);
            var oldMarks = targetStyles.Select(s => (string)(s["definition"] as JObject)?["reportingMark"])
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (oldMarks.Count > 1)
                throw new InvalidOperationException(destinationId + " has multiple reporting marks. " +
                    "Set up its styles manually before running bulk copy.");
            string existingMark = oldMarks.Count == 1 ? oldMarks[0] : null;
            // Existing styles define their own railroad identity. Do not silently change it.
            string mark = existingMark ?? ValidateReportingMark(requestedMark, destinationId);
            if (!string.IsNullOrWhiteSpace(requestedMark) && existingMark != null &&
                !string.Equals(existingMark, requestedMark.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(destinationId + " already has reporting mark " +
                    existingMark + ". Refusing to replace it with " + requestedMark + ".");
            if (targetStyles.Count > sourceStyles.Count)
                throw new InvalidOperationException(destinationId + " has more styles than the source. " +
                    "Refusing to discard or arbitrarily match existing styles.");
            if (targetStyles.Any(s => (((JObject)s["definition"])["carIdentifiers"] as JArray)?.Count != 1))
                throw new InvalidOperationException(destinationId + " has shared CarStyle entries. " +
                    "Copying would change other cars; split the style manually first.");

            for (int i = 0; i < sourceStyles.Count; i++)
            {
                JObject template = sourceStyles[i];
                if (i < targetStyles.Count)
                {
                    UpdateExistingStyle(template, targetStyles[i], destinationId, mark);
                    continue;
                }
                JObject style = (JObject)template.DeepClone();
                string id = UniqueStyleId(objects, destinationId, mark);
                style["identifier"] = id;
                JObject def = (JObject)style["definition"];
                def["name"] = mark;
                def["reportingMark"] = mark;
                def["carIdentifiers"] = new JArray(destinationId);
                if (style["metadata"] is JObject meta)
                {
                    meta["name"] = mark;
                }
                // Preserve the source's style rules and carSeries as an editable starting
                // point. They can be changed in the in-game definition editor later.
                objects.Add(style);
            }
            string sourceMark = (string)((JObject)sourceStyles[0]["definition"])["reportingMark"];
            EnsureReportingMark(objects, FindMarkTemplate(objects, sourceMark), destinationId, mark);
        }

        private static void ApplyBaseRoadNumber(JObject target, RoadNumberOverrides numbers)
        {
            if (numbers == null || string.IsNullOrWhiteSpace(numbers.BaseRoadNumber))
            {
                return; // Blank means inherit source, even if source has no field.
            }
            ((JObject)target["definition"])["baseRoadNumber"] = numbers.BaseRoadNumber.Trim();
        }

        private static JObject EnsureStyleForRange(JArray objects, string destinationId, string requestedMark)
        {
            List<JObject> styles = LinkedStyles(objects, destinationId);
            if (styles.Count > 1)
            {
                throw new InvalidOperationException(destinationId +
                    " has multiple CarStyles; edit numbering ranges individually in Railroader.");
            }
            if (styles.Count == 1)
            {
                return styles[0];
            }
            // Can create a standalone new style even when the source has no CarStyle.
            string mark = ValidateReportingMark(requestedMark, destinationId);
            EnsureReportingMark(objects, null, destinationId, mark);
            JObject style = new JObject {
                ["identifier"] = UniqueStyleId(objects, destinationId, mark),
                ["metadata"] = new JObject { ["name"] = mark },
                ["definition"] = new JObject {
                    ["kind"] = "CarStyle", ["name"] = mark,
                    ["reportingMark"] = mark,
                    ["carIdentifiers"] = new JArray(destinationId),
                    ["styles"] = new JObject(),
                    ["carSeries"] = new JObject(),
                    ["components"] = new JObject()
                }
            };
            objects.Add(style);
            return style;
        }

        private static void ApplyRoadSeries(JArray objects, string destinationId,
            RoadNumberOverrides numbers, string requestedMark)
        {
            if (numbers == null || !numbers.HasRange)
            {
                return;
            }
            JObject style = EnsureStyleForRange(objects, destinationId, requestedMark);
            JObject definition = (JObject)style["definition"];
            JObject series = definition["carSeries"] as JObject ?? new JObject();
            string name = (numbers.SeriesName ?? "").Trim();
            if (name.Length == 0)
            {
                if (series.Properties().Count() != 1)
                {
                    throw new InvalidOperationException("Provide a series name for " + destinationId +
                        " (there is not exactly one existing series to update).");
                }
                name = series.Properties().Single().Name;
            }
            if (name.IndexOfAny(new[] { '/', '\\' }) >= 0)
            {
                throw new InvalidOperationException("Series name cannot contain a slash: " + name);
            }
            JObject entry = series[name] as JObject;
            if (entry == null)
            {
                // For a newly cloned one-series template, a new name means rename it,
                // preserving any other fields on that original series.
                JObject old = series.Properties().Count() == 1
                    ? series.Properties().Single().Value as JObject : null;
                entry = old != null ? (JObject)old.DeepClone() : new JObject();
                if (old != null)
                {
                    series.RemoveAll();
                }
            }
            if (!string.IsNullOrWhiteSpace(numbers.SeriesStart))
            {
                entry["start"] = numbers.SeriesStart.Trim();
            }
            if (!string.IsNullOrWhiteSpace(numbers.SeriesEnd))
            {
                entry["end"] = numbers.SeriesEnd.Trim();
            }
            if (string.IsNullOrWhiteSpace((string)entry["start"]) ||
                string.IsNullOrWhiteSpace((string)entry["end"]))
            {
                throw new InvalidOperationException("Series '" + name + "' for " + destinationId +
                    " needs both start and end. Provide the missing value(s).");
            }
            series[name] = entry;
            definition["carSeries"] = series;
        }

        internal static string CopyToTargets(string pack, string sourceId, IEnumerable<string> targetIds,
            bool askConfirmation, RelatedCopyOptions related = null)
        {
            JArray objects = Read(pack, out JToken root);
            var candidates = Sources(pack);
            Choice sourceChoice = candidates.SingleOrDefault(c => string.Equals(c.Id, sourceId, StringComparison.OrdinalIgnoreCase));
            if (sourceChoice == null)
            {
                throw new InvalidOperationException("Source must be an existing root prefab definition.");
            }
            JObject source = objects.OfType<JObject>().Single(o => string.Equals((string)o["identifier"], sourceId, StringComparison.OrdinalIgnoreCase));
            HashSet<string> desired = new HashSet<string>(targetIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            desired.Remove(sourceId);
            if (desired.Count == 0)
            {
                return "No other definitions to update.";
            }
            var allowed = candidates.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
            List<JObject> targets = new List<JObject>();
            foreach (string targetId in desired)
            {
                if (!allowed.TryGetValue(targetId, out Choice c))
                {
                    throw new InvalidOperationException("Target is not a root prefab definition: " + targetId);
                }
                if (!string.Equals(c.Kind, sourceChoice.Kind, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Skipping incompatible definition kind for " + c.Id);
                }
                var matched = objects.OfType<JObject>().Where(o =>
                    string.Equals((string)o["identifier"], targetId, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matched.Count != 1)
                {
                    throw new InvalidOperationException("Target must occur exactly once: " + targetId);
                }
                targets.Add(matched[0]);
            }

            // Work on a detached container: validate ALL changes before backing up and
            // committing. This includes related styles and reporting marks.
            var stagedRoot = root.DeepClone();
            JArray staged = (JArray)((JObject)stagedRoot)["objects"];
            var stagedSource = staged.OfType<JObject>().Single(o =>
                string.Equals((string)o["identifier"], sourceId, StringComparison.OrdinalIgnoreCase));
            int updatedCount = 0;
            foreach (JObject target in targets)
            {
                string id = (string)target["identifier"];
                JObject stagedTarget = staged.OfType<JObject>().Single(o =>
                    string.Equals((string)o["identifier"], id, StringComparison.OrdinalIgnoreCase));
                JObject updated = CopyDefinition(stagedSource, stagedTarget);
                RoadNumberOverrides numbers = null;
                if (related != null)
                {
                    related.RoadNumbers.TryGetValue(id, out numbers);
                }
                ApplyBaseRoadNumber(updated, numbers);
                if (!JToken.DeepEquals(updated, stagedTarget))
                {
                    stagedTarget.Replace(updated);
                    updatedCount++;
                }
                if (related?.IncludeCarStyles == true &&
                    string.Equals(sourceChoice.Kind, "Car", StringComparison.OrdinalIgnoreCase))
                {
                    string mark = null;
                    related.TargetMarks.TryGetValue(id, out mark);
                    if (HasLinkedStylesInArray(staged, sourceId))
                    {
                        CopyLinkedCarStyles(staged, sourceId, id, mark);
                    }
                }
                if (numbers != null && numbers.HasRange && sourceChoice.Kind == "Car")
                {
                    string mark = null;
                    related.TargetMarks.TryGetValue(id, out mark);
                    ApplyRoadSeries(staged, id, numbers, mark);
                }
            }
            if (JToken.DeepEquals(root, stagedRoot))
            {
                return "All selected definitions and related styles already match source settings.";
            }
            if (askConfirmation && !EditorUtility.DisplayDialog("Copy definition settings?",
                "Copy settings from " + sourceId + " to " + targets.Count + " other " + sourceChoice.Kind +
                " definition(s)?\n\nThis replaces non-generated settings, including components, " +
                "dimensions and animations. Target IDs, names, model references and baked " +
                "Colorizer data are retained. Optional road-number overrides apply to each destination.\n\n" +
                (related?.IncludeCarStyles == true && string.Equals(sourceChoice.Kind, "Car", StringComparison.OrdinalIgnoreCase)
                    ? "Related CarStyle entries will also be copied. Existing reporting marks and road-number " +
                      "series are preserved. New styles use the chosen reporting marks, with the source's " +
                      "road-number series as an editable starting point.\n\n"
                    : "") + "A backup will be created first.",
                "Copy settings", "Cancel"))
                return "Canceled. No definitions changed.";

            string path = RailroaderAssetPackTools.Absolute(DefinitionsPath(pack));
            string backup = path + ".before-copy-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".bak";
            File.Copy(path, backup, false);
            File.WriteAllText(path, stagedRoot.ToString(Formatting.Indented) + "\n", new UTF8Encoding(false));
            AssetDatabase.ImportAsset(DefinitionsPath(pack), ImportAssetOptions.ForceSynchronousImport);
            return "Copied " + sourceId + " to " + targets.Count + " definition(s)" +
                (related?.IncludeCarStyles == true && sourceChoice.Kind == "Car"
                    ? " including linked CarStyle/ReportingMark entries" : "") +
                ". Backup: " + Path.GetFileName(backup);
        }
    }
}
#endif
