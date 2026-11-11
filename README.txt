Railroader ADK Tools - Unity Source Updater (one-time setup)
=============================================================

ONE-TIME SETUP
1. Open your existing Railroader ADK Unity project folder.
2. Copy/merge the Assets folder from this updater ZIP into that project.
   It creates Assets/Editor/RailroaderADKToolsInstaller/ (outside managed tools).
3. Open Unity and wait for the editor script to compile.

INSTALL FUTURE UPDATES (UNITY CAN STAY OPEN)
1. In Unity, choose Railroader > ADK Tools > Install Update...
2. Drag the latest RailroaderADKTools_v3.0_*_SourceOnly.zip into the window,
   or click Browse ZIP...
3. Review the source-file counts and click Install Update, then confirm.
4. Unity reloads the changed scripts. No terminal or manual extraction needed.

WHAT GETS REPLACED
- Assets/Scripts/AssetPack/Editor/AssetTools/
- Assets/Scripts/AssetPack/Authoring/Colorizer/

All old files in those two directories are removed, including files that no
longer exist in the release. Everything else, including the base ADK, prefabs,
textures, definitions, and the installer itself, is left alone.

SAFETY
- Only source-only releases with both source folders + .asmdef/.meta files
  are accepted. ZIP paths outside the two managed folders are rejected.
- The installer stages extraction outside Assets, then backs up the existing
  toolkit before deletion. Failure during replacement triggers rollback.
- Backups are kept under Library/RailroaderADKTools/InstallerBackups.
- Existing folder .meta GUIDs are preserved, so folder references stay stable.
- Do not put your own scripts in the two managed folders: updates replace them.
- Don't install while Unity is compiling, importing, or running Play Mode.
- Use only source ZIPs from a trusted maintainer: they contain executable C#.

The installer deliberately does NOT install the base ADK for you.
The installer is separate from ADK Tools' runtime/source ZIP and needs to be
installed only once. It is not overwritten by future source updates.

The Unity package has been checked for expected structure and compatibility
with the Beta 1.5.8 source-only ZIP. A live Unity compile/test is still needed.
