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

### M5 — Entry & Temple
Awakening storyline, Shrine research, Chosen People scenario, Temple room role +
tiers + holy furniture, Offering Bowl, Pylon.

### M6 — Alignment & Hand
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
