# Building MIMESIS VR

The mod is compiled with the C# compiler from the .NET 8 SDK against the game's own assemblies. The game's files are not in this repository: you need your own copy of MIMESIS.

## Requirements

- .NET SDK 8 (`dotnet`), on Linux or WSL (the script uses the SDK's `csc.dll`).
- For the native helper only: `x86_64-w64-mingw32-gcc` (package `gcc-mingw-w64-x86-64`).

## Reference assemblies

Copy these files into `build/ref/` (they must not be committed - see `.gitignore`):

- from `MIMESIS\MIMESIS_Data\Managed\`: `Assembly-CSharp.dll`, `mscorlib.dll`, `netstandard.dll`, `System.dll`, `System.Core.dll`, `Unity.Cinemachine.dll`, `Unity.InputSystem.dll`, `Unity.RenderPipelines.Core.Runtime.dll`, `Unity.RenderPipelines.Universal.Runtime.dll`, `Unity.TextMeshPro.dll`, `UnityEngine.dll`, `UnityEngine.AnimationModule.dll`, `UnityEngine.AudioModule.dll`, `UnityEngine.CoreModule.dll`, `UnityEngine.IMGUIModule.dll`, `UnityEngine.InputLegacyModule.dll`, `UnityEngine.InputModule.dll`, `UnityEngine.PhysicsModule.dll`, `UnityEngine.ScreenCaptureModule.dll`, `UnityEngine.SharedInternalsModule.dll`, `UnityEngine.TextCoreFontEngineModule.dll`, `UnityEngine.TextCoreTextEngineModule.dll`, `UnityEngine.TextRenderingModule.dll`, `UnityEngine.UI.dll`, `UnityEngine.UIModule.dll`;
- `UniverseLib.Mono.dll` (from a release package of this mod or from UniverseLib);
- for the BepInEx build: `BepInEx.Core.dll`, `BepInEx.Unity.Mono.dll`, `0Harmony.dll` from BepInEx 6 (`BepInEx\core\`).

For the MelonLoader build also copy `MelonLoader.dll` and `0Harmony.dll` from `MIMESIS\MelonLoader\net35\` into `build/ref_ml/`.

## Build

```
LOADER=ML bash build/build.sh src out/MimesisVR.dll        # MelonLoader (main)
bash build/build.sh src out/UnityVRMod.dll                 # BepInEx 6 (fallback)
```

The build is deterministic: the same sources and references give the same file.

Native helper (render-thread frame submission):

```
x86_64-w64-mingw32-gcc -shared -O2 -s -o MimesisVRNative.dll native/mvr_native.c
```

`UnityGraphicsHelper.dll` is built from `UnityGraphicsHelper/` (Visual Studio, from UnityVRMod).

## Package layout (MelonLoader)

```
Mods/MimesisVR.dll
UserLibs/UniverseLib.Mono.dll
UserData/MimesisVR/Native/openxr_loader.dll
UserData/MimesisVR/Native/UnityGraphicsHelper.dll
UserData/MimesisVR/Native/MimesisVRNative.dll
```

## Korean texts

`src/Lang/ko.txt` is the source of the built-in Korean texts; after editing it run `python Lang/make_langko.py` in `src/` to regenerate `src/Mimesis/LangKo.cs`.
