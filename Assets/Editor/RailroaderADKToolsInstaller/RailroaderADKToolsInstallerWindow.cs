// Railroader ADK Tools: self-contained Unity source installer.
// Install this script once OUTSIDE the managed source folders.
// Release-owned folders come from ADKTools.Release.json inside each ZIP.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Railroader.ADKToolsInstaller
{
    public sealed class RailroaderADKToolsInstallerWindow : EditorWindow
    {
        private const string ManifestName = "ADKTools.Release.json";
        private const string StateRelative = "ProjectSettings/RailroaderADKToolsInstalledSource.json";
        private const string BackupRootRelative = "Library/RailroaderADKTools/InstallerBackups";
        private const string StagingRootRelative = "Library/RailroaderADKTools/InstallerStaging";
        private const string AssetPackRelative = "Assets/Scripts/AssetPack";
        private const string InstallerRelative = "Assets/Editor/RailroaderADKToolsInstaller";
        private const int MaxEntries = 1000;
        private const int MaxManagedDirectories = 32;
        private const long MaxSingleFileBytes = 16L * 1024 * 1024;
        private const long MaxTotalBytes = 128L * 1024 * 1024;
        private const int MaxManifestBytes = 64 * 1024;

        // Used ONLY to recognize an installation made before release manifests existed.
        // Future managed folders are supplied exclusively by the ZIP and saved installation state.
        private static readonly string[] LegacyFolders =
        {
            "Assets/Scripts/AssetPack/Editor/AssetTools",
            "Assets/Scripts/AssetPack/Authoring/Colorizer"
        };

        private static readonly HashSet<string> AllowedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".cs", ".asmdef", ".asmref", ".rsp", ".uxml", ".uss",
                ".shader", ".compute", ".hlsl", ".cginc", ".meta"
            };

        [Serializable]
        private sealed class ReleaseManifest
        {
            public int schemaVersion;
            public string version;
            public string[] managedDirectories;
        }

        [Serializable]
        private sealed class InstalledState
        {
            public int schemaVersion;
            public string version;
            public string[] managedDirectories;
        }

        private sealed class PackageInfo
        {
            public readonly List<string> Paths = new List<string>();
            public readonly List<string> ManagedDirectories = new List<string>();
            public int SourceCount;
            public int MetadataCount;
            public string Version;
            public bool IsLegacy;
        }

        private string _zipPath = string.Empty;
        private string _status = "Drop a source-only ADK Tools ZIP or browse for one.";
        private MessageType _statusType = MessageType.Info;
        private PackageInfo _package;
        private Vector2 _scroll;

        [MenuItem("Railroader/ADK Tools/Install Update...")]
        public static void Open()
        {
            var window = GetWindow<RailroaderADKToolsInstallerWindow>("ADK Tools Update");
            window.minSize = new Vector2(540f, 390f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Railroader ADK Tools — Source Updater", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Managed folders are declared by each release ZIP, not hardcoded into this updater. " +
                "The installer stages, backs up, and replaces only those toolkit-owned folders.",
                MessageType.Info);

            Rect dropArea = GUILayoutUtility.GetRect(0f, 66f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drop a RailroaderADKTools SourceOnly.zip here\nor use Browse ZIP...");
            HandleDragAndDrop(dropArea);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(_zipPath, EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Browse ZIP...", GUILayout.Width(116f)))
                {
                    string picked = EditorUtility.OpenFilePanel("Select ADK Tools update", "", "zip");
                    if (!string.IsNullOrEmpty(picked)) SelectZip(picked);
                }
            }

            if (_package != null)
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(95f));
                try
                {
                    List<string> retired = GetRetiredFolders(_package);
                    EditorGUILayout.LabelField("Release: " + _package.Version +
                        (_package.IsLegacy ? " (legacy ZIP)" : string.Empty), EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(_package.SourceCount + " source files, " +
                        _package.MetadataCount + " metadata files", EditorStyles.miniLabel);
                    EditorGUILayout.Space(3);
                    EditorGUILayout.LabelField("Install / replace:", EditorStyles.boldLabel);
                    foreach (string dir in _package.ManagedDirectories)
                        EditorGUILayout.LabelField("  " + dir, EditorStyles.wordWrappedLabel);
                    if (retired.Count > 0)
                    {
                        EditorGUILayout.Space(3);
                        EditorGUILayout.LabelField("Remove directories retired since the previous release:",
                            EditorStyles.boldLabel);
                        foreach (string dir in retired)
                            EditorGUILayout.LabelField("  " + dir, EditorStyles.wordWrappedLabel);
                    }
                }
                catch (Exception ex)
                {
                    EditorGUILayout.HelpBox("Can't inspect installed update state: " + ex.Message,
                        MessageType.Error);
                }
                finally { EditorGUILayout.EndScrollView(); }
            }

            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, _statusType);

            bool busy = EditorApplication.isCompiling || EditorApplication.isUpdating ||
                        EditorApplication.isPlayingOrWillChangePlaymode;
            if (busy)
                EditorGUILayout.HelpBox("Finish compilation/importing and exit Play Mode first.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(_package == null || busy))
            {
                if (GUILayout.Button("Install Update", GUILayout.Height(34f))) InstallSelected();
            }
            EditorGUILayout.LabelField("Backups: " + BackupRootRelative, EditorStyles.miniLabel);
        }

        private void HandleDragAndDrop(Rect dropArea)
        {
            Event e = Event.current;
            if (!dropArea.Contains(e.mousePosition)) return;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            string candidate = null;
            foreach (string candidatePath in DragAndDrop.paths)
            {
                if (candidatePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = candidatePath;
                    break;
                }
            }
            DragAndDrop.visualMode = candidate == null ? DragAndDropVisualMode.Rejected : DragAndDropVisualMode.Copy;
            if (candidate != null && e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                SelectZip(candidate);
            }
            e.Use();
        }

        private static string ProjectRoot
        {
            get { return Directory.GetParent(Application.dataPath).FullName; }
        }

        private static string InProject(string relative)
        {
            return Path.Combine(ProjectRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void CheckProject()
        {
            if (!Directory.Exists(InProject("Assets")) ||
                !Directory.Exists(InProject("ProjectSettings")) ||
                !Directory.Exists(InProject(AssetPackRelative)))
                throw new InvalidOperationException("This must be an existing Railroader ADK Unity project.");
        }

        private void SelectZip(string path)
        {
            _package = null;
            _zipPath = string.Empty;
            try
            {
                if (!Path.IsPathRooted(path)) path = InProject(path);
                path = Path.GetFullPath(path);
                if (!File.Exists(path)) throw new FileNotFoundException("ZIP not found", path);
                CheckProject();
                _package = InspectZip(path);
                _zipPath = path;
                _status = "Package validated. Review the listed directories before installing.";
                _statusType = MessageType.Info;
            }
            catch (Exception ex)
            {
                _status = "Invalid update: " + ex.Message;
                _statusType = MessageType.Error;
            }
            Repaint();
        }

        private static void ValidateRelativePath(string relative)
        {
            if (string.IsNullOrEmpty(relative) || relative.StartsWith("/", StringComparison.Ordinal) ||
                relative.IndexOf('\\') >= 0 || relative.IndexOf(':') >= 0 ||
                relative.IndexOf('\0') >= 0 || relative.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*' }) >= 0)
                throw new InvalidDataException("Invalid path: " + relative);
            foreach (string segment in relative.Split('/'))
                if (segment.Length == 0 || segment == "." || segment == ".." ||
                    segment.EndsWith(".", StringComparison.Ordinal) ||
                    segment.EndsWith(" ", StringComparison.Ordinal))
                    throw new InvalidDataException("Unsafe path: " + relative);
        }

        private static List<string> ValidateManagedDirectories(string[] declared)
        {
            if (declared == null || declared.Length == 0 || declared.Length > MaxManagedDirectories)
                throw new InvalidDataException("Release needs between 1 and " + MaxManagedDirectories +
                    " managed directories.");
            var clean = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string relative in declared)
            {
                ValidateRelativePath(relative);
                string[] parts = relative.Split('/');
                // Refuse broad project folders. A managed folder must be a specific leaf
                // under Assets, never Assets, Editor, AssetPack, or Authoring itself.
                if (parts.Length < 4 || !parts[0].Equals("Assets", StringComparison.Ordinal) ||
                    relative.Equals("Assets/Scripts/AssetPack/Editor", StringComparison.OrdinalIgnoreCase) ||
                    relative.Equals("Assets/Scripts/AssetPack/Authoring", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith(InstallerRelative + "/", StringComparison.OrdinalIgnoreCase) ||
                    relative.Equals(InstallerRelative, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unsafe managed directory: " + relative);
                if (!seen.Add(relative)) throw new InvalidDataException("Duplicate managed directory: " + relative);
                foreach (string earlier in clean)
                    if (relative.StartsWith(earlier + "/", StringComparison.OrdinalIgnoreCase) ||
                        earlier.StartsWith(relative + "/", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Nested managed directories are not supported: " + relative);
                clean.Add(relative);
            }
            return clean;
        }

        private static ReleaseManifest ParseReleaseManifest(string json)
        {
            ReleaseManifest manifest;
            try { manifest = JsonUtility.FromJson<ReleaseManifest>(json); }
            catch (Exception ex) { throw new InvalidDataException("Invalid release manifest JSON.", ex); }
            if (manifest == null || manifest.schemaVersion != 1)
                throw new InvalidDataException("Unsupported release manifest schema.");
            if (string.IsNullOrEmpty(manifest.version) || manifest.version.Length > 100)
                throw new InvalidDataException("Release manifest is missing a valid version.");
            ValidateManagedDirectories(manifest.managedDirectories);
            return manifest;
        }

        private static PackageInfo InspectZip(string zipPath)
        {
            var info = new PackageInfo();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalBytes = 0;
            var entryNames = new List<string>();
            var manifestText = (string)null;

            using (var file = File.OpenRead(zipPath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read, false))
            {
                if (archive.Entries.Count == 0 || archive.Entries.Count > MaxEntries)
                    throw new InvalidDataException("Unexpected ZIP entry count.");
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = entry.FullName;
                    ValidateRelativePath(name);
                    if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                        throw new InvalidDataException("ZIP symlinks are not supported: " + name);
                    if (!seen.Add(name)) throw new InvalidDataException("Duplicate path: " + name);
                    if (entry.Length < 0 || entry.Length > MaxSingleFileBytes)
                        throw new InvalidDataException("Oversized ZIP entry: " + name);
                    totalBytes += entry.Length;
                    if (totalBytes > MaxTotalBytes) throw new InvalidDataException("ZIP is too large.");
                    entryNames.Add(name);
                    if (name.Equals(ManifestName, StringComparison.Ordinal))
                    {
                        if (entry.Length > MaxManifestBytes)
                            throw new InvalidDataException("Release manifest is too large.");
                        using (var reader = new StreamReader(entry.Open(), Encoding.UTF8, true))
                            manifestText = reader.ReadToEnd();
                    }
                }
            }

            if (manifestText != null)
            {
                ReleaseManifest manifest = ParseReleaseManifest(manifestText);
                info.Version = manifest.version;
                info.ManagedDirectories.AddRange(ValidateManagedDirectories(manifest.managedDirectories));
            }
            else
            {
                // Old Beta 1.5.8 source-only ZIPs did not contain a manifest.
                // This fallback is NOT used for future directory changes.
                info.Version = "Legacy source-only release";
                info.IsLegacy = true;
                info.ManagedDirectories.AddRange(LegacyFolders);
            }

            var sourceCountPerFolder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string managed in info.ManagedDirectories) sourceCountPerFolder[managed] = 0;
            foreach (string name in entryNames)
            {
                if (name == ManifestName) continue;
                bool owned = false;
                string folder = null;
                foreach (string managed in info.ManagedDirectories)
                {
                    if (name.StartsWith(managed + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        owned = true;
                        folder = managed;
                        break;
                    }
                    if (name.Equals(managed + ".meta", StringComparison.OrdinalIgnoreCase))
                    {
                        owned = true;
                        break;
                    }
                }
                // Old releases included this shared parent metadata file; never install it.
                bool legacyParentMeta = info.IsLegacy &&
                                        name == AssetPackRelative + "/Authoring.meta";
                if (!owned && !legacyParentMeta)
                    throw new InvalidDataException("Unexpected ZIP path outside managed directories: " + name);
                if (!AllowedExtensions.Contains(Path.GetExtension(name)))
                    throw new InvalidDataException("Not a source/metadata file: " + name);
                if (legacyParentMeta) continue;
                info.Paths.Add(name);
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) info.MetadataCount++;
                else
                {
                    info.SourceCount++;
                    if (folder != null) sourceCountPerFolder[folder]++;
                    if (!seen.Contains(name + ".meta"))
                        throw new InvalidDataException("Missing Unity .meta for: " + name);
                }
            }
            foreach (var count in sourceCountPerFolder)
                if (count.Value == 0)
                    throw new InvalidDataException("No source files in managed directory: " + count.Key);
            return info;
        }

        private static InstalledState ReadState()
        {
            string file = InProject(StateRelative);
            if (!File.Exists(file))
            {
                // A first upgrade from the old source-only ZIP still removes
                // legacy folders if the new release retired either of them.
                return new InstalledState { version = "legacy", managedDirectories = LegacyFolders };
            }
            InstalledState state;
            try { state = JsonUtility.FromJson<InstalledState>(File.ReadAllText(file)); }
            catch (Exception ex) { throw new InvalidDataException("Corrupt installed-source state.", ex); }
            if (state == null || state.schemaVersion != 1)
                throw new InvalidDataException("Unsupported installed-source state format.");
            ValidateManagedDirectories(state.managedDirectories);
            return state;
        }

        private static List<string> GetRetiredFolders(PackageInfo package)
        {
            var keep = new HashSet<string>(package.ManagedDirectories, StringComparer.OrdinalIgnoreCase);
            return ReadState().managedDirectories.Where(p => !keep.Contains(p)).ToList();
        }

        private static List<string> GetAffectedFolders(PackageInfo package)
        {
            var union = new HashSet<string>(package.ManagedDirectories, StringComparer.OrdinalIgnoreCase);
            union.UnionWith(ReadState().managedDirectories);
            var result = union.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            // Across releases, a former managed directory must not be moved under
            // another managed directory; deleting nested directories twice is unsafe.
            for (int i = 0; i < result.Count; i++)
                for (int j = i + 1; j < result.Count; j++)
                    if (result[j].StartsWith(result[i] + "/", StringComparison.OrdinalIgnoreCase) ||
                        result[i].StartsWith(result[j] + "/", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Nested previous/new managed directories: " +
                            result[i] + " / " + result[j]);
            return result;
        }

        private static void EnsureNoReparsePoints(string relative)
        {
            string folder = ProjectRoot;
            foreach (string part in relative.Split('/'))
            {
                folder = Path.Combine(folder, part);
                if (Directory.Exists(folder) &&
                    (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Refusing to modify symlink/junction: " + relative);
            }
        }


        private static void EnsureNoLinksInside(string root)
        {
            if (!Directory.Exists(root)) return;
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count != 0)
            {
                string next = pending.Pop();
                foreach (string entry in Directory.GetFileSystemEntries(next))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Managed folder contains a symlink/junction: " + entry);
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                }
            }
        }

        private static void PreflightTargets(PackageInfo package, List<string> affected)
        {
            var previous = new HashSet<string>(ReadState().managedDirectories, StringComparer.OrdinalIgnoreCase);
            foreach (string dir in affected)
            {
                EnsureNoReparsePoints(dir);
                string target = InProject(dir);
                if (File.Exists(target)) throw new IOException("A file blocks managed folder: " + dir);
                EnsureNoLinksInside(target);
                if (!previous.Contains(dir) && Directory.Exists(target) &&
                    Directory.EnumerateFileSystemEntries(target).Any())
                    throw new IOException("New managed directory already has files: " + dir +
                        ". Move them elsewhere first to avoid deleting unrelated work.");
            }
        }

        private void InstallSelected()
        {
            if (_package == null || string.IsNullOrEmpty(_zipPath) ||
                EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;

            string staging = null;
            try
            {
                CheckProject();
                PackageInfo selected = InspectZip(_zipPath); // Check again before touching project.
                List<string> affected = GetAffectedFolders(selected);
                PreflightTargets(selected, affected);
                List<string> retired = GetRetiredFolders(selected);
                string confirmation = "Release: " + selected.Version +
                    "\n\nReplace directories:\n" + string.Join("\n", selected.ManagedDirectories.ToArray());
                if (retired.Count > 0)
                    confirmation += "\n\nRemove retired directories:\n" + string.Join("\n", retired.ToArray());
                confirmation += "\n\nAll affected source folders will be backed up first. " +
                    "Locally edited toolkit files will be overwritten. " +
                    "Only install ZIPs from a trusted source.";
                if (!EditorUtility.DisplayDialog("Install ADK Tools Update?", confirmation,
                    "Install", "Cancel")) return;

                string stagingRoot = InProject(StagingRootRelative);
                staging = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
                ExtractToStaging(_zipPath, selected, staging);
                string backup = Path.Combine(InProject(BackupRootRelative),
                    DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                    Guid.NewGuid().ToString("N").Substring(0, 8));
                BackupExisting(affected, backup);

                bool editing = false;
                try
                {
                    AssetDatabase.StartAssetEditing();
                    editing = true;
                    try
                    {
                        ReplaceManagedFolders(selected, affected, staging, backup);
                        WriteState(selected);
                    }
                    catch (Exception installError)
                    {
                        try { RestoreBackup(affected, backup); }
                        catch (Exception rollbackError)
                        {
                            throw new IOException("Install AND rollback failed. Manual backup at: " +
                                backup + "\nInstall: " + installError.Message +
                                "\nRollback: " + rollbackError.Message, rollbackError);
                        }
                        throw new IOException("Install failed; previous files restored. " +
                            installError.Message, installError);
                    }
                }
                finally
                {
                    if (editing) AssetDatabase.StopAssetEditing();
                }
                _status = "Installed " + selected.Version + ". Backup: " + backup;
                _statusType = MessageType.Info;
                Debug.Log("[Railroader ADK Tools] Installed " + selected.Version + ". Backup: " + backup);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
            catch (Exception ex)
            {
                _status = "Update unsuccessful: " + ex.Message;
                _statusType = MessageType.Error;
                Debug.LogError("[Railroader ADK Tools] " + ex);
                EditorUtility.DisplayDialog("ADK Tools Update", _status, "OK");
            }
            finally
            {
                if (staging != null)
                {
                    try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                    catch (Exception ex) { Debug.LogWarning("Couldn't remove updater staging: " + ex.Message); }
                }
            }
        }

        private static void ExtractToStaging(string zipPath, PackageInfo package, string staging)
        {
            Directory.CreateDirectory(staging);
            var wanted = new HashSet<string>(package.Paths, StringComparer.OrdinalIgnoreCase);
            using (var file = File.OpenRead(zipPath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read, false))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.FullName == ManifestName) continue;
                    if (!wanted.Remove(entry.FullName) &&
                        !(package.IsLegacy && entry.FullName == AssetPackRelative + "/Authoring.meta"))
                        throw new InvalidDataException("Package changed or unexpected entry: " + entry.FullName);
                    if (!package.Paths.Contains(entry.FullName)) continue; // shared legacy parent meta
                    string target = Path.Combine(staging,
                        entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = entry.Open())
                    using (var output = new FileStream(target, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[64 * 1024];
                        int read;
                        long copied = 0;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            copied += read;
                            if (copied > entry.Length || copied > MaxSingleFileBytes)
                                throw new InvalidDataException("ZIP entry exceeds advertised size: " + entry.FullName);
                            output.Write(buffer, 0, read);
                        }
                        if (copied != entry.Length)
                            throw new InvalidDataException("ZIP entry truncated: " + entry.FullName);
                    }
                }
            }
            if (wanted.Count > 0) throw new InvalidDataException("ZIP changed during extraction.");
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                string tail = directory.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(target, tail));
            }
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string tail = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
                string output = Path.Combine(target, tail);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.Copy(file, output, true);
            }
        }

        private static void BackupExisting(List<string> affected, string backup)
        {
            Directory.CreateDirectory(backup);
            foreach (string relative in affected)
            {
                string old = InProject(relative);
                string target = Path.Combine(backup, relative.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(old)) CopyDirectory(old, target);
                if (File.Exists(old + ".meta"))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(old + ".meta", target + ".meta", false);
                }
            }
            string state = InProject(StateRelative);
            if (File.Exists(state)) File.Copy(state, Path.Combine(backup, "InstalledSource.json"));
        }

        private static void ReplaceManagedFolders(PackageInfo package, List<string> affected,
            string staging, string backup)
        {
            foreach (string relative in affected)
            {
                string existing = InProject(relative);
                if (Directory.Exists(existing)) Directory.Delete(existing, true);
                if (File.Exists(existing + ".meta")) File.Delete(existing + ".meta");
            }
            foreach (string relative in package.ManagedDirectories)
            {
                string target = InProject(relative);
                string source = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(source)) throw new IOException("Missing staged folder: " + relative);
                CopyDirectory(source, target);
                string oldMeta = Path.Combine(backup, relative.Replace('/', Path.DirectorySeparatorChar)) + ".meta";
                string newMeta = source + ".meta";
                string chosenMeta = File.Exists(oldMeta) ? oldMeta : newMeta;
                if (File.Exists(chosenMeta)) File.Copy(chosenMeta, target + ".meta", true);
            }
        }

        private static void WriteState(PackageInfo package)
        {
            var state = new InstalledState
            {
                schemaVersion = 1,
                version = package.Version,
                managedDirectories = package.ManagedDirectories.ToArray()
            };
            string target = InProject(StateRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            // File.Replace is not available consistently on all Unity editor platforms.
            string temporary = target + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(state, true), new UTF8Encoding(false));
            if (File.Exists(target)) File.Delete(target);
            File.Move(temporary, target);
        }

        private static void RestoreBackup(List<string> affected, string backup)
        {
            foreach (string relative in affected)
            {
                string target = InProject(relative);
                string old = Path.Combine(backup, relative.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(target)) Directory.Delete(target, true);
                if (File.Exists(target + ".meta")) File.Delete(target + ".meta");
                if (Directory.Exists(old)) CopyDirectory(old, target);
                if (File.Exists(old + ".meta")) File.Copy(old + ".meta", target + ".meta", true);
            }
            string savedState = Path.Combine(backup, "InstalledSource.json");
            string targetState = InProject(StateRelative);
            if (File.Exists(targetState)) File.Delete(targetState);
            if (File.Exists(savedState))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetState));
                File.Copy(savedState, targetState);
            }
        }
    }
}
#endif
