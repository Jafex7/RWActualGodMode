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
            Check(px[inside + 3] == 255 && px[inside] > 200, $"cursor pixels: inside opaque gold ({px[inside]},{px[inside + 1]},{px[inside + 2]},{px[inside + 3]})");
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

            StartAwakeningPhase();
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

            ResearchProjectDef research = AGM_AwakeningDefOf.AGM_ShrineResearch;
            t.Reveal();
            Check(t.Revealed && !research.IsHidden && research.PrerequisitesCompleted, "revelation reveals and unlocks the shrine research");

            new ScenPart_StartWithShrine().PostGameStart();
            Check(research.IsFinished, "Chosen People scenario part finishes the shrine research");
            Complete();
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
            Check(tracker.IsUnlocked(water, 0) && tracker.IsUnlocked(food, 0) && tracker.IsUnlocked(wood, 0), "level I of water/food/wood unlocked from the start");
            Check(!tracker.IsUnlocked(food, 1), "food II still locked (needs 150 lifetime power)");

            CellFinder.TryFindRandomCellNear(shrine.Position, map, 6, c => c.Standable(map) && c.GetFirstItem(map) == null, out IntVec3 near);
            dp.Add(100f);

            // Food I
            float before = dp.Power;
            int meals = CountOf(map, ThingDefOf.MealSimple);
            AcceptanceReport r = MiracleUtility.TryCast(food, 0, near, map);
            Check(r.Accepted, "food I cast accepted " + r.Reason);
            Check(CountOf(map, ThingDefOf.MealSimple) - meals == 2, $"food I spawned 2 meals (got {CountOf(map, ThingDefOf.MealSimple) - meals})");
            Check(Mathf.Approximately(before - dp.Power, 10f), $"food I cost 10 (spent {before - dp.Power:F1})");

            // Wood I
            int logs = CountOf(map, ThingDefOf.WoodLog);
            r = MiracleUtility.TryCast(wood, 0, near, map);
            Check(r.Accepted && CountOf(map, ThingDefOf.WoodLog) - logs == 25, $"wood I spawned 25 logs (got {CountOf(map, ThingDefOf.WoodLog) - logs})");

            // Water I puts out a fire
            CellFinder.TryFindRandomCellNear(shrine.Position, map, 8, c => c.Standable(map) && c.GetFirstItem(map) == null && (c - near).LengthHorizontal > 4f, out IntVec3 fireCell);
            // Spawn directly: vanilla only starts fires on flammable cells.
            Fire fire = (Fire)ThingMaker.MakeThing(ThingDefOf.Fire);
            fire.fireSize = 0.5f;
            GenSpawn.Spawn(fire, fireCell, map);
            Check(fireCell.ContainsStaticFire(map), "test fire started");
            r = MiracleUtility.TryCast(water, 0, fireCell, map);
            Check(r.Accepted && !fireCell.ContainsStaticFire(map), "water I extinguished the fire");

            // Outside influence is refused and costs nothing
            float radius = shrine.TryGetComp<CompShrine>().Props.influenceRadius;
            IntVec3 far = map.AllCells.FirstOrDefault(c => c.Standable(map) && !c.Fogged(map) && (c - shrine.Position).LengthHorizontal > radius + 5f);
            before = dp.Power;
            r = MiracleUtility.TryCast(food, 0, far, map);
            Check(!r.Accepted && Mathf.Approximately(before, dp.Power), "cast outside influence refused, no power spent: " + r.Reason);

            // Not enough power is refused
            dp.DebugSet(3f);
            r = MiracleUtility.TryCast(food, 0, near, map);
            Check(!r.Accepted && Mathf.Approximately(dp.Power, 3f), "cast without enough power refused: " + r.Reason);
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
                        bestWrong = Mathf.Min(bestWrong, m.runnerUpDistance);
                    }
                    else
                    {
                        Log.Message(Tag + $"    misread {miracle.defName} as {m.miracle?.defName} (d={m.distance:F2})");
                    }
                }
            }
            Log.Message(Tag + $"  simulated drawings: {correct}/{trials} recognized; worst correct d={worstCorrect:F2}, closest wrong d={bestWrong:F2}");
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

            // Blessed food: miracle meals are blessed, stay blessed when split, and give faith when eaten
            dp.Add(100f);
            CellFinder.TryFindRandomCellNear(shrine.Position, map, 6, c => c.Standable(map) && c.GetFirstItem(map) == null && c.GetFirstPawn(map) == null, out IntVec3 spot);
            MiracleUtility.TryCast(DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Food"), 0, spot, map);
            Thing meal = map.listerThings.ThingsOfDef(ThingDefOf.MealSimple).FirstOrDefault(t => miracles.IsBlessed(t));
            Check(meal != null, "food miracle meals are blessed");
            if (meal != null)
            {
                Thing piece = meal.stackCount > 1 ? meal.SplitOff(1) : meal;
                Check(miracles.IsBlessed(piece), "blessing survives a stack split");
                faith.CurLevel = 0.5f;
                piece.Ingested(pawn, 1f);
                float expected = FaithTuningDef.Get.blessedFoodGain * FaithTraitUtility.Factor(pawn, f => f.gainFactor);
                Check(Mathf.Abs(faith.CurLevel - 0.5f - expected) < 0.002f, $"eating blessed food gives faith (+{faith.CurLevel - 0.5f:F3}, expected +{expected:F3})");
            }

            // Heal: locked until a colonist is downed; heals up to 10 severity, worst first
            MiracleDef heal = DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Heal");
            Check(!miracles.IsUnlocked(heal, 0), "heal locked before anyone is downed");
            miracles.RecordEvent(MiracleTracker.Event_ColonistDowned);
            Check(miracles.IsUnlocked(heal, 0), "heal unlocked after a colonist is downed");
            for (int i = 0; i < 3; i++)
            {
                pawn.TakeDamage(new DamageInfo(DamageDefOf.Cut, 6f));
            }
            float severityBefore = MiracleWorker_Heal.HealableInjuries(pawn).Sum(h => h.Severity);
            dp.Add(100f);
            AcceptanceReport r = MiracleUtility.TryCast(heal, 0, pawn.Position, map);
            float severityAfter = MiracleWorker_Heal.HealableInjuries(pawn).Sum(h => h.Severity);
            float healed = severityBefore - severityAfter;
            Check(r.Accepted && Mathf.Abs(healed - Mathf.Min(10f, severityBefore)) < 0.01f,
                $"heal I mended {healed:F1} of {severityBefore:F1} severity (expected {Mathf.Min(10f, severityBefore):F1}) {r.Reason}");
            Check(!MiracleUtility.CanCast(heal, 0, spot, map).Accepted, "heal refuses a cell with no one in it");

            // Request answered: cast the asked-for miracle near the requester
            PrayerRequestDef foodRequest = DefDatabase<PrayerRequestDef>.GetNamed("AGM_Request_Food");
            dp.Add(100f);
            PrayerRequest req = requests.Create(pawn, foodRequest);
            faith.CurLevel = 0.5f;
            MiracleUtility.TryCast(DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Food"), 0, NearFreeCell(pawn, map), map);
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
            MiracleUtility.TryCast(DefDatabase<MiracleDef>.GetNamed("AGM_Miracle_Wood"), 0, NearFreeCell(shrine, map), map);
            Check(!requests.Active.Contains(req) && other.Faith().CurLevel > 0.5f, "wish for a sign granted by any miracle; others share the joy");

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
