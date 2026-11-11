#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Railroader.AssetTools
{
    /// <summary>
    /// Reminds creators that their in-game edits are still in ModDev rather than in the ADK.
    /// Nothing is read or copied from the game's installed Mods directory.
    /// </summary>
    [InitializeOnLoad]
    internal static class RailroaderPullReminder
    {
        private const string PreferencePrefix = "Railroader.AssetTools.PullReminder.";
        private const double PollSeconds = 30.0;
        private const double FocusDelaySeconds = 2.0;
        private static readonly HashSet<string> DataExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".json", ".riff", ".rrhorn", ".rrwhistle", ".rl"
        };

        private sealed class FileChange
        {
            internal string Source;
            internal string Destination;
            internal string Relative;
            internal long SourceTicks;
            internal long SourceLength;
        }

        private sealed class PackChange
        {
            internal string Identifier;
            internal readonly List<FileChange> Files = new List<FileChange>();
        }

        private static readonly string ProjectPreferencePrefix = PreferencePrefix +
            StableHash(Application.dataPath) + ".";
        private static bool enabled;
        private static double nextScan;
        private static bool showingDialog;
        private static bool scanQueued;
        // Avoid repeatedly reading large map/data files when the source and target have not changed.
        private static readonly Dictionary<string, bool> equalFileCache =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> warnedDuplicateIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static RailroaderPullReminder()
        {
            enabled = EditorPrefs.GetBool(PrefKey("Enabled"), true);
            nextScan = EditorApplication.timeSinceStartup + 8.0;
            EditorApplication.update += Update;
            EditorApplication.focusChanged += OnFocusChanged;
        }

        private static string PrefKey(string name)
        {
            // Scope preferences to this Unity project, not to all installed ADKs.
            return ProjectPreferencePrefix + name;
        }

        private static string StableHash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 20);
            }
        }

        internal static bool Enabled
        {
            get { return enabled; }
            set
            {
                if (enabled != value)
                {
                    enabled = value;
                    EditorPrefs.SetBool(PrefKey("Enabled"), value);
                }
            }
        }

        internal static string DefaultModDevPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Railroader", "ModDev");
        }

        internal static string ModDevPath
        {
            get
            {
                string configured = EditorPrefs.GetString(PrefKey("ModDevPath"), "");
                return string.IsNullOrWhiteSpace(configured) ? DefaultModDevPath() : configured;
            }
            set
            {
                EditorPrefs.SetString(PrefKey("ModDevPath"), value ?? "");
                EditorPrefs.DeleteKey(PrefKey("IgnoredChanges"));
                nextScan = EditorApplication.timeSinceStartup + 1.0;
            }
        }

        private static void OnFocusChanged(bool hasFocus)
        {
            // Returning to Unity after saving something in Railroader is the important case.
            if (hasFocus)
            {
                nextScan = EditorApplication.timeSinceStartup + FocusDelaySeconds;
            }
        }

        private static void Update()
        {
            if (!Enabled || showingDialog || scanQueued || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.timeSinceStartup < nextScan)
            {
                return;
            }
            nextScan = EditorApplication.timeSinceStartup + PollSeconds;
            scanQueued = true;
            EditorApplication.delayCall += AutomaticCheck;
        }

        private static void AutomaticCheck()
        {
            scanQueued = false;
            if (!Enabled || showingDialog || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }
            try
            {
                List<PackChange> changes = Scan();
                string fingerprint = Fingerprint(changes);
                if (changes.Count > 0 && fingerprint != EditorPrefs.GetString(PrefKey("IgnoredChanges"), ""))
                {
                    ShowReminder(changes, fingerprint);
                }
            }
            catch (Exception ex)
            {
                // A broken or unavailable ModDev directory should never interrupt Unity editing.
                Debug.LogWarning("[Railroader Pull Reminder] Could not check ModDev: " + ex.Message);
            }
        }

        [MenuItem("Railroader/Pull Reminder/Check Now")]
        private static void CheckNow()
        {
            if (showingDialog)
            {
                return;
            }
            try
            {
                if (!Directory.Exists(ModDevPath))
                {
                    EditorUtility.DisplayDialog("Pull Reminder",
                        "ModDev was not found at:\n" + ModDevPath +
                        "\n\nSet the correct directory under Railroader > Pull Reminder > Settings.", "OK");
                    return;
                }
                List<PackChange> changes = Scan();
                if (changes.Count == 0)
                {
                    EditorUtility.DisplayDialog("Pull Reminder", "No newer, differing ModDev data files " +
                        "were found in the asset packs currently loaded in the ADK.", "OK");
                    return;
                }
                ShowReminder(changes, Fingerprint(changes));
            }
            catch (Exception ex)
            {
                Debug.LogError("[Railroader Pull Reminder] " + ex);
                EditorUtility.DisplayDialog("Pull Reminder", "Unable to scan ModDev:\n" + ex.Message, "OK");
            }
        }

        [MenuItem("Railroader/Pull Reminder/Settings")]
        private static void OpenSettings()
        {
            GetSettingsWindow();
        }

        private static void GetSettingsWindow()
        {
            RailroaderPullReminderSettings.ShowWindow();
        }

        private static string Identifier(string infoPath)
        {
            try
            {
                string text = File.ReadAllText(infoPath);
                PackInfo info = JsonUtility.FromJson<PackInfo>(text);
                return info == null || string.IsNullOrWhiteSpace(info.identifier)
                    ? null : info.identifier.Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }

        [Serializable]
        private sealed class PackInfo
        {
            public string identifier;
        }

        private static List<PackChange> Scan()
        {
            List<PackChange> results = new List<PackChange>();
            string modDevRoot = Path.GetFullPath(ModDevPath);
            if (!Directory.Exists(modDevRoot))
            {
                return results;
            }

            // The ModDev directory name is not guaranteed to match the pack identifier.
            // Join by Info.json identifier, not by directory name or the selected ADK pack.
            Dictionary<string, string> modDevPacks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in Directory.GetDirectories(modDevRoot))
            {
                string info = Path.Combine(dir, "Info.json");
                if (!File.Exists(info))
                {
                    continue;
                }
                string id = Identifier(info);
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }
                if (modDevPacks.ContainsKey(id))
                {
                    ambiguous.Add(id);
                }
                else
                {
                    modDevPacks.Add(id, dir);
                }
            }
            foreach (string id in ambiguous)
            {
                modDevPacks.Remove(id);
                if (warnedDuplicateIds.Add(id))
                {
                    Debug.LogWarning("[Railroader Pull Reminder] Multiple ModDev directories have ID " +
                        id + "; skipping ambiguous pull source.");
                }
            }

            foreach (string adkPack in RailroaderAssetPackTools.Packs())
            {
                string id = Identifier(RailroaderAssetPackTools.Absolute(adkPack + "/Info.json"));
                if (string.IsNullOrEmpty(id) || !modDevPacks.TryGetValue(id, out string sourceDir))
                {
                    continue;
                }
                string targetDir = RailroaderAssetPackTools.Absolute(adkPack);
                PackChange pack = new PackChange { Identifier = id };
                foreach (string source in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    if (!DataExtensions.Contains(Path.GetExtension(source)))
                    {
                        continue;
                    }
                    string relative = source.Substring(sourceDir.TrimEnd(Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar).Length + 1);
                    string destination = Path.GetFullPath(Path.Combine(targetDir, relative));
                    string targetRoot = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!destination.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    DateTime sourceWrite = File.GetLastWriteTimeUtc(source);
                    bool exists = File.Exists(destination);
                    DateTime targetWrite = exists ? File.GetLastWriteTimeUtc(destination) : DateTime.MinValue;
                    // Differences in the reverse direction are NOT an invitation to pull.
                    // They may be unpublished local ADK edits that must not be overwritten.
                    if (exists && sourceWrite <= targetWrite.AddSeconds(1.0))
                    {
                        continue;
                    }
                    FileInfo src = new FileInfo(source);
                    FileInfo dst = exists ? new FileInfo(destination) : null;
                    if (exists && EqualWithCache(src, dst))
                    {
                        continue;
                    }
                    pack.Files.Add(new FileChange
                    {
                        Source = source,
                        Destination = destination,
                        Relative = relative.Replace('\\', '/'),
                        SourceTicks = src.LastWriteTimeUtc.Ticks,
                        SourceLength = src.Length
                    });
                }
                if (pack.Files.Count > 0)
                {
                    pack.Files.Sort((a, b) => string.Compare(a.Relative, b.Relative,
                        StringComparison.OrdinalIgnoreCase));
                    results.Add(pack);
                }
            }
            results.Sort((a, b) => string.Compare(a.Identifier, b.Identifier,
                StringComparison.OrdinalIgnoreCase));
            return results;
        }

        private static bool EqualWithCache(FileInfo source, FileInfo target)
        {
            string key = source.FullName + "|" + source.Length + "|" +
                source.LastWriteTimeUtc.Ticks + "|" + target.FullName + "|" +
                target.Length + "|" + target.LastWriteTimeUtc.Ticks;
            if (equalFileCache.TryGetValue(key, out bool equal))
            {
                return equal;
            }
            equal = FilesEqual(source.FullName, target.FullName);
            if (equalFileCache.Count >= 2048)
            {
                equalFileCache.Clear();
            }
            equalFileCache[key] = equal;
            return equal;
        }

        private static bool FilesEqual(string first, string second)
        {
            FileInfo a = new FileInfo(first);
            FileInfo b = new FileInfo(second);
            if (a.Length != b.Length)
            {
                return false;
            }
            byte[] left = new byte[65536];
            byte[] right = new byte[65536];
            using (FileStream x = File.OpenRead(first))
            using (FileStream y = File.OpenRead(second))
            {
                int count;
                while ((count = x.Read(left, 0, left.Length)) != 0)
                {
                    int position = 0;
                    while (position < count)
                    {
                        int read = y.Read(right, position, count - position);
                        if (read == 0)
                        {
                            return false;
                        }
                        position += read;
                    }
                    for (int i = 0; i < count; i++)
                    {
                        if (left[i] != right[i])
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        private static string Fingerprint(List<PackChange> changes)
        {
            StringBuilder builder = new StringBuilder();
            foreach (PackChange pack in changes)
            {
                foreach (FileChange file in pack.Files)
                {
                    builder.Append(pack.Identifier).Append('|').Append(file.Relative).Append('|')
                        .Append(file.SourceTicks).Append('|').Append(file.SourceLength).Append('\n');
                }
            }
            return StableHash(builder.ToString());
        }

        private static void ShowReminder(List<PackChange> changes, string fingerprint)
        {
            showingDialog = true;
            try
            {
                int fileCount = changes.Sum(p => p.Files.Count);
                StringBuilder message = new StringBuilder();
                message.AppendLine("In-game changes were found in ModDev for " + changes.Count +
                    " loaded asset pack(s):\n");
                foreach (PackChange pack in changes.Take(18))
                {
                    message.AppendLine("  " + pack.Identifier + " (" + pack.Files.Count + " file(s))");
                    foreach (FileChange file in pack.Files.Take(3))
                    {
                        message.AppendLine("      " + file.Relative);
                    }
                    if (pack.Files.Count > 3)
                    {
                        message.AppendLine("      ... and " + (pack.Files.Count - 3) + " more");
                    }
                }
                if (changes.Count > 18)
                {
                    message.AppendLine("... and " + (changes.Count - 18) + " more packs");
                }
                message.AppendLine("\nPull copies these " + fileCount +
                    " newer data files from ModDev into the ADK, with backups. " +
                    "It does not modify FBX, textures, prefabs, or asset bundles.");
                message.AppendLine("\nIgnore dismisses these exact changes until another file changes. " +
                    "You can always run Railroader > Pull Reminder > Check Now.");
                if (EditorUtility.DisplayDialog("Railroader - Unsynced in-game edits", message.ToString(),
                    "Pull", "Ignore"))
                {
                    Pull(changes);
                }
                else
                {
                    EditorPrefs.SetString(PrefKey("IgnoredChanges"), fingerprint);
                }
            }
            finally
            {
                showingDialog = false;
                nextScan = EditorApplication.timeSinceStartup + PollSeconds;
            }
        }

        private static void Pull(List<PackChange> pending)
        {
            // Re-evaluate before overwriting anything: the user may have edited a file
            // while the reminder dialog was open.
            List<PackChange> current = Scan();
            if (Fingerprint(current) != Fingerprint(pending))
            {
                EditorUtility.DisplayDialog("Pull Reminder", "The files changed while the reminder " +
                    "was open. Run Check Now to review the latest changes.", "OK");
                return;
            }
            int total = current.Sum(p => p.Files.Count);
            if (!EditorUtility.DisplayDialog("Confirm pull from ModDev",
                "Copy " + total + " updated/new data file(s) from " + current.Count +
                " pack(s) into the Unity ADK?\n\nExisting ADK files will be backed up to " +
                "Library/RailroaderADKTools/PullBackups before being overwritten.\n\n" +
                "Changes made only in the ADK are not pulled or overwritten.", "Pull files", "Cancel"))
            {
                // Canceling the second confirmation should not reopen the reminder every 30 seconds.
                EditorPrefs.SetString(PrefKey("IgnoredChanges"), Fingerprint(current));
                return;
            }
            string backupRoot = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "Library", "RailroaderADKTools", "PullBackups", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            int copied = 0;
            try
            {
                // Back up EVERY existing destination before doing any replacement.
                foreach (PackChange pack in current)
                {
                    foreach (FileChange file in pack.Files)
                    {
                        if (File.Exists(file.Destination))
                        {
                            string backup = Path.Combine(backupRoot, pack.Identifier.Replace('/', '_'),
                                file.Relative.Replace('/', Path.DirectorySeparatorChar));
                            Directory.CreateDirectory(Path.GetDirectoryName(backup));
                            File.Copy(file.Destination, backup, false);
                        }
                    }
                }
                foreach (PackChange pack in current)
                {
                    foreach (FileChange file in pack.Files)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(file.Destination));
                        File.Copy(file.Source, file.Destination, true);
                        copied++;
                    }
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                EditorPrefs.DeleteKey(PrefKey("IgnoredChanges"));
                EditorUtility.DisplayDialog("Pull complete", "Pulled " + copied + " file(s) from " +
                    current.Count + " pack(s).\n\nBackups (when needed):\n" + backupRoot,
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Railroader Pull Reminder] Pull failed after " + copied +
                    " file(s): " + ex);
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Pull incomplete", "The pull stopped after " + copied +
                    " copied file(s).\n\n" + ex.Message + "\n\nBackups:\n" + backupRoot +
                    "\n\nCheck the Console before trying again.", "OK");
            }
        }
    }

    internal sealed class RailroaderPullReminderSettings : EditorWindow
    {
        private string modDevPath;

        internal static void ShowWindow()
        {
            RailroaderPullReminderSettings window = GetWindow<RailroaderPullReminderSettings>(
                "Pull Reminder Settings");
            window.minSize = new Vector2(460, 175);
            window.Show();
        }

        private void OnEnable()
        {
            modDevPath = RailroaderPullReminder.ModDevPath;
        }

        private void OnGUI()
        {
            RailroaderADKToolsVersion.DrawHeader("Asset Pack Pull Reminder");
            EditorGUILayout.HelpBox("Checks loaded ADK packs for newer, differing data files in " +
                "Railroader's ModDev directory. Checks on return to Unity and about every 30 seconds. " +
                "It does not change imported models or prefab assets.", MessageType.Info);
            RailroaderPullReminder.Enabled = EditorGUILayout.Toggle("Automatic reminders",
                RailroaderPullReminder.Enabled);
            EditorGUILayout.BeginHorizontal();
            modDevPath = EditorGUILayout.TextField("ModDev directory", modDevPath);
            if (GUILayout.Button("Browse", GUILayout.Width(72)))
            {
                string selected = EditorUtility.OpenFolderPanel("Choose Railroader ModDev folder",
                    Directory.Exists(modDevPath) ? modDevPath :
                    RailroaderPullReminder.DefaultModDevPath(), "");
                if (!string.IsNullOrEmpty(selected))
                {
                    modDevPath = selected;
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save settings"))
            {
                RailroaderPullReminder.ModDevPath = modDevPath.Trim();
            }
            if (GUILayout.Button("Use default directory"))
            {
                modDevPath = RailroaderPullReminder.DefaultModDevPath();
                RailroaderPullReminder.ModDevPath = "";
            }
            EditorGUILayout.EndHorizontal();
            if (!Directory.Exists(modDevPath))
            {
                EditorGUILayout.HelpBox("The chosen directory does not exist. Configure the same " +
                    "ModDev folder used by the ADK Asset Packs window.", MessageType.Warning);
            }
        }
    }
}
#endif
