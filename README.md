## v3.0 Beta 1.3 updates

**Import:** Importer now creates or preserves the root prefab *asset only*. It does not add or replace scene instances. Use Unity to place an instance manually when needed.

**Road numbers when copying definitions:** In the Importer's **Copy settings from another model definition** section, optionally enter a base road number and, for cars, a series name/start/end. In **Definition Starter → Copy Configuration to Other Models**, the same overrides are available *for each destination*. Blank entries inherit from the source, and a destination's existing numbering series is kept unless you specifically override it. Road series belong to the separate `CarStyle.carSeries` object; base number belongs to the `Car`/`DieselLocomotive` definition. To add a range where none exists, provide a series name and both endpoints, and a reporting mark if a new CarStyle must be created. Re-copies still back up Definitions.json and stage changes before writing.

**New definitions:** Car and Diesel starter presets offer an optional base road number. The new range fields are for the *copy* workflows, because the initial bare Car stub does not create a CarStyle/ReportingMark automatically.

**Errors:** Expected editor setup/validation errors now use modal dialogs. Unexpected programming/Unity errors still produce Console stack traces for debugging.

---

> **v3.0 Beta 1.2:** compile repair for Definition Review and MaterialMap imports. Read [INSTALL_UPGRADE.md](INSTALL_UPGRADE.md) if VS Code reports unresolved toolkit types after upgrading.

# Railroader ADK Tools v3.0 Beta 1.2

Bug-fix and namespace follow-up to Beta 1, retaining the v2.9.2 behavior baseline.

**Fixes**

- Corrected the Colorizer Baker's style-name collision `do/while` loop, which was malformed by the Beta 1 formatter.
- Corrected `RailroaderAnimationService.Read` to declare the result of `SerializedObject.FindProperty(...)` as a `SerializedProperty`, not a `SerializedObject`.
- Added the `AssetPack.Common.Components` import for `AnimationMap` in the Animation Builder.
- Moved the previously global Editor utilities into `namespace Railroader.AssetTools`. The authoring `CustomColorizer` component remains in `Railroader.AssetTools.Colorizer`, with the same fields, class name and `.meta` GUID.
- Added an explicit import in the Colorizer blacklist so it resolves the shared `RailroaderAssetPackTools` helper in the parent namespace.
- Expanded an Animation Builder `using (EditorGUI.DisabledScope)` block for clearer control flow.

**VS Code / Unity:** Copy `Assets/` into your existing **Railroader ADK Unity project**, then open that **Unity project root** in VS Code (not just this extracted source package). Let Unity regenerate the C# solution/project files if necessary. The `.asmdef` `rootNamespace` field does **not** itself declare a C# namespace; Beta 1.1 now declares those namespaces explicitly. A plain folder containing only these scripts won't resolve Unity, Editor, or ADK assembly references by itself.

This package is **not independently compiled**; the official Railroader ADK project / Unity Editor is required for compilation. Check `TESTING.md` and back up your project before installing.

---

# Railroader ADK Tools v2.9.2 — Material Builder: Texture Refresh

This release adds a targeted Painter texture refresh without reimporting the FBX or rebuilding an existing prefab.

## Refresh textures on an existing model or livery

1. Export the updated texture set(s) from Substance 3D Painter, **keeping the same texture set/material names** as the original FBX/livery.
2. In Unity, choose **Railroader → Material Builder** and select **ADK Asset Pack** mode.
3. Select your pack, then under **Reimport Substance Painter Textures**, select a **Model** or **Livery** from the target dropdown. For multiple liveries sharing an FBX, choose the specific livery; do **not** choose its base model unless you intend to update that base model's textures.
4. Browse for the exported texture folder. Browse defaults to `Documents/Adobe/Adobe Substance 3D Painter/export` when present, otherwise Desktop.
5. Leave **Update existing .mat assets after copying** checked if shader/texture-property assignments may have changed; uncheck for a simple pixel-only refresh where existing material settings should remain untouched. **Either way, the FBX importer is not remapped.**
6. Click **Preview Texture Reimport** to inspect the target paths and replaced/new counts. Click **Reimport Textures for Selected Model / Livery**, review the confirmation, then accept.
7. Verify the model/livery's appearance in Unity, then rebuild the asset pack to see the update in Railroader.

### What gets changed

- Only recognized image maps (`.png`, `.jpg`, `.jpeg`, `.tga`) are copied into the **selected** model/livery's existing `Textures` folder. For example, `Body_BaseColor.png` is copied to `<target>/Textures/Body/Body_BaseColor.png`.
- An existing image is **overwritten at its path without changing its `.meta` file**. That keeps its Unity GUID and all references to it. New images are imported by Unity normally.
- The optional material update runs the existing material asset creation/update logic but deliberately does **not** remap the FBX, edit the prefab, regenerate the prefab, or create definitions.
- If material names change, new `.mat` assets might be created **without being assigned to renderers**. Run the separate **Build / Update Materials** action if you actually want to remap slots; that older action can affect the full pack.
- The refresh **does not delete** textures that are no longer exported. A conflicting map/extension in the same target folder is rejected with an explanatory error rather than silently choosing one.
- Folder layouts follow the existing Importer: direct `Body_BaseColor.png` goes into `Textures/Body/`, while images inside nested source directories keep their relative subfolders. The export must contain one version of each shader map per material.

### Which target to pick?

| Your situation | Choose in the refresh dropdown | Outcome |
| --- | --- | --- |
| Standalone model | `Model: Models/.../Car.fbx` | Updates only the base model's texture set |
| Truck pack with several FBXs | `Model: Models/.../TruckB.fbx` | Doesn't change TruckA's textures |
| Shared FBX with a fresh paint job | `Livery: GP38_NS (uses GP38.fbx)` | Updates GP38_NS's textures/materials only; other liveries are untouched |

A preview is recommended, particularly when selecting broad Painter export directories. Only recognized maps are imported; unrelated exports and `.meta` files are ignored. Existing Unity materials, animation mappings, Colorizers, prefab hierarchies, definitions, and imported FBX assets are preserved.

**Unity/test caveat:** This is an uncompiled Editor integration test build. Use a backed-up pack and confirm that the selected model's material names correspond to Painter's texture-set names.

---

# Railroader ADK Tools v2.9.1 — Linked style copying

v2.9.1 extends **Copy Configuration to Other Models** (Definition Starter) and the **Copy settings from another model definition** importer option. This responds to multi-livery packs where a car's decals copied successfully, but its separate `CarStyle` and `ReportingMark` definitions did not.

## What 2.9.1 copies

- The model-specific `Car` configuration still copies as before, preserving destination `identifier`, `modelIdentifier`, display name, description, and Colorizer-managed components/styles.
- With **Also copy linked CarStyles / ReportingMarks** selected, the tool looks for `CarStyle` entries whose `carIdentifiers` contains the source Car ID.
- If a target Car **has an existing CarStyle** (such as `example/atsfipd` in the supplied test file), its **existing reporting mark, style ID, name, and `carSeries` are retained**. General style rules are copied from the source; existing `Renderer.*` rules remain destination-specific.
- If the target has **no CarStyle** (such as `example/bmipd`), enter the intended reporting mark (e.g., `BM`) in the Definition Starter UI. The tool creates a unique `CarStyle` with its `carIdentifiers` pointing to that target, and creates or reuses a corresponding `ReportingMark` definition. The source's `styles`, `carSeries`, and other styling data provide an editable starting point.
- Existing `ReportingMark` definitions are never overwritten. It reuses an entry when the mark already exists in the same namespace.
- Multiple styles from the source can be cloned. If the destination already has *more* styles than the source or uses a CarStyle shared by multiple cars, the tool refuses the copy instead of ambiguously overwriting data.

**Important:** CarStyles include road-number ranges (`carSeries`) and visual rules. A *new* livery inherits the source's ranges and may not have correct numbering until you edit them. This release does **not** guess railroad reporting marks from FBX filenames, and it does **not** automatically modify unrelated definitions.

## Manual copy

1. Open **Railroader → Definition Starter** and choose the pack.
2. Pick the source Car in **Copy Configuration to Other Models**.
3. Leave **Also copy linked CarStyles / ReportingMarks** checked.
4. For each target without its own CarStyle, enter the reporting mark that livery should use. Targets with existing styles show their preserved marks instead.
5. Click **Copy Settings to Other Models of This Kind**, review confirmation, and confirm. A timestamped `.bak` of `Definitions.json` is written before changes.

Uncheck linked-style copying to retain the older **model configuration only** behavior. Non-Car kinds retain their previous behavior automatically.

## Import-time copy

In **Asset Importer**, check **Copy settings from another model definition** and choose a source Car. If that Car has linked styles, **Copy linked CarStyle / ReportingMark too** is on by default. **Enter the new livery's actual reporting mark** before importing. If you prefer to configure styles later, uncheck the linked copy option. The tool refuses an import missing the required mark rather than silently creating a style under the source railroad.

Importing a new prefab/definition copies linked entries only if the target definition did not already exist. For an existing livery, use the manual action.

## Example from a multi-livery example Definitions.json

- Source `example/athipd` has `example/style.cplt` linking to `example/athipd` and a `example/CPLT` ReportingMark.
- `example/atsfipd` already has a separate `example/style.atsf` and `example/ATSF`. Copying from CPLT to ATSF preserves ATSF's reporting mark and car-number series.
- `example/bmipd` has no CarStyle or ReportingMark. Enter `BM` as its reporting mark. The copier creates a separate `example/style.bm` (or collision-safe suffix), links it to `example/bmipd`, and creates `example/BM` unless one already exists.

This is an **uncompiled Unity test build**. Test on a backed-up pack and examine the generated JSON before building in Railroader.

---

# Railroader ADK Tools v2.8.5 — Colorizer Material Blacklist

**Unity integration test build.** Built from v2.8.4. This release modifies only Colorizer bulk authoring and adds a per-pack blacklist file; the baker/definition writer, Importer, Material Builder, and prefab workflow are unchanged.

## v2.8.5: Material Blacklist

A creator can bulk-assign colors to every eligible mesh and then clean up glass, windows, mirrors, etc. without deleting the authoring prefab.

In **Railroader → Colorizer Baker → Colorizer Markers → Material Blacklist**:

1. Add material-name fragments, one per line. Defaults: `glass`, `window`, `mirror`, `windshield`, `windscreen`, `transparent`.
2. Matching is **case-insensitive substring matching on the assigned Unity material name**, not the mesh name or style class. For example, `window` matches `WindowGlass` and `WindowFrame` (remove it if that is too broad).
3. Click **Save Material Blacklist** to save it in the current pack at `Authoring/ColorizerBlacklist.json`. The list is per asset pack and can be shared in source control.
4. **Auto-Assign by Material**, **Reset ALL to Material Groups**, and **Apply Common Group to ALL Parts** now skip matching slots. They automatically save your edited blacklist when run.
5. **Remove Blacklisted Markers** is for *after-the-fact cleanup*. It asks for confirmation and removes only the `CustomColorizer` components on matching material slots in the **authoring prefab**. If a Mesh Renderer has both `Body` and `Glass`, the `Body` marker stays. Meshes, materials, the FBX, animations, and other components are untouched.

**Important:** Removal does not reverse an existing bake immediately. After cleanup, rebake the Colorizer and rebuild the asset pack to refresh the in-game definition/prefab. If a material name has been changed in the *authoring* prefab, match the name currently assigned in that renderer slot. Manual marker creation in Unity is not blocked by the blacklist.

### Test checklist (blacklist)

- On a fresh pack, ensure the default blacklist appears. Add `HeadlightGlass`, save, close and reopen the Colorizer window, and verify the same entries load.
- Run Auto-Assign on a sample with `Body`, `WindowGlass`, `Mirror` and verify only `Body` is marked.
- Manually attach a marker to `WindowGlass`, then run **Remove Blacklisted Markers**. Verify only that marker disappears.
- On a single Mesh Renderer with `Element 0 = Body` and `Element 1 = Glass`, confirm only slot 1's marker is removed.
- Reset all groups; ensure blacklisted slots are skipped and existing blacklisted markers are untouched until explicit removal.
- Verify that an empty blacklist excludes nothing and that no custom markers on non-blacklisted slots are removed.

## What's new

### 1. Copy an existing definition to the other models

Open **Railroader → Definition Starter** and expand **Copy Configuration to Other Models**.

- The **first eligible model definition** in `Definitions.json` is the default source. You can choose another from the dropdown.
- Click **Copy Settings to Other Models of This Kind**. This updates all other **root-prefab** definitions of the *same* `kind` (e.g. Truck to Truck, Car to Car). It skips CarStyle and non-model entries.
- The action prompts for confirmation and creates a timestamped backup of `Definitions.json` before writing.
- It replaces destination **non-generated definition settings** with the source's settings: e.g. car dimensions, wheel/truck configuration, animation settings and ordinary components. Review these if the models differ mechanically.

**These destination-specific properties are NOT copied from the source:**

- Top-level `identifier` (always kept)
- `metadata.name` (always kept; other source metadata may copy)
- `definition.kind` (must already match)
- `definition.modelIdentifier` (always kept)
- Existing generated Colorizer components (`assettools-colorizer-*`) and their associated `Renderer.<styleClass>` style rules
- Separate generated `CarStyle` entries (untouched)

Unrelated top-level properties of the destination entry are also preserved. This is intended for multiple liveries and physically identical truck models; avoid applying it to fundamentally different car types. The action **replaces** existing destination non-generated settings—backup/confirmation are intentional.

### 2. Copy on import (optional)

In **Railroader → Asset Importer**, under **Definitions**, check **Copy settings from another model definition**. This option is **OFF by default**. It appears when there is an existing definition of the selected kind.

1. Choose **Import New FBX** or **New Livery (Reuse FBX)**.
2. Enable **Add starter definition for imported prefab (if missing)**.
3. Set **Definition kind** to match the desired source.
4. Check **Copy settings from another model definition** and choose **Copy from**.
5. Import. The tool creates the new prefab and definition, then clones the source configuration into **the new definition only**.

An **existing destination definition is never overwritten by the import checkbox**. Use the explicit manual copy action for that. If no compatible existing definition exists, the new starter entry remains a stub.

### Sample definition container

```json
{
  "objects": [
    { "identifier": "demo/GP38", "metadata": { "name": "GP38" },
      "definition": { "kind": "DieselLocomotive", "modelIdentifier": "./GP38", "weight": 200000 } },
    { "identifier": "demo/GP38_CSX", "metadata": { "name": "GP38 CSX" },
      "definition": { "kind": "DieselLocomotive", "modelIdentifier": "./GP38_CSX" } }
  ]
}
```

After copying GP38 → GP38_CSX, the second entry gains `weight: 200000` but retains its own identifier, display name, kind, and `modelIdentifier`. This example illustrates copy behavior, **not** a complete playable locomotive definition.

## Retained from v2.8.3

- Multi-model import into one pack
- Shared-FBX livery import with separate material overrides
- FBX-instance-based **Prefab Variant** creation
- Definition Starter with `{ "objects": [...] }` containers
- Material Builder auto metallic/specular shader selection
- Colorizer v2.7.4 behavior
- Scrollable editor windows

## Installing

Extract this ZIP over the existing v2.8.3 scripts, preserving existing `.meta` files. The **new** `RailroaderDefinitionCopier.cs` and `.meta` must be placed alongside `RailroaderStarterDefinitions.cs` under `Assets/Scripts/AssetPack/Editor/AssetTools/`.

Do not install duplicate copies of the scripts. Back up your Unity project / asset pack before doing test imports.

## Limitations

- This package was checked structurally but **not compiled or run in the Railroader ADK Unity Editor**.
- Copies configuration JSON; it does not make different models geometrically or mechanically equivalent.
- It does **not** automatically rewrite arbitrary references to the source car or to referenced child assets in copied fields. Carefully review the copied definition, especially wheel transforms, model-specific animation names, and dependencies.
- It uses the object-container definition format required by the Railroader 0.6 game error encountered during v2.8 testing.

## v2.8.6: New definition and prefab defaults

**Definition Starter** and **Asset Importer** now display a scrollable JSON preview before creating a new definition. Confirm to create; cancel to skip the JSON entry. Existing definitions are never overwritten. The `Definitions.json` root remains `{ "objects": [...] }` for the current game build.

**Trucks:** choose number of axles (default 2), wheel diameter in meters (1.0 example), wheelbase in meters (2.8 example), and optionally specify one wheel-transform path per line using `/` as the hierarchy separator. Leaving the path box empty auto-detects transform names beginning with `Wheel` from the prefab. Default brake animation is ON; use a clip name already present in the prefab's `AnimationMap` (the FBX root name is used if blank). Do not assume detected wheel paths or example dimensions are correct on every truck.

**Truck recoloring:** the default-on checkbox adds the `Truck` RendererGroup to the new definition and a `Truck` entry to the root prefab's `MaterialMap`, if a material named `Truck` (or containing that word) is available. Existing maps and entries are preserved. If no matching material exists, the color group is omitted with a review warning. Other existing authoring/Colorizer features are unchanged.

**Diesels:** adds `carType: "LD"` and a `rr/diesel-fuel` slot in `Gallons`, with a default capacity of 600 gallons. This is not starting fuel. Diesel dimensions, truck identifier, weight and traction settings are still model-specific.

**Car/diesel prefabs:** on import or through Definition Starter, default-on prefab setup adds a root `BoxCollider` encompassing all mesh geometry, and sets the root object to the `Clickable` Unity layer. A root collider already present is preserved; the existing FBX source is never changed. Missing `Clickable` layer produces a warning, not a guessed layer number. Disable the option if your model requires hand-shaped colliders instead.

**Steam:** remains a basic starter. Steam needs wheelset, tender and boiler authoring that cannot reliably be inferred from a generic FBX.

**Out of scope:** importing animation clips and configuring `AnimationMap` (planned for 2.9). A brake animation reference without the matching map/clip will not function. See `TESTING.md` for the test flow.

## v2.9 — Animation Builder

Use **Railroader → Animation Builder** to automate animation *asset mapping* without duplicating the in-game Definition Editor's gameplay configuration.

1. Pick an existing ADK asset pack and one of its **root-level prefabs**. New v2.8.3+ FBX-based variants are supported, as are older wrapper prefabs with nested FBX objects.
2. The builder finds the imported FBX source and enumerates its clips. FBX preview clips are ignored. Existing AnimationMap entries are shown separately and **preserved**.
3. Select the additional clips you want to map; edit their friendly names if needed. Optionally use the **Add external clip** object picker for a custom `.anim` or clip from another imported model. Use **Apply AnimationMap** to create/append the map and ensure an Animator.
4. Use **Validate** to check clip names already referenced in the selected model's `Definitions.json` entry. Missing entries are reported; unreferenced map entries are informational, since they may be used by scripts or authored later.
5. Build the asset pack using the normal **Railroader → Asset Packs** window and test in-game. The importer now offers **Set up AnimationMap from imported FBX clips** (on by default) after creating a root prefab, including new liveries that share one FBX.

### Design boundaries

- Railroader's docs say **AnimationMap and Animator must be on the animated FBX model root**, which is not always the outer root in legacy wrapper prefabs. A prefab variant made from the FBX typically has those at the root already. Existing maps located on a different object are **not** relocated automatically; fix in Unity first to avoid losing manually authored entries.
- **Trucks require an AnimationMap**: import creates one even if no clips were found, but you must supply the actual wheel/brake clips and ensure the definition references correct names. Wheels' `wheelTransforms` and runtime wheel spin are separate from mapping clips.
- The builder does **not** create or replace definitions, ToggleAnimation/PropertyAnimation/Brake components, Animator Controllers, or animation curves. Set up gameplay in the in-game editor as before.
- **Existing populated mappings are never overwritten automatically.** New clips whose names collide with existing map keys receive an editable suffix such as `(2)`. Edit existing entries directly in Unity if you intend to replace their clips. Empty entries can be repaired by choosing their exact existing name.
- Clip playback requires Unity Generic (non-Legacy) FBX rig/animation import, correct Blender NLA export settings, and curves relative to the map GameObject. The builder warns about legacy clips and excludes them from automatic selection.
- Always save and exit Prefab Mode before bulk editing; the builder blocks changes while a prefab stage is open to prevent stale Prefab Mode changes overwriting edits.
- Existing user-defined `Definitions.json` values are not modified. Validate recognizes `{"objects":[...]}` and matches `./PrefabName` or this pack's domain-qualified model identifier.

**This ZIP is an uncompiled Unity integration test build.** Back up test asset packs and check the Console. If the ADK's `AnimationMap` differs from the expected serialized list of `(name, AnimationClip)` pairs, the builder reports an error instead of silently guessing and rewriting it.
