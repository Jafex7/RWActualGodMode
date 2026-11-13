# Actual God Mode — Design Notes

Black & White–inspired deity gameplay for RimWorld 1.7. C# + Harmony.

## General decisions
- **Standalone core**, no DLC required.
- **Optional DLC integrations** (loaded only when the DLC is active):
  - Ideology: worship meme/precepts, worship ritual, relics as influence sources.
  - Royalty: meditation at the shrine also produces Divine Power.
- Everything tunable via **ModSettings** (decay rates, gains, costs).
- **No redundancy with DLCs**: our systems stand on their own; when a DLC is
  active, we plug into its existing systems (meditation, rituals, relics) and do
  not duplicate them.

## 1. Faith & Divine Power

### Faith (per pawn)
- A custom `Need` (0–100%) on each colonist. Something the player manages.
- **Decays very slowly** by default; decay multiplier is configurable (including 0).
- Colonists shouldn't expect their god to solve everything: small hardships are
  normal, and only sustained or severe ones shake faith.

**Raises faith**
- Witnessing a miracle (bigger if it helped them directly: fed, healed, saved).
- Enemies vanquished by divine power.
- **Gratitude**: needs are met and mood stays mostly content over time.
- Praying at the shrine (small).
- *(proposed)* Answered prayer requests.

**Lowers faith (hardship / neglect)**
- Frequent mental breaks.
- Serious illness or injury.
- Starving, freezing, prolonged misery.
- Deaths of colonists.
- *(proposed)* Ignored prayer requests.

**Abuse** (the god directly harming its own people) is handled under Alignment.

### Patience (hardship buffer) — decided for M4
- Each pawn has a hidden **patience** buffer. Hardship drains it first; faith only
  drops once it's empty. It refills slowly in good times.
- Size = base × trait factors × small personal variation → different loyalty levels.
- Ongoing drains: malnutrition, hypothermia/heatstroke, sickness, misery (mood below
  minor break threshold). Event drains: own mental break, colonist death (more if
  close), being downed.
- **Gratitude**: once a day, a mostly-content day with no hardship gives a little faith.
- **Blessed food**: eating food made by a miracle gives the eater extra faith.
- All numbers live in `Defs/Faith/AGM_FaithTuning.xml`.

### Heal — decided for M4
- Facts (checked in code): summary health % comes only from fresh injuries
  (≈ severity/75 each, multiplicative); scars, disease and missing parts don't count.
  Injuries can be partially healed. Bleeding scales with severity; tended wounds don't
  bleed. A tended wound **can't be re-tended** until the tend expires.
- **No tending miracle**: a low-quality miracle tend would block a doctor's proper tend.
- **Heal I "Mend"**: single pawn, ~10 severity total, most bleeding/painful wounds
  first. Doesn't tend, scars/missing parts/disease untouched. Enough to get a downed
  pawn up; doctors still needed. Unlocks after a colonist is first downed.
- *Later* **Heal II "Blessing of recovery"**: more healing + a day-long buff (faster
  natural healing, better tend quality when a doctor treats them). Empowers doctors.
- *Later, powerful*: cure disease, restore scars/limbs, resurrect. **Open**: these may be
  separate miracles or very high Heal levels; decide when we get there.

### Prayer requests (v1)
- Decided: answered by casting the requested miracle **within ~8 cells** of the
  requester before the deadline. Deadline depends on urgency (≈1 day normally;
  hours when in danger; a bleeding pawn's deadline follows their bleed-out time).
- Requests only ask for miracles the god has unlocked.
- *Later*: "smite the animal hunting me" once Lightning/Smite exists.
- **Needs-driven**: a pawn in hardship (starving, sick, injured, grieving, freezing)
  may pray for the matching miracle.
- **Storyteller-driven**: occasional non-urgent wishes (rain for the crops, a
  blessing for a wedding, a good harvest), fired as an `IncidentDef`.
- Shown as a letter plus a persistent alert with a countdown.
- Answered in time: big faith boost for the requester and nearby witnesses.
- Ignored: bigger faith drop than the hardship alone.
- Request frequency per pawn = base × trait modifiers × faith factor, with cooldowns.

### Traits
- Trait effects come from a `DefModExtension` (`FaithTraitExtension`) attached to
  TraitDefs, including vanilla traits via XML patches. Modifiers multiply, so trait
  combinations happen without special-casing.
- Fields: faith decay, gain, hardship tolerance, request frequency, prayer output.
- New traits: **Zealot**, **Sceptic**; more fun traits later (e.g. Needy Believer).
- Vanilla examples: Lazy/Slothful → more requests; Pessimist → bigger hardship
  hits; Optimist → more gratitude.
- **Future**: traits also shape *what* pawns ask for. E.g. Psychopath/Bloodlust
  asks to witness suffering (smite a rival); Greedy asks for riches.

## 2. The Awakening (how the Shrine arrives)
The narrative entry point of the mod. Kept simple for v1.
- `GameComponent` state machine; save-friendly.
- **One chosen pawn** only.
- The player has **no powers** before the Shrine exists.
- Stages (v1):
  1. **Dormant**: after some days, a chance per day to begin. Picks the chosen
     pawn (weighted away from Sceptics).
  2. **Whispers**: the chosen pawn says odd things in the social log
     ("it always rains when there's a fire", "I saw a flat triangle in the sky").
  3. **Trance**: a short, harmless mental state. The pawn walks somewhere and
     draws the cursor shape in the dirt (a fading decal). Flavor only.
  4. **Revelation**: unlocks the Shrine research project.
- **Chosen People** scenario: skips the Awakening; starts with a Shrine.
- Setting to skip the Awakening (research available from the start).

- Implemented (M5a). The Revelation happens at the **next dawn** (≥ 4 h after the
  drawing); a sleeping chosen pawn is woken. Miracle unlocks don't run before the
  god is revealed (first check after that is silent).

### Feedback from first play (to refine)
- **Context-aware whispers**: lines should reflect things that actually happened
  in the colony (a fire, a raid, a death, rain, a lucky escape) rather than generic
  ones ("it always rains when there's a fire" felt odd when it had never rained).
- **Guided path after the Revelation** (quest-like, not railroading): research and
  build the Shrine → flavor + tips once built (Worship schedule, Hand of God) →
  convince the others with a first miracle → answer a first prayer. Optional
  hints; consider real Quests-tab entries.
- **Believers and conversion**: before the Awakening no one has faith. The chosen
  pawn is the first believer; others get the Faith need only once convinced
  (witnessing a miracle, the chosen one preaching, a friend's answered prayer).
  Chosen People: everyone starts as a believer at low faith (it just happened).

- **Awakening trigger (future, no change yet)**: make the Dormant stage flavorful
  without stalling: (1) hidden readiness raises the daily chance with each notable
  colony event; (2) hardship (downing, raid, starvation, death) sharply raises it
  for a day or so: someone in need is heard; (3) the chosen pawn is linked to the
  event (the downed one, the one nearest the fire), so whispers feel personal;
  (4) safety net: guaranteed start by ~day 10-14. Numbers in the tuning file.
- **Balance pass (future)**: revisit all miracle level requirements. Water II is the
  only level II without a lifetime-power requirement.
- **Zealot (future)**: consider making it earnable through long, deep faith
  rather than (or as well as) a starting trait.

### Deferred
- Night vigil climax (fits Ideology rituals).
- The **Prophet** (first believer gets a special role; likely an Ideology role).
- Repeatable Awakenings.

## 3. Temple & Worship
- **One central Shrine** per colony; **Pylons** extend influence.
- Enclosing the Shrine in a room makes a **Temple** (custom `RoomRoleDef`).
- **Temple quality** = room impressiveness + holy furniture − conflicting use
  (beds, workbenches). Quality sets the **temple tier**.
- Holy furniture: our own **incense burner** and **statues/idols**; vanilla
  sculptures count. With Ideology: its kneel sheets/pillows, lecterns, altars and
  braziers count (no prayer mats of our own).
- **Prayer slots** (cap): limited number of pawns can pray at once, set by tier.
  Draft: outdoor 2, chapel 4, temple 6, grand temple 10 (configurable).
- **Group bonus**: each worshipper's output is multiplied by the number praying
  together (draft: +10% per extra worshipper).
- **Worship time assignment**: new timetable block so the player can schedule
  group worship.
- **Offering Bowl** (research): pawns haul items into it; the player consumes
  the contents for Divine Power based on market value.
- *Later*: **Sacrifice Altar** for living beings (research); with Ideology, hook
  into its sacrifice ritual instead of duplicating it.
- *Later*: Shrine visuals change with tier, lifetime power, and alignment.

## 4. Miracles
- **Fixed cost** per miracle level. Cost is the only limiter (cooldowns on the
  backburner).
- **Miracle levels**: each miracle has levels (e.g. Food I/II/III). A level has a
  fixed cost and effect size. Early: Water I puts out a small fire, Food I feeds
  one pawn for a day, Wood I is enough for a campfire.
- **Starting miracles**: Water, Food, Wood.
- **Unlocks** (new miracles and new levels) use composable requirements defined in
  XML on each `MiracleDef`, mixed and matched:
  - number of believers / average faith / lifetime Divine Power
  - surviving a situation (e.g. a raid of at least X points → Shield)
  - witnessing something (lightning strike, big fire → Lightning, Fireball)
  - number of sacrifices (aggressive miracles)
  - alignment range
- Unlocks arrive as a letter framed as the colonists' idea ("After the raid, they
  pray for protection").
- Some miracles are **alignment-locked**, others **change effect** with alignment.
- **Gesture casting** (primary): draw a symbol with the mouse, the miracle charges
  in the cursor, click a target inside influence to cast. A toolbar/"Book of
  Gestures" stays as a reference and fallback.
  - Input: hold a key + left-drag, **or** toggle Hand of God mode (left-drag draws).
  - Gestures are `GestureDef`s: one or more templates, each a list of strokes,
    each stroke a list of points in a 0–1 box. The same data draws the symbol in
    the Book of Gestures.
  - Recognizer: **$P point-cloud** (handles multi-stroke, ignores stroke order and
    direction). The gesture ends when the key is released, or after a short pause
    in toggle mode.
  - Per-gesture options: rotation sensitivity, minimum match score.
  - **Player gesture recorder** (not a dev tool): from the Book of Gestures, the
    player can re-record any miracle's gesture at any time.
    - Draw it ~3 times; the recorder checks the samples agree with each other.
    - Conflict check: warns if the new gesture is too close to another miracle's.
    - Tips on what works (distinct shapes, 1–3 strokes, avoid near-duplicates).
    - "Reset to default" per gesture. Defaults ship with the mod.
    - Saved in mod settings (global across saves, like keybindings).
  - No scoring of drawing quality.
- *Later polish*: miracle animations and richer effects (current: flash, sparks,
  splashes, puddles, plus placeholder icons).
- Pool of later miracles: Heal, Lightning, Fireball, Shield (vanilla projectile
  interceptor), Calm, Growth, Storm, Earthquake, Teleport, Inspire, Resurrect.

## 5. Alignment (Good ↔ Evil)
General direction agreed; specifics to be drilled down later.
- One global value, −100 (evil) to +100 (good), moved by deeds.
- **Slow drift toward neutral** (configurable). Regular deeds easily outpace it, so
  staying good/evil doesn't fight the system, but a god can fall from grace or be
  redeemed over time. Letters when crossing thresholds.
- Neglect (ignored requests) costs faith, not alignment.
- **Smiting own pawns**: evil, unless the pawn is an active threat to other
  believers (berserk, murderous rage, hostile), then it's neutral ("justice").

### Good god
- Faith from love: bigger gratitude gains from a content, well-kept colony;
  higher hardship tolerance.
- Best miracle variants. E.g. Food at high good spawns a special divine food
  (lembas-like: long-lasting nutrition + mood buff), or treats like chocolate.

### Evil god
- Faith from fear; relies more on sacrifices, possibly forcing the colony to raid
  for victims.
- Locked out of inherently good miracles; gets dark versions:
  - **Transfer wounds/disease** instead of Heal (cheap; ideally onto an enemy).
  - **Rip life** from someone to revive a faithful pawn.
  - Food miracle spawns human meat.
- **Mood management via fear, not happiness** (proposal):
  - Punishing a pawn in a mental break gives witnesses a "Won't be next" thought:
    small mood effect, but a **lower mental break threshold** for a while (fear
    keeps them in line rather than making them happy).
  - Witnessing the god's wrath pleases bloodthirsty/zealous pawns.
  - Sacrifices give a colony-wide dark buff.
- With Anomaly: Raise the Dead uses its shamblers rather than our own.

## 6. Hand of God
Lives in Hand of God mode with gesture casting:
click a thing = grab, drag from empty ground = gesture, release = put down.
- **v1: moving only** (no throwing). Grab pawns (colonists, prisoners, animals,
  enemies) and items; carry them; set them down. No buildings or trees.
- Implementation: grabbed thing is despawned into a ticking container on our
  MapComponent, drawn at the cursor each frame, respawned on a valid cell on release.
- **Cost guideline: "how much are you affecting the world?"**
  - Moving items: low (helping haul after a harvest).
  - Lifting a living creature: higher.
  - Imparting force to hurt (throwing, later): highest.
  - Scaled by mass and time held.
- **Lift capacity** grows with believers. Leaving influence drops what you hold.
- Alignment: gently carrying a downed colonist to bed = small good deed;
  hurting own pawns = evil (with the "justice" exception).
- **No hand graphic.** Hand of God mode changes the **cursor**: custom cursor
  texture plus glow/particles at the cursor, tinted by alignment (golden sparks
  for good, pale blue for neutral, red embers/smoke for evil). Gesture trail uses
  the same style.
- *Later*: **throwing** pawns and items (flying-pawn / projectile, damage by mass
  and speed, cost by force imparted).

### Hand v1 as built (M6b)
- **Input** (Hand of God mode, nothing charged): press on a pawn or item and
  - **hold still** ~0.25 s → grabbed, **drag**, release to set down;
  - **click** (no movement) → grabbed, carried until the next click (left or right);
  - **move** more than 12 px before that → it's a gesture stroke, as before (so signs
    can start over stockpiles). A press while a multi-stroke sign is pending never grabs.
  - While something is held the mode stays on (even if the hold key is released) and
    no signs are drawn.
- **Grabbable**: any spawned pawn (colonist, prisoner, animal, enemy, visitor) and any
  haulable item (incl. corpses, minified furniture). Not buildings, plants, filth.
- **Capacity** = 35 kg + 15 kg per living believer (3 believers = 80 kg; a human with
  gear is ~61 kg). Pawn mass = body + gear/inventory. A stack too heavy to lift whole is
  lifted in part (as many as fit).
- **Costs** (power): items 0.5 + 0.02/kg to pick up, 0.05/kg per in-game hour held;
  pawns 3 + 0.03/kg to pick up, 0.15/kg per hour held (a colonist ≈ 4.8 to lift and
  ≈ 9/h, i.e. ~0.15 per real second at normal speed). Power running out sets it down.
- **Influence**: only things inside it can be grabbed; moving the cursor outside drops
  the thing at the last cell inside.
- **Beds**: a pawn set down on a bed they can use is tucked in. Carrying a downed,
  non-hostile person to a bed = +1 alignment, not if they were taken from a bed, and at
  most once per person per day (in-memory guard).
- The held pawn keeps ticking (hunger, bleeding) and still counts as on the map; the
  game doesn't end while the last colonist is in the hand. A thing in the hand when
  saving is set down on load.
- **Reactions** (flavor, not a faith source): a lifted person shouts and gets a small
  0.5-day memory that doesn't stack: good god "lifted by the god" +2 ("Praise be!"),
  neutral "picked up by something unseen" 0 ("Whoa!"), evil or hostile pawn "seized by
  the god" −2 ("No, please!"). Animals don't react.
- **Set down on work** (B&W-style "drop a villager by a tree"): a player colonist set
  down deliberately on something starts work there, like vanilla's right-click
  "Prioritize …" but with no menu. Order: the main things on the cell (bench bills,
  blueprints/frames, plants, items to haul), then filth, then the cell (sowing). Within a
  target, the pawn's own work priority order decides. An **undesignated tree** is marked
  for cutting (removed again if the pawn can't do it). If only unassigned/disabled work
  fits: a warning in vanilla wording ("Cannot cut oak tree: Not assigned to plant
  cutting"), no order. Not for drafted, downed or breaking pawns, and not for drops
  that happen on their own (edge of influence, out of power).
- **Dangerous drops** (built): a deliberate drop on/next to fire (r1.5), or within 5
  cells (in sight) of pawns hostile to the dropped one, is judged exactly like a
  Lightning strike on them (shared `WrathJudgement`, times `dangerDropFactor` = 1):
  own follower −10, helpless enemy −5, prisoner −6, visitor −8, colony animal −3,
  punishment of a non-violent break −4 (ends the break, onlookers cowed), justice
  against a violent break and **retribution** against an attacker: noted, neutral.
  Evil god: onlookers gain fear-faith. A **drafted** colonist dropped among enemies is
  being sent to fight: not judged (fire still is). The cursor warns "Danger: fire" /
  "Danger: hostiles (n)" before you drop. Plain drops don't hurt; no work order is given
  on a dangerous drop. Later ideas: deep water, toxic/vacuum, falling from height.
  - **Pinned (user)**: harming a **visitor** (dangerous drop, and Lightning too) should
    anger their faction like attacking them does in vanilla (goodwill loss / hostility,
    via the vanilla goodwill & history-event system rather than our own). The cursor
    warning says "This will anger their faction" before the drop. Raiders are fair game.
- **Later (user ideas, M6b feedback)**:
  - Drop interactions with a **choice** (food: eat vs haul; corpse: butcher vs bury…):
    maybe a menu later; for now only work-giver jobs, which don't need one.
  - **Drop rules** (user idea, planned): an in-game settings window with a category
    tree like the storage filter (apparel, weapons, food, corpses, raw resources…, plus
    non-item kinds: workbenches, blueprints/frames, trees, crops, filth, growing zones,
    animals). Each category gets **one** action from the ones that make sense for it
    (apparel: wear / equip-haul / haul / ignore; food: eat / haul / ignore; corpse:
    butcher / bury / haul / ignore; filth: clean / ignore…), inherited down the tree
    like storage settings, with sensible defaults. "Ignore" = just set the pawn down.
    Anything specific is still done the vanilla way (drop beside it, right-click).
    - Work-giver actions (haul, clean, cut, build, bills) reuse `HandWorkOrder`; others
      (wear, equip, eat) build the vanilla job directly (`JobDefOf.Wear`, `Equip`,
      `Ingest`).
    - **Cursor preview** of the order before dropping: the rule tells what to show
      ("will wear: parka"). Full checks (reachable, assigned, reservable) need the pawn
      spawned, and a held pawn isn't, so the preview shows the rule's intent plus the
      checks that don't need the map (work type assigned/disabled), and the drop does the
      rest (warning if it still fails).
    - Settings stored per save (game component) or globally (mod settings): TBD.
  - **Enemies struggle**: cost much more to lift, or locked until a milestone.
  - Lifting several things at once.

## 7. Impressiveness & Factions
- Other factions have an **Awe** value toward the god, raised when their pawns
  (visitors, traders, caravans, raiders, prisoners) witness miracles. Good deeds
  build love; fearsome ones build fear.
- Awe thresholds: curious → pilgrims → gifts/tribute → **converted**.
- Pawns of a faction that believes can hold Faith and pray at the Shrine
  (pilgrims produce power). Other visitors don't.
- **World map as background**: believing factions trigger events. Converted
  factions give passive power, send gifts, and make requests (send a caravan,
  perform a miracle at their settlement). Lets a small colony become powerful.
- Raiders may break and flee from terrifying miracles.
- Prisoners: miracles lower recruitment resistance; with Ideology, hook into its
  conversion system.
- **End state**: every humanoid faction converted to your faith.
- *v2+*: miracles performed at other settlements (needs a generated map for the
  visit, e.g. as a quest site).

## 8. The Creature (later phase)
- "Anoint" miracle consecrates an animal as the god's Avatar.
- Default: intelligent-ish animals only. **Setting: any animal** ("Everyone fear
  the God's yorkshire terrier!").
- Grows over time; learning AI shaped by reward/punish with the cursor; learns
  miracles by watching; own alignment and look.
- Specifics decided at implementation time.

# Roadmap

### M0 — Project setup ✔
Mod folder layout, About.xml, C# project (net472, RimWorld 1.7 reference
assemblies, Harmony), build to `1.7/Assemblies`, link into the game's Mods folder,
ModSettings skeleton.

### M1 — Faith & Divine Power ✔
Faith need, Divine Power `GameComponent` + readout, Shrine building, Pray job,
prayer slots, group bonus, Worship time assignment.

### M2 — Miracles (toolbar casting) ✔
`MiracleDef` + levels + composable unlock requirements, influence (Shrine radius),
Water / Food / Wood level I (and II behind requirements). Cast from the bottom-bar
"Miracles" tab; witnesses gain a little faith.

### M3 — Hand of God mode ✔
Mode toggle + hold-key input, cursor FX, $P recognizer, default gestures, Book of
Gestures, player gesture recorder. Verified in-game. Settings: gesture tolerance,
pause between strokes, label position.

**Start with a Waylith spike** (small throwaway test, before building the system).
1.7's docs say nothing about cursor/input/UI-drawing APIs; they go through
`UnityStubs` and should work, but Hand of God mode depends on them. Verify in-game:
- Custom cursor texture swap (`Cursor.SetCursor` / RimWorld's custom cursor) and
  restoring the default.
- Reading mouse position, button down/drag/up, and a held modifier key every
  frame, without breaking vanilla selection, orders, or camera panning.
- Consuming input so a left-drag in the mode doesn't also draw a selection box.
- Drawing a screen-space trail (GUI lines/textures) following the mouse.
- Spawning flecks/particles at the mouse's map position every few frames
  (cursor glow), and the performance cost (frame profiler).
- Drawing a despawned thing at the cursor (needed in M6 for Hand moving).
Record results in `docs/WAYLITH_NOTES.md`.

### M4 — Faith dynamics ✔
Hardship / gratitude / witnessing events, `FaithTraitExtension` + vanilla trait
patches, Zealot / Sceptic, prayer requests (needs-driven + incidents).

### M5 — Entry & Temple (M5a ✔, M5a+ ✔, M5b temple pending in-game test)
Awakening storyline, Shrine research, Chosen People scenario, Temple room role +
tiers + holy furniture, Offering Bowl, Pylon.

### M6 — Alignment & Hand (M6a ✔, M6b self-tested, pending in-game test)
Alignment value + deeds + drift, alignment-tinted cursor, Hand moving (grab/carry/
place) with costs and lift capacity.

### v2
Faction Awe and conversion, throwing, Sacrifice Altar, alignment miracle variants
(divine food, transfer wounds…), more miracles, DLC integrations (Ideology,
Royalty meditation, Anomaly).

### v3
The Creature.

## Later / doors open
- **True Neutral** god with its own perks.
- **Ideology**: colony "inspired" by the god proposes precepts (e.g. an evil god's
  colony asks "would eating human flesh please you?"; answering yes adopts
  Cannibalism-approved or similar). Precepts reacting to alignment.

### Divine Power (global)
- A pool stored in a `GameComponent`, shown in a top-of-screen readout.
- **Cap**: flat 100 in M1 (placeholder). To decide: cap grows with believers
  and temple tier (proposal: base + per believer + temple tier bonus).
- Generated by colonists praying at the **Shrine**, a central object similar to an
  anima tree. It is built entirely from our own defs and comps (no Royalty
  dependency): pray spots in a radius, output scaling, visual growth.
- Own `JobDriver_Pray` (no Royalty meditation dependency). With Royalty active,
  the Shrine is also a meditation focus.
- Output scales with each worshipper's Faith.
- *Later*: bonuses when the shrine sits in a proper temple room with temple
  furniture around it (similar to Ideology ritual-room quality).

### Influence
- Miracles can only target cells inside the god's influence.
- Influence sources:
  - Shrine (large radius).
  - Pylon/obelisk building (extends influence; vulnerable, so it must be protected).
  - Very faithful pawns (small personal radius).
  - Pawns carrying a holy object.

## Open questions
See conversation log; resolved items get moved into the sections above.

## M5b notes
- Tier thresholds (impressiveness 30/60/100, holy 1/3/6) may be easy: the self-test's
  "temple" build already scored 98 impressiveness with masterwork sculptures. Revisit
  in the balance pass.
- **Offering balance (future)**: a rich colony can convert a hoard of silver into a
  full power pool instantly, and shedding wealth also lowers raid strength in
  RimWorld. Ideas: a cooldown, or diminishing returns when many offerings are made
  in a short time; variety (the god values different kinds of gifts, repeats are
  worth less).
- Assets: see `docs/ASSETS.md` for the catalog of placeholders to replace.

## God progression (idea, M6b feedback)
A "research" for the god's own powers, bought with prayer points (power gathered): an
ability tree where the player picks upgrades, e.g. cheaper miracles, more lift capacity,
**the ability to lift living things**, then **to lift enemies**, etc. Each costs an amount
of power to buy. To design later; it would also be the natural place to gate the
Hand's stronger abilities instead of giving them all at once.

## Sound (to do near the end)
SFX wanted for: drawing a gesture (while drawing / recognized / failed), each miracle
cast, lifting and setting down, offering, revelation. See `docs/ASSETS.md`.

## M6b notes (balance pass)
- **Lift capacity grows too fast (user playtest)**: a Chosen People start had 3
  believers at once, so 80 kg straight away, and hardly anything is heavier. Idea: scale
  capacity with total prayer points gathered (lifetime power) as well as believers, or
  make it a purchasable upgrade (see God progression). The user liked the trickle cost
  while holding: keep it.
- Hand costs are guesses: check that carrying a colonist across the base feels cheap
  enough to use for rescues but not free; items might be too cheap to matter (a whole
  steel stack costs ~1 power), which is intended ("helping haul").
- Capacity 35 + 15/believer means a lone believer can't lift a human (50 kg); needs 2.
  Big animals (thrumbo, elephant) need many believers, by design.
