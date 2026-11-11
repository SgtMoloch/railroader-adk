# Beta 1.5.7 — Colorizer Bake prefab preservation

Previous bakes **replaced** the game-ready prefab with a clone of the `Authoring/` prefab. If the game-ready prefab had collider objects or other components added after the Authoring copy was created, these were silently removed during a later bake.

## New behavior

- `Authoring/` supplies `CustomColorizer` markers and the source materials only.
- Bake loads the existing game-facing root `.prefab` using `PrefabUtility.LoadPrefabContents`, updates the matched renderer material slots and owned `MaterialMap` entries, and saves that same prefab.
- Does not create or replace the game prefab hierarchy from the authoring copy. Existing colliders, animation components, user-added GameObjects, transforms and other authored components are kept.
- Renderer matching is by named hierarchy path (disambiguating repeated sibling names) and renderer component type. A missing renderer, changed mesh or missing material slot stops the bake before writing assets, instead of applying material changes to the wrong object.
- Repeated bakes replace only the tool's **own** generated MaterialMap entries. Other entries remain untouched.
- Collider components are snapshotted before baking and compared both before saving **and after the saved prefab is reloaded**. If any collider is missing, added or unexpectedly modified, the bake fails and restores file backups.
- The existing generated-material and definition backup/rollback logic is retained.

This does **not** repair colliders already lost in a previous build. Restore them from an earlier prefab backup or recreate them once before using the updated Bake.
