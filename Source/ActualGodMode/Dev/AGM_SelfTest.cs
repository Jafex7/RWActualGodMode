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

            Texture2D cursor = HandOfGodCursor.Texture;
            Check(cursor != null && cursor.width == 32 && cursor.height == 32, "Hand of God cursor texture generated");
            Cursor.SetCursor(cursor, HandOfGodCursor.Hotspot, CursorMode.Auto);
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            Log.Message(Tag + "  (cursor set and reset; an 'Invalid texture used for cursor' warning would mean Waylith rejected it)");

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

        private static int CountOf(Map map, ThingDef def) => map.listerThings.ThingsOfDef(def).Sum(t => t.stackCount);

        private void Fail(string reason)
        {
            finished = true;
            Log.Error(Tag + "RESULT: FAIL " + reason);
            Root.Shutdown();
        }
    }
}
