# v3.0 Beta 1.5.8 — Livery collider and Clickable inheritance

- The **New Livery (Reuse FBX)** importer can copy the selected existing model prefab's collider hierarchy and Clickable layers by default. Users may select a source prefab referencing the same FBX.
- Copies all 3D Collider components and their serialized settings, creating missing empty collider holders and preserving the livery's own FBX/material setup. No source renderers, Colorizer data, or gameplay components are cloned.
- Hides **Add model-sized BoxCollider + Clickable layer** for livery imports, and prevents Definition Starter from adding a generic collider afterward. New-model and standalone Definition Starter workflows are unchanged.
- Existing livery collider edits are preserved on reimport. See `LIVERY_COLLIDERS_BETA1.5.8.md`

---

# v3.0 Beta 1.5.7 — Colorizer prefab and collider preservation

- Fixed a destructive Colorizer Bake workflow: the game prefab is no longer replaced by an Authoring/ clone, which could remove collider GameObjects added directly to the game prefab since the authoring copy was made.
- Bake now loads and edits the **existing** game prefab, copies only colorizer material assignments/owned MaterialMap entries from the authoring markers, and saves it in place.
- Adds explicit renderer/mesh matching, repeat-bake-safe generated MaterialMap entries, and a collider snapshot check before and after save. Unexpected collider changes roll back via backups.
- Unrelated prefabs, Model Reimport, Material Builder, Importer, and Global Pull Reminder behavior remain unchanged.
- Existing lost colliders are not reconstructed automatically; restore/recreate them before rebaking. See `COLORIZER_BAKE_BETA1.5.7.md`.

---

# v3.0 Beta 1.5.6 — Global ModDev pull reminder

- Added **Railroader > Pull Reminder > Check Now / Settings**. Automatically checks all locally loaded ADK asset packs for newer, differing ModDev data files when returning to Unity and roughly every 30 seconds.
- Uses `Info.json` identifiers to match packs across directories; shows each affected pack in a **Pull / Ignore** dialog and remembers ignored changes across sessions.
- Pull makes backups of overwritten ADK files under `Library/RailroaderADKTools/PullBackups`, copies only new/updated loose data files (`.json`, `.riff`, `.rrhorn`, `.rrwhistle`, `.rl`), then refreshes Unity. ADK-newer files are never overwritten.
- Default ModDev path is Documents/Railroader/ModDev; configure the same custom path if the stock ADK uses a different one.
- Adds only one Editor `.cs` file plus `.meta`; all Beta 1.5.5 import/reimport functionality remains unchanged. See `PULL_REMINDER_BETA1.5.6.md`

---

# v3.0 Beta 1.5.5 — Model Reimport browse locations

- **Updated FBX Browse:** Starts on the current user's Desktop when no previously chosen valid model source exists, matching **Asset Importer → Import New FBX**.
- **Painter export Browse:** Starts in the user's `Documents/Adobe/Adobe Substance 3D Painter/export` folder when available (otherwise Desktop), matching the Asset Importer texture picker.
- Keeps the user's previously selected valid source file/folder as the next browse location. The existing model's location inside the Unity project is no longer used as the default browse folder.
- Uses the same directory-resolving functions as Asset Importer; the Model Reimport service now accepts directory fallbacks as well as file fallbacks.
- No changes to asset reimport, texture copying, backups, prefab/model references, `.meta` files, other editor tools, or the Beta 1.5.4 importer performance optimizations.

---

# v3.0 Beta 1.5.4 — Asset Importer GUI performance

- Stop recursively scanning every asset pack and reading every Info.json on each editor repaint. The Asset Pack picker now caches its pack paths and display labels.
- Cache per-pack imported FBX/OBJ model lists rather than running `AssetDatabase.FindAssets("t:Model")` on each redraw. Unity project changes or the existing picker **Refresh** button invalidate the shared caches.
- Only discover existing FBX/OBJ models when the **New Livery (Reuse FBX)** mode actually needs them; the **Import New FBX** mode no longer scans for models.
- Cache the Asset Importer's copy-source list and the selected car's linked-CarStyle lookup. Changes to the selected pack/kind, `Definitions.json` modification timestamp/length, or Unity project changes invalidate this cache.
- Import actions still use **fresh** definition reads and still perform the same file copying and synchronous Unity asset refresh. Cache invalidation is explicit after imports.
- Does not modify Material Builder material generation, model reimport, prefab/definition logic, or any gameplay/authoring components.
- These changes target **idle GUI CPU use**; high CPU during an actual synchronous Unity import may still be normal. 

---

# v3.0 Beta 1.5.3 — In-place FBX / Texture Reimport

- Added **Railroader → Model Reimport** with an Asset Importer shortcut.
- Keeps the existing imported FBX asset path and `.meta` GUID; **no prefab recreation or authoring prefab edits**.
- Optional model/livery-scoped Painter texture refresh using the existing refresh service.
- Defaults to preserving all `.mat` settings and prefab overrides; can optionally refresh `.mat` assets.
- Previews linked prefabs, backs up touched files outside Assets, and warns if the FBX object/mesh/material names change.

See `MODEL_REIMPORT_BETA1.5.3.md`.

---

# v3.0 Beta 1.5.2 — Shorter style IDs and clean livery names

- **No duplicate reporting marks in new CarStyle IDs.** The ID generator removes the destination's reporting mark from the beginning of the sanitized model identifier before appending it, e.g. `freeman/bnsfh351` + `BNSF` now generates `freeman/style.h351bnsf`, not `freeman/style.bnsfh351bnsf`. It also avoids appending the same mark again when the model name already ends with it. The existing model-specific collision numbering (`-2`, `-3`, ...) still applies.
- **No automatic `_Livery` name.** Switching to **New Livery (Reuse FBX)** now starts with a blank editable name rather than `<fbx>_Livery`. The user enters an intentional unique name because `<fbx>.prefab` is normally already taken by the original model. Generated root prefab, model-folder, and definition names follow the entered name without an added suffix.
- **Backwards compatible:** Existing prefabs, filenames, `.meta` GUIDs, `LiverySource.json` manifests, definitions, and style IDs are never renamed. Re-running a copy leaves existing style IDs untouched. The `_Livery` removal applies to future generated names only; it does not strip characters that creators intentionally enter.

---

# v3.0 Beta 1.5.1 — Independent reporting marks and model-specific style IDs

- **Create marks without a source CarStyle:** In Asset Importer and Definition Starter, enable linked-style copying/creation and enter a destination reporting mark. If the source has no linked CarStyle and the destination has none, the copy now creates a minimal CarStyle **and** ReportingMark even with no numbering-range or decal-finish overrides. Leaving the mark blank still copies only the car definition. Existing destination styles are left alone.
- **Model-prefixed new style identifiers:** Newly created style IDs use `<namespace>/style.<destination-car-name><reporting-mark>`, e.g. `freeman/style.ipdboxcaratsf` instead of `freeman/style.atsf`. Two different car models can share `ATSF` without reusing the same style identifier; a numeric `-2`, `-3`, ... suffix handles true collisions. Existing CarStyle IDs are not renamed or migrated.
- **Keep reporting marks shared:** The separate `ReportingMark` object (`freeman/ATSF`, for example) is still reused for the same mark in a pack; only the CarStyle IDs need to be model-specific.
- **Importer validation:** Invalid marks are rejected before source files are copied; numbering/decal overrides can still use the entered destination mark even when the linked-style copy option is disabled.


---

# v3.0 Beta 1.5 — Material Builder tabs

- Split **Railroader → Material Builder** into two top-level tabs: **Build / Map Materials** and **Reimport Textures**.
- The Build / Map Materials tab retains the existing **ADK Asset Pack / Manual** modes, shader selection, preview and material-building actions.
- Reimport Textures now has its own asset-pack selector, model/livery target selector, Substance Painter folder browse, preview, and reimport action. The material shader selector appears on this tab when **Update existing .mat assets** is checked.
- The selected asset pack and shader settings are shared across both tabs; status messages are kept separate so a build result does not appear under Reimport (or vice versa).
- The tab bar stays visible at the top, and each tab has independent scrolling. This is a UI-only change: no FBX import/remap, prefab, definition, material-service or texture-copy logic was modified.

---

# v3.0 Beta 1.4 — Numbering UI and decal finish

- **Single Base road number field in the Asset Importer:** removed the duplicate copy-only text box. The existing optional field in the definition defaults section now also acts as the copy override (blank means inherit the source). The Definition Starter's separate new-definition form and per-destination bulk-copy fields remain intentionally distinct.
- **Clarified numbering series key:** the UI now calls it **Numbering series key**, e.g. `ACFR` in `carSeries: {"ACFR": {"start":"7703","end":"9850"}}`. This is not the ReportingMark field; when one existing series exists, leave the key empty to update it. With no existing series, provide a key, start, end, and reporting mark.
- **Optional decal finish override** during import-time or manual Car definition copying. When enabled, an editable `#RRGGBB` color plus a 0.0–1.0 shininess slider produce `CarStyle.definition.styles.Decal.color: "smooth(#222222, 0.0)"` (the default preview). The numeric fraction is serialized with invariant decimal separator; always uses `smooth(...)`.
- **Safe by default:** finish override is OFF, preserving the source/destination style behavior from Beta 1.3. If enabled, overwrites ONLY the `color` field of the `Decal` style class, preserving other Decal fields, other style classes, target-specific reporting marks and numbering series. If a style is missing, can create a minimal linked CarStyle/ReportingMark when given a valid mark, even if the source has none. Invalid colors and out-of-range numbers fail before files are written.
- **Scope:** The `Decal` style class may affect both reporting-mark and road-number decals, not individual physical materials. This does not generate other gameplay components or rewrite existing prefab assets.
- No change to Colorizer, Animation Builder, material mappings, prefab variants, or public Unity-serialized components.

---

# v3.0 Beta 1.3 — Import, Road Numbers & User-Friendly Errors

- **Importer no longer adds prefabs to the active scene.** Removed the scene-placement/replacement checkboxes, call and now-unused helper. It still saves an FBX-based Prefab Variant in the chosen pack.
- **Road-number overrides during definition copying:** optional base road number (Car/DieselLocomotive), and optional CarStyle series name/start/end for Cars. Leave fields blank to inherit source values. Existing destination-specific CarStyle series are preserved; if missing, source ranges are inherited. If the source has no range, supply a series name and both endpoints to add one.
- **Manual bulk copy:** Road-number fields are per destination, so each repaint can get a different base number and car series. A minimal CarStyle and ReportingMark can be generated when the source has no style, if a new range and valid reporting mark are provided.
- **Importer auto-copy:** Same optional overrides for newly imported models/liveries; existing definitions remain protected.
- **Starter definitions:** Optional base road number on new Car/Diesel entries; blank omits the field.
- **Editor dialogs, first pass:** Expected user-correctable errors in Importer, Material Builder, Animation Builder, Definition Starter and Colorizer show explanatory popups rather than red `Debug.LogException` spam. Unexpected exceptions still log full details to Console *and* show a dialog. No changes to underlying exception propagation in helper methods.
- No changes to Unity authoring component names/serialized fields or existing `.meta` GUIDs. No game or Unity compilation available in this environment.

---

# v3.0 Beta 1.2 — Compile Repair / Installation Sync

## Source fixes

- `RailroaderDefinitionReview.cs`: made `accepted` an **instance** field; removed the conflicting local declaration in `Confirm`. Fixes CS0844 and CS0176.
- `RailroaderPrefabSetup.cs`: added `using AssetPack.Common.Components;` so `MaterialMap` and its `MapEntry` type resolve (CS0246).
- Updated visible editor version labels to Beta 1.2.

## Issues in the supplied screenshot that are *not* reproduced by this archive

- `RailroaderAssetImporter.cs` references classes that already exist in the **same** `Railroader.AssetTools` namespace and assembly in Beta 1.1. If they remain unresolved, check whether all 19 toolkit C# files are actually present in the Unity project and included in the regenerated `Railroader.AssetTools.Editor.csproj`.
- `RailroaderMaterialRecovery.cs` is **not shipped** in Beta 1.1 or Beta 1.2. It is a leftover or independently added source file in the user's project. If keeping it, its references to `RailroaderAssetPackTools` now need `using Railroader.AssetTools;`, unless that script has been superseded and can be removed after confirming it is unused.
- Old `Freeman.Colorizer.*.asmdef` assemblies are not part of this toolkit. Only remove them if their scripts are truly superseded and no other assets depend on them.

---

# Railroader ADK Tools — changelog

## v3.0 Beta 1.1 — Compiler fixes and Editor namespaces

- Repaired invalid `do/while` in `ColorizerBaker.GatherTargets` introduced by Beta 1's automated brace pass; original material-name collision behavior retained.
- Repaired `SerializedObject`/`SerializedProperty` type mismatch in Animation Builder's map reader.
- Imported `AssetPack.Common.Components` in Animation Builder, to resolve the ADK's `AnimationMap` component.
- Declared `namespace Railroader.AssetTools` consistently across the 12 editor utility files; the `.asmdef` `rootNamespace` previously did not do this automatically. Corrected Colorizer blacklist's reference to parent namespace helper.
- Preserved EditorWindow class names, source filenames, Unity GUIDs, authoring component namespaces, serialized field names, prefab workflows and definition semantics.


# Railroader ADK Tools — changelog

## v3.0 Beta 1 — Generic naming and coding style


# v2.9.2 — Targeted Substance Painter texture refresh

- Added **Reimport Substance Painter Textures** to **Railroader → Material Builder** in ADK Asset Pack mode.
- Target selector lists each imported **base model** and each **livery** separately. Shared-FBX liveries are updated independently using their `LiverySource.json` manifest and `Models/<livery>/Textures` folder.
- Select an updated Painter export folder. Preview lists all supported image maps, the destination, and how many will be overwritten or newly added. Apply requires confirmation.
- Copy/overwrite recognized image maps **in place**, preserving existing Unity `.meta` GUIDs. Do not copy `.meta`, `.fbx`, `.obj`, `.mat`, or any unrelated source file. Never rebuild the prefab, modify `Definitions.json`, or touch the FBX importer/remappings.
- Optional **Update existing .mat assets after copying** (default on) refreshes shader/texture properties using existing Material Builder logic, **without** FBX remaps or livery prefab overrides. New materials may be generated but are not assigned to model slots until the existing Build / Update Materials action is deliberately run.
- Source browse defaults to the installed user's Substance 3D Painter export folder, with Desktop fallback. Filenames and nested folders follow existing Asset Importer conventions.
- Prevent conflicting texture-property replacements (e.g. `Body_BaseColor.png` beside `Body_Diffuse.tga` in the same target folder), duplicate exports, and directory escape paths. No automatic deletion of old textures.
- No changes to Importer, Definition Starter, definition copying, Animation Builder, Colorizer, or the existing full-pack Build Materials path.

# v2.9.1 — Linked CarStyle & ReportingMark copying

- Extended the v2.8.4 manual and import-time definition copier: optional linked `CarStyle` and `ReportingMark` handling when copying `Car` definitions.
- Source `CarStyle` links are discovered via `definition.carIdentifiers`. New style entries get unique namespaced identifiers, the target car identifier and the selected target reporting mark; missing `ReportingMark` entries are created or existing ones reused.
- Existing destination CarStyles retain their own identifier, reporting mark, style name, car-number series and target-specific `Renderer.*` overrides, while inheriting source visual style rules.
- New destination styles inherit the source style settings AND `carSeries`, which must be reviewed in the in-game editor for that railroad. Reporting marks are explicit per target rather than silently copied.
- Full JSON copy is staged in memory, validated, and written once after a timestamped backup; re-running does not create duplicate linked style definitions.
- No changes to Animation Builder, Colorizer, or other v2.9 functionality. Unity compilation/in-game behavior not verified.

# Railroader ADK Tools — changelog

## v2.8.6 — Definition presets and prefab interaction

- Truck starter now writes editable `numberOfAxles` (default 2), `diameter` (default 1.0 m), `length` (default 2.8 m), and `wheelTransforms`. Wheel paths are detected from prefab transforms named `Wheel...`, or supplied manually as newline-separated relative hierarchy paths (slash between levels).
- Truck brake animation reference is enabled by default; clip name uses the shared FBX root name unless overridden. This references an existing clip; **v2.8.6 does not create AnimationMap or import clips** (scheduled for 2.9). Check the matching AnimationMap in your truck prefab.
- Optional truck RendererGroup (default on): `styleClass: truck`, `materialName: Truck`. The importer/definition starter adds or reuses a **single** MaterialMap entry `Truck` pointing to a material named `Truck`. If it cannot find a `Truck` material it omits the RendererGroup rather than producing a dangling reference.
- Diesel starter now adds `carType: LD` and a default **600-gallon** `rr/diesel-fuel` load slot, with editable fuel capacity. This does not fill the tank automatically.
- Definition Starter and Importer show a **scrollable modal JSON review** before writing a new definition. Existing definition IDs remain untouched, consistent with v2.8.5.
- New car and diesel prefab imports (and manually created definitions) optionally add a root BoxCollider based on combined local mesh bounds and set its layer to `Clickable` (default on). Existing root BoxColliders are not changed. If the Unity Clickable layer is missing, setup is skipped with a warning rather than guessing another layer.
- Steam definition generation stays as a basic starter. Tender, wheelset, and steam animation automation are out of scope.
- No changes to prefab-variant creation, multi-model/livery support, Colorizer bake, blacklist, or definition-copying logic.

Known limitations:  Defaults for truck geometry must be reviewed. Auto wheel-path matching is best effort and requires inspection on complex hierarchies. Root collider bounds include every rendered mesh (including inactive). If a review is canceled, assets already imported remain, but that definition entry is not written.

## v2.9 — Animation Builder

- Added **Railroader → Animation Builder** (separate from Material Builder and Colorizer).
- Discover imported FBX animation clips from the selected root prefab's source FBX, excluding preview clips; allow an additional `.anim` or FBX clip to be selected manually.
- Propose a friendly AnimationMap name for each unmapped clip; allow selecting/skipping/renaming suggestions in a scrollable window with a fixed Apply footer.
- Create or update the prefab's AnimationMap on the **animated FBX model root**, and ensure an Animator exists on that same GameObject. Does not create an Animator Controller.
- Reruns append only previously unmapped clips; preserve non-empty existing mappings, names, and clips. Can fill an existing empty entry when a proposed name exactly matches. Never silently move an existing AnimationMap from the wrong root or replace duplicate maps.
- New Asset Importer default-on option: set up AnimationMap after prefab creation, both for new FBXs and shared-FBX liveries. Creates an empty required map for trucks without detected clips; cars/diesels without clips are not assigned empty new maps.
- Read-only definition validation for the selected root prefab: check its existing `clipName` references against the map, report empty or duplicate entries, and list unreferenced clips as informational. No definition writes.
- Reject Legacy clips when applying; recommend Generic rig import. Continue to support the working v2.8.3 prefab-variant hierarchy.
- v2.8.6 definition presets, material tools, blacklist, and Colorizer bake are unchanged.