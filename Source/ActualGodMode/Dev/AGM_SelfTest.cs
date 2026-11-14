using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// Automated in-game smoke test. Inert unless the game is launched with
    /// "-quicktest -agmselftest". Places a shrine, puts every colonist on the Worship
    /// schedule, runs the game for a few hours, logs results with "[AGM SelfTest]", then quits.
    /// </summary>
    public class AGM_SelfTest : GameComponent
    {
        private const string Arg = "agmselftest";
        private const string Tag = "[AGM SelfTest] ";
        private const int TestDurationTicks = GenDate.TicksPerHour * 6;
        private const int ReportIntervalTicks = GenDate.TicksPerHour;

        private static readonly bool Enabled = GenCommandLine.CommandLineArgPassed(Arg);

        private bool setUp;
        private bool finished;
        private int startTick;
        private int maxWorshippers;
        private Thing shrine;

        public AGM_SelfTest(Game game)
        {
        }

        public override void GameComponentUpdate()
        {
            if (!Enabled || finished)
            {
                return;
            }
            if (!setUp)
            {
                TrySetUp();
            }
            if (setUp && Find.TickManager.CurTimeSpeed != TimeSpeed.Ultrafast)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
            }
        }

        public override void GameComponentTick()
        {
            if (!Enabled || !setUp || finished)
            {
                return;
            }
            if (awakeningPhase)
            {
                AwakeningTick();
                return;
            }
            if (syncDone)
            {
                return;
            }
            CompShrine comp = shrine.TryGetComp<CompShrine>();
            if (comp.CurrentWorshippers > maxWorshippers)
            {
                maxWorshippers = comp.CurrentWorshippers;
            }
            int elapsed = Find.TickManager.TicksGame - startTick;
            if (elapsed > 0 && elapsed % ReportIntervalTicks == 0)
            {
                Report(elapsed);
            }
            if (elapsed >= TestDurationTicks)
            {
                Finish();
            }
        }

        private void TrySetUp()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned.ToList();
            if (colonists.Count == 0)
            {
                return;
            }

            Log.Message(Tag + "Setting up. Colonists: " + colonists.Count + ", devMode: " + Prefs.DevMode);
            foreach (Pawn p in colonists)
            {
                Log.Message(Tag + $"  {p.LabelShort}: faith need={(p.Faith() != null ? p.Faith().CurLevelPercentage.ToStringPercent() : "MISSING")}");
            }

            shrine = AGM_TestScenarios.EnsureShrine(map);
            if (shrine == null)
            {
                Fail("No room to place the shrine.");
                return;
            }
            Log.Message(Tag + $"Shrine placed at {shrine.Position}. Pray spots: {shrine.TryGetComp<CompShrine>().PraySpots.Count}");

            // Nothing unlocks before the god is revealed; once revealed, the starting miracles unlock.
            MiracleDef water = DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Water");
            MiracleTracker.Instance.CheckUnlocks();
            Check(!MiracleTracker.Instance.IsUnlocked(water), "no miracles unlock before the god is revealed");
            AwakeningTracker.Instance.MarkRevealedByScenario();
            MiracleTracker.Instance.CheckUnlocks();
            Check(MiracleTracker.Instance.IsUnlocked(water), "starting miracles unlock once revealed");

            // Nobody believes before the Awakening; converting gives them the Faith need.
            Check(colonists.All(p => p.Faith() == null), "non-believers have no Faith need");
            BelieverTracker.Instance.ConvertAllColonists();
            Check(colonists.All(p => p.Faith() != null), "converted colonists get the Faith need");

            foreach (Pawn p in colonists)
            {
                for (int hour = 0; hour < GenDate.HoursPerDay; hour++)
                {
                    p.timetable.SetAssignment(hour, AGM_DefOf.AGM_Worship);
                }
                p.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
            }

            startTick = Find.TickManager.TicksGame;
            setUp = true;
        }

        private void Report(int elapsed)
        {
            CompShrine comp = shrine.TryGetComp<CompShrine>();
            Log.Message(Tag + $"t+{elapsed / GenDate.TicksPerHour}h power={DivinePower.Instance.Power:F2} worshippers={comp.CurrentWorshippers}");
            foreach (Pawn p in shrine.Map.mapPawns.FreeColonistsSpawned)
            {
                Log.Message(Tag + $"  {p.LabelShort}: job={p.CurJobDef?.defName ?? "none"} faith={p.Faith()?.CurLevelPercentage.ToStringPercent()}");
            }
        }

        private int failures;

        private void Check(bool condition, string what)
        {
            if (!condition)
            {
                failures++;
            }
            Log.Message(Tag + (condition ? "  ok   " : "  FAIL ") + what);
        }

        private bool syncDone;

        private void Finish()
        {
            syncDone = true;
            DivinePower dp = DivinePower.Instance;
            Log.Message(Tag + $"Worship phase: power={dp.Power:F2} lifetime={dp.LifetimePower:F2} maxSimultaneousWorshippers={maxWorshippers}");
            Check(dp.Power > 0f, "worship generated power");
            Check(maxWorshippers == shrine.TryGetComp<CompShrine>().Slots, "worshippers filled all prayer slots");

            byte[] px = HandOfGodCursor.Rgba;
            int inside = (10 * HandOfGodCursor.Size + 5) * 4;
            int outside = (30 * HandOfGodCursor.Size + 30) * 4;
            Check(px[inside + 3] == 255 && px[inside] + px[inside + 1] + px[inside + 2] > 300,
                $"cursor pixels: inside opaque, in the {HandOfGodCursor.CurrentStyle} style ({px[inside]},{px[inside + 1]},{px[inside + 2]},{px[inside + 3]})");
            Check(px[outside + 3] == 0, "cursor pixels: corner transparent");
            HandOfGodCursor.Apply();
            HandOfGodCursor.Restore();

            try
            {
                RunGestureTests();
            }
            catch (System.Exception e)
            {
                Check(false, "gesture tests threw: " + e);
            }

            try
            {
                RunMiracleTests();
            }
            catch (System.Exception e)
            {
                Check(false, "miracle tests threw: " + e);
            }

            try
            {
                RunProgressionTests();
            }
            catch (System.Exception e)
            {
                Check(false, "progression tests threw: " + e);
            }

            try
            {
                RunFaithTests();
            }
            catch (System.Exception e)
            {
                Check(false, "faith tests threw: " + e);
            }

            try
            {
                RunTempleTests();
            }
            catch (System.Exception e)
            {
                Check(false, "temple tests threw: " + e);
            }

            try
            {
                RunInfluenceTrackTests();
            }
            catch (System.Exception e)
            {
                Check(false, "influence track tests threw: " + e);
            }

            StartAwakeningPhase();
        }

        /// <summary>M5b: temple tiers, conflicting use, offering bowl, pylon influence/tending/desecration.</summary>
        private void RunTempleTests()
        {
            Map map = shrine.Map;
            DivinePower dp = DivinePower.Instance;
            CompShrine comp = shrine.TryGetComp<CompShrine>();
            Log.Message(Tag + "Temple phase");

            Check(comp.Temple.tierIndex == 0 && comp.Slots == 2, "open-air shrine: tier 0, 2 prayer slots");
            float capBefore = dp.MaxPower;

            for (int tier = 1; tier <= 3; tier++)
            {
                TempleInfo t = AGM_TestScenarios.BuildTestTemple(map, tier);
                Log.Message(Tag + $"  built for tier {tier}: got '{t.Tier.label}' (tier {t.tierIndex}), enclosed {t.enclosed}, impressiveness {t.impressiveness:F1}, holy {t.holyFurniture:0.#}");
                if (tier == 1)
                {
                    Check(t.enclosed && t.tierIndex >= 1, "enclosed room with holy furniture becomes at least a chapel");
                    Check(comp.Slots == t.Tier.prayerSlots && comp.Slots > 2, $"temple tier raises prayer slots ({comp.Slots})");
                    Check(dp.MaxPower > capBefore, $"temple tier raises the power cap ({capBefore:F0} -> {dp.MaxPower:F0})");
                }
            }

            // A bed in the room: not a temple
            TempleInfo current = comp.Temple;
            IntVec3 bedCell = current.room.Cells.FirstOrDefault(c => c.Standable(map) && c.GetFirstBuilding(map) == null && (c - shrine.Position).LengthHorizontal > 3f);
            Thing bed = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog), bedCell, map);
            comp.InvalidateTemple();
            Check(comp.Temple.conflictingUse && comp.Temple.tierIndex == 0, "a bed in the room stops it counting as a temple");
            bed.Destroy();
            comp.InvalidateTemple();

            // Offering bowl: 100 silver -> 10 power
            IntVec3 bowlCell = NearFreeCell(map.mapPawns.FreeColonistsSpawned.First(), map);
            var bowl = (Building_OfferingBowl)GenSpawn.Spawn(ThingMaker.MakeThing(AGM_TempleDefOf.AGM_OfferingBowl, ThingDefOf.WoodLog), bowlCell, map);
            bowl.SetFaction(Faction.OfPlayer);
            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = 100;
            GenSpawn.Spawn(silver, bowlCell, map);
            dp.DebugSet(0f);
            float gained = bowl.Offer();
            Check(Mathf.Abs(gained - 10f) < 0.5f && bowl.Contents.Count == 0, $"offering 100 silver gives ~10 power and empties the bowl (+{gained:F1})");

            // Pylon: extends influence while tended; goes dark untended; believers come to tend it
            float radius = comp.Props.influenceRadius;
            IntVec3 pylonCell = map.AllCells.FirstOrDefault(c => c.Standable(map) && !c.Fogged(map) && c.GetFirstBuilding(map) == null
                && (c - shrine.Position).LengthHorizontal > radius + 10f && (c - shrine.Position).LengthHorizontal < radius + 20f);
            Thing pylon = GenSpawn.Spawn(ThingMaker.MakeThing(AGM_TempleDefOf.AGM_Pylon, ThingDefOf.BlocksGranite), pylonCell, map);
            pylon.SetFaction(Faction.OfPlayer);
            CompPylon pc = pylon.TryGetComp<CompPylon>();
            IntVec3 nearPylon = pylonCell + new IntVec3(3, 0, 0);
            Check(InfluenceUtility.InInfluence(nearPylon, map), "a tended pylon extends the god's influence");
            pc.DebugSetTended(0f);
            Check(!InfluenceUtility.InInfluence(nearPylon, map), "an untended (dark) pylon extends nothing");
            pc.DebugSetTended(0.3f);
            Pawn tender = map.mapPawns.FreeColonistsSpawned.First(p => p.HasFaith());
            Check(new WorkGiver_TendPylon().HasJobOnThing(tender, pylon), "believers have work tending a pylon that is wearing off");
            pc.Tend();
            Check(pc.Active && pc.Tended >= 1f, "tending restores the pylon");

            // Destroyed by enemies: believers feel it
            pylon.Kill();
            Check(tender.Faith().History.Entries.Any(e => e.label == "AGM_Cause_PylonDestroyed".Translate().ToString()), "a destroyed pylon is felt by believers (faith history)");
        }

        private void Complete()
        {
            finished = true;
            Log.Message(Tag + $"RESULT: {(failures == 0 ? "PASS" : "FAIL")} ({failures} failure(s))");
            Root.Shutdown();
        }

        // ---- Awakening phase (needs real ticks: the trance pawn walks and draws) ----

        private bool awakeningPhase;
        private int awakeningStartTick;
        private Pawn awakeningPawn;
        private const int AwakeningTimeoutTicks = 6000;

        private void StartAwakeningPhase()
        {
            Log.Message(Tag + "Awakening phase");
            AwakeningTracker t = AwakeningTracker.Instance;
            ResearchProjectDef research = AGM_AwakeningDefOf.AGM_ShrineResearch;
            try
            {
                t.ResetToDormant();
                Check(research.IsHidden && !research.PrerequisitesCompleted, "shrine research hidden and locked before the Revelation");

                awakeningPawn = shrine.Map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => !p.Downed && p.Awake() && !p.InMentalState);
                Check(awakeningPawn != null && t.Begin(awakeningPawn) && t.Stage == AwakeningStage.Whispers, "awakening begins with a chosen pawn");
                string topic = t.PickWhisperTopic();
                Check(topic != "Generic", $"whispers prefer something the colony lived through (topic: {topic})");
                int logBefore = Find.PlayLog.AllEntries.Count;
                for (int i = 0; i < AwakeningTuningDef.Get.whispersNeeded; i++)
                {
                    t.Whisper();
                }
                Check(Find.PlayLog.AllEntries.Count - logBefore == AwakeningTuningDef.Get.whispersNeeded, "each whisper adds a social log entry");
                Check(t.Stage == AwakeningStage.Trance, "after the whispers comes the trance stage");
                Check(t.TryStartTrance() && awakeningPawn.MentalStateDef == AGM_AwakeningDefOf.AGM_DivineTrance, "chosen pawn enters the divine trance");
            }
            catch (System.Exception e)
            {
                Check(false, "awakening setup threw: " + e);
                Complete();
                return;
            }
            awakeningPhase = true;
            awakeningStartTick = Find.TickManager.TicksGame;
        }

        private void AwakeningTick()
        {
            AwakeningTracker t = AwakeningTracker.Instance;
            bool timedOut = Find.TickManager.TicksGame - awakeningStartTick > AwakeningTimeoutTicks;
            bool done = t.Stage == AwakeningStage.AwaitingRevelation && !awakeningPawn.InMentalState;
            if (!done && !timedOut)
            {
                return;
            }
            awakeningPhase = false;
            Map map = shrine.Map;
            int signs = map.listerThings.ThingsOfDef(AGM_AwakeningDefOf.AGM_Filth_DivineSign).Count;
            Check(!timedOut, $"trance pawn drew the sign (took {Find.TickManager.TicksGame - awakeningStartTick} ticks)");
            Check(signs > 0, $"the strange drawing exists on the map ({signs})");
            Check(!awakeningPawn.InMentalState, "trance ends after drawing");

            int revealAt = t.NextEventTick;
            float longitude = Find.WorldGrid.LongLatOf(map.Tile).x;
            long revealAbs = revealAt + (Find.TickManager.TicksAbs - Find.TickManager.TicksGame);
            int hour = GenDate.HourOfDay(revealAbs, longitude);
            float hoursAway = (revealAt - Find.TickManager.TicksGame) / (float)GenDate.TicksPerHour;
            Check(hour == AwakeningTuningDef.Get.revelationHour && hoursAway >= AwakeningTuningDef.Get.revelationDelayHours - 0.1f && hoursAway <= 28.1f,
                $"revelation scheduled for the next dawn ({hour}:00 local, in {hoursAway:F1} h)");

            ResearchProjectDef research = AGM_AwakeningDefOf.AGM_ShrineResearch;
            t.Reveal();
            Check(t.Revealed && !research.IsHidden && research.PrerequisitesCompleted, "revelation reveals and unlocks the shrine research");

            new ScenPart_StartWithShrine().PostGameStart();
            Check(research.IsFinished, "Chosen People scenario part finishes the shrine research");

            // The god's path: steps already done complete on their own; it waits on the first answered prayer
            GodGuideTracker guide = GodGuideTracker.Instance;
            guide.DebugReset();
            guide.Start();
            for (int i = 0; i < 6; i++)
            {
                guide.CheckProgress();
            }
            Check(guide.Step == GuideStep.AnswerPrayer, $"guide skips completed steps and waits for a first answered prayer (at {guide.Step})");
            guide.Notify_PrayerAnswered();
            guide.CheckProgress();
            Check(guide.Step == GuideStep.Done, "guide completes after a prayer is answered");

            try
            {
                RunAlignmentTests();
            }
            catch (System.Exception e)
            {
                Check(false, "alignment tests threw: " + e);
            }
            try
            {
                RunHandTests();
            }
            catch (System.Exception e)
            {
                Check(false, "hand tests threw: " + e);
            }
            Complete();
        }

        /// <summary>M6a: deeds, bands, Lightning classification, punishment, fear, cursor style, deliverance prayer.</summary>
        private void RunAlignmentTests()
        {
            Log.Message(Tag + "Alignment phase");
            Map map = shrine.Map;
            AlignmentTracker alignment = AlignmentTracker.Instance;
            DivinePower dp = DivinePower.Instance;
            MiracleDef lightning = AGM_AlignmentDefOf.AGM_Miracle_Lightning;
            MiracleDef heal = DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Heal");
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned.Where(p => !p.Dead).ToList();
            Pawn a = colonists[0];
            Pawn b = colonists[1];

            // Bands
            alignment.DebugSet(30f);
            Check(alignment.Band == AlignmentBand.Kind && alignment.GratitudeFactor > 1f, "+30 is 'kind', with a gratitude bonus");
            alignment.DebugSet(-70f);
            Check(alignment.Band == AlignmentBand.Wrathful && HandOfGodCursor.CurrentStyle == HandStyle.Evil, "-70 is 'wrathful', and the hand turns evil");
            byte[] evilPx = HandOfGodCursor.Rgba;
            int inside = (10 * HandOfGodCursor.Size + 5) * 4;
            Check(evilPx[inside] > evilPx[inside + 1] * 2 && evilPx[inside + 3] == 255, "evil cursor is red");
            alignment.DebugSet(0f);
            Check(HandOfGodCursor.CurrentStyle == HandStyle.Neutral, "0 is neutral (pale hand)");

            // Healing is good
            Teleport(a, shrine.Position + new IntVec3(2, 0, 2), map);
            a.TakeDamage(new DamageInfo(DamageDefOf.Cut, 5f, armorPenetration: 99f)); // armor must not absorb it
            dp.Add(dp.MaxPower);
            MiracleUtility.TryCast(heal, a.Position, map);
            Check(alignment.Value > 0.5f, $"healing a follower is a good deed ({alignment.Value:F1})");

            // Lightning: who is struck decides the deed
            MiracleTracker.Instance.RecordEvent(MiracleTracker.Event_Raid);
            Check(MiracleTracker.Instance.IsUnlocked(lightning), "lightning unlocks after surviving a raid");
            Check(WrathJudgement.Classify(a) == WrathJudgement.Target.OwnFollower, "a calm colonist counts as 'own follower'");

            alignment.DebugSet(0f);
            dp.Add(dp.MaxPower);
            MiracleUtility.TryCast(lightning, a.Position, map);
            Check(alignment.Value <= AlignmentTuningDef.Get.smiteOwnFollower + 0.5f, $"striking your own follower is evil ({alignment.Value:F1})");

            // Punishment: strike someone mid-break; a witness nearby is cowed
            alignment.DebugSet(0f);
            Teleport(b, shrine.Position + new IntVec3(-2, 0, -2), map);
            Teleport(a, shrine.Position + new IntVec3(4, 0, -2), map); // 6 cells away: outside the strike even after the random +-2
            b.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Wander_Sad, forced: true);
            Check(WrathJudgement.Classify(b) == WrathJudgement.Target.Punishment, "a colonist in a (non-violent) break counts as punishment");
            dp.Add(dp.MaxPower);
            MiracleUtility.TryCast(lightning, b.Position, map);
            Check(!b.InMentalState, "punishment ends the break");
            Check(a.health.hediffSet.HasHediff(AGM_AlignmentDefOf.AGM_WontBeNext), "a witness of punishment is cowed (lower break threshold)");
            Check(alignment.Value < 0f && alignment.Value > AlignmentTuningDef.Get.smiteOwnFollower, $"punishment is evil, but less than striking an innocent ({alignment.Value:F1})");

            // Evil god: witnessing wrath strengthens faith out of fear
            alignment.DebugSet(-40f);
            Teleport(a, shrine.Position + new IntVec3(4, 0, 0), map);
            a.Faith().CurLevel = 0.5f; // room to grow
            float faithBefore = a.Faith().CurLevel;
            dp.Add(dp.MaxPower);
            MiracleUtility.TryCast(lightning, shrine.Position + new IntVec3(-4, 0, 3), map);
            Check(a.Faith().CurLevel > faithBefore && a.needs.mood.thoughts.memories.GetFirstMemoryOfDef(AGM_AlignmentDefOf.AGM_FearedWrath) != null,
                "under an evil god, witnessing wrath raises faith (fear) and weighs on mood");

            // Bolts: with a rank in the lightning track a charge holds 2 bolts, one per click.
            PowerTrackDef boltTrack = DefDatabase<PowerTrackDef>.GetNamed("AGM_Track_Lightning");
            GodProgression.Instance.DebugSetRank(boltTrack, 1);
            dp.Add(dp.MaxPower);
            float boltPower = dp.Power;
            var bolts = new MiracleChannel(lightning, map);
            bolts.Press(shrine.Position + new IntVec3(-4, 0, 3), 0f);
            bool oneLeft = bolts.units == 1 && !bolts.finished;
            bolts.Update(shrine.Position + new IntVec3(-4, 0, 3), 1f); // holding doesn't fire again
            bool heldOnce = bolts.units == 1;
            bolts.Release();
            bolts.Press(shrine.Position + new IntVec3(-4, 0, -3), 2f);
            Check(oneLeft && heldOnce && bolts.finished && bolts.units == 2 && Mathf.Abs(boltPower - dp.Power - lightning.Stats.cost) < 0.01f,
                $"a 2-bolt charge fires one bolt per click, then ends ({bolts.units} bolts, {boltPower - dp.Power:F0} power for {lightning.Stats.cost})");
            GodProgression.Instance.DebugSetRank(boltTrack, 0);

            // Deliverance prayer: an enemy close by
            Faction enemy = Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
            if (enemy != null)
            {
                Pawn raider = PawnGenerator.GeneratePawn(PawnKindDefOf.Villager, enemy);
                GenSpawn.Spawn(raider, CellFinder.RandomClosewalkCellNear(a.Position, map, 4), map);
                Check(PrayerRequestWorker_Hunted.Threat(a) != null, "an enemy nearby can make a believer pray for deliverance");
                Check(WrathJudgement.Classify(raider) == WrathJudgement.Target.Enemy, "an armed enemy counts as a just target");

                // The bolt: pinpoint, no fire; downs an unarmored baseliner; a neighbour is untouched.
                var bolt = (MiracleWorker_Lightning)lightning.Worker;
                Check(bolt.DamageTo(raider) / bolt.ResistFactor(raider) >= 49.9f && bolt.DamageTo(a) <= 10.01f,
                    $"a bolt smites an enemy hard and only stings your own ({bolt.DamageTo(raider):F0} vs {bolt.DamageTo(a):F0})");
                raider.apparel?.DestroyAll();
                raider.story?.traits?.allTraits.Clear();
                raider.health.hediffSet.DirtyCache();
                Teleport(raider, shrine.Position + new IntVec3(-5, 0, 5), map);
                IntVec3 strikeAt = raider.Position;
                Teleport(a, strikeAt + IntVec3.East, map); // right next to the target, as in melee
                float aHealth = a.health.summaryHealth.SummaryHealthPercent;
                int FiresNear() => map.listerThings.ThingsOfDef(ThingDefOf.Fire).Count(f => (f.Position - strikeAt).LengthHorizontal < 4f);
                int firesBefore = FiresNear();
                dp.Add(dp.MaxPower);
                MiracleUtility.TryCast(lightning, strikeAt, map);
                Log.Message(Tag + $"  a random raider (any genes), stripped, after one bolt: downed {raider.Downed} (pain {raider.health.hediffSet.PainTotal:P0}, dead: {raider.Dead}, healthScale {raider.HealthScale:F2}, injuries: {string.Join(", ", raider.health.hediffSet.hediffs.OfType<Hediff_Injury>().Select(h => h.Part?.Label + " " + h.Severity.ToString("F1")))})");
                Check(Mathf.Abs(a.health.summaryHealth.SummaryHealthPercent - aHealth) < 0.001f, "the bolt is pinpoint: the colonist next to it is untouched");

                // Sample: more unarmored enemies (all should go down), and some in marine armor (logged).
                string SmiteSample(bool armored, out int downed)
                {
                    downed = 0;
                    var pains = new List<string>();
                    for (int i = 0; i < 6; i++)
                    {
                        Pawn e = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, enemy, forceBaselinerChance: 1f, allowAddictions: false));
                        e.story?.traits?.allTraits.Clear();
                        GenSpawn.Spawn(e, GenRadial.RadialCellsAround(shrine.Position + new IntVec3(-5, 0, 5), 4f, true).First(c => c.InBounds(map) && c.Standable(map) && c.GetFirstPawn(map) == null), map);
                        e.apparel?.DestroyAll();
                        if (armored)
                        {
                            foreach (string name in new[] { "Apparel_PowerArmor", "Apparel_PowerArmorHelmet" })
                            {
                                ThingDef ad = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                                if (ad != null)
                                {
                                    e.apparel.Wear((Apparel)ThingMaker.MakeThing(ad, GenStuff.DefaultStuffFor(ad)));
                                }
                            }
                        }
                        e.health.hediffSet.DirtyCache();
                        dp.Add(dp.MaxPower);
                        MiracleUtility.TryCast(lightning, e.Position, map);
                        if (e.Downed || e.Dead)
                        {
                            downed++;
                        }
                        pains.Add(e.Dead ? "dead" : e.health.hediffSet.PainTotal.ToString("P0"));
                        RemoveTestPawn(e);
                    }
                    return string.Join(" ", pains);
                }
                Teleport(a, shrine.Position + new IntVec3(6, 0, -6), map); // nobody else where the samples stand
                string bare = SmiteSample(false, out int bareDowned);
                Check(bareDowned == 6, $"bolts down unarmored enemies ({bareDowned}/6; pain: {bare})");
                string armoredPains = SmiteSample(true, out int armoredDowned);
                Log.Message(Tag + $"  marine armor: {armoredDowned}/6 downed by one bolt (pain: {armoredPains})");
                Check(FiresNear() == firesBefore && !raider.HasAttachment(ThingDefOf.Fire), "the bolt starts no fires");
                RemoveTestPawn(raider);

                // Own colonist: a sting on the torso that never scars.
                HealInjuries(a);
                dp.Add(dp.MaxPower);
                MiracleUtility.TryCast(lightning, a.Position, map);
                List<Hediff_Injury> stings = a.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList();
                Check(!a.Downed && !a.Dead && stings.Count > 0 && stings.Sum(h => h.Severity) <= 15f
                    && stings.All(h => h.Part == a.RaceProps.body.corePart && h.TryGetComp<HediffComp_GetsPermanent>()?.permanentDamageThreshold >= 9999f),
                    $"your own colonist only gets a torso sting that won't scar ({stings.Sum(h => h.Severity):F1})");
                HealInjuries(a);

                // A mech: burned and EMP-stunned.
                if (Faction.OfMechanoids != null)
                {
                    PawnKindDef mechKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Mech_Lancer");
                    if (mechKind != null)
                    {
                        Pawn mech = PawnGenerator.GeneratePawn(mechKind, Faction.OfMechanoids);
                        GenSpawn.Spawn(mech, shrine.Position + new IntVec3(-5, 0, 5), map);
                        dp.Add(dp.MaxPower);
                        MiracleUtility.TryCast(lightning, mech.Position, map);
                        Check(mech.Dead || mech.stances.stunner.Stunned, "a bolt EMP-stuns a mech");
                        RemoveTestPawn(mech);
                    }
                }
            }
            Log.Message(Tag + "  deeds ledger:\n" + alignment.Deeds.TooltipSection("AGM_Alignment_LedgerHeader", asPercent: false));
        }

        /// <summary>M6b: the Hand lifts, carries and sets down; capacity, costs, influence, bed deed, game-over guard.</summary>
        private void RunHandTests()
        {
            Log.Message(Tag + "Hand phase");
            Map map = shrine.Map;
            GodHand hand = GodHand.On(map);
            DivinePower dp = DivinePower.Instance;
            AlignmentTracker alignment = AlignmentTracker.Instance;
            HandTuningDef t = HandTuningDef.Get;
            Check(hand != null, "every map has the god's hand");
            GodProgression prog = GodProgression.Instance;
            PowerTrackDef strength = AGM_ProgressionDefOf.AGM_Track_Strength;
            float capacity = GodHand.Capacity;
            Check(prog.RankOf(strength) == 0 && Mathf.Approximately(capacity, strength.baseValue),
                $"the starting hand lifts {strength.baseValue} kg, whatever the believers ({capacity:F0} kg, {BelieverTracker.Instance.LivingCount} believers)");

            // Items: a light stack is lifted whole, carried (costing power over time), and set down.
            IntVec3 near = CellFinder.RandomClosewalkCellNear(shrine.Position + new IntVec3(3, 0, 0), map, 2);
            Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
            steel.stackCount = 20;
            GenPlace.TryPlaceThing(steel, near, map, ThingPlaceMode.Direct, out steel);
            dp.DebugSet(dp.MaxPower);
            float before = dp.Power;
            Thing under = GodHand.GrabbableAt(steel.Position, map);
            Check(under == steel || under is Pawn, $"the hand finds the steel under the cursor (or a pawn standing on it: {under.ToStringSafe()})");
            AcceptanceReport canGrab = hand.CanGrab(steel);
            Check(canGrab.Accepted && hand.TryGrab(steel, drag: false), "the hand lifts a stack of steel " + canGrab.Reason);
            Check(hand.Holding && !steel.Spawned && dp.Power < before, $"lifted steel leaves the map and costs power ({before - dp.Power:F2})");
            if (!hand.Holding)
            {
                return;
            }
            float beforeHold = dp.Power;
            hand.ChargeHold(GenDate.TicksPerHour);
            Check(dp.Power < beforeHold, $"holding costs power per hour ({beforeHold - dp.Power:F2}/h for {GodHand.MassOf(hand.Held):F0} kg)");
            IntVec3 dest = CellFinder.RandomClosewalkCellNear(shrine.Position + new IntVec3(-3, 0, 2), map, 2);
            hand.MoveTo(dest);
            Check(hand.Place(dest) && !hand.Holding && steel.Spawned && steel.Position.InHorDistOf(dest, 2f), "the hand sets the steel down where it points");

            // A stack heavier than the hand can lift is taken in part.
            ThingDef heavyDef = DefDatabase<ThingDef>.AllDefs.FirstOrDefault(d => d.category == ThingCategory.Item && d.EverHaulable
                && d.stackLimit > 1 && !d.MadeFromStuff && d.GetStatValueAbstract(StatDefOf.Mass) * d.stackLimit > capacity * 1.2f);
            if (heavyDef != null)
            {
                Thing heavy = ThingMaker.MakeThing(heavyDef);
                heavy.stackCount = heavyDef.stackLimit;
                GenPlace.TryPlaceThing(heavy, near, map, ThingPlaceMode.Near, out heavy);
                int liftable = GodHand.LiftableCount(heavy);
                int total = heavy.stackCount;
                Log.Message(Tag + $"  {heavyDef.defName} unit mass {GodHand.UnitMass(heavy):F2} kg; can lift {liftable} of {total}");
                hand.TryGrab(heavy, drag: true);
                Check(hand.Holding && hand.Held.stackCount == liftable && heavy.Spawned && heavy.stackCount == total - liftable,
                    "a too-heavy stack is lifted in part, the rest stays");
                hand.Place(near);
            }

            // The soft border: just outside, things cost more and the hand is weaker; far out, nothing reaches.
            IntVec3 justOut = map.AllCells.Where(c => Mathf.Abs(InfluenceUtility.DistanceOutside(c, map) - 1f) < 0.3f && c.Standable(map)).FirstOrFallback(IntVec3.Invalid);
            IntVec3 twoOut = map.AllCells.Where(c => Mathf.Abs(InfluenceUtility.DistanceOutside(c, map) - 2.5f) < 0.3f && c.Standable(map)).FirstOrFallback(IntVec3.Invalid);
            IntVec3 beyond = map.AllCells.Where(c => !InfluenceUtility.InReach(c, map) && c.Standable(map)).FirstOrFallback(IntVec3.Invalid);
            if (justOut.IsValid)
            {
                float f1 = InfluenceUtility.CostFactor(justOut, map);
                MiracleDef water = DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Water");
                Check(f1 > 1.2f && f1 < 2f && MiracleUtility.CostAt(water, justOut, map) > water.Stats.cost
                    && MiracleChannel.UnitCostAt(water, justOut, map) > MiracleChannel.BaseUnitCost(water),
                    $"one cell outside the influence a miracle still works but costs more (x{f1:F2})");
                Check(MiracleUtility.CanCast(DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Water"), justOut, map).Accepted, "a miracle can be cast just outside the influence");
            }
            if (twoOut.IsValid)
            {
                Check(InfluenceUtility.CostFactor(twoOut, map) > InfluenceUtility.CostFactor(justOut.IsValid ? justOut : twoOut, map) && GodHand.CapacityAt(twoOut, map) < GodHand.Capacity * 0.5f,
                    $"further out costs climb and the hand weakens (x{InfluenceUtility.CostFactor(twoOut, map):F1}, {GodHand.CapacityAt(twoOut, map):F0} kg)");
            }
            if (beyond.IsValid)
            {
                Check(!MiracleUtility.CanCast(DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Water"), beyond, map).Accepted, "far beyond the influence nothing can be cast");
            }

            // Too far for what is held: it drops at the last cell where the hand could hold it.
            Thing silver = GenSpawn.Spawn(ThingDefOf.Silver, near, map);
            hand.TryGrab(silver, drag: false);
            if (beyond.IsValid)
            {
                hand.MoveTo(beyond);
                Check(!hand.Holding && silver.Spawned && InfluenceUtility.InReach(silver.Position, map), "moving out of the god's reach drops the held thing where the hand could still hold it");
            }
            hand.Place(near);

            // Out of power: the held thing is set down.
            hand.TryGrab(silver, drag: false);
            dp.DebugSet(0f);
            hand.ChargeHold(60);
            Check(!hand.Holding && silver.Spawned, "running out of power sets the held thing down");

            // Light touch: a rank makes lifting cheaper.
            float fullPrice = GodHand.PickupCost(silver, 10f);
            prog.DebugSetRank(AGM_ProgressionDefOf.AGM_Track_LightTouch, 1);
            Check(GodHand.PickupCost(silver, 10f) < fullPrice * 0.8f && GodHand.HoldCostPerHour(silver, 10f) < t.itemHoldCostPerKgPerHour * 10f,
                $"light touch makes lifting and holding cheaper ({fullPrice:F2} -> {GodHand.PickupCost(silver, 10f):F2})");
            prog.DebugSetRank(AGM_ProgressionDefOf.AGM_Track_LightTouch, 0);

            // The starting hand can't lift the living; Strength + Lift living things can.
            Pawn standing = map.mapPawns.FreeColonistsSpawned.First(p => !p.Dead && !p.Downed);
            dp.DebugSet(dp.MaxPower);
            Check(!hand.CanGrab(standing).Accepted && GodHand.GrabbableAt(standing.Position, map) is Pawn == (standing.Position.GetFirstItem(map) == null),
                "the starting hand can't lift a colonist: " + hand.CanGrab(standing).Reason);
            AcceptanceReport lockedLift = prog.CanBuy(AGM_ProgressionDefOf.AGM_Track_LiftLiving);
            Check(!lockedLift.Accepted && lockedLift.Reason.IndexOf(AGM_ProgressionDefOf.AGM_Track_Strength.label, System.StringComparison.OrdinalIgnoreCase) >= 0,
                $"lift living things needs Strength first, and says so ({lockedLift.Reason?.Replace("\n", " ")})");
            Check(AGM_DefOf.AGM_Shrine.building?.relatedBuildCommands?.Contains(AGM_TempleDefOf.AGM_Pylon) == true,
                "the shrine offers to build temple things (pylon)");
            Check(HarmonyLib.Harmony.GetPatchInfo(HarmonyLib.AccessTools.Method(typeof(TooltipGiverList), nameof(TooltipGiverList.DispenseAllThingTooltips)))
                ?.Prefixes.Any(p => p.owner == ActualGodModeMod.PackageId) == true, "map tooltips are hidden while the hand is out (patched)");
            Check(prog.TryBuy(strength) && GodHand.Capacity > 60f, $"strength rank 1 lifts a person ({GodHand.Capacity:F0} kg)");
            dp.DebugSet(dp.MaxPower);
            Check(prog.TryBuy(AGM_ProgressionDefOf.AGM_Track_LiftLiving) && hand.CanGrab(standing).Accepted, "after buying lift living things the hand can take a colonist");

            // A downed colonist carried to a bed: tucked in, and a small good deed. The game doesn't end while they're lifted.
            Pawn colonist = map.mapPawns.FreeColonistsSpawned.First(p => !p.Dead && !p.Downed);
            Teleport(colonist, shrine.Position + new IntVec3(2, 0, 3), map);
            Building_Bed bed = AGM_TestScenarios.SetUpHandTest(map); // downs the first standing colonist
            Check(colonist.Downed, "the hand test setup downs a colonist (anesthetic)");
            Log.Message(Tag + $"  colonist mass {GodHand.MassOf(colonist):F0} kg, capacity {GodHand.Capacity:F0} kg");
            if (GodHand.MassOf(colonist) > GodHand.Capacity)
            {
                Log.Message(Tag + "  (colonist heavier than strength rank 1: skipping the colonist checks)");
                colonist.health.RemoveHediff(colonist.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Anesthetic));
                RunHandWorkOrderTests(map, hand, colonist);
                return;
            }
            dp.DebugSet(dp.MaxPower);
            alignment.DebugSet(0f);
            AcceptanceReport canLift = hand.CanGrab(colonist);
            Check(hand.TryGrab(colonist, drag: true) && !colonist.Spawned, $"the hand lifts a colonist {canLift.Reason} (holding {hand.Held.ToStringSafe()}, power {DivinePower.Instance.Power:F0}, in influence {InfluenceUtility.InInfluence(colonist.PositionHeld, map)}, at {colonist.PositionHeld} shrine {shrine.Position} spawned {shrine.Spawned} shrines {map.listerThings.ThingsOfDef(AGM_DefOf.AGM_Shrine).Count}, outside {InfluenceUtility.DistanceOutside(colonist.PositionHeld, map):F1}, reach {InfluenceUtility.InReach(colonist.PositionHeld, map)})");
            Check(colonist.needs.mood.thoughts.memories.GetFirstMemoryOfDef(AGM_HandDefOf.AGM_LiftedStartled) != null,
                "a person lifted by a neutral god is startled (flavor thought)");
            Check(map.mapPawns.FreeColonists.Contains(colonist), "a lifted colonist still counts as on the map");
            Find.GameEnder.CheckOrUpdateGameOver();
            Check(!Find.GameEnder.gameEnding, "the game doesn't end while a colonist is in the hand");
            hand.MapComponentTick(); // the held pawn ticks inside the hand
            hand.MoveTo(bed.Position);
            hand.Place(bed.Position);
            Check(colonist.Spawned && colonist.InBed() && colonist.CurrentBed() == bed, "a downed colonist set down on a bed is tucked in");
            Check(alignment.Value > 0f, $"carrying a downed colonist to bed is a good deed ({alignment.Value:F1})");
            hand.TryGrab(colonist, drag: false);
            hand.Place(bed.Position);
            Check(Mathf.Approximately(alignment.Value, t.carryToBedDeed), "moving them again from the bed earns nothing more");
            colonist.health.RemoveHediff(colonist.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Anesthetic));

            RunHandWorkOrderTests(map, hand, colonist);
            RunDropRuleTests(map, hand);
            RunDangerDropTests(map, hand, colonist);
        }

        /// <summary>Drop rules: categories, inheritance, and each action (wear, equip, eat, haul, just set down).</summary>
        private void RunDropRuleTests(Map map, GodHand hand)
        {
            Log.Message(Tag + "  drop rules");
            DropRules rules = DropRules.Instance;
            Check(rules != null, "drop rules exist in the game");
            if (rules == null)
            {
                return;
            }
            rules.ResetAll();
            AGM_TestScenarios.DropRulesTestSet set = AGM_TestScenarios.SetUpDropRulesTest(map);
            if (set.parka == null || set.rifle == null || set.meal == null || set.steel == null)
            {
                Log.Message(Tag + "  (no room for the drop rules test things: skipping)");
                return;
            }
            DropCategoryDef Cat(string name) => DefDatabase<DropCategoryDef>.GetNamed(name);
            Check(DropCategoryDef.For(set.parka) == Cat("AGM_Drop_Apparel") && DropCategoryDef.For(set.rifle) == Cat("AGM_Drop_Weapons")
                && DropCategoryDef.For(set.meal) == Cat("AGM_Drop_Food") && DropCategoryDef.For(set.steel) == Cat("AGM_Drop_Items")
                && DropCategoryDef.For(shrine) == Cat("AGM_Drop_Buildings") && DropCategoryDef.ForCell(shrine.Position, map) == Cat("AGM_Drop_Ground"),
                "drop categories: parka = apparel, rifle = weapons, meal = food, steel = items, shrine = buildings, else ground");
            Check(DefDatabase<DropCategoryDef>.AllDefsListForReading.All(c => rules.Effective(c) == DropAction.Work),
                "by default every drop does the work there (as without rules)");
            rules.Set(Cat("AGM_Drop_Items"), DropAction.Haul);
            rules.Set(Cat("AGM_Drop_Buildings"), DropAction.Ignore);
            Check(rules.Effective(Cat("AGM_Drop_Apparel")) == DropAction.Haul && rules.Effective(Cat("AGM_Drop_Workbenches")) == DropAction.Ignore
                && rules.Explicit(Cat("AGM_Drop_Apparel")) == null, "a category with nothing set takes its parent's action");
            rules.Set(Cat("AGM_Drop_Apparel"), DropAction.Wear);
            Check(rules.Effective(Cat("AGM_Drop_Apparel")) == DropAction.Wear && rules.Effective(Cat("AGM_Drop_Food")) == DropAction.Haul,
                "a category's own action wins over its parent's");
            rules.Set(Cat("AGM_Drop_Filth"), DropAction.Wear);
            Check(rules.Effective(Cat("AGM_Drop_Filth")) == DropAction.Work, "an action that makes no sense for a category can't be set");
            rules.ResetAll();

            Pawn worker = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => !p.Dead && !p.Downed && !p.InMentalState
                && !p.WorkTagIsDisabled(WorkTags.Violent) && !p.WorkTagIsDisabled(WorkTags.Shooting) && !p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)
                && HandWorkOrder.CanTakeOrders(p) && GodHand.MassOf(p) <= GodHand.Capacity);
            if (worker == null)
            {
                Log.Message(Tag + "  (no colonist to drop for the drop rules: skipping the actions)");
                return;
            }
            JobDef DropOn(Thing on, out string preview)
            {
                worker.jobs.StopAll();
                DivinePower.Instance.DebugSet(DivinePower.Instance.MaxPower);
                hand.TryGrab(worker, drag: false);
                hand.MoveTo(on.Position);
                preview = DropRuleOrder.Preview(worker, on.Position, map, out string problem) + (problem != null ? " / " + problem : "");
                hand.Place(on.Position, order: true);
                JobDef job = worker.CurJob?.playerForced == true ? worker.CurJobDef : null;
                worker.jobs.StopAll();
                return job;
            }

            rules.Set(Cat("AGM_Drop_Apparel"), DropAction.Wear);
            JobDef wear = DropOn(set.parka, out string wearPreview);
            Check(wear == JobDefOf.Wear && wearPreview.IndexOf(set.parka.def.label, System.StringComparison.OrdinalIgnoreCase) >= 0, $"apparel rule 'wear': set down on a parka, they put it on ({wear?.defName}; '{wearPreview}')");

            rules.Set(Cat("AGM_Drop_Weapons"), DropAction.Equip);
            JobDef equip = DropOn(set.rifle, out string equipPreview);
            // Standing on it, the equip job finishes at once: the rifle is in their hands.
            Check(equip == JobDefOf.Equip || worker.equipment?.Primary == set.rifle, $"weapons rule 'equip': set down on a rifle, they pick it up ({equip?.defName}; '{equipPreview}')");

            rules.Set(Cat("AGM_Drop_Food"), DropAction.Eat);
            JobDef eat = DropOn(set.meal, out string eatPreview);
            Check(eat == JobDefOf.Ingest, $"food rule 'eat': set down on a meal, they eat it ({eat?.defName}; '{eatPreview}')");

            rules.Set(Cat("AGM_Drop_Items"), DropAction.Ignore);
            JobDef ignored = DropOn(set.steel, out string ignorePreview);
            Check(ignored == null && ignorePreview.StartsWith("AGM_Drop_Will_Ignore".Translate()),
                $"items rule 'just set down': set down on steel, nothing ordered ({ignored?.defName}; '{ignorePreview}')");

            // Haul: needs somewhere to haul to.
            rules.Set(Cat("AGM_Drop_Items"), DropAction.Haul);
            worker.workSettings.EnableAndInitialize();
            worker.workSettings.SetPriority(WorkTypeDefOf.Hauling, 1);
            var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 c in GenRadial.RadialCellsAround(shrine.Position + new IntVec3(0, 0, 6), 2f, true)
                .Where(c => c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null && map.zoneManager.ZoneAt(c) == null))
            {
                zone.AddCell(c);
            }
            JobDef haul = DropOn(set.steel, out string haulPreview);
            Check(haul == JobDefOf.HaulToCell || haul?.defName.StartsWith("Haul") == true,
                $"items rule 'haul': set down on steel, they haul it to storage ({haul?.defName}; '{haulPreview}')");
            zone.Delete();

            rules.ResetAll();
            foreach (Thing t in new[] { set.parka, set.rifle, set.meal, set.steel })
            {
                if (t != null && !t.Destroyed)
                {
                    t.Destroy();
                }
            }
        }

        /// <summary>Dropping someone into fire / among enemies is judged like a strike.</summary>
        private void RunDangerDropTests(Map map, GodHand hand, Pawn exclude)
        {
            AlignmentTracker alignment = AlignmentTracker.Instance;
            DivinePower dp = DivinePower.Instance;
            Pawn victim = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => p != exclude && !p.Dead && !p.Downed && !p.InMentalState && !p.Drafted);
            IntVec3 spot = GenRadial.RadialCellsAround(shrine.Position, 7f, false).FirstOrDefault(c => c.InBounds(map) && c.Standable(map)
                && InfluenceUtility.InInfluence(c, map) && c.GetFirstPawn(map) == null && !c.ContainsStaticFire(map) && (c - shrine.Position).LengthHorizontal > 3f);
            if (victim == null || !spot.IsValid || GodHand.MassOf(victim) > GodHand.Capacity)
            {
                Log.Message(Tag + "  (no colonist/spot for danger drops: skipping)");
                return;
            }
            IntVec3 fireCell = spot + IntVec3.East;
            Fire fire = (Fire)GenSpawn.Spawn(ThingDefOf.Fire, fireCell.InBounds(map) ? fireCell : spot, map);
            Check(GodHand.DangerAt(spot, map, victim, out _) == GodHand.DropDanger.Fire, "a cell next to fire is a dangerous drop");
            alignment.DebugSet(0f);
            dp.DebugSet(dp.MaxPower);
            hand.TryGrab(victim, drag: true);
            hand.MoveTo(spot);
            hand.Place(spot, order: true);
            Check(alignment.Value <= AlignmentTuningDef.Get.smiteOwnFollower * AlignmentTuningDef.Get.dangerDropFactor + 0.5f,
                $"dropping your own follower into fire is evil, like striking them ({alignment.Value:F1})");
            if (!fire.Destroyed)
            {
                fire.Destroy();
            }
            victim.jobs.StopAll();

            // An attacker dropped among your people: retribution, not evil.
            Faction enemy = Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
            if (enemy == null)
            {
                return;
            }
            Pawn raider = PawnGenerator.GeneratePawn(PawnKindDefOf.Villager, enemy);
            GenSpawn.Spawn(raider, CellFinder.RandomClosewalkCellNear(shrine.Position + new IntVec3(-3, 0, -3), map, 2), map);
            Check(!hand.CanGrab(raider).Accepted, "lifting an enemy needs lift enemies: " + hand.CanGrab(raider).Reason);
            GodProgression.Instance.DebugSetRank(AGM_ProgressionDefOf.AGM_Track_LiftEnemies, 1);
            if (GodHand.MassOf(raider) <= GodHand.Capacity)
            {
                alignment.DebugSet(0f);
                dp.DebugSet(dp.MaxPower);
                IntVec3 among = CellFinder.RandomClosewalkCellNear(victim.Position, map, 1);
                hand.TryGrab(raider, drag: true);
                hand.MoveTo(among);
                hand.Place(among, order: true);
                Check(Mathf.Approximately(alignment.Value, 0f) && alignment.Deeds.Entries.Any(e => e.label == "AGM_Deed_Drop_Enemy".Translate(raider.LabelShort).ToString()),
                    $"dropping an attacker among your people is retribution: noted, not evil ({alignment.Value:F1})");
            }
            raider.Destroy();
        }

        /// <summary>Setting a colonist down on a tree puts them to cutting it (if assigned), or warns.</summary>
        private void RunHandWorkOrderTests(Map map, GodHand hand, Pawn exclude)
        {
            Pawn worker = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => p != exclude && !p.Dead && !p.Downed && !p.WorkTypeIsDisabled(WorkTypeDefOf.PlantCutting));
            if (worker == null)
            {
                Log.Message(Tag + "  (no colonist able to cut plants: skipping hand work orders)");
                return;
            }
            Plant SpawnTree(IntVec3 around)
            {
                // (Grass or bushes there are cleared; a cell is never "default" (0,0,0) when none fits.)
                IntVec3 c = GenRadial.RadialCellsAround(around, 8f, true).Where(x => x.InBounds(map) && x.Standable(map)
                    && (x.GetPlant(map) == null || !x.GetPlant(map).def.plant.IsTree) && x.GetEdifice(map) == null
                    && map.fertilityGrid.FertilityAt(x) > 0.5f && InfluenceUtility.InInfluence(x, map)
                    && x.GetFirstPawn(map) == null && x.GetFirstItem(map) == null).DefaultIfEmpty(IntVec3.Invalid).First();
                if (!c.IsValid)
                {
                    return null;
                }
                c.GetPlant(map)?.Destroy();
                var tree = (Plant)GenSpawn.Spawn(ThingDef.Named("Plant_TreeOak"), c, map);
                tree.Growth = 1f;
                return tree;
            }
            Plant oak = SpawnTree(shrine.Position + new IntVec3(4, 0, -3));
            if (oak == null)
            {
                Log.Message(Tag + "  (no fertile cell for a test tree: skipping hand work orders)");
                return;
            }
            GenSpawn.Spawn(ThingDefOf.Filth_Dirt, oak.Position, map); // the tree, not the dirt under it, is what they're set on
            worker.workSettings.EnableAndInitialize();
            worker.workSettings.SetPriority(WorkTypeDefOf.PlantCutting, 1);
            DivinePower.Instance.DebugSet(DivinePower.Instance.MaxPower);
            GodProgression.Instance.DebugSetRank(AGM_ProgressionDefOf.AGM_Track_Strength, 2); // anyone with gear
            AcceptanceReport grab = hand.CanGrab(worker);
            Check(hand.TryGrab(worker, drag: false), $"the hand lifts the woodcutter ({GodHand.MassOf(worker):F0} kg) {grab.Reason}");
            hand.MoveTo(oak.Position);
            hand.Place(oak.Position, order: true);
            bool designated = map.designationManager.DesignationOn(oak, DesignationDefOf.CutPlant) != null;
            bool cutting = (worker.CurJobDef?.defName ?? "").StartsWith("CutPlant") || worker.jobs.jobQueue.Any(q => q.job.def.defName.StartsWith("CutPlant"));
            Check(designated && cutting, $"a woodcutter set down on a tree marks it and starts cutting (designated {designated}, job {worker.CurJobDef?.defName}, takes orders {HandWorkOrder.CanTakeOrders(worker)}, mental {worker.MentalStateDef?.defName}, downed {worker.Downed}, drafted {worker.Drafted}, spawned {worker.Spawned}, pain {worker.health.hediffSet.PainTotal:P0}, reach {worker.CanReach(oak, Verse.AI.PathEndMode.Touch, Danger.Deadly)}, at {worker.Position} tree {oak.Position})");

            // Not assigned: a warning, and the tree is left unmarked.
            Plant oak2 = SpawnTree(shrine.Position + new IntVec3(-4, 0, -3));
            if (oak2 != null)
            {
                worker.jobs.StopAll();
                worker.workSettings.SetPriority(WorkTypeDefOf.PlantCutting, 0);
                HandWorkOrder.Result r = HandWorkOrder.OrderWork(worker, oak2.Position);
                Check(!r.ordered && !r.text.NullOrEmpty() && map.designationManager.DesignationOn(oak2) == null,
                    $"not assigned to cutting: a warning, no order, tree left alone ('{r.text}')");
                worker.workSettings.SetPriority(WorkTypeDefOf.PlantCutting, 3);
                oak2.Destroy();
            }
            worker.jobs.StopAll();
            oak.Destroy();
        }

        /// <summary>Removes a test pawn, alive or dead (with its corpse).</summary>
        private static void RemoveTestPawn(Pawn p)
        {
            if (p.Dead)
            {
                p.Corpse?.Destroy();
            }
            if (!p.Destroyed)
            {
                p.Destroy();
            }
        }

        private static void HealInjuries(Pawn p)
        {
            foreach (Hediff_Injury injury in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList())
            {
                p.health.RemoveHediff(injury);
            }
        }

        private static void Teleport(Pawn p, IntVec3 c, Map map)
        {
            IntVec3 dest = CellFinder.RandomClosewalkCellNear(c, map, 2);
            p.jobs?.StopAll();
            p.Position = dest;
            p.Notify_Teleported();
        }

        /// <summary>Influence branch: Long reach, Pylon strength, Heralds (living pylons).</summary>
        private void RunInfluenceTrackTests()
        {
            Log.Message(Tag + "Influence tracks phase");
            Map map = shrine.Map;
            GodProgression prog = GodProgression.Instance;

            // Long reach: a cell just past the base reach comes within reach, and costs less near the edge.
            float baseReach = InfluenceUtility.MaxReachOutside;
            IntVec3 pastReach = map.AllCells.FirstOrFallback(c => !c.Fogged(map) && InfluenceUtility.DistanceOutside(c, map) > baseReach + 0.2f
                && InfluenceUtility.DistanceOutside(c, map) < baseReach + 1.2f, IntVec3.Invalid);
            IntVec3 justOut = map.AllCells.FirstOrFallback(c => Mathf.Abs(InfluenceUtility.DistanceOutside(c, map) - 3f) < 0.3f, IntVec3.Invalid);
            if (pastReach.IsValid && justOut.IsValid)
            {
                float cost0 = InfluenceUtility.CostFactor(justOut, map);
                float lift0 = InfluenceUtility.LiftFactor(justOut, map);
                prog.DebugSetRank(AGM_ProgressionDefOf.AGM_Track_LongReach, 1);
                Check(InfluenceUtility.InReach(pastReach, map) && InfluenceUtility.CostFactor(justOut, map) < cost0 && InfluenceUtility.LiftFactor(justOut, map) > lift0,
                    $"long reach stretches the soft border (reach {baseReach} -> {InfluenceUtility.MaxReachOutside}, 3 cells out: cost x{cost0:F2} -> x{InfluenceUtility.CostFactor(justOut, map):F2})");
                prog.DebugSetRank(AGM_ProgressionDefOf.AGM_Track_LongReach, 0);
                Check(!InfluenceUtility.InReach(pastReach, map), "without long reach that cell is out of reach again");
            }

            // Pylon strength: a wider radius.
            ThingDef pylonDef = AGM_TempleDefOf.AGM_Pylon;
            IntVec3 pylonSpot = GenRadial.RadialCellsAround(shrine.Position, 12f, false).FirstOrFallback(c => c.InBounds(map) && c.Standable(map)
                && c.GetFirstItem(map) == null && c.GetFirstPawn(map) == null && c.GetEdifice(map) == null, IntVec3.Invalid);
            Thing pylonThing = pylonSpot.IsValid
                ? GenSpawn.Spawn(ThingMaker.MakeThing(pylonDef, pylonDef.MadeFromStuff ? GenStuff.DefaultStuffFor(pylonDef) : null), pylonSpot, map)
                : null;
            CompPylon pylon = pylonThing?.TryGetComp<CompPylon>();
            Check(pylon != null, "a test pylon for pylon strength");
            if (pylon != null)
            {
                float r0 = pylon.Radius;
                prog.DebugSetRank(AGM_ProgressionDefOf.AGM_Track_PylonStrength, 3);
                Check(Mathf.Approximately(pylon.Radius, r0 * 1.5f), $"pylon strength widens a pylon ({r0:F1} -> {pylon.Radius:F1})");
                prog.DebugSetRank(AGM_ProgressionDefOf.AGM_Track_PylonStrength, 0);
                pylonThing.Destroy();
            }

            // Heralds: a very faithful colonist carries a small circle of influence with them.
            HeraldTracker heralds = HeraldTracker.Instance;
            Pawn herald = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => !p.Dead && !p.Downed && p.Faith() != null);
            IntVec3 far = map.AllCells.FirstOrFallback(c => c.Standable(map) && !c.Fogged(map) && c.GetFirstPawn(map) == null
                && InfluenceUtility.DistanceOutside(c, map) > 7f && InfluenceUtility.DistanceOutside(c, map) < 9f, IntVec3.Invalid);
            if (heralds == null || herald == null || !far.IsValid)
            {
                Log.Message(Tag + "  (no believer or far cell for herald checks: skipping)");
                return;
            }
            IntVec3 home = herald.Position;
            herald.Faith().CurLevel = 0.95f;
            Check(!heralds.CanAnoint(herald).Accepted, "no heralds before the power is bought: " + heralds.CanAnoint(herald).Reason);
            prog.DebugSetRank(AGM_ProgressionDefOf.AGM_Track_Heralds, 1);
            herald.jobs?.StopAll();
            herald.Position = far;
            herald.Notify_Teleported();
            Check(!InfluenceUtility.InInfluence(herald.Position, map), "the herald's spot is outside the influence before anointing");
            Check(heralds.Anoint(herald) && InfluenceUtility.InInfluence(herald.Position, map)
                && InfluenceUtility.InInfluence(herald.Position + new IntVec3(4, 0, 0), map), "an anointed herald carries influence around them (radius ~4.9)");
            Check(herald.GetGizmos().OfType<Command_Action>().Any(c => c.defaultLabel == "AGM_Herald_Dismiss".Translate().ToString()), "a herald has a dismiss command");
            Pawn other = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => p != herald && p.Faith() != null);
            if (other != null)
            {
                other.Faith().CurLevel = 0.95f;
                Check(!heralds.CanAnoint(other).Accepted, "only one herald at rank 1: " + heralds.CanAnoint(other).Reason);
            }
            herald.Faith().CurLevel = 0.5f;
            Check(!InfluenceUtility.InInfluence(herald.Position, map), "a herald whose faith fell carries no influence");
            herald.Faith().CurLevel = 0.95f;
            Check(InfluenceUtility.InInfluence(herald.Position, map), "it comes back with their faith");
            heralds.Dismiss(herald);
            prog.DebugSetRank(AGM_ProgressionDefOf.AGM_Track_Heralds, 0);
            herald.Position = home; // exactly where they were: later phases expect the colony as it was
            herald.Notify_Teleported();
        }

        /// <summary>God progression: buying ranks of a power track, miracle stats, the cap gate, prerequisites.</summary>
        private void RunProgressionTests()
        {
            DivinePower dp = DivinePower.Instance;
            GodProgression prog = GodProgression.Instance;
            MiracleDef water = DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Water");
            PowerTrackDef track = DefDatabase<PowerTrackDef>.GetNamed("AGM_Track_Water");
            Log.Message(Tag + "Progression phase");
            float powerBefore = dp.Power;

            Check(prog != null && PowerTrackDef.ForMiracle(water) == track && water.Stats.amount == 9 && Mathf.Approximately(water.Stats.cost, 5f),
                $"water starts at its XML numbers ({water.Stats.amount} tiles for {water.Stats.cost})");
            float unitBefore = MiracleChannel.BaseUnitCost(water);

            dp.DebugSet(dp.MaxPower);
            float before = dp.Power;
            Check(prog.TryBuy(track) && prog.RankOf(track) == 1, "bought water rank 1");
            Check(water.Stats.amount == 14 && Mathf.Approximately(water.Stats.cost, 6.5f) && Mathf.Approximately(before - dp.Power, track.ranks[0].price),
                $"water rank 1: {water.Stats.amount} tiles for {water.Stats.cost}, paid {before - dp.Power:F0}");
            Check(MiracleChannel.BaseUnitCost(water) < unitBefore, $"a rank lowers the price per tile ({unitBefore:F2} -> {MiracleChannel.BaseUnitCost(water):F2})");

            var req = new Req_PowerRank { track = track, rank = 2 };
            Check(!req.IsMet(), "a rank-2 prerequisite isn't met at rank 1");

            prog.DebugSetRank(track, 2);
            Check(req.IsMet(), "a rank-2 prerequisite is met at rank 2");
            dp.DebugSet(dp.MaxPower);
            AcceptanceReport r = prog.CanBuy(track);
            float price3 = track.ranks[2].price;
            Check(price3 > dp.MaxPower ? !r.Accepted : r.Accepted,
                $"rank 3 (price {price3}) is {(price3 > dp.MaxPower ? "refused" : "allowed")} with a pool cap of {dp.MaxPower}: {r.Reason}");

            prog.DebugSetRank(track, track.MaxRank);
            Check(!prog.CanBuy(track).Accepted && water.Stats.amount == track.ranks[track.MaxRank - 1].amount, "a maxed track can't be bought and uses its last rank");

            prog.DebugSetRank(track, 0);
            Check(water.Stats.amount == 9, "back at rank 0, water uses its XML numbers again");
            dp.DebugSet(powerBefore);
        }

        private void RunMiracleTests()
        {
            Map map = shrine.Map;
            DivinePower dp = DivinePower.Instance;
            MiracleTracker tracker = MiracleTracker.Instance;
            MiracleDef water = DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Water");
            MiracleDef food = DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Food");
            MiracleDef wood = DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Wood");

            Log.Message(Tag + "Miracle phase");
            Check(tracker.IsUnlocked(water) && tracker.IsUnlocked(food) && tracker.IsUnlocked(wood), "water/food/wood unlocked from the start");

            CellFinder.TryFindRandomCellNear(shrine.Position, map, 6, c => c.Standable(map) && c.GetFirstItem(map) == null, out IntVec3 near);
            dp.Add(100f);

            // One-shot casts (the whole charge at once).
            float before = dp.Power;
            int crops0 = food.channel.things.Sum(d => CountOf(map, d));
            AcceptanceReport r = MiracleUtility.TryCast(food, near, map);
            int crops = food.channel.things.Sum(d => CountOf(map, d)) - crops0;
            Check(r.Accepted && crops == food.Stats.amount, $"a one-shot food cast drops the whole charge of crops ({crops} of {food.Stats.amount}) " + r.Reason);
            Check(Mathf.Approximately(before - dp.Power, food.Stats.cost), $"food costs {food.Stats.cost} (spent {before - dp.Power:F1})");

            int logs = CountOf(map, ThingDefOf.WoodLog);
            r = MiracleUtility.TryCast(wood, near, map);
            Check(r.Accepted && CountOf(map, ThingDefOf.WoodLog) - logs == wood.Stats.amount, $"wood spawned {wood.Stats.amount} logs (got {CountOf(map, ThingDefOf.WoodLog) - logs})");

            // Water puts out a fire
            CellFinder.TryFindRandomCellNear(shrine.Position, map, 8, c => c.Standable(map) && c.GetFirstItem(map) == null && (c - near).LengthHorizontal > 4f, out IntVec3 fireCell);
            // Spawn directly: vanilla only starts fires on flammable cells.
            Fire fire = (Fire)ThingMaker.MakeThing(ThingDefOf.Fire);
            fire.fireSize = 0.5f;
            GenSpawn.Spawn(fire, fireCell, map);
            Check(fireCell.ContainsStaticFire(map), "test fire started");
            r = MiracleUtility.TryCast(water, fireCell, map);
            Check(r.Accepted && !fireCell.ContainsStaticFire(map), "water extinguished the fire");

            RunChannelTests(map, near, water, food);

            // Outside influence is refused and costs nothing
            IntVec3 far = map.AllCells.FirstOrDefault(c => c.Standable(map) && !c.Fogged(map) && !InfluenceUtility.InReach(c, map));
            before = dp.Power;
            r = MiracleUtility.TryCast(food, far, map);
            Check(!r.Accepted && Mathf.Approximately(before, dp.Power), "cast beyond the soft border refused, no power spent: " + r.Reason);

            // Not enough power is refused
            dp.DebugSet(3f);
            r = MiracleUtility.TryCast(food, near, map);
            Check(!r.Accepted && Mathf.Approximately(dp.Power, 3f), "cast without enough power refused: " + r.Reason);
        }

        /// <summary>
        /// Hold-to-cast. Water and wood are pour-only: the charge is the level's amount, paid per unit,
        /// poured over as many presses as needed. Food keeps its click and streams crops while held.
        /// </summary>
        private void RunChannelTests(Map map, IntVec3 near, MiracleDef water, MiracleDef food)
        {
            DivinePower dp = DivinePower.Instance;
            MiracleDef wood = DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Wood");
            dp.Add(100f);

            // Two rows of six open cells inside the influence, each cell on fire.
            bool Open(IntVec3 x) => x.InBounds(map) && x.Standable(map) && x.GetFirstItem(map) == null && x.GetFirstPawn(map) == null && InfluenceUtility.InInfluence(x, map);
            CellFinder.TryFindRandomCellNear(shrine.Position, map, 8, c =>
                Enumerable.Range(0, 6).All(i => Open(c + new IntVec3(i, 0, 0)) && Open(c + new IntVec3(i, 0, 1))), out IntVec3 a);
            if (!a.IsValid)
            {
                Check(false, "found two rows of open cells for the channel test");
                return;
            }
            for (int i = 0; i < 6; i++)
            {
                for (int z = 0; z < 2; z++)
                {
                    Fire fire = (Fire)ThingMaker.MakeThing(ThingDefOf.Fire);
                    fire.fireSize = 0.5f;
                    GenSpawn.Spawn(fire, a + new IntVec3(i, 0, z), map);
                }
            }
            MiracleStats w1 = water.Stats;
            float tile = MiracleChannel.UnitCostAt(water, a, map);
            float t = 1000f;
            float before = dp.Power;
            var ch = new MiracleChannel(water, map);
            ch.Press(a, t);
            ch.Update(a + new IntVec3(5, 0, 0), t + 0.1f);
            bool rowOut = Enumerable.Range(0, 6).All(i => !(a + new IntVec3(i, 0, 0)).ContainsStaticFire(map));
            Check(rowOut && ch.units == 6 && Mathf.Abs(before - dp.Power - 6 * tile) < 0.01f,
                $"pouring water along a row puts out each fire, paid per tile ({ch.units} tiles, {before - dp.Power:F2} power, {tile:F2} each = cost/{w1.amount})");
            float spentRow = before - dp.Power;
            ch.Update(a, t + 0.3f);
            Check(Mathf.Abs(before - dp.Power - spentRow) < 0.001f && ch.units == 6, "going back over watered tiles is free and doesn't count");
            ch.Release();
            ch.Update(a + new IntVec3(0, 0, 1), t + 10f);
            Check(ch.units == 6 && !ch.finished, "releasing pauses the pour: no time limit, the rest of the charge stays");
            ch.Press(a + new IntVec3(0, 0, 1), t + 20f);
            ch.Update(a + new IntVec3(5, 0, 1), t + 20.1f);
            int secondRowOut = Enumerable.Range(0, 6).Count(i => !(a + new IntVec3(i, 0, 1)).ContainsStaticFire(map));
            Check(ch.finished && ch.units == w1.amount && secondRowOut == w1.amount - 6 && Mathf.Abs(before - dp.Power - w1.cost) < 0.01f,
                $"pressing again goes on until the full charge ({w1.amount} tiles, {before - dp.Power:F1} power) is used, then stops");
            foreach (Thing leftover in map.listerThings.ThingsOfDef(ThingDefOf.Fire).ToList())
            {
                leftover.Destroy(); // the charge ran out before the last fires: don't leave them for later tests
            }

            // Wood: one log at a time; the full amount takes pourSeconds of holding (time released doesn't count).
            int logs = CountOf(map, ThingDefOf.WoodLog);
            MiracleStats wl = wood.Stats;
            before = dp.Power;
            var wc = new MiracleChannel(wood, map);
            wc.Press(near, t);
            float now = t;
            for (; now < t + 1.5f; now += 0.05f)
            {
                wc.Update(near, now);
            }
            int half = wc.units;
            wc.Release();
            wc.Update(near, now + 5f);
            Check(half > 5 && half < wl.amount && wc.units == half, $"holding wood drops logs one by one ({half} after 1.5 s), none while released");
            wc.Press(near, now + 10f);
            for (now += 10f; now < t + 14f && !wc.finished; now += 0.05f)
            {
                wc.Update(near, now);
            }
            int gotLogs = CountOf(map, ThingDefOf.WoodLog) - logs;
            Check(wc.finished && gotLogs == wl.amount && Mathf.Abs(before - dp.Power - wl.cost) < 0.01f,
                $"the full wood charge drops {wl.amount} logs for {wl.cost} power (got {gotLogs}, {before - dp.Power:F1})");

            // Food: blessed crops one at a time, the full charge over its pour time.
            int crops = food.channel.things.Sum(d => CountOf(map, d));
            before = dp.Power;
            var fc = new MiracleChannel(food, map);
            fc.Press(near, t);
            for (now = t; now < t + food.channel.pourSeconds + 0.5f && !fc.finished; now += 0.02f)
            {
                fc.Update(near, now);
            }
            int got = food.channel.things.Sum(d => CountOf(map, d)) - crops;
            Check(fc.finished && got == food.Stats.amount && Mathf.Abs(before - dp.Power - food.Stats.cost) < 0.01f,
                $"holding food drops its {food.Stats.amount} crops one by one in about {food.channel.pourSeconds} s ({got} crops, {before - dp.Power:F1} power, {now - t:F1} s)");

            // Running dry: the pour pauses and keeps the rest of its charge.
            dp.DebugSet(0.1f);
            var dry = new MiracleChannel(wood, map);
            dry.Press(near, t);
            dry.Update(near, t);
            Check(!dry.holding && !dry.finished && !dry.message.NullOrEmpty() && Mathf.Approximately(dp.Power, 0.1f), "out of power, a pour pauses and keeps the rest of its charge");
            dp.Add(100f);
        }

        /// <summary>
        /// Simulates human drawings of each default gesture (size, jitter, rotation, reversed
        /// direction, shuffled strokes, dense sampling) and checks they're recognized. Also logs
        /// the closest wrong match and some non-gestures, to calibrate the threshold.
        /// </summary>
        private void RunGestureTests()
        {
            Log.Message(Tag + $"Gesture phase (threshold {GestureLibrary.Threshold:F2})");
            var rng = new System.Random(1234);
            float worstCorrect = 0f;
            float bestWrong = float.MaxValue;
            string closestPair = "";
            int trials = 0, correct = 0;

            foreach (MiracleDef miracle in GestureLibrary.MiraclesWithGestures)
            {
                List<List<Vector2>> template = miracle.gesture.templates[0].AsPointLists();
                for (int t = 0; t < 40; t++)
                {
                    List<List<Vector2>> drawn = SimulateDrawing(template, rng);
                    GestureMatch m = GestureLibrary.Recognize(drawn);
                    trials++;
                    if (m.miracle == miracle && m.Recognized)
                    {
                        correct++;
                    }
                    if (m.miracle == miracle)
                    {
                        worstCorrect = Mathf.Max(worstCorrect, m.distance);
                        if (m.runnerUpDistance < bestWrong)
                        {
                            bestWrong = m.runnerUpDistance;
                            closestPair = miracle.defName + " drawn, " + m.runnerUp?.defName + " close";
                        }
                    }
                    else
                    {
                        Log.Message(Tag + $"    misread {miracle.defName} as {m.miracle?.defName} (d={m.distance:F2})");
                    }
                }
            }
            Log.Message(Tag + $"  simulated drawings: {correct}/{trials} recognized; worst correct d={worstCorrect:F2}, closest wrong d={bestWrong:F2} ({closestPair})");
            Check(bestWrong > GestureLibrary.Threshold, $"no wrong sign comes within the threshold ({bestWrong:F2} > {GestureLibrary.Threshold:F2})");
            Check(correct >= trials * 0.95f, "at least 95% of simulated drawings recognized");
            Check(worstCorrect < bestWrong, "worst correct match still closer than any wrong one");

            // Non-gestures should be rejected (logged for calibration).
            var line = new List<List<Vector2>> { new List<Vector2> { new Vector2(0, 0), new Vector2(200, 0) } };
            var scribble = new List<List<Vector2>> { RandomWalk(rng, 40) };
            var dot = new List<List<Vector2>> { new List<Vector2> { new Vector2(10, 10), new Vector2(11, 11) } };
            foreach (var (name, g) in new[] { ("straight line", line), ("scribble", scribble), ("dot", dot) })
            {
                GestureMatch m = GestureLibrary.Recognize(g);
                Log.Message(Tag + $"  {name}: best={m.miracle?.defName} d={m.distance:F2} recognized={m.Recognized}");
            }
            Check(!GestureLibrary.Recognize(scribble).Recognized, "random scribble rejected");
        }

        private static List<List<Vector2>> SimulateDrawing(List<List<Vector2>> template, System.Random rng)
        {
            float size = 80f + (float)rng.NextDouble() * 260f;
            float angle = ((float)rng.NextDouble() * 2f - 1f) * 10f * Mathf.Deg2Rad;
            float jitter = 0.025f;
            Vector2 origin = new Vector2((float)rng.NextDouble() * 800f, (float)rng.NextDouble() * 600f);
            var strokes = new List<List<Vector2>>();
            foreach (List<Vector2> s in template)
            {
                var dense = new List<Vector2>();
                for (int i = 1; i < s.Count; i++)
                {
                    for (int k = 0; k < 6; k++)
                    {
                        Vector2 p = Vector2.Lerp(s[i - 1], s[i], k / 6f) - new Vector2(0.5f, 0.5f);
                        p = new Vector2(p.x * Mathf.Cos(angle) - p.y * Mathf.Sin(angle), p.x * Mathf.Sin(angle) + p.y * Mathf.Cos(angle));
                        p += new Vector2(((float)rng.NextDouble() * 2f - 1f) * jitter, ((float)rng.NextDouble() * 2f - 1f) * jitter);
                        dense.Add(origin + p * size);
                    }
                }
                dense.Add(origin + (s[s.Count - 1] - new Vector2(0.5f, 0.5f)) * size);
                if (rng.NextDouble() < 0.5)
                {
                    dense.Reverse();
                }
                strokes.Add(dense);
            }
            // Shuffle stroke order.
            for (int i = strokes.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (strokes[i], strokes[j]) = (strokes[j], strokes[i]);
            }
            return strokes;
        }

        private static List<Vector2> RandomWalk(System.Random rng, int steps)
        {
            var pts = new List<Vector2> { Vector2.zero };
            for (int i = 0; i < steps; i++)
            {
                pts.Add(pts[pts.Count - 1] + new Vector2((float)rng.NextDouble() * 60f - 30f, (float)rng.NextDouble() * 60f - 30f));
            }
            return pts;
        }

        /// <summary>M4: traits, patience buffer, blessed food, Heal, prayer requests and wishes.</summary>
        private void RunFaithTests()
        {
            Map map = shrine.Map;
            DivinePower dp = DivinePower.Instance;
            MiracleTracker miracles = MiracleTracker.Instance;
            PrayerRequestTracker requests = PrayerRequestTracker.Instance;
            Pawn pawn = map.mapPawns.FreeColonistsSpawned.First();
            Need_Faith faith = pawn.Faith();
            Log.Message(Tag + "Faith phase (test pawn: " + pawn.LabelShort + ")");

            // Traits multiply: Lazy (x1.5 requests) + Zealot (x1.5 requests) = x2.25
            float before = FaithTraitUtility.Factor(pawn, f => f.requestFrequencyFactor);
            TraitDef industriousness = DefDatabase<TraitDef>.GetNamed("Industriousness");
            if (pawn.story.traits.HasTrait(industriousness))
            {
                pawn.story.traits.RemoveTrait(pawn.story.traits.GetTrait(industriousness));
            }
            foreach (string name in new[] { "AGM_Sceptic", "AGM_Zealot" })
            {
                TraitDef t = DefDatabase<TraitDef>.GetNamed(name);
                if (pawn.story.traits.HasTrait(t))
                {
                    pawn.story.traits.RemoveTrait(pawn.story.traits.GetTrait(t));
                }
            }
            float baseline = FaithTraitUtility.Factor(pawn, f => f.requestFrequencyFactor);
            pawn.story.traits.GainTrait(new Trait(industriousness, -1));
            pawn.story.traits.GainTrait(new Trait(DefDatabase<TraitDef>.GetNamed("AGM_Zealot")));
            float combined = FaithTraitUtility.Factor(pawn, f => f.requestFrequencyFactor);
            Check(Mathf.Abs(combined - baseline * 2.25f) < 0.001f, $"Lazy + Zealot request frequency multiplies (x{combined / baseline:F2}, before traits x{before:F2})");

            // Patience absorbs hardship before faith drops
            float hardshipFactor = FaithTraitUtility.Factor(pawn, f => f.hardshipFactor);
            faith.CurLevel = 0.5f;
            faith.DebugSetPatience(0.1f);
            faith.Hardship(0.05f / hardshipFactor);
            Check(Mathf.Abs(faith.CurLevel - 0.5f) < 0.001f, $"small hardship absorbed by patience (faith {faith.CurLevel:F3})");
            faith.Hardship(0.15f / hardshipFactor);
            Check(Mathf.Abs(faith.CurLevel - 0.4f) < 0.001f, $"hardship beyond patience lowers faith by the excess (faith {faith.CurLevel:F3}, expected 0.400)");

            // Blessed food: miracle crops are blessed, stay blessed when split, don't stack with ordinary
            // food, bless what's cooked from them, and give faith when eaten.
            dp.Add(100f);
            CellFinder.TryFindRandomCellNear(shrine.Position, map, 6, c => c.Standable(map) && c.GetFirstItem(map) == null && c.GetFirstPawn(map) == null, out IntVec3 spot);
            MiracleUtility.TryCast(DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Food"), spot, map);
            Thing crop = map.listerThings.ThingsOfDef(ThingDef.Named("RawPotatoes")).Concat(map.listerThings.ThingsOfDef(ThingDef.Named("RawCorn")))
                .Concat(map.listerThings.ThingsOfDef(ThingDef.Named("RawRice"))).FirstOrDefault(t => miracles.IsBlessed(t) && t.stackCount > 2);
            Check(crop != null, "food miracle crops are blessed");
            if (crop != null)
            {
                Thing plain = ThingMaker.MakeThing(crop.def);
                Check(!crop.CanStackWith(plain) && !plain.CanStackWith(crop), "blessed food doesn't stack with ordinary food");
                Thing piece = crop.SplitOff(1);
                Check(miracles.IsBlessed(piece), "blessing survives a stack split");
                Thing forCooking = crop.SplitOff(1);
                Pawn cook = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(c => !c.WorkTypeIsDisabled(DefDatabase<WorkTypeDef>.GetNamed("Cooking"))) ?? pawn;
                List<Thing> cooked = GenRecipe.MakeRecipeProducts(DefDatabase<RecipeDef>.GetNamed("CookMealSimple"), cook,
                    new List<Thing> { forCooking }, forCooking, null).ToList();
                Check(cooked.Count > 0 && cooked.All(miracles.IsBlessed), "a meal cooked from blessed food is blessed");
                plain.Destroy();
                faith.CurLevel = 0.5f;
                piece.Ingested(pawn, 1f);
                float expected = FaithTuningDef.Get.blessedFoodGain * FaithTraitUtility.Factor(pawn, f => f.gainFactor);
                Check(Mathf.Abs(faith.CurLevel - 0.5f - expected) < 0.002f, $"eating blessed food gives faith (+{faith.CurLevel - 0.5f:F3}, expected +{expected:F3})");
            }

            // Heal: locked until a colonist is downed; heals up to 10 severity, worst first
            MiracleDef heal = DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Heal");
            Check(!miracles.IsUnlocked(heal), "heal locked before anyone is downed");
            miracles.RecordEvent(MiracleTracker.Event_ColonistDowned);
            Check(miracles.IsUnlocked(heal), "heal unlocked after a colonist is downed");
            for (int i = 0; i < 3; i++)
            {
                pawn.TakeDamage(new DamageInfo(DamageDefOf.Cut, 6f));
            }
            float severityBefore = MiracleWorker_Heal.HealableInjuries(pawn).Sum(h => h.Severity);
            dp.Add(100f);
            AcceptanceReport r = MiracleUtility.TryCast(heal, pawn.Position, map);
            float severityAfter = MiracleWorker_Heal.HealableInjuries(pawn).Sum(h => h.Severity);
            float healed = severityBefore - severityAfter;
            Check(r.Accepted && Mathf.Abs(healed - Mathf.Min(10f, severityBefore)) < 0.01f,
                $"heal I mended {healed:F1} of {severityBefore:F1} severity (expected {Mathf.Min(10f, severityBefore):F1}) {r.Reason}");
            Check(!MiracleUtility.CanCast(heal, spot, map).Accepted, "heal refuses a cell with no one in it");

            // Poured heal: stays on the first pawn, pauses elsewhere, stops (unpaid) once nothing is left to heal.
            pawn.TakeDamage(new DamageInfo(DamageDefOf.Cut, 4f));
            float toHeal = MiracleWorker_Heal.HealableInjuries(pawn).Sum(h => h.Severity);
            float powerBefore = dp.Power;
            var hc = new MiracleChannel(heal, map);
            float now = 1000f;
            hc.Press(pawn.Position, now);
            for (; now < 1001f; now += 0.05f)
            {
                hc.Update(pawn.Position, now);
            }
            int unitsOnPawn = hc.units;
            for (; now < 1002f; now += 0.05f)
            {
                hc.Update(spot, now);
            }
            Check(unitsOnPawn > 0 && hc.units == unitsOnPawn && hc.target == pawn, $"poured heal stays on its pawn and pauses elsewhere ({unitsOnPawn} units)");
            for (; now < 1010f && !hc.finished; now += 0.05f)
            {
                hc.Update(pawn.Position, now);
            }
            float left = MiracleWorker_Heal.HealableInjuries(pawn).Sum(h => h.Severity);
            float paid = powerBefore - dp.Power;
            // Clearly less to mend than the charge: it stops early. (Close to the charge, a unit can mend a
            // little less than its share on the last scraps, so either outcome is fine there.)
            bool early = toHeal < hc.Budget - 1f;
            Check(hc.finished && (left < 0.01f || hc.units == hc.Budget) && (!early || hc.units < hc.Budget)
                && Mathf.Abs(paid - hc.units * MiracleChannel.BaseUnitCost(heal)) < 0.01f,
                $"poured heal mended {toHeal - left:F1} of {toHeal:F1} in {hc.units}/{hc.Budget} units, paying only those ({paid:F1})");

            // A bigger charge (heal rank 3) than the wounds: it stops when they're mended, the rest unpaid.
            PowerTrackDef healTrack = DefDatabase<PowerTrackDef>.GetNamed("AGM_Track_Heal");
            GodProgression.Instance.DebugSetRank(healTrack, 3);
            dp.Add(100f);
            pawn.TakeDamage(new DamageInfo(DamageDefOf.Cut, 5f));
            toHeal = MiracleWorker_Heal.HealableInjuries(pawn).Sum(h => h.Severity);
            powerBefore = dp.Power;
            hc = new MiracleChannel(heal, map);
            hc.Press(pawn.Position, now);
            for (float end = now + 10f; now < end && !hc.finished; now += 0.05f)
            {
                hc.Update(pawn.Position, now);
            }
            left = MiracleWorker_Heal.HealableInjuries(pawn).Sum(h => h.Severity);
            paid = powerBefore - dp.Power;
            Check(toHeal > 0f && hc.finished && left < 0.01f && hc.units < hc.Budget && Mathf.Abs(paid - hc.units * MiracleChannel.BaseUnitCost(heal)) < 0.01f,
                $"a heal bigger than the wounds stops once they're mended ({toHeal:F1} severity, {hc.units}/{hc.Budget} units, paid {paid:F1})");
            GodProgression.Instance.DebugSetRank(healTrack, 0);

            // Request answered: cast the asked-for miracle near the requester
            PrayerRequestDef foodRequest = DefDatabase<PrayerRequestDef>.GetNamed("AGM_Request_Food");
            dp.Add(100f);
            PrayerRequest req = requests.Create(pawn, foodRequest);
            faith.CurLevel = 0.5f;
            MiracleUtility.TryCast(DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Food"), NearFreeCell(pawn, map), map);
            Check(!requests.Active.Contains(req), "food request answered by Food cast nearby");
            Check(faith.CurLevel - 0.5f >= foodRequest.reward * FaithTraitUtility.Factor(pawn, f => f.gainFactor) - 0.001f,
                $"answered prayer raised faith (+{faith.CurLevel - 0.5f:F3})");

            // Request ignored: penalty when it expires
            req = requests.Create(pawn, foodRequest);
            faith.CurLevel = 0.5f;
            requests.DebugExpire(req);
            requests.UpdateActive();
            bool stillNeeded = foodRequest.Worker.StillNeeded(req);
            Check(!requests.Active.Contains(req), "expired request removed");
            Log.Message(Tag + $"  (pawn still hungry: {stillNeeded}; penalty only applies if the need hadn't passed)");
            if (stillNeeded)
            {
                Check(faith.CurLevel < 0.5f, $"ignored prayer lowered faith (faith {faith.CurLevel:F3})");
            }

            // Faith history and thoughts
            var memories = pawn.needs.mood.thoughts.memories;
            Check(memories.GetFirstMemoryOfDef(AGM_ThoughtDefOf.AGM_PrayerAnswered) != null, "answered prayer gives the 'prayer answered' thought");
            Check(memories.GetFirstMemoryOfDef(AGM_ThoughtDefOf.AGM_WitnessedMiracle) != null, "witnessing gives the 'witnessed a miracle' thought");
            Check(memories.GetFirstMemoryOfDef(AGM_ThoughtDefOf.AGM_AteBlessedMeal) != null, "blessed meal gives its thought");
            Check(memories.GetFirstMemoryOfDef(AGM_ThoughtDefOf.AGM_FaithShaken) != null, "hardship past patience gives 'faith shaken'");
            var history = faith.History.Entries;
            Check(history.Any(e => e.absorbed > 0f), "history records hardship absorbed by patience");
            Check(history.Any(e => e.label == "AGM_Cause_BlessedMeal".Translate().ToString() && e.delta > 0f), "history records the blessed meal");
            string section = faith.History.TooltipSection();
            Log.Message(Tag + "  tooltip history:\n" + section);
            Check(!section.NullOrEmpty(), "faith tooltip shows a history section");

            // Ignored past the deadline: penalty (a wish never withdraws itself, so the expiry path runs)
            PrayerRequestDef sign = DefDatabase<PrayerRequestDef>.GetNamed("AGM_Wish_Sign");
            req = requests.Create(pawn, sign);
            faith.CurLevel = 0.5f;
            requests.DebugExpire(req);
            requests.UpdateActive();
            float penalty = sign.ignoredPenalty * FaithTraitUtility.Factor(pawn, f => f.hardshipFactor);
            Check(!requests.Active.Contains(req) && Mathf.Abs(0.5f - faith.CurLevel - penalty) < 0.001f,
                $"ignored prayer lowered faith by its penalty (-{0.5f - faith.CurLevel:F3}, expected -{penalty:F3})");
            Check(memories.GetFirstMemoryOfDef(AGM_ThoughtDefOf.AGM_PrayerIgnored) != null, "ignored prayer gives the 'unanswered' thought");

            // Wish: a sign, answered by any miracle on the map, rewards everyone
            Pawn other = map.mapPawns.FreeColonistsSpawned.First(p => p != pawn);
            other.Faith().CurLevel = 0.5f;
            req = requests.Create(pawn, sign);
            dp.Add(100f);
            MiracleUtility.TryCast(DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Wood"), NearFreeCell(shrine, map), map);
            Check(!requests.Active.Contains(req) && other.Faith().CurLevel > 0.5f, "wish for a sign granted by any miracle; others share the joy");

            // Conversion rules: Sceptics are never talked into it; Zealots convince easily; preaching skips Sceptics
            BelieverTracker believers = BelieverTracker.Instance;
            Pawn listener = map.mapPawns.FreeColonistsSpawned.First(p => p != pawn);
            TraitDef scepticDef = DefDatabase<TraitDef>.GetNamed("AGM_Sceptic");
            TraitDef zealotDef = DefDatabase<TraitDef>.GetNamed("AGM_Zealot");
            if (listener.story.traits.HasTrait(zealotDef))
            {
                listener.story.traits.RemoveTrait(listener.story.traits.GetTrait(zealotDef));
            }
            listener.story.traits.GainTrait(new Trait(scepticDef));
            believers.DebugDeconvert(listener);
            Check(listener.Faith() == null, "deconverted pawn loses the Faith need");
            Check(!believers.TryConvert(listener, 1f, byTalk: true), "a Sceptic is never converted by talk (even at 100% base chance)");
            pawn.Faith().CurLevel = 0.8f;
            Check(new InteractionWorker_Preach().RandomSelectionWeight(pawn, listener) == 0f, "believers don't preach to Sceptics");
            listener.story.traits.RemoveTrait(listener.story.traits.GetTrait(scepticDef));
            Check(new InteractionWorker_Preach().RandomSelectionWeight(pawn, listener) > 0f, "believers may preach to an ordinary non-believer");
            listener.story.traits.GainTrait(new Trait(zealotDef));
            Check(believers.TryConvert(listener, 0.34f, byTalk: true) && listener.Faith() != null, "someone inclined to believe (Zealot) is convinced easily (34% x3)");

            // Storyteller incident can fire with a shrine and a believer
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                p.Faith().CurLevel = 0.6f;
            }
            IncidentDef wishIncident = DefDatabase<IncidentDef>.GetNamed("AGM_PrayerWish");
            var parms = StorytellerUtility.DefaultParmsNow(wishIncident.category, map);
            Check(wishIncident.Worker.CanFireNow(parms), "wish incident can fire");
            int count = requests.Active.Count;
            Check(wishIncident.Worker.TryExecute(parms) && requests.Active.Count == count + 1, "wish incident creates a wish");
        }

        private static IntVec3 NearFreeCell(Thing near, Map map)
        {
            CellFinder.TryFindRandomCellNear(near.Position, map, 4, c => c.Standable(map) && c.GetFirstItem(map) == null && c.GetFirstPawn(map) == null, out IntVec3 cell);
            return cell;
        }

        private static int CountOf(Map map, ThingDef def) => map.listerThings.ThingsOfDef(def).Sum(t => t.stackCount);

        private void Fail(string reason)
        {
            finished = true;
            Log.Error(Tag + "RESULT: FAIL " + reason);
            Root.Shutdown();
        }
    }
}
