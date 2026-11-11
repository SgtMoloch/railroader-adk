# Beta 1.5.6 — Asset Pack Pull Reminder

When editing a definition in Railroader, **Save** writes the data to `Documents/Railroader/ModDev`, not directly into Unity's `Assets/AssetPacks`. If you build before pulling, you can overwrite those in-game edits. Beta 1.5.6 adds a reminder that checks **all packs currently loaded into the ADK**, not just the pack selected in the stock Asset Packs window.

## What it does

- Automatically scans after Unity opens, about every **30 seconds**, and shortly after Unity regains focus (for example, after editing in Railroader).
- Matches packs in ModDev and the ADK by their **`Info.json` identifier**, not by directory name.
- Detects **new or changed ModDev data files** (`.json`, `.riff`, `.rrhorn`, `.rrwhistle`, `.rl`) only when the ModDev version is newer than the ADK version and the contents are different. Ignores unchanged files, generated bundles, FBX, textures, materials, and `.meta` files.
- Lists each pack with outstanding changes and up to three example file names in a **Pull / Ignore** dialog.
- **Pull** asks for confirmation, backs up any destination files being overwritten to `Library/RailroaderADKTools/PullBackups`, then copies the changed data files from ModDev to the existing ADK pack and refreshes Unity's AssetDatabase once.
- **Ignore** remembers the set of changed ModDev files for this Unity project, even across Editor restarts. It prompts again when the outstanding ModDev data changes. A manual check always displays outstanding changes, even if previously ignored.
- Checks are skipped during compilation, Unity asset importing, and Play Mode. File-content comparison is cached by source/destination timestamps and sizes to reduce repeated IO.

## Menus

- **Railroader > Pull Reminder > Check Now** — manual check (also works after Ignore).
- **Railroader > Pull Reminder > Settings** — enable/disable auto reminders and set the exact ModDev directory.

**Important:** The reminder defaults to `Documents/Railroader/ModDev`. If you configured a different ModDev folder under the stock **Railroader > Asset Packs > Paths** UI, configure the **same directory** in Pull Reminder Settings; it cannot automatically read the stock ADK Paths setting.

## Safety and scope

- This is an independent **data-file-only pull**, not an invocation of the stock ADK's full per-pack Pull implementation. Use the stock ADK Pull if you need its full file synchronization behavior (such as deletes). The reminder deliberately does **not** mirror deletions or pull changes from the ADK back to ModDev.
- It skips conflicts where the **ADK file appears newer**, rather than overwriting possible unpublished local work. Review these manually in the stock Asset Packs window.
- An absent matching ModDev pack is ignored. Duplicate identifiers in ModDev are skipped with a Console warning to avoid choosing the wrong directory.
- Backups are outside `Assets` and do not change `.meta` GUIDs. If a copy fails after some files are copied, it reports an incomplete pull and the location of backups.
- The feature installs only a new Editor `.cs` script plus its `.meta`. It does not edit any existing importer, Material Builder, reimport, or prefab code.
