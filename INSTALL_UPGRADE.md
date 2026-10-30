# Beta 1.2 — Unity / VS Code upgrade and diagnostics

**Back up the Unity project before removing anything.**

1. Close Unity and VS Code. From this ZIP, copy the complete `Assets/Scripts/AssetPack/Authoring/Colorizer/` and `Assets/Scripts/AssetPack/Editor/AssetTools/` trees into the corresponding locations in the ADK project. Preserve the `.meta` files; they carry the same Unity GUIDs as Beta 1.1.
2. Compare the project's Editor/AssetTools folder against the ZIP. The package has **18 editor C# files** including six in `Colorizer/`; one authoring C# file is under `Authoring/Colorizer/`. In particular, make sure `RailroaderStarterDefinitions.cs`, `RailroaderDefinitionCopier.cs`, `RailroaderLiveryManifest.cs`, `RailroaderPrefabSetup.cs`, and `RailroaderAssetPackTools.cs` all exist at the expected paths.
3. Remove **obsolete toolkit source files only after checking they are not independently used**. For example, `RailroaderMaterialRecovery.cs` appears in the reported VS Code errors but is *not* a file shipped in Beta 1.1 or 1.2. If retaining it, add `using Railroader.AssetTools;` so it can see our now-namespaced `RailroaderAssetPackTools` (the helper is internal, so it must be compiled in a compatible assembly).
4. Check for duplicate or obsolete `Freeman.Colorizer.*.asmdef` and scripts left over from the original Colorizer. These are not supplied by the current package. Do not blindly delete if they're still used elsewhere.
5. Start Unity and allow script import. Check its **Console** for the *first* actual error rather than the full cascade.
6. In Unity, use `Edit > Preferences > External Tools > Regenerate project files` if supported by the installed IDE integration. Reopen the Unity project **root** in VS Code, rather than only `Assets/.../AssetTools`.
7. If the importer still shows missing symbols, open `Railroader.AssetTools.Editor.csproj` and verify that the C# files in item 2 are included in `<Compile Include=...>` entries. They must all be in the `Railroader.AssetTools.Editor` assembly. These are Unity-generated project files; do not edit them by hand.

## Important

Global-to-namespaced code migration was done in Beta 1.1. `.asmdef` `rootNamespace` does *not* automatically put C# types inside that namespace. This package explicitly declares `namespace Railroader.AssetTools` for editor scripts and `namespace Railroader.AssetTools.Colorizer` for Colorizer helpers.

If the errors remain after refreshing, the highest-value next artifacts are **Unity Console errors** and `Railroader.AssetTools.Editor.csproj` from the actual project, not a screenshot containing only the VS Code error list.
