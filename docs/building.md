# Building and sharing Galaxus

Players don't need Godot or .NET. Each build bundles the .NET runtime.

## Exporting

You need the Godot 4.7.2 .NET editor, the .NET 10 SDK, and the matching .NET export templates. Download `Godot_v4.7.2-stable_mono_export_templates.tpz` from the Godot 4.7.2 release on GitHub, unzip it, and put the contents of its `templates` folder in `~/Library/Application Support/Godot/export_templates/4.7.2.stable.mono/` (macOS; the folder is elsewhere on Windows and Linux).

Then run:

```
tools/export.sh
```

It exports headless, so no Godot window opens. The presets live in `game/export_presets.cfg`. The script produces:

| Folder | Zip | Contents |
| --- | --- | --- |
| `build/macos/` | `build/Galaxus-macos.zip` | `Galaxus.app`, universal (Apple silicon and Intel), ad hoc signed |
| `build/windows/` | `build/Galaxus-windows.zip` | `Galaxus.exe`, `Galaxus.pck`, `data_Galaxus_windows_x86_64/` (the .NET runtime and game assemblies), `mods/` |
| `build/linux/` | `build/Galaxus-linux.zip` | `Galaxus.x86_64`, `Galaxus.pck`, `data_Galaxus_linuxbsd_x86_64/`, `mods/` |

The game content in `game/data` is packed into the `.pck` (the presets include `data/*.json`). `build/` is ignored by git.

## How players run it

Unzip first, and keep the files together.

- **Mac:** the app is ad hoc signed, not notarised. The first time, right-click `Galaxus.app`, choose Open, then Open again. If macOS still refuses, go to System Settings, Privacy and Security, and choose Open Anyway.
- **Windows:** double-click `Galaxus.exe`. SmartScreen may warn about an unknown publisher. Click More info, then Run anyway.
- **Linux:** run `chmod +x Galaxus.x86_64`, then `./Galaxus.x86_64`.

## Mods

The game looks for mod folders in three places: `mods/` beside the executable, the Godot user data folder (`user://mods`), and the repo's `mods/` when run from a checkout. The Windows and Linux zips include the example mod in `mods/` beside the executable. The macOS app has no sensible folder beside its executable, so Mac players should use `~/Library/Application Support/Godot/app_userdata/Galaxus/mods/`.

## Minimum spec

These are best estimates, not tested figures.

- A 64-bit CPU: Intel or AMD x86_64 on Windows and Linux; Apple silicon or Intel on macOS.
- Windows 10 or later, a recent Linux distribution with glibc 2.31 or newer, or macOS 11 or later (the Intel slice allows 10.12, but the bundled .NET runtime needs a newer system in practice).
- A GPU and driver with OpenGL 3.3 support. The game uses Godot's Compatibility renderer.
- About 500 MB of free disk space after unzipping, and 2 GB of RAM.

## Not covered

- Windows and Linux builds are exported but have not been run on those systems.
- ARM builds for Windows and Linux are not produced.
- The macOS build is not notarised, so Gatekeeper will always ask on first launch.

## Galaxus Preview

The export also makes **Galaxus Preview** for each platform (`build/GalaxusPreview-<platform>.zip`): the same game, opening straight into the ship and station viewer, for showing people the ships, warships and stations without playing. It's the same code with the `viewer` feature switched on in its export preset. The full game has the viewer too, under "Ships and stations" on the main menu, and `--viewer` opens it from the command line.

