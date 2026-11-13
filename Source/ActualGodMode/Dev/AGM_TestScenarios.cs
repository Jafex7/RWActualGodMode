using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// Scenario builders shared by the headless self-test and dev-mode debug actions, so the
    /// player can recreate any test setup on their own save. Add one per milestone.
    /// </summary>
    public static class AGM_TestScenarios
    {
        private const string Category = "Actual God Mode";

        /// <summary>Returns the map's shrine, placing one near the colonists if there isn't one.</summary>
        public static Thing EnsureShrine(Map map)
        {
            Thing existing = ShrineUtility.ShrineOn(map);
            if (existing != null)
            {
                return existing;
            }
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned.ToList();
            IntVec3 origin = colonists.Count > 0 ? colonists[0].Position : map.Center;
            if (!CellFinder.TryFindRandomCellNear(origin, map, 12,
                    c => c.Standable(map) && GenAdj.CellsAdjacent8Way(new TargetInfo(c, map)).All(a => a.InBounds(map) && a.Standable(map)),
                    out IntVec3 cell))
            {
                return null;
            }
            Thing shrine = GenSpawn.Spawn(ThingMaker.MakeThing(AGM_DefOf.AGM_Shrine, ThingDefOf.BlocksGranite), cell, map);
            shrine.SetFaction(Faction.OfPlayer);
            return shrine;
        }

        /// <summary>Shrine, full power, every miracle unlocked, colonists at the given faith.</summary>
        public static Thing SetUpGodColony(Map map, float faith = 0.6f)
        {
            Thing shrine = EnsureShrine(map);
            AwakeningTracker.Instance?.MarkRevealedByScenario();
            if (!AGM_AwakeningDefOf.AGM_ShrineResearch.IsFinished)
            {
                Find.ResearchManager.FinishProject(AGM_AwakeningDefOf.AGM_ShrineResearch, doCompletionDialog: false);
            }
            MiracleTracker.Instance?.RecordEvent(MiracleTracker.Event_ColonistDowned);
            MiracleTracker.Instance?.DebugUnlockAll();
            DivinePower dp = DivinePower.Instance;
            dp?.Add(dp.MaxPower);
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                Need_Faith f = p.Faith();
                if (f != null)
                {
                    f.CurLevel = faith;
                }
            }
            return shrine;
        }

        [DebugAction(Category, "Set up god test colony", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 100)]
        private static void SetUpGodColonyAction()
        {
            Thing shrine = SetUpGodColony(Find.CurrentMap);
            if (shrine == null)
            {
                Messages.Message("Couldn't find room for a shrine near the colonists.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.CameraDriver.JumpToCurrentMapLoc(shrine.Position);
            Messages.Message("God test colony ready: shrine, full power, all miracles, colonists at 60% faith.", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction(Category, "Wound pawn (for Heal / prayer)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void WoundPawn(Pawn p)
        {
            for (int i = 0; i < 3; i++)
            {
                p.TakeDamage(new DamageInfo(DamageDefOf.Cut, 6f));
            }
        }

        [DebugAction(Category, "Make pawn hungry", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void MakeHungry(Pawn p)
        {
            if (p.needs?.food != null)
            {
                p.needs.food.CurLevelPercentage = 0.1f;
            }
        }
    }
}
