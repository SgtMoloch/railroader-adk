#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Railroader.AssetTools
{
    // Editor-only helpers shared by the two tools. No dependency on ADK implementation details.
    internal static class RailroaderAssetPackTools
    {
        internal const string PackRoot = "Assets/AssetPacks";
        internal const string MetallicShader = "Railroader/Standard Car Shader Metallic (Shared)";
        internal const string SpecularShader = "Railroader/Standard Car Shader Specular (Shared)";
        internal const string LegacySpecularShader = "Railroader/Standard Car Shader (Shared)";

        /// <summary>
        /// Resolve the actual Railroader car shader from this ADK project. The shader's
        /// menu name has changed between the original community tools and newer ADKs,
        /// so never rely solely on a hardcoded Shader.Find string.
        /// </summary>
        internal static Shader FindCarShader(bool specular)
        {
            string required = specular ? "_SpecGlossMap" : "_MetallicGlossMap";
            string[] names = specular
                ? new[] { SpecularShader, LegacySpecularShader,
                    "Railroader/Standard Car Shader (Specular) (Shared)" }
                : new[] { MetallicShader, "Railroader/Standard Car Shader (Metallic) (Shared)" };

            // Fast path for installations whose shader names match the documentation.
            foreach (string name in names)
            {
                Shader shader = Shader.Find(name);
                if (HasProperty(shader, required))
                {
                    return shader;
                }
            }

            // Search the installed shared pack directly. This also handles shaders
            // that Unity can load as assets but that Shader.Find cannot yet locate.
            List<Shader> candidates = new List<Shader>();
            HashSet<string> searched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] roots = AssetDatabase.IsValidFolder("Assets/SharedPacks")
                ? new[] { "Assets/SharedPacks" } : new string[0];
            if (roots.Length > 0)
            {
                foreach (string filter in new[] { "t:Shader", "t:ShaderGraph" })
                    foreach (string guid in AssetDatabase.FindAssets(filter, roots))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        if (!searched.Add(path))
                        {
                            continue;
                        }
                        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                        if (shader != null && shader.name.IndexOf("Car", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            candidates.Add(shader);
                        }
                    }
            }

            // Consider the installed shader assets, never generic URP shaders.
            Shader best = candidates
                .Where(s => HasProperty(s, required) &&
                    s.name.IndexOf("Standard Car", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(s => ShaderScore(s, specular))
                .FirstOrDefault();
            if (best != null)
            {
                return best;
            }

            // Last resort: scan all Unity shader assets for non-standard ADK layouts.
            foreach (string guid in AssetDatabase.FindAssets("t:Shader"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!searched.Add(path))
                {
                    continue;
                }
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader != null && shader.name.IndexOf("Railroader/Standard Car", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    candidates.Add(shader);
                }
            }
            best = candidates
                .Where(s => HasProperty(s, required) &&
                    s.name.IndexOf("Standard Car", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(s => ShaderScore(s, specular))
                .FirstOrDefault();
            if (best != null)
            {
                return best;
            }

            string found = candidates.Count == 0 ? "none" : string.Join(", ", candidates
                .Select(s => s.name + " [" + (HasProperty(s, required) ? "has" : "missing") + " " + required + "]")
                .Distinct().OrderBy(x => x).ToArray());
            throw new InvalidOperationException(
                "Could not locate a Railroader Standard Car " + (specular ? "Specular" : "Metallic") +
                " shader with property " + required + ". Tried: " + string.Join(", ", names) +
                ". Car shader assets found: " + found +
                ". Check Assets/SharedPacks/shared in your ADK project.");
        }

        private static bool HasProperty(Shader shader, string name)
        {
            return shader != null && shader.FindPropertyIndex(name) >= 0;
        }

        private static int ShaderScore(Shader shader, bool specular)
        {
            string name = shader.name;
            int score = 0;
            if (name.StartsWith("Railroader/", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }
            if (name.IndexOf("Shared", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 10;
            }
            if (name.IndexOf(specular ? "Specular" : "Metallic", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 50;
            }
            if (name.IndexOf(specular ? "Metallic" : "Specular", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score -= 50;
            }
            return score;
        }

        [Serializable]
        private sealed class Info
        {
            public string identifier;
        }

        internal static string Absolute(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) ||
                !(assetPath == "Assets" || assetPath.StartsWith("Assets/", StringComparison.Ordinal)))
                throw new ArgumentException("Expected a Unity Assets/ path: " + assetPath);
            return Path.GetFullPath(Path.Combine(Application.dataPath,
                assetPath == "Assets" ? "" : assetPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar)));
        }

        internal static bool ValidPack(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   path.StartsWith(PackRoot + "/", StringComparison.OrdinalIgnoreCase) &&
                   Directory.Exists(Absolute(path)) && File.Exists(Absolute(path + "/Info.json"));
        }

        internal static List<string> Packs()
        {
            List<string> paths = new List<string>();
            string root = Absolute(PackRoot);
            if (!Directory.Exists(root))
            {
                return paths;
            }
            foreach (string infoFile in Directory.GetFiles(root, "Info.json", SearchOption.AllDirectories))
            {
                string path = AssetPathFromAbsolute(Path.GetDirectoryName(infoFile));
                if (path != null && ValidPack(path))
                {
                    paths.Add(path);
                }
            }
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        // UI identity comes from Info.json, not the (arbitrary) pack directory name.
        // The slash is intentional: Unity Popup uses it to group packs into domain submenus.
        internal static string PackIdentifier(string pack)
        {
            try
            {
                Info info = JsonUtility.FromJson<Info>(File.ReadAllText(Absolute(pack + "/Info.json")));
                string id = info == null ? null : info.identifier;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    id = id.Trim().Replace('\\', '/');
                    string[] parts = id.Split('/');
                    if (parts.Length == 2 && parts.All(p => !string.IsNullOrWhiteSpace(p)))
                    {
                        return id;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Railroader Tools] Could not read pack identifier from " + pack +
                    "/Info.json: " + ex.Message);
            }
            // Keep malformed/older packs selectable so ADK itself can validate them.
            return "Other/" + Path.GetFileName(pack);
        }

        internal static string AssetPathFromAbsolute(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return null;
            }
            string root = Path.GetFullPath(Application.dataPath).Replace('\\','/').TrimEnd('/');
            string path = Path.GetFullPath(fullPath).Replace('\\','/').TrimEnd('/');
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return "Assets";
            }
            return path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)
                ? "Assets" + path.Substring(root.Length) : null;
        }

        internal static List<string> RootPrefabs(string pack)
        {
            return AssetDatabase.FindAssets("t:Prefab", new[] { pack })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(Path.GetDirectoryName(p)?.Replace('\\','/'), pack,
                                          StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static List<string> Models(string pack)
        {
            string modelFolder = pack + "/Model";
            string modelsFolder = pack + "/Models";
            var roots = new[] { modelFolder, modelsFolder }
                .Where(x => Directory.Exists(Absolute(x))).ToArray();
            if (roots.Length == 0)
            {
                return new List<string>();
            }
            return AssetDatabase.FindAssets("t:Model", roots)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => (p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
                             p.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)) &&
                             AssetImporter.GetAtPath(p) is ModelImporter)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Root prefab identifiers are per model, independent of the asset pack name.
        internal static bool ValidAssetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name != name.Trim() ||
                name == "." || name == "..") return false;
            return name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) < 0 &&
                !name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A model can be referenced by a prefab variant at its root (the new import
        /// workflow) or by a nested FBX inside a regular prefab (older imports).
        /// Check the ROOT as well as children: a variant may have no child objects.
        /// </summary>
        private static bool PrefabReferencesModel(GameObject root, string modelPath)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject obj = transform.gameObject;
                UnityEngine.Object[] sources =
                {
                    PrefabUtility.GetCorrespondingObjectFromSource(obj),
                    PrefabUtility.GetCorrespondingObjectFromOriginalSource(obj)
                };
                foreach (UnityEngine.Object source in sources)
                {
                    if (source != null && string.Equals(AssetDatabase.GetAssetPath(source),
                        modelPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        internal static void ValidateNamedPrefab(string pack, string modelPath, string name)
        {
            if (!ValidPack(pack) || !ValidAssetName(name))
            {
                throw new InvalidOperationException("Invalid pack or prefab name");
            }
            string path = pack + "/" + name + ".prefab";
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null && File.Exists(Absolute(path)))
            {
                throw new InvalidOperationException("A file already exists at " + path);
            }
            if (asset != null && !(asset is GameObject))
            {
                throw new InvalidOperationException("Existing asset is not a prefab: " + path);
            }
            if (asset == null)
            {
                return;
            }
            GameObject root = (GameObject)asset;
            // Unlike previous importers, we do NOT fill a regular empty prefab with a
            // nested FBX. Doing so would create a different hierarchy from the one
            // produced by dragging an FBX instance from Hierarchy into Project.
            if (PrefabUtility.GetPrefabAssetType(root) != PrefabAssetType.Variant &&
                root.transform.childCount == 0 && root.GetComponent<Renderer>() == null)
                throw new InvalidOperationException("The existing prefab is empty: " + path +
                    ". v2.8.3 creates an FBX-based prefab variant instead of filling empty prefabs. " +
                    "If this is a disposable test prefab, delete it from Unity's Project panel " +
                    "and import again. The importer will not delete it automatically.");
            if (!PrefabReferencesModel(root, modelPath))
                throw new InvalidOperationException("Prefab '" + path +
                    "' already exists but is not linked to " + modelPath +
                    ". Choose another name or update that prefab manually.");
        }

        /// <summary>
        /// Mirrors the Unity authoring workflow:
        ///   (1) drag an imported FBX from Project into Hierarchy;
        ///   (2) drag that FBX *instance itself* back into Project to make a prefab;
        ///   (3) open the resulting prefab for further editing.
        ///
        /// Saving a prefab INSTANCE ROOT (not an empty wrapper around a nested FBX)
        /// produces a Prefab Variant. Unity documents this behavior explicitly.
        /// We use a temporary preview scene to avoid adding a second raw FBX to the
        /// user's live scene. The prefab is saved as an asset only.
        /// </summary>
        internal static string EnsureNamedPrefab(string pack, string modelPath, string name, out string action)
        {
            ValidateNamedPrefab(pack, modelPath, name);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                throw new InvalidOperationException("Model asset unavailable: " + modelPath);
            }
            string path = pack + "/" + name + ".prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                action = PrefabUtility.GetPrefabAssetType(existing) == PrefabAssetType.Variant
                    ? "Existing FBX-based prefab variant preserved: " + name
                    : "Existing populated prefab preserved (legacy nested-FBX layout): " + name;
                return path;
            }

            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject modelInstance = null;
            try
            {
                // This is the equivalent of dragging the FBX directly into Hierarchy.
                // Keep the imported root/Animator/scale/hierarchy unchanged.
                modelInstance = PrefabUtility.InstantiatePrefab(model, preview) as GameObject;
                if (modelInstance == null || !PrefabUtility.IsAnyPrefabInstanceRoot(modelInstance) ||
                    PrefabUtility.GetOutermostPrefabInstanceRoot(modelInstance) != modelInstance)
                    throw new InvalidOperationException("Could not instantiate the imported model as a prefab root: " +
                        modelPath);
                if (PrefabUtility.SaveAsPrefabAsset(modelInstance, path) == null)
                {
                    throw new InvalidOperationException("Could not create FBX prefab variant: " + path);
                }

                GameObject created = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (created == null || PrefabUtility.GetPrefabAssetType(created) != PrefabAssetType.Variant)
                    throw new InvalidOperationException("Unity did not create the expected prefab variant at " +
                        path + ". Check the imported FBX and Unity Console before proceeding.");
                action = "Created FBX-based prefab variant: " + name;
                return path;
            }
            finally
            {
                if (modelInstance != null)
                {
                    UnityEngine.Object.DestroyImmediate(modelInstance);
                }
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }


    }

    /// <summary>Shared Substance Painter/Railroader texture conventions for both tools.
    /// The suffix is removed to recover the source FBX material name; matching is
    /// case-insensitive and anchored at the end so unrelated names are not misread.
    /// </summary>
    internal static class RailroaderTextureNames
    {
        private static readonly (string suffix, string property)[] Names =
        {
            ("_SpecularSmoothness", "_SpecGlossMap"),
            ("_MetallicGlossMap", "_MetallicGlossMap"),
            ("_SpecGlossMap", "_SpecGlossMap"),
            ("_MetalAOSm", "_MetallicGlossMap"),
            ("_BaseColor", "_MainTex"),
            ("_BaseMap", "_MainTex"),
            ("_OcclusionMap", "_OcclusionMap"),
            ("_Occlusion", "_OcclusionMap"),
            ("_MainTex", "_MainTex"),
            ("_BumpMap", "_BumpMap"),
            ("_NormalMap", "_BumpMap"),
            ("_Diffuse", "_MainTex"),
            ("_Normal", "_BumpMap"),
            ("_AO", "_OcclusionMap")
        };

        internal static bool TryParse(string file, out string materialName, out string property)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            foreach (var pair in Names)
            {
                if (name.EndsWith(pair.suffix, StringComparison.OrdinalIgnoreCase) &&
                    name.Length > pair.suffix.Length)
                {
                    materialName = name.Substring(0, name.Length - pair.suffix.Length);
                    property = pair.property;
                    return true;
                }
                // Files such as BaseColor.png are also valid inside a named folder.
                if (name.Equals(pair.suffix.Substring(1), StringComparison.OrdinalIgnoreCase))
                {
                    materialName = null;
                    property = pair.property;
                    return true;
                }
            }
            materialName = null;
            property = null;
            return false;
        }
    }
}
#endif
