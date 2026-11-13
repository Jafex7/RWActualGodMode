# RimWorld 1.7 / Waylith — Modding Notes

Collected Nov 2026 from:
- RimWorld wiki: https://rimworldwiki.com/wiki/Modding_Tutorials/RimWorld_1.7_Mod_Updates
- RimWorld wiki: https://rimworldwiki.com/wiki/Modding_Tutorials/Setting_up_a_solution
- Ludeon 1.7 Modder Primer (auto-updated, work in progress):
  https://docs.google.com/document/d/e/2PACX-1vSykw5_DYaaw42nEznI9zmBZVT_Vgg2bxYq_v2XWvAsHC3QSKFuDK2OyiQ5WC9jvVRUcrjpFt7xLLF5/pub

Re-check these sources before relying on anything here; 1.7 docs are still evolving.

## Engine
- Waylith: Ludeon's in-house engine. .NET 10 (CoreCLR, no Mono), Vulkan 1.2.
- Assemblies:
  | DLL | Role |
  |---|---|
  | `Assembly-CSharp.dll` | Game code, almost unchanged from 1.6 |
  | `Waylith.dll` | Engine: rendering, input, frame lifecycle, audio, assets |
  | `UnityStubs.dll` | Unity API shim forwarding to Waylith. Mods are meant to keep using it. |
  | `RimWorld.dll` | Host: startup, render features, UI |
- 1.6 mods "just work" in ~99% of cases. Known breaks: Prepatcher, mods changing
  asset loading.

## Project setup
- `net10.0`, SDK-style csproj.
- `AppendTargetFrameworkToOutputPath=false`, `GenerateDependencyFile=false`.
- Reference (Private=false) from the game root folder: `Assembly-CSharp.dll`,
  `UnityStubs.dll`, `Waylith.dll`, possibly `RimWorld.dll`.
- No Unity DLL references; `using UnityEngine;` still works (stubs).
- Harmony **3.0**: NuGet `Lib.Harmony` `3.0.0-rc.3` with `ExcludeAssets="runtime"`,
  matching what the Harmony mod (`brrainz.harmony` v3.0.1) actually runs
  (log: "Harmony library v3.0.0.0-rc.3").
  Harmony 3 enforces `__instance` / `__result` types, and `PatchAll()` without
  an argument is obsolete: use `PatchAll(typeof(X).Assembly)`.
- IDE: VS2026 or Rider 2026+ (older ones don't support .NET 10). CLI: .NET 10 SDK.
- Debugging: attach to the process (no Doorstop). VS may need env var
  `VSDebugger_ValidateDotnetDebugLibSignatures=0`.

## Shaders / rendering
- New shaders are GLSL (`.vert.glsl` / `.frag.glsl`, `#version 450`) in a mod's
  `Shaders/` folder, compiled on load. Bad shaders render magenta.
- Texture coords flipped vs Unity; `_ST` is xy offset, zw scale.
- Descriptor set 1 is engine-reserved; set 2 = material data.

## Tools
- Launch args: `-mod=<packageId;...>` (load only these + Core/DLCs), `-console`,
  `-WatchShaders`, `-Verbose`, `-ColdBoot`, `-Headless`, `-NoWindow`.
- Frame profiler (Actions/tools > General): can instrument a mod assembly and its
  Harmony patches.
- Log: `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log`
- Jobs: `Waylith.Jobs` / `WaylithJobRuntime` replace Unity Jobs/Burst.

## Automated load test
- `-mod=` only loads mods from the game's **local** `Mods` folder; Workshop mods
  (like Harmony) are ignored ("is not in the local Mods folder, ignoring").
- Instead, use an isolated save-data folder with its own mod list:
  `RimWorld.exe -Headless "-savedatafolder=<dir>"` with `<dir>/Config/ModsConfig.xml`
  listing `brrainz.harmony`, Core, DLCs, `jafex.actualgodmode`. The player's real
  config and saves are untouched.
- The log still goes to the normal `Player.log` (overwriting the player's last log).
- **Use `tools/selftest.ps1`**: it does all of the below (profile in `_selftest/`,
  dev mode on, build, run, parse log, exit code).
- **In-game self-test**: add `-quicktest -agmselftest`. `Dev/AGM_SelfTest.cs` places a
  shrine, schedules everyone to Worship, runs 6 in-game hours at ultrafast, logs
  `[AGM SelfTest]` lines plus `RESULT: PASS/FAIL`, and quits (~35 s total).
- Without a self-test, headless doesn't exit by itself; kill it after load. Load errors (XML, patches,
  DefOf) appear within ~10 s after "[Actual God Mode] Loaded."

## Local reference source
Decompiled with `ilspycmd` into `_reference/decompiled/` (Assembly-CSharp,
Waylith, UnityStubs, RimWorld). Not part of the mod; never ship or commit it.
Re-run after game updates:
`ilspycmd -p -o _reference/decompiled/<Name> -r <game> <game>/<Name>.dll`

## Verified in code
- `UnityEngine.Cursor.SetCursor` is **implemented** in UnityStubs: it converts the
  texture to RGBA and calls `Waylith.PlatformSpecific.HardwareCursor.SetImage`.
  Passing `null` resets to the default. The texture's pixels must be readable, or
  it logs a warning and resets. Vanilla wrapper: `Verse.CustomCursor`.
- `Input.mousePosition` and `Input.GetMouseButton` exist in UnityStubs.
- **Code-built textures lose their CPU pixels.** `new Texture2D` + `SetPixels` + `Apply`
  works for GPU drawing, but Waylith's texture residency later evicts the CPU copy, and
  with no source file it can't reload, so CPU reads (`GetPixel`, hardware cursor) return
  zeros. A cursor made this way showed as a **black square**. Fix used: keep our own
  RGBA byte[] (rows top-down) and call `Waylith.PlatformSpecific.HardwareCursor.SetImage(key,
  rgba, w, h, hotX, hotY)` directly. To restore, call `Cursor.SetCursor(null, …)` first
  (clears the stub's last-texture cache), then `CustomCursor.Activate()`.
- **Flecks run on game time**: they freeze while paused, and flecks spawned while paused
  all appear at once on unpause. Don't spawn cursor particles while paused.
- Hand of God mode owns map input by prefixing `MapInterface.HandleMapClicks` and
  `HandleLowPriorityInput` (verified in-game: selection/designators paused, UI and camera fine).
- `\` (Backslash) opens a dev menu in dev mode; `=` (Equals) is unused by vanilla.
- Other mods (e.g. Multiplayer) log "probably needs a StaticConstructorOnStartup
  attribute" warnings at startup; those aren't ours.

## Unknown / not documented yet
- Cursor API (custom cursor textures), IMGUI/Widgets changes, input APIs, Graphics.DrawMesh,
  flecks/motes, audio. Verify against the decompiled assemblies and in-game tests.
