#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Railroader.AssetTools
{
    /// <summary>
    /// Refreshes Substance Painter textures in exactly one model/livery texture folder.
    /// Image files are overwritten in place, keeping Unity .meta GUIDs and existing
    /// material/prefab references intact. No FBX or prefab remapping is performed.
    /// </summary>
    internal static class RailroaderTextureRefresh
    {
        internal sealed class Target
        {
            internal string Label;
            internal string TextureRoot;
            internal string ModelPath;
            internal string PrefabPath;
            internal bool IsLivery;
        }

        internal sealed class Copy
        {
            internal string Source;
            internal string Destination;
            internal bool Replaces;
            internal bool SameFile;
        }

        internal sealed class Plan
        {
            internal Target Target;
            internal string SourceRoot;
            internal readonly List<Copy> Files = new List<Copy>();
            internal int Skipped;
            internal int ReplaceCount => Files.Count(x => x.Replaces && !x.SameFile);
            internal int NewCount => Files.Count(x => !x.Replaces && !x.SameFile);
            internal int SameCount => Files.Count(x => x.SameFile);

            internal string Describe()
            {
                return "Target: " + Target.Label + "\nDestination: " + Target.TextureRoot +
                    "\nRecognized image files: " + Files.Count +
                    "\nReplace existing: " + ReplaceCount +
                    "\nAdd new: " + NewCount +
                    "\nAlready at destination: " + SameCount +
                    "\nSkipped unsupported/unrecognized files: " + Skipped +
                    "\n\n" + String.Join("\n", Files.Take(12).Select(c =>
                        (c.SameFile ? "SAME " : c.Replaces ? "UPDATE " : "NEW ") +
                        c.Destination).ToArray()) +
                    (Files.Count > 12 ? "\n... and " + (Files.Count - 12) + " more" : "");
            }
        }

        internal static List<Target> Targets(string pack)
        {
            List<Target> results = new List<Target>();
            if (!RailroaderAssetPackTools.ValidPack(pack))
            {
                return results;
            }
            foreach (string model in RailroaderAssetPackTools.Models(pack))
            {
                string root = RailroaderMaterialService.TextureRootForModel(pack, model);
                if (string.IsNullOrEmpty(root))
                {
                    // For an FBX imported without textures yet, use its own scoped folder.
                    root = model.StartsWith(pack + "/Model/", StringComparison.OrdinalIgnoreCase)
                        ? pack + "/Model/Textures"
                        : Path.GetDirectoryName(model).Replace('\\', '/') + "/Textures";
                }
                results.Add(new Target {
                    ModelPath = model,
                    TextureRoot = root,
                    Label = "Model: " + model.Substring(pack.Length + 1)
                });
            }
            foreach (var livery in RailroaderLiveryManifest.FindInPack(pack))
            {
                results.Add(new Target {
                    IsLivery = true,
                    ModelPath = livery.SourceModelPath,
                    PrefabPath = livery.PrefabPath,
                    TextureRoot = Path.GetDirectoryName(livery.ManifestPath).Replace('\\', '/') + "/Textures",
                    Label = "Livery: " + Path.GetFileNameWithoutExtension(livery.PrefabPath) +
                        " (uses " + Path.GetFileName(livery.SourceModelPath) + ")"
                });
            }
            return results;
        }

        private static bool IsImage(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga";
        }

        internal static Plan Prepare(Target target, string sourceFolder)
        {
            if (target == null || string.IsNullOrEmpty(target.TextureRoot))
            {
                throw new InvalidOperationException("Select a model or livery to refresh");
            }
            if (string.IsNullOrWhiteSpace(sourceFolder))
            {
                throw new InvalidOperationException("Select an exported Substance Painter texture folder");
            }
            string source = Path.GetFullPath(sourceFolder.Trim().Trim('"'))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException("Texture export folder not found: " + source);
            }
            string destination = RailroaderAssetPackTools.Absolute(target.TextureRoot);
            var plan = new Plan { SourceRoot = source, Target = target };
            HashSet<string> planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (!IsImage(file))
                {
                    plan.Skipped++; continue;
                }
                if (!RailroaderTextureNames.TryParse(file, out string materialName, out string property))
                {
                    plan.Skipped++;
                    continue;
                }
                string relative = file.Substring(source.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string subfolder = Path.GetDirectoryName(relative);
                string relativeDestination;
                if (string.IsNullOrEmpty(subfolder))
                {
                    // Match the Asset Importer's existing Painter convention.
                    // Body_BaseColor.png -> Textures/Body/Body_BaseColor.png
                    string group = !string.IsNullOrWhiteSpace(materialName)
                        ? materialName : Path.GetFileName(source);
                    if (string.IsNullOrWhiteSpace(group))
                    {
                        throw new InvalidOperationException("Cannot determine material name for " + file);
                    }
                    relativeDestination = Path.Combine(group, Path.GetFileName(file));
                }
                else
                {
                    // Painter exports using $project/$textureSet folders are supported,
                    // preserving the importer's existing relative layout.
                    relativeDestination = relative;
                }
                string destFile = Path.GetFullPath(Path.Combine(destination, relativeDestination));
                if (!destFile.StartsWith(destination + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Texture destination escapes pack: " + destFile);
                if (!planned.Add(destFile))
                {
                    throw new InvalidOperationException("Two export files map to the same texture: " + destFile);
                }

                // Do not silently leave an old _Diffuse alongside a new _BaseColor,
                // or a .png beside a new .tga for the same shader property. Such
                // duplicates can cause Material Builder to fail or use a stale map.
                string destFolder = Path.GetDirectoryName(destFile);
                if (Directory.Exists(destFolder))
                    foreach (string old in Directory.GetFiles(destFolder))
                    {
                        if (string.Equals(old, destFile, StringComparison.OrdinalIgnoreCase) || !IsImage(old))
                        {
                            continue;
                        }
                        if (RailroaderTextureNames.TryParse(old, out _, out string oldProperty) &&
                            string.Equals(property, oldProperty, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException(
                                "A different texture already fills " + property + " in " + destFolder +
                                ": " + Path.GetFileName(old) + ". Rename/remove the old image before " +
                                "importing " + Path.GetFileName(file) + ". Existing files were not changed.");
                    }

                plan.Files.Add(new Copy {
                    Source = Path.GetFullPath(file), Destination = destFile,
                    Replaces = File.Exists(destFile),
                    SameFile = string.Equals(Path.GetFullPath(file), destFile,
                        StringComparison.OrdinalIgnoreCase)
                });
            }
            if (plan.Files.Count == 0)
                throw new InvalidOperationException("No recognized Substance Painter maps in " + source +
                    ". Expected e.g. Body_BaseColor.png, Body_Normal.png, or Body_MetalAOSm.png.");
            // Reject duplicate map properties in the incoming export, even when the
            // two files have different suffixes or are in different nested folders.
            HashSet<string> incoming = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var copy in plan.Files)
            {
                RailroaderTextureNames.TryParse(copy.Source, out string name, out string property);
                string key = (name ?? Path.GetFileName(Path.GetDirectoryName(copy.Source))) + "|" + property;
                if (!incoming.Add(key))
                    throw new InvalidOperationException("Duplicate exported " + property + " for material '" +
                        key.Split('|')[0] + "'. Choose a folder containing one version of each map.");
            }
            return plan;
        }

        internal static string Execute(Plan plan, bool updateMaterials,
            RailroaderShaderSelection selection, UnityEngine.Shader customShader)
        {
            if (plan == null || plan.Files.Count == 0)
            {
                throw new InvalidOperationException("Preview the texture refresh before applying");
            }
            // All paths are recalculated immediately before copying; don't trust an
            // old preview if files or the target pack changed since it was shown.
            Plan current = Prepare(plan.Target, plan.SourceRoot);
            if (current.Files.Count != plan.Files.Count ||
                !current.Files.Select(c => c.Source + " => " + c.Destination)
                    .SequenceEqual(plan.Files.Select(c => c.Source + " => " + c.Destination),
                        StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Texture export contents changed since confirmation. " +
                    "Preview and confirm again; no files were copied.");
            foreach (var file in current.Files.Where(c => !c.SameFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.Destination));
                // File.Copy(..., overwrite:true) does not touch the destination's .meta.
                // Existing texture GUIDs and all .mat references remain intact.
                File.Copy(file.Source, file.Destination, true);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string materialResult = "Materials unchanged (existing texture references remain linked)";
            if (updateMaterials)
            {
                try
                {
                    materialResult = RailroaderMaterialService.UpdateMaterialAssetsOnly(
                        current.Target.TextureRoot, selection, customShader);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Texture files were copied and imported, but updating .mat assets failed: " +
                        ex.Message + ". Existing texture GUIDs remain intact; correct the issue and " +
                        "run Material Builder again.", ex);
                }
            }
            return "Refreshed " + (current.ReplaceCount + current.NewCount) + " texture files for " +
                current.Target.Label + " (" + current.ReplaceCount + " replaced, " + current.NewCount +
                " added). " + materialResult +
                ". No FBX, prefab, or other livery was remapped. " +
                "New material names require the separate Build / Update Materials action.";
        }
    }
}
#endif
