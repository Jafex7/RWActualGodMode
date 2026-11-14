# Actual God Mode — project guide

Black & White–inspired deity mod for **RimWorld 1.7** (C# + Harmony 3). Author `jafex`,
package ID `jafex.actualgodmode`. Repo: https://github.com/Jafex7/RWActualGodMode

Read before changing things:
- `docs/DESIGN.md`: every design decision and the roadmap (M0–M6, v2, v3). Update it when decisions are made.
- `docs/WAYLITH_NOTES.md`: 1.7 / Waylith engine facts, Harmony 3 rules, what's verified vs unknown.
- `docs/ASSETS.md`: catalog of every texture/icon/sound and whether it is a placeholder. Update it when adding assets.

## Status
- Done: **M0** setup, **M1** faith / divine power / shrine / worship, **M2** miracles (toolbar casting).
- Done: **M3** Hand of God mode (gesture casting, Book of Gestures, recorder), verified in-game.
- Done: **M4** faith dynamics (patience buffer, hardship, gratitude, blessed food, faith history tooltip, thoughts,
  traits + vanilla patches, Zealot/Sceptic, Heal I, prayer requests, storyteller wishes).
- **M5a** entry: Awakening (whispers → trance → revelation at dawn), hidden Shrine research, Chosen People
  scenario. Verified in-game.
- **M5a+**: believers & conversion (witness / preach / friend's answered prayer), the god's path (guided letters +
  alert), context-aware whispers. Verified in-game.
- **M5b** temple: tiers (room impressiveness + holy furniture → prayer slots, power cap), idol, incense burner,
  vanilla/DLC holy furniture patches, Offering Bowl, Pylon (tending, influence, desecration), "Build test temple",
  placement influence preview. Verified in-game.
- **M6a** alignment: value + bands + drift + deed ledger; Lightning (real weather strike; deed depends on target:
  enemy / helpless / follower / justice / punishment / prisoner / visitor); good god = gratitude + patience; evil god =
  fear-faith; "cowed" after punishment; deliverance prayer; alignment-tinted cursor/trail/particles.
  Verified in-game.
- **M6b** the Hand: grab (click or hold-still on a pawn/item) / carry / set down; capacity by believers,
  pickup + per-hour costs by mass, drop outside influence or out of power, tuck into bed (+ good deed for a downed
  person). `GodHand` MapComponent holds the thing. Verified in-game. Then: lifted pawns react (flavor thought + shout by
  alignment), and a colonist set down on work starts it (`HandWorkOrder`, vanilla work-giver checks, trees get marked).
  Dropping into danger (fire / hostiles) is judged like a strike (`WrathJudgement`, shared with Lightning).
  Soft influence border: past the edge, miracles and the Hand cost more and the Hand weakens, sharply with distance
  (`InfluenceUtility.CostFactor` / `LiftFactor` / `InReach`, `AGM_InfluenceTuning.xml`).
  Soft border verified in-game. Poured miracles (`MiracleChannel`, `<channel>` in the MiracleDef): Water, Food, Wood
  are cast by holding the click (the charge = the miracle's amount, paid per unit; release pauses, right-click
  cancels). **Miracles have no levels** (numbers on the def, read via `MiracleDef.Stats` for future upgrades), and
  the Miracles tab charges the hand like a gesture (no designator). Blessed crops: don't stack with plain food,
  cooking keeps the blessing. Verified in-game.
- **God progression** (Powers page, second tab of the **Divine** window, formerly "Miracles"): `PowerTrackDef` tracks in three branches,
  ranks paid from the pool (prices under the temple-tier caps). Miracles: one track per miracle (amount + cost per
  rank); Heal is poured on one pawn, Lightning holds bolts. Hand: starts items-only at 40 kg; Strength, Light touch,
  Lift living things, Lift enemies. Influence: Long reach, Pylon strength, Heralds (living pylons, gizmo).
  Verified in-game. Playtest polish: greyed Buy buttons say what to unlock, shrine build buttons for
  temple things, map tooltips hidden while the hand is out, Lightning is a pinpoint divine smite (no fire; 50 in
  5 hits + lightning shock downs an unarmored enemy; 10% death chance; EMP on mechs; own people only get a 10 sting).
  Polish verified in-game.
- **Drop rules** (Divine tab > Drop rules): per-save rules for what a colonist set down by the hand does with
  what's under them (`DropCategoryDef` tree, actions work / haul / wear / equip / eat / just set down, inherited
  down the tree; cursor preview). Self-tested; awaiting in-game test.
- Next: see the roadmap in DESIGN.md (v2: factions/awe, throwing, …).

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
| `Source/…/Faith/` | `BelieverTracker` (who believes; Faith need gate; conversion; `InteractionWorker_Preach`), `Need_Faith` (faith + hidden patience buffer, gratitude), `FaithHardship` (ongoing hardship + Harmony event hooks: breaks, deaths, downed, blessed food), `FaithTraitExtension` (trait factors), `FaithTuningDef` (all numbers, `Defs/Faith/AGM_FaithTuning.xml`), `FaithUtility` (`pawn.Faith()`) |
| `Source/…/Requests/` | `PrayerRequestDef`, `PrayerRequestWorker` (+ Hungry/Cold/Fire/Wounded/RainForCrops/Sign), `PrayerRequestTracker` (create/answer/expire), `Alert_PrayerRequests`, `IncidentWorker_PrayerWish` |
| `Source/…/Alignment/` | `AlignmentTracker` (value, bands, drift, deed ledger, effects; `AGM_AlignmentTuning.xml`), `WrathJudgement` (who was harmed → deed, punishment, fear-faith witnesses; used by Lightning and dangerous drops; ledger keys `AGM_Deed_Smite_*` / `AGM_Deed_Drop_*`), `MiracleWorker_Lightning`, `AGM_AlignmentDefOf` |
| `Source/…/Temple/` | `TempleTuningDef` (tiers, `AGM_TempleTuning.xml`), `TempleUtility.Evaluate`, `HolyFurnitureExtension`, `RoomRoleWorker_Temple`, `Building_OfferingBowl`, `CompPylon` + tend work/job, `AGM_TempleDefOf` |
| `Source/…/Guide/` | `GodGuideTracker` (the god's path steps + letters), `Alert_GodGuide` |
| `Source/…/Awakening/` | `AwakeningTracker` (stages, tuning def `AGM_AwakeningTuning`), `DivineTrance` (trance mental state, draw-sign job, research hide/lock patches, `ScenPart_StartWithShrine`) |
| `Source/…/Power/` | `DivinePower` GameComponent (global pool, cap, lifetime) |
| `Source/…/Shrine/` | `CompShrine` (prayer spots, slots, group bonus, power rate), `ShrineUtility` + one-per-map PlaceWorker, `InfluenceUtility` |
| `Source/…/Worship/` | `JobDriver_PrayAtShrine`, job/joy givers, `WorshipUtility.TryMakePrayJob` |
| `Source/…/Miracles/` | `MiracleDef` (+ `MiracleStats`, `MiracleChannelProps`), `MiracleRequirement` (+ `Req_*`), `MiracleWorker` (+ subclasses, `ChannelUnit`), `MiracleChannel` (poured charge), `MiracleTracker` (unlocks, events, blessed food), `MiracleUtility` (CanCast/TryCast/witnesses), `MiracleCostLabel`, `MainTabWindow_Miracles` (charges the hand via `HandOfGodMode.Charge`) |
| `Source/…/Progression/` | `PowerTrackDef` (+ `PowerRank`, `PowerBranch`), `GodProgression` (ranks bought, `Value`/`Has`/`Rank`, cap gate), `PowersPage` (UI), `HeraldTracker` (+ anoint gizmo patch), `AGM_ProgressionDefOf` |
| `Source/…/HandOfGod/` | `HandOfGodMode` (state: toggle/hold, strokes, recognition, charged miracle, casting, label, grab-vs-stroke input), `HandOfGodCursor` (RGBA cursor via Waylith `HardwareCursor`), `GodHand` (MapComponent + IThingHolder: held thing, capacity, costs, place/tuck, game-over guard patch), `HandTuningDef` (`AGM_HandTuning.xml`), `HandWorkOrder` (set a colonist down on work → prioritized job, like vanilla's right-click), drop rules: `DropCategoryDef` (+ `DropAction`, category workers), `DropRules` (per-save choices), `DropRuleOrder` (plan / execute / preview), `Window_DropRules` |
| `Source/…/Gestures/` | `GestureDef` (+ template/stroke/custom), `PointCloudRecognizer` ($P), `GestureLibrary` (default vs player gestures, threshold, matching), `Window_BookOfGestures`, `Dialog_RecordGesture` |
| `Source/…/Patches/` | Harmony patches: Worship schedule block, power readout, Hand of God input (`MapInterface.HandleMapClicks` / `HandleLowPriorityInput`), hover tooltips hidden while the hand is out |
| `Source/…/Dev/` | Debug actions (`AGM_DebugActions`, `AGM_AwakeningDebug`), `AGM_TestScenarios` (shared setups), `AGM_SelfTest` (runs only with `-agmselftest`; phases: worship → sync tests → awakening) |
| `Defs/` | XML defs by feature (Faith, Worship, Shrine, Miracles, Gestures, Requests, Awakening, HandOfGod, Progression) |
| `Patches/` | XML patch operations (think trees, vanilla trait extensions) |
| `Textures/` | Our textures; placeholders generated by `tools/gen_textures.ps1` |
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
- **Add a miracle**: add a `ActualGodMode.MiracleDef` in `Defs/Miracles/` (cost/radius/amount/thingDef, optional
  `requirements`, optional `<channel>` to pour it), reuse `MiracleWorker_SpawnThing` / `_Water` or subclass
  `MiracleWorker` (`CanCastAt`, `Cast`, `DrawPreview`, `ChannelUnit` if poured), add strings, add a check to
  `AGM_SelfTest.RunMiracleTests`. Read numbers through `def.Stats`, not the raw fields (upgrades will hook there).
- **Add a power track**: an `ActualGodMode.PowerTrackDef` in `Defs/Progression/` (branch, order, ranks with `price`;
  a miracle track sets `miracle` and per-rank `amount`/`cost`; others set `baseValue`, per-rank `value` and an
  `effectKey` string, or have one rank as an ability). Prerequisites: `Req_PowerRank`. Code reads it through
  `GodProgression.Value(track)` / `Has(track)` (add the def to `AGM_ProgressionDefOf`). Keep prices under the cap
  of the temple tier meant to unlock them. Debug actions: "Powers: all tracks +1 rank" / "Powers: reset all".
- **Add a drop rule category**: a `ActualGodMode.DropCategoryDef` in `Defs/HandOfGod/AGM_DropCategories.xml` (parent,
  order, `actions`, a `DropCategoryWorker` subclass or `thingCategories` for items). New actions: add to `DropAction`,
  handle in `DropRuleOrder.Execute`/`Preview`, add `AGM_Drop_Action_*` and `AGM_Drop_Will_*` strings, and a check in
  `AGM_SelfTest.RunDropRuleTests` (setup: `AGM_TestScenarios.SetUpDropRulesTest`).
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
- Never end a mental state (or a job) from inside a job's toil finish action: it tears the job down mid-cleanup
  (NullReferenceException in `Toil.Cleanup`). Set a flag and let the mental state recover on its own tick.
- Code-built textures don't keep CPU pixels; real textures (PNG in `Textures/`) are needed for things on the map.
- **Don't use FilthMaker for scripted marks**: vanilla filth placement rolls a random chance per terrain and soil
  accepts only "Unnatural" filth while rough stone accepts only "Terrain". Spawn the filth thing directly.
- Before the Awakening **nobody has the Faith need** (`BelieverTracker` + `ShouldHaveNeed` patch). Test setups must
  convert colonists (`AGM_TestScenarios.SetUpGodColony` / `BelieverTracker.ConvertAllColonists`).
- The user may have RimWorld open: the build then fails to copy the DLL ("locked by RimWorld"). Compile-check with
  `-p:OutputPath=<scratch>\` and ask them to close the game before running the self-test. Never kill their game.
- Patching DLC defs: wrap in `PatchOperationConditional` (an xpath that matches nothing is an error).
- Harmony 3: patch the class that *declares* a method (e.g. `IncidentWorker_Raid`, not `IncidentWorker_RaidEnemy`)
  and type-check the instance inside the patch.
- The log is `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log`.
- Vanilla already has a `Pray` job/joy giver (private room prayer); ours is `AGM_PrayAtShrine`.
- A thing held by a `MapComponent` that implements `IThingHolder` counts as on the map (`Map.GetChildHolders`
  includes such components), but vanilla's game-over check only looks at *spawned* colonists: `Patch_GameOverWhileHeld`.
- `ThingOwner.TryAdd` doesn't despawn: `DeSpawn()` a pawn (or `SplitOff` an item) first. `TryDrop` respawns pawns too.
