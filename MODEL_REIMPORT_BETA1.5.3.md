# Model Reimport — Beta 1.5.3 (built on Beta 1.5.2)

This is an experimental, non-destructive **existing model update** workflow. It is separate from the initial Asset Importer. It does **not** migrate or rebuild prefabs.

## What it does

- Open **Railroader → Model Reimport**, or press **Reimport an Existing FBX / Textures...** in the Asset Importer.
- Select an existing asset pack and its imported FBX (or OBJ).
- Select a newer FBX source file. The tool overwrites the **existing Unity asset path**, NOT a new path, preserving the existing `.meta` GUID, ModelImporter settings, and model-linked prefab variants. If the FBX has already been updated in place, selecting that same file path forces an import.
- Optionally enable a *scoped* Substance Painter texture reimport. Choose the base model or **one specific livery** referencing this FBX; an FBX update impacts ALL linked liveries, whereas a texture update affects only the chosen texture folder.
- By default, the tool does **not** regenerate `.mat` assets, adjust shader/color settings, remap FBX materials, save prefabs, change BoxColliders, change Colorizers, rewrite AnimationMaps, or modify `Definitions.json`.
- Optional **Update existing .mat** uses the existing Beta 1.5.2 texture material update service. This may modify shader/texture properties; leave it off for a pure pixel refresh with existing colors.
- Before writing, the tool backs up the model, all dependent prefab files, the textures that will be replaced, and (when material updates are enabled) existing material files to `<Unity project>/Library/RailroaderADKTools/ReimportBackups/...` including their `.meta` files. This is a recovery copy, not a substitute for a full project backup or version control.
- After importing, it compares object paths, mesh names, and material-slot names and warns about mismatches. It doesn't try to blindly copy components onto newly named meshes.

## Why this preserves the prefab

The existing ADK importer creates a root **Prefab Variant of the imported FBX Model Prefab**. Unity tracks that model through its imported-asset identity (its `.meta` GUID). Replacing the source *contents* at the same path allows Unity to rebuild the model while prefab overrides remain in the original `.prefab` asset. Root BoxColliders, added components, material assignments, and other authoring settings are not erased or programmatically re-created by this workflow. Existing authoring prefab assets are also not re-saved by this tool.

**Important:** This does NOT guarantee overrides survive changes in hierarchy. Renaming, splitting, removing, or re-parenting an FBX object; changing mesh/material slot names or order; or altering pivots/scales can detach prefab child overrides, animations, per-object Colorizers, or child colliders. Even when the hierarchy matches, geometric changes can make existing BoxColliders and placement values inappropriate. Unity may resolve imported mesh subasset references differently if the export structure changes.
