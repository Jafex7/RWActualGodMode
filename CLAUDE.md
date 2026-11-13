# Actual God Mode — project guide

Black & White–inspired deity mod for **RimWorld 1.7** (C# + Harmony 3). Author `jafex`,
package ID `jafex.actualgodmode`. Repo: https://github.com/Jafex7/RWActualGodMode

Read before changing things:
- `docs/DESIGN.md`: every design decision and the roadmap (M0–M6, v2, v3). Update it when decisions are made.
- `docs/WAYLITH_NOTES.md`: 1.7 / Waylith engine facts, Harmony 3 rules, what's verified vs unknown.

## Status
- Done: **M0** setup, **M1** faith / divine power / shrine / worship, **M2** miracles (toolbar casting).
- Done: **M3** Hand of God mode (gesture casting, Book of Gestures, recorder), verified in-game.
- Done: **M4** faith dynamics (patience buffer, hardship, gratitude, blessed food, faith history tooltip, thoughts,
  traits + vanilla patches, Zealot/Sceptic, Heal I, prayer requests, storyteller wishes).
- Next after M4: **M5** entry & temple (Awakening, Shrine research, Chosen People, temple tiers, Offering Bowl, Pylon).

## Commands
- Build: `dotnet build Source/ActualGodMode/ActualGodMode.csproj -c Release` (outputs to `1.7/Assemblies/`).
- **Self-test** (build + headless in-game test, ~40 s): `powershell -ExecutionPolicy Bypass -File tools/selftest.ps1`
  Prints load errors plus `[AGM SelfTest]` checks; exit 0 = PASS. **Tell the user before running it** (it launches their game).
- The game's `Mods/ActualGodMode` is a junction to this folder, so builds are live in-game.

## Layout
| Path | Contents |
|---|---|
| `Source/ActualGodMode/ActualGodModeMod.cs`, `…Settings.cs` | Mod entry (Harmony `PatchAll`), mod settings |
| `Source/…/AGM_DefOf.cs` | `[DefOf]` references to our defs |
| `Source/…/Faith/` | `Need_Faith` (faith + hidden patience buffer, gratitude), `FaithHardship` (ongoing hardship + Harmony event hooks: breaks, deaths, downed, blessed food), `FaithTraitExtension` (trait factors), `FaithTuningDef` (all numbers, `Defs/Faith/AGM_FaithTuning.xml`), `FaithUtility` (`pawn.Faith()`) |
| `Source/…/Requests/` | `PrayerRequestDef`, `PrayerRequestWorker` (+ Hungry/Cold/Fire/Wounded/RainForCrops/Sign), `PrayerRequestTracker` (create/answer/expire), `Alert_PrayerRequests`, `IncidentWorker_PrayerWish` |
| `Source/…/Power/` | `DivinePower` GameComponent (global pool, cap, lifetime) |
| `Source/…/Shrine/` | `CompShrine` (prayer spots, slots, group bonus, power rate), `ShrineUtility` + one-per-map PlaceWorker, `InfluenceUtility` |
| `Source/…/Worship/` | `JobDriver_PrayAtShrine`, job/joy givers, `WorshipUtility.TryMakePrayJob` |
| `Source/…/Miracles/` | `MiracleDef` + `MiracleLevel`, `MiracleRequirement` (+ `Req_*`), `MiracleWorker` (+ subclasses), `MiracleTracker` (unlocks), `MiracleUtility` (CanCast/TryCast/witnesses), `Designator_Miracle`, `MainTabWindow_Miracles` |
| `Source/…/HandOfGod/` | `HandOfGodMode` (state: toggle/hold, strokes, recognition, charged miracle, casting, label), `HandOfGodCursor` (RGBA cursor via Waylith `HardwareCursor`) |
| `Source/…/Gestures/` | `GestureDef` (+ template/stroke/custom), `PointCloudRecognizer` ($P), `GestureLibrary` (default vs player gestures, threshold, matching), `Window_BookOfGestures`, `Dialog_RecordGesture` |
| `Source/…/Patches/` | Harmony patches: Worship schedule block, power readout, Hand of God input (`MapInterface.HandleMapClicks` / `HandleLowPriorityInput`) |
| `Source/…/Dev/` | Debug actions, `AGM_SelfTest` (runs only with `-agmselftest`) |
| `Defs/` | XML defs by feature (Faith, Worship, Shrine, Miracles) |
| `Patches/` | XML patch operations (think tree) |
| `Languages/English/Keyed/ActualGodMode.xml` | All UI text |
| `_reference/decompiled/` | Decompiled game assemblies (git-ignored, never ship). **Check APIs here, don't guess.** |
| `_selftest/` | Isolated game profile for the self-test (git-ignored) |

## Conventions
- Def names, keys and classes use the `AGM_` prefix; C# namespace `ActualGodMode`.
- All player-facing text goes in keyed translation strings, never hard-coded.
- Tunable numbers live in XML (CompProperties, def fields), not in code constants.
- One concept per file, with a summary comment at the top of each class.
- Standalone first: no DLC required. DLC integrations use `ModsConfig.*Active` / `MayRequire` and must plug into
  DLC systems, not duplicate them.
- Harmony 3: `PatchAll(typeof(X).Assembly)`; `__instance` / `__result` types must match exactly.
- Extend `AGM_SelfTest` when adding gameplay, so the headless test covers it.
- **Test setups go in `Dev/AGM_TestScenarios.cs`** as shared builders: the self-test calls them, and each is also a
  dev-mode debug action so the user can recreate the same setup on their own save (e.g. "Set up god test colony",
  later "Build test temple"). Add one per milestone.
- Commit per milestone or fix; git identity is set per-repo. Push when the user agrees.

## Recipes
- **Add a miracle**: add a `ActualGodMode.MiracleDef` in `Defs/Miracles/` (levels with cost/radius/amount/thingDef
  and optional `requirements`), reuse `MiracleWorker_SpawnThing` / `_Water` or subclass `MiracleWorker`
  (`CanCastAt`, `Cast`, `DrawPreview`), add strings, add a check to `AGM_SelfTest.RunMiracleTests`.
- **Add a gesture**: `ActualGodMode.GestureDef` in `Defs/Gestures/` (points in a 0–1 box, **y down**), reference it from
  the miracle's `<gesture>`. Rerun the self-test: it simulates drawings of every gesture and logs the closest wrong
  match. If two signs get close, change a shape rather than the threshold (`GestureLibrary.BaseThreshold`, calibrated).
- **Add a prayer request / wish**: `ActualGodMode.PrayerRequestDef` in `Defs/Requests/` (miracle, mtbDays, deadline,
  rewards, letter text); subclass `PrayerRequestWorker` for the trigger (`CanRequest`), `StillNeeded`, `DeadlineTicks`
  and `IsAnsweredBy`. Requests only ask for unlocked miracles. Set `isWish` for storyteller-only ones.
- **Make a trait affect faith**: add a `FaithTraitExtension` (per degree factors) via `PatchOperationAddModExtension`
  (see `Patches/AGM_VanillaTraits.xml`).
- **Add an unlock requirement**: subclass `MiracleRequirement` (`IsMet`, `Description`), then reference it in XML as
  `<li Class="ActualGodMode.Req_X">`.
- **Anything reading the schedule**: vanilla `JobGiver_GetRest` / `JobGiver_Work` throw on unknown time assignments.
  `Patch_WorshipTimeAssignment` makes Worship read as Recreation inside them. Check for new throw sites if adding
  schedule-dependent code.

## Gotchas
- `-mod=` only loads mods from the local Mods folder (ignores Workshop Harmony). Use the self-test script instead.
- Def config errors only show with dev mode on (the self-test profile has it on).
- Vanilla only starts fires on flammable cells; spawn `Fire` directly in tests.
- Textures built in code lose their CPU pixels under Waylith (hardware cursor showed a black square); see
  WAYLITH_NOTES.md. Flecks freeze while paused. `\` is a dev-mode key; our toggle default is `=`.
- Headless can't test input or visuals: list what the user should check in-game.
- The log is `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log`.
- Vanilla already has a `Pray` job/joy giver (private room prayer); ours is `AGM_PrayAtShrine`.
