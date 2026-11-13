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

            IntVec3 origin = colonists[0].Position;
            if (!CellFinder.TryFindRandomCellNear(origin, map, 10,
                    c => c.Standable(map) && GenAdj.CellsAdjacent8Way(new TargetInfo(c, map)).All(a => a.InBounds(map) && a.Standable(map)),
                    out IntVec3 cell))
            {
                Fail("No room to place the shrine.");
                return;
            }
            shrine = GenSpawn.Spawn(ThingMaker.MakeThing(AGM_DefOf.AGM_Shrine, ThingDefOf.BlocksGranite), cell, map);
            shrine.SetFaction(Faction.OfPlayer);
            Log.Message(Tag + $"Shrine placed at {cell}. Pray spots: {shrine.TryGetComp<CompShrine>().PraySpots.Count}");

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

        private void Finish()
        {
            finished = true;
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

            Log.Message(Tag + $"RESULT: {(failures == 0 ? "PASS" : "FAIL")} ({failures} failure(s))");
            Root.Shutdown();
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

        private static int CountOf(Map map, ThingDef def) => map.listerThings.ThingsOfDef(def).Sum(t => t.stackCount);

        private void Fail(string reason)
        {
            finished = true;
            Log.Error(Tag + "RESULT: FAIL " + reason);
            Root.Shutdown();
        }
    }
}
