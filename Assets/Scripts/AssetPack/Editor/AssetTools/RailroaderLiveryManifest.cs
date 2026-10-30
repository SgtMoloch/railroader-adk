#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Railroader.AssetTools
{
    /// <summary>Describes a new livery that reuses an FBX already in this asset pack.
    /// This lives in Models/&lt;livery&gt;/ so the Material Builder can rebuild it later.</summary>
    [Serializable]
    internal sealed class RailroaderLiveryManifest
    {
        public string sourceModelPath;
        public string prefabPath;

        internal string SourceModelPath => sourceModelPath;
        internal string PrefabPath => prefabPath;
        [NonSerialized] internal string ManifestPath;

        internal static string PathFor(string pack, string liveryName) =>
            pack + "/Models/" + liveryName + "/LiverySource.json";

        internal static void ValidateExisting(string path, string sourceModel, string prefab)
        {
            if (!File.Exists(RailroaderAssetPackTools.Absolute(path)))
            {
                return;
            }
            var saved = JsonUtility.FromJson<RailroaderLiveryManifest>(
                File.ReadAllText(RailroaderAssetPackTools.Absolute(path)));
            if (saved == null || !string.Equals(saved.sourceModelPath, sourceModel,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(saved.prefabPath, prefab, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Livery manifest already exists with a different " +
                    "source FBX or prefab. Choose another livery name: " + path);
        }

        internal static void Save(string path, string sourceModel, string prefab)
        {
            ValidateExisting(path, sourceModel, prefab);
            if (File.Exists(RailroaderAssetPackTools.Absolute(path)))
            {
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(RailroaderAssetPackTools.Absolute(path)));
            File.WriteAllText(RailroaderAssetPackTools.Absolute(path),
                JsonUtility.ToJson(new RailroaderLiveryManifest {
                    sourceModelPath = sourceModel, prefabPath = prefab
                }, true) + "\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        internal static List<RailroaderLiveryManifest> FindInPack(string pack)
        {
            string root = pack + "/Models";
            if (!Directory.Exists(RailroaderAssetPackTools.Absolute(root)))
            {
                return new List<RailroaderLiveryManifest>();
            }
            List<RailroaderLiveryManifest> results = new List<RailroaderLiveryManifest>();
            foreach (string absolute in Directory.GetFiles(RailroaderAssetPackTools.Absolute(root),
                "LiverySource.json", SearchOption.AllDirectories))
            {
                string path = RailroaderAssetPackTools.AssetPathFromAbsolute(absolute);
                if (path == null)
                {
                    continue;
                }
                var saved = JsonUtility.FromJson<RailroaderLiveryManifest>(File.ReadAllText(absolute));
                if (saved == null || string.IsNullOrEmpty(saved.SourceModelPath) ||
                    string.IsNullOrEmpty(saved.PrefabPath)) continue;
                saved.ManifestPath = path;
                results.Add(saved);
            }
            return results.OrderBy(x => x.ManifestPath, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
#endif
