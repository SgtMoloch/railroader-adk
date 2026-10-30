using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Railroader.AssetTools;

namespace Railroader.AssetTools.Colorizer
{
    /// <summary>
    /// Per-pack, portable material-name exclusions for bulk Colorizer authoring.
    /// Stored under Authoring/, so no new root-level Railroader asset is created.
    /// Matching is case-insensitive substring matching against a renderer material's name.
    /// </summary>
    internal static class ColorizerMaterialBlacklist
    {
        [Serializable]
        private sealed class Settings
        {
            public string[] materialNameContains = new string[0];
        }

        private static readonly string[] DefaultTerms =
        {
            "glass", "window", "mirror", "windshield", "windscreen", "transparent"
        };

        internal static string FilePath(string pack) =>
            pack.TrimEnd('/') + "/Authoring/ColorizerBlacklist.json";

        internal static List<string> Load(string pack)
        {
            if (!RailroaderAssetPackTools.ValidPack(pack))
            {
                throw new InvalidOperationException("Select a valid ADK asset pack first.");
            }
            string path = RailroaderAssetPackTools.Absolute(FilePath(pack));
            if (!File.Exists(path))
            {
                return new List<string>(DefaultTerms);
            }
            Settings settings = JsonUtility.FromJson<Settings>(File.ReadAllText(path));
            if (settings == null || settings.materialNameContains == null)
            {
                throw new InvalidOperationException("Invalid material blacklist: " + FilePath(pack));
            }
            return Normalize(settings.materialNameContains);
        }

        internal static List<string> Parse(string input) =>
            Normalize((input ?? "").Split(new[] { '\r', '\n', ',', ';' },
                StringSplitOptions.RemoveEmptyEntries));

        private static List<string> Normalize(IEnumerable<string> names) =>
            names.Select(x => (x ?? "").Trim())
                .Where(x => x.Length != 0 && !x.StartsWith("#", StringComparison.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        internal static bool Matches(string materialName, IList<string> terms)
        {
            if (string.IsNullOrEmpty(materialName) || terms == null)
            {
                return false;
            }
            return terms.Any(term => !string.IsNullOrWhiteSpace(term) &&
                materialName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static void Save(string pack, IList<string> terms)
        {
            if (!RailroaderAssetPackTools.ValidPack(pack))
            {
                throw new InvalidOperationException("Select a valid ADK asset pack first.");
            }
            string dir = pack.TrimEnd('/') + "/Authoring";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder(pack, "Authoring");
            }
            string path = FilePath(pack);
            string absolute = RailroaderAssetPackTools.Absolute(path);
            var data = new Settings { materialNameContains = Normalize(terms ?? new string[0]).ToArray() };
            File.WriteAllText(absolute, JsonUtility.ToJson(data, true));
            AssetDatabase.ImportAsset(path);
        }
    }
}
