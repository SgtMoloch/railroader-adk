# Beta 1.5.8 — New Livery Collider and Clickable Inheritance

In **Railroader > Asset Importer > New Livery (Reuse FBX)**, the new **Copy colliders and Clickable layers from an existing prefab** option is enabled by default.

1. Select the shared FBX as before.
2. Choose a **Copy from prefab** source. The selector lists root prefabs referencing the same FBX, preferring a base model prefab over a registered secondary livery.
3. Import a uniquely named livery. It still becomes a **variant of the shared FBX**, not a variant of the source prefab.
4. On first import, the tool copies all `Collider` components from the source prefab, including multiple colliders on one object and colliders on extra child objects. It copies collider serialized settings (BoxCollider dimensions, triggers, PhysicMaterial, MeshCollider mesh, etc.) and matching GameObject layers such as `Clickable`. It also creates empty collider-holder GameObjects where they do not exist on the new variant.
5. Materials, renderers, Colorizer components, animation maps, and other source-prefab components are **not** copied. The new livery can still use its own texture materials.

The old **Add model-sized BoxCollider + Clickable layer** control is hidden for **New Livery**. The existing generic setup remains available for **Import New FBX** and **Definition Starter**. Starter-definition creation is prevented from adding a second generic collider to new liveries.

## Safety / limitations

- Copying happens **only when creating a new livery**. Re-importing an existing registered livery preserves its own colliders and layers rather than resetting to the source.
- Source and destination must reference the **same original FBX/OBJ**, and the selected source must be an FBX-based **Prefab Variant**. Legacy regular prefabs containing a separate nested FBX are rejected because their transform paths are not equivalent.
- Colliders belonging to the source's extra GameObjects are copied to new **empty** GameObjects, not full clones of source objects. That avoids unintentionally importing source material/rendering/Colorizer overrides.
- Colliders are copied even if a sibling collider already exists: same-type collider slots are matched by ordinal to avoid accidental duplicates. Collider settings are synchronized for the new prefab.
- Newly created livery prefab is removed if the initial collider copying fails. Already existing registered liveries are not altered by this copying step.
- Editing the base prefab *after* creating the livery does **not** automatically propagate independent collider overrides; this is a one-time copy.

