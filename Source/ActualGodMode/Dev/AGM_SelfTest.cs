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

            // Deliverance prayer: an enemy close by
            Faction enemy = Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
            if (enemy != null)
            {
                Pawn raider = PawnGenerator.GeneratePawn(PawnKindDefOf.Villager, enemy);
                GenSpawn.Spawn(raider, CellFinder.RandomClosewalkCellNear(a.Position, map, 4), map);
                Check(PrayerRequestWorker_Hunted.Threat(a) != null, "an enemy nearby can make a believer pray for deliverance");
                Check(WrathJudgement.Classify(raider) == WrathJudgement.Target.Enemy, "an armed enemy counts as a just target");
                raider.Destroy();
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
            float capacity = GodHand.Capacity;
            Check(Mathf.Approximately(capacity, t.baseLiftKg + t.liftPerBelieverKg * BelieverTracker.Instance.LivingCount) && capacity > 60f,
                $"lift capacity grows with believers ({capacity:F0} kg, {BelieverTracker.Instance.LivingCount} believers)");

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

            // A downed colonist carried to a bed: tucked in, and a small good deed. The game doesn't end while they're lifted.
            Pawn colonist = map.mapPawns.FreeColonistsSpawned.First(p => !p.Dead && !p.Downed);
            Teleport(colonist, shrine.Position + new IntVec3(2, 0, 3), map);
            Building_Bed bed = AGM_TestScenarios.SetUpHandTest(map); // downs the first standing colonist
            Check(colonist.Downed, "the hand test setup downs a colonist (anesthetic)");
            Log.Message(Tag + $"  colonist mass {GodHand.MassOf(colonist):F0} kg, capacity {GodHand.Capacity:F0} kg");
            if (GodHand.MassOf(colonist) > GodHand.Capacity)
            {
                // Lightning in the alignment phase can kill a believer, shrinking the hand.
                Log.Message(Tag + "  (too few living believers to lift a colonist: skipping the colonist checks)");
                colonist.health.RemoveHediff(colonist.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Anesthetic));
                RunHandWorkOrderTests(map, hand, colonist);
                return;
            }
            dp.DebugSet(dp.MaxPower);
            alignment.DebugSet(0f);
            AcceptanceReport canLift = hand.CanGrab(colonist);
            Check(hand.TryGrab(colonist, drag: true) && !colonist.Spawned, $"the hand lifts a colonist {canLift.Reason} (holding {hand.Held.ToStringSafe()}, power {DivinePower.Instance.Power:F0}, in influence {InfluenceUtility.InInfluence(colonist.PositionHeld, map)})");
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
            RunDangerDropTests(map, hand, colonist);
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
                IntVec3 c = GenRadial.RadialCellsAround(around, 6f, true).FirstOrDefault(x => x.InBounds(map) && x.Standable(map)
                    && x.GetPlant(map) == null && x.GetEdifice(map) == null && map.fertilityGrid.FertilityAt(x) > 0.5f
                    && InfluenceUtility.InInfluence(x, map) && x.GetFirstPawn(map) == null && x.GetFirstItem(map) == null);
                if (!c.IsValid)
                {
                    return null;
                }
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
            hand.TryGrab(worker, drag: false);
            hand.MoveTo(oak.Position);
            hand.Place(oak.Position, order: true);
            bool designated = map.designationManager.DesignationOn(oak, DesignationDefOf.CutPlant) != null;
            bool cutting = (worker.CurJobDef?.defName ?? "").StartsWith("CutPlant") || worker.jobs.jobQueue.Any(q => q.job.def.defName.StartsWith("CutPlant"));
            Check(designated && cutting, $"a woodcutter set down on a tree marks it and starts cutting (designated {designated}, job {worker.CurJobDef?.defName})");

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

        private static void Teleport(Pawn p, IntVec3 c, Map map)
        {
            IntVec3 dest = CellFinder.RandomClosewalkCellNear(c, map, 2);
            p.jobs?.StopAll();
            p.Position = dest;
            p.Notify_Teleported();
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
