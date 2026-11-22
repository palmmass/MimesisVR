# Notices

## MIMESIS VR

Copyright (C) 2026 Palmmass

MIMESIS VR is a modified version of UnityVRMod and is distributed under the GNU General Public License, version 3 (see [LICENSE](LICENSE)). This program comes with ABSOLUTELY NO WARRANTY, to the extent permitted by applicable law.

**Modifications (GPL-3.0 section 5(a)).** Starting from UnityVRMod as of July 2025 (the first commits of this repository's history), the code was changed from November 2026 on to support MIMESIS: OpenXR rendering and frame submission, motion controller input and button remapping, character arm and finger tracking, melee by swinging, wrist interface, settings panel, MelonLoader support, localization and related changes. The full list of changes is the history of this repository and [CHANGELOG.md](CHANGELOG.md); files added or changed for MIMESIS are mainly in `src/Mimesis/`, `src/Loader/`, `native/` and `build/`.

## UnityVRMod

Copyright (C) 2025 NewUnityModder - https://github.com/NewUnityModder/UnityVRMod

Licensed under the GNU General Public License v3.0. The source of the included `UnityGraphicsHelper.dll` is in `UnityGraphicsHelper/`.

## Third-party components shipped in the release packages

| Component | Use | License | Source |
|---|---|---|---|
| UniverseLib (`UniverseLib.Mono.dll`) | Unity helper library | GNU LGPL 2.1 | https://github.com/yukieiji/UniverseLib (fork of https://github.com/sinai-dev/UniverseLib) |
| OpenXR Loader (`openxr_loader.dll`) | Connects to the headset runtime | Apache License 2.0, Copyright The Khronos Group Inc. | https://github.com/KhronosGroup/OpenXR-SDK |

The license texts are in [licenses/](licenses/). UniverseLib is used unmodified as a separate library; you may replace `UniverseLib.Mono.dll` with your own build of it.

## Not included

- **MelonLoader** (Apache License 2.0, https://github.com/LavaGang/MelonLoader) is installed separately by the user or by a mod manager; it is a dependency, not part of this mod.
- **The game.** MIMESIS is developed by ReLU Games and published by KRAFTON. No game files are included in this repository or in the release packages; building the mod requires your own copy of the game (see [BUILDING.md](BUILDING.md)). MIMESIS VR is not affiliated with or endorsed by ReLU Games or KRAFTON. "MIMESIS" is used only to name the game this mod is for.
