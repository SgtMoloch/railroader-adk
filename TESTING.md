# Beta 1.3 focused tests

1. Open Importer (both New FBX and New Livery); confirm no **Add prefab to scene** or scene-replacement toggles. Import into a test pack. Root prefab must be saved, but active scene hierarchy and dirty state must be untouched.
2. Create car and diesel starter definitions with optional **Base road number**, preserving leading zeros, and without it (no baseRoadNumber field). JSON preview should show the chosen value before writing.
3. Copy a source Car with `baseRoadNumber: "7703"` and one `CarStyle.carSeries` entry (`ACFR: 7703–9850`) into a new livery with all override fields blank. It inherits 7703 and the source's series. Existing distinct target CarStyle ranges must be preserved.
4. Copy source Car to new livery and enter **Base road number = 6101**, **Series name = TEST**, **start = 6101**, **end = 6199**. Verify destination Car has base 6101, destination CarStyle has TEST 6101–6199, and the source is unchanged.
5. Copy a Car with NO linked `CarStyle` or number series. Enter a new reporting mark, series name/start/end. Verify a new `ReportingMark` and `CarStyle` linked to the destination are created. Test copying only a base road number with no source CarStyle (no style should be created).
6. Try range override without series name on a source missing all series; expect a friendly popup and **no mutation or backup** (all changes are staged before writing). Try a partial range with no inherited endpoint; same result.
7. In Definition Starter's manual bulk copy, give two destinations *different* base/series overrides. Confirm values stay independent. Repeat copy and check no duplicate style entries.
8. Confirm styled destinations retain their own reporting mark; no unrelated CarStyles/ReportingMarks or Colorizer-generated entries change. Definition copy still creates a `.bak` before committing.
9. Intentionally trigger Colorizer **Remove ALL** while authoring Prefab Mode is open; check a dialog appears rather than a red Console stack trace. Then close Prefab Mode, retry, and verify behavior. Also trigger Importer invalid path and Material Builder missing files; verify dialogs.
10. Trigger a truly unexpected failure (if available in a safe test project) and confirm a Console exception still appears for diagnosis. Test Animation Builder, Material Builder and Colorizer normal operations for regression.
11. Check C# compilation in Unity and reload the VS Code solution; all original metadata GUIDs and assembly names should remain unchanged. In-game validate generated definitions when ready.

**This build is statically checked and packaged, not Unity-compiled.**

---

# Beta 1.2 compile-focused tests

1. Open the full Unity ADK project and check that all 19 toolkit C# files compile without errors.
2. Open Definition Starter and invoke preview; confirm no `accepted` name conflict or modal selection error.
3. Import or configure a truck with a `Truck` material and verify the MaterialMap helper compiles and preserves existing entries.
4. Reopen VS Code *after* Unity project regeneration; confirm `RailroaderStarterDefinitionKind`, `RailroaderAssetPackTools`, and `RailroaderDefinitionCopier` are resolved.
5. Verify that a legacy `RailroaderMaterialRecovery.cs` has been either removed (if unused) or updated to reference `Railroader.AssetTools`, and that no stale `Freeman.Colorizer` code is interfering.

---

# v3.0 Beta 1.1 — Compile and regression checklist

1. Make a copy/backup of the existing Railroader ADK project before upgrading scripts.
2. Copy the package `Assets` tree into the original **Unity ADK project root**. Do not open this ZIP folder by itself as a C# project; let Unity import and regenerate VS Code project files.
3. **Compile:** confirm Unity Console shows no errors in `ColorizerBaker.cs`, `RailroaderAnimationBuilder.cs`, `RailroaderDefinitionOptionsUI.cs` (notably `RailroaderStarterDefinitionKind`), or the other Editor assembly sources.
4. **Colorizer Baker:** generate colors on two distinct source materials sharing the same style class, confirm output assets get `body.mat`, `body-2.mat`, and baked metadata still resolves; test blacklist and marker removal.
5. **Animation Builder:** inspect an FBX with clips, add a map, validate existing `clipName` references, then repeat Apply to confirm mappings aren't duplicated.
6. **Definition Starter:** confirm Kind dropdown and all presets load; generate a new car and truck definition (including wheel references) with JSON preview.
7. **Importer:** import a new FBX as a prefab variant and add a livery sharing its FBX, verify material and animation mappings still work.
8. **Material Builder:** refresh an existing livery's textures and confirm no FBX remap or prefab recreation.
9. Confirm existing saved `CustomColorizer` markers and prefab assets still resolve after upgrade.

This beta has passed source/static checks, but **Unity compilation and game testing remain outstanding**. Please share exact Unity Console errors with line numbers if any remain.

---

# v3.0 Beta 1 — regression checklist

**Before upgrading:** back up the Unity project. This release is intended to be a *style-only* refactor of v2.9.2.

1. Import scripts over v2.9.2, keeping `.meta` file identities; confirm both authoring and editor assemblies compile with no missing components.
2. Open all four original tools plus Animation Builder from Railroader menu; confirm window drawing, UI state, and every command still responds.
3. Import a new FBX as a prefab variant and a second livery sharing it; ensure both retain their model materials and animations.
4. Refresh a livery texture set in Material Builder; check no FBX or prefab remapping occurs.
5. Generate a Truck and Diesel definition and preview JSON; confirm truck animation references, material mapping, and diesel fuel slot still match v2.9.2.
6. Copy settings and linked reporting marks/car styles between cars; verify destination-specific fields and existing styles are preserved.
7. Test Colorizer blacklist, batch marker removal, baking, and metadata generation; check existing serialized `CustomColorizer` instances remain valid.
8. Build a pack in Railroader and validate definitions and animations in-game.

**Not tested here:** Unity Editor compile or Railroader gameplay. The style pass is not a substitute for in-editor regression testing.

---

# v2.9.2 — Targeted texture refresh smoke tests

1. Open **Railroader → Material Builder**, ADK Asset Pack mode; verify target dropdown lists every base FBX and each `LiverySource.json` separately, even for liveries sharing one FBX. Verify UI scrolls on a small window.
2. In an existing base model, record GUID from `Body_BaseColor.png.meta` and material reference. Re-export modified Body maps. Refresh only that base model; verify source FBX bytes and prefab unchanged, texture bytes updated, `.meta` GUID unchanged, material still references the same texture asset.
3. In a pack with base `GP38.fbx` and liveries `GP38_CSX`, `GP38_NS`, re-export NS textures. Choose `Livery: GP38_NS`, preview and apply. Confirm CSX texture assets, base materials, FBX importer external object map, both prefab YAML files and all other liveries are unchanged. NS `.mat` updates are allowed when the checkbox is on, but NS prefab overrides stay the same.
4. Uncheck **Update existing .mat assets**, re-export different pixels using identical texture filenames, and refresh. Existing `.mat` YAML must remain unchanged, yet Unity preview should show updated pixels.
5. With **Update existing .mat assets** on, refresh the same export twice. The second run should report replacements or already-at-destination; no duplicate `.mat` files, new GUIDs for existing images, or unrelated asset changes.
6. Source contains `Body_BaseColor.png`, `Body_Normal.png`, `Body_MetalAOSm.png` plus unrelated `.psd`, `.fbx`, `.meta` and an unrecognized PNG. Only recognized images should import; preview should count skipped files.
7. Test a nested Substance export (`Body/Body_BaseColor.png`, `Doors/Doors_BaseColor.png`) and legacy `Model/Textures` pack. Verify target paths match the existing importer layout and do not touch neighboring models.
8. Attempt to replace existing `Body_BaseColor.png` with `Body_Diffuse.tga` in the same destination folder. The tool must **refuse before copying** with an explanatory message. Remove/rename the old map intentionally before changing workflows/extensions.
9. Cancel confirmation; no bytes or GUIDs changed. Select a nonexistent folder, one with only unrelated files, and a folder with duplicate map properties; verify clear errors and no asset writes.
10. After refresh, use full **Build / Update Materials** manually only when changed material names require it; verify old pack-wide operation still works unchanged.
11. Confirm the rest of v2.9.1: Animation Builder, Importer prefab variant/livery creation, Definition Starter/copy, Colorizer bake/blacklist still function. Compile in installed Unity ADK and rebuild a test asset pack for Railroader.

---

# v2.9.1 Linked CarStyle / ReportingMark regression checks

Use a copy of the example multi-livery Definitions.json (or another pack with a matching set of actual root prefabs) and back up the asset pack first.

1. **Existing style:** Source `example/athipd`, target `example/atsfipd`. Enable linked-style copying. `example/style.atsf`, `ATSF`, and the existing ATSF carSeries must remain unchanged in identity, mark and numbering. Its style rules should mirror the source's non-Renderer rules. No duplicate ReportingMark or CarStyle should appear.
2. **Missing style:** Source `example/athipd`, target `example/bmipd`, reporting mark `BM`. Confirm a new linked `CarStyle` with `carIdentifiers: ["example/bmipd"]` and `reportingMark: "BM"`, and an independently identified `ReportingMark` object for BM. Other entries remain intact.
3. **Repeat:** Run the same copy again. Do not generate a second BM style, ReportingMark, or change the existing target carSeries.
4. **Same reporting mark:** Copy into two different cars that both legitimately use `CPLT`. Ensure distinct CarStyle IDs, but only one namespace-local CPLT ReportingMark definition.
5. **Collision:** If `example/style.bm` already exists for a different car, a unique suffixed CarStyle ID must be generated. If `example/BM` is occupied by a non-ReportingMark kind, refuse safely.
6. **Safety:** Cancel confirmation; ensure Definitions.json bytes remain unchanged. Force an invalid mark or existing shared CarStyle; ensure no partial changes or backup/writes occur. Confirm a successful copy creates one `.bak` and one valid `{ "objects": [...] }` file.
7. **Importer:** Import a new livery with Copy settings enabled; choose source, select linked styles, enter a mark. The new definition should retain its own ID/model identifier; related objects should be added. Reimport should preserve those entries instead of replacing them.
8. **Compatibility:** Truck/diesel definition copying must still work without requiring marks. Colorizer-generated `assettools-colorizer-*` components should remain destination-owned. Animation Builder and existing prefabs must be unchanged from v2.9.

**Manual verification required:** Run Unity compilation, inspect definition JSON produced in Unity, build the pack and test styles/reporting marks in game. No Unity Editor available to run these tests here.

---

# v2.8.5 Blacklist Test Checklist

1. Open a fresh authoring prefab with renderers using `Body`, `Glass`, `Window` and `Mirror` materials.
2. Check the blacklist defaults and save; confirm the per-pack JSON file is created under `Authoring/`.
3. Run Auto-Assign by Material; confirm `Body` is marked and blacklisted slots aren't.
4. Temporarily remove `glass` from the blacklist and assign a marker to glass. Add `glass` back. Run Remove Blacklisted Markers; confirm only the glass marker is removed.
5. Test a single renderer with two material slots (one glass, one painted); only the blacklisted slot marker should be removed.
6. Test Apply Common Group and Reset to Material Groups. Neither should touch excluded slots.
7. Reopen the window and confirm saved blacklist is loaded. Test an empty blacklist.
8. Re-bake and verify the generated game-ready prefab and definitions exclude removed colorizer controls.
9. Verify existing import/new livery, definition-copy, Material Builder, and animation-prefab workflows still work.

**Note:** Removing authoring markers does not immediately update previously baked assets. Rebake and rebuild the pack.

---

# v2.8.4 Unity test checklist

Use a **disposable or backed-up** ADK asset pack. Do not test destructive copy on production definitions first.

1. **Compilation:** Import v2.8.4. Verify **Railroader → Definition Starter** and **Asset Importer** open without Console errors.
2. **Manual Car → Car:** Make two Car definitions and prefabs with different identifiers, names, and `modelIdentifier` values; add e.g. `weight` and an ordinary component to the first. Copy with the manual action. Verify target receives the new configuration but keeps its own `identifier`, `metadata.name`, `kind`, `modelIdentifier`.
3. **Idempotent:** Copy again. Verify the tool reports no changes and does not create an unnecessary backup.
4. **Multiple targets:** Add a third Car definition and repeat. All other eligible Cars update; an unrelated `Truck` and generated `CarStyle` do NOT update.
5. **Colorizer preservation:** On the target, add an `assettools-colorizer-*` RendererGroup and its `Renderer.body` rule, with a different source-generated RendererGroup on the source. After copying, verify the target Colorizer data remains and source Colorizer data does not leak across.
6. **Same-kind safeguard:** Try to copy Truck configuration to Car. Confirm manual action only includes Truck targets, and Importer lists only compatible kinds.
7. **Import checkbox OFF:** Import a new livery with starter-definition creation; ensure it stays a minimal starter with its own generated fields.
8. **Import checkbox ON:** Select a real source definition and import another livery. The new definition receives source settings but keeps the new ID, name and prefab reference. The source is unchanged.
9. **Reimport safety:** Reimport a livery whose definition already exists with the checkbox ON. Verify its existing configured entry is preserved.
10. **Malformed JSON:** Confirm a broken `Definitions.json` produces a useful error and is not overwritten. The manual copy should leave the file untouched.
11. **Backup:** For an actual manual change, ensure a `Definitions.json.before-copy-*.bak` file is created and JSON remains parseable.
12. **Runtime:** Build the pack in ADK and load it in Railroader. Check the copied car/truck definition in the in-game editor and test any copied animation/transform settings for compatibility.

This is source-reviewed but **not a verified in-game release**.

## v2.8.6 verification (Unity and Railroader)

1. Import a new truck FBX that contains `Wheel`, `Wheel.001`, etc. Ensure the preview contains each wheel path in `wheelTransforms`; adjust for nested children, axle count and wheel measurements. Cancel once and verify that no definition entry was written; import again and confirm.
2. With a material named `Truck`, confirm root prefab contains **one** `MaterialMap` with a `Truck` entry. Verify that `Definitions.json` has `components.renderergroup-1` referencing `Truck`. Import again and ensure no duplicate map entry, no duplicate definition.
3. Test a truck without a `Truck` material: preview should warn and omit the RendererGroup. Test a truck with an existing MaterialMap: do not replace existing map entries.
4. Test brake-animation checkbox on/off and custom clip name. With an existing `AnimationMap`, verify the name matches. This build does **not** add clips or create AnimationMap.
5. Import a diesel: preview should show `LD`, a 600-gallon `rr/diesel-fuel` slot (or edited capacity). Spawn a **fresh** locomotive, fill fuel as needed, release brakes and test movement.
6. Import a freight car and diesel. Confirm the root prefab has exactly one new BoxCollider matching mesh bounds and its layer says `Clickable`. Verify the imported FBX asset is not modified. Reimport: preserve existing BoxCollider.
7. Test a pack missing the `Clickable` layer; operation must warn/skip rather than choose an arbitrary numeric layer.
8. Test two liveries sharing an FBX: newly imported prefabs should retain distinct materials and independent collider/map overrides; the shared FBX material mappings must not be changed.
9. Confirm Definition Starter scrolls and its modal review supports lengthy JSON; check that existing definitions and manually authored fields are untouched.
10. Regression: v2.8.5 material blacklist, bulk marker removal, Colorizer Bake, v2.8.3 animated Prefab Variant, and v2.8.4 definition copying.

## v2.9 Animation Builder regression checklist

1. In a fresh pack import an FBX with multiple Generic NLA animation clips. With **Set up AnimationMap** enabled, verify that the generated prefab variant has one AnimationMap and one Animator **on the FBX root**, and the map references all genuine FBX clips (no `__preview__` clips).
2. Reopen Animation Builder, verify the discovered mappings appear as **Already mapped**; click Apply again. There should be zero duplicate entries and no modified existing names or clips.
3. Add one new NLA clip and reimport the FBX, then refresh the Builder. Only the new clip should be proposed. Apply and confirm the existing entries were preserved.
4. Rename an existing map key by hand in Unity; rerun. The existing name should survive. Create another clip whose name collides with the existing map key: verify the new suggestion is suffixed `(2)` and does not change the original mapping.
5. Add an empty AnimationMap entry manually. Select a clip and use the same key name; Apply should fill only the empty clip slot.
6. Import two liveries referencing the *same* FBX. Verify each root prefab has its own AnimationMap and Animator, both reference the same source AnimationClip assets, and editing one map doesn't modify the shared FBX or other livery's map.
7. For an old prefab with the FBX nested inside a wrapper, verify mapping and Animator land on the nested FBX model root, not the outer wrapper.
8. Build a truck with a missing animation clip. Importer should still add the required empty AnimationMap; Builder/Validate should report the unresolved brake clip without fabricating an animation.
9. Open one populated authoring prefab in Prefab Mode and attempt Apply from Builder. The operation should refuse without modifying the prefab. Close Prefab Mode and retry.
10. Intentionally add two AnimationMap components, or put an existing one on the wrong root. Builder must warn/refuse to edit without moving or deleting either component.
11. In Definitions.json use an existing brakeAnimation.clipName or ToggleAnimation clipName: Validate should report missing names, resolve valid ones and identify unreferenced mapped clips as informational. Confirm Definitions.json bytes remain unchanged after Apply/Validate.
12. Confirm clips marked Legacy are not automatically selected and are rejected if manually selected. Fix FBX Rig to Generic and retest.
13. Check long lists and narrow Unity windows: scrolling works and Apply/Validate footer remains reachable.
14. Smoke-test v2.8.6 Material Builder, Colorizer (including blacklist), Definition Starter, prefab variants, and import without animation checkbox checked.
15. Confirm Unity script compilation and ADK AnimationMap serialization. This package was not compiled in Unity; if an AnimationMap schema mismatch is reported, provide the actual ADK AnimationMap source for a precise adjustment.
