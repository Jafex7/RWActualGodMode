using LudeonTK;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    public static class AGM_DebugActions
    {
        private const string Category = "Actual God Mode";

        [DebugAction(Category, "Divine power +50", allowedGameStates = AllowedGameStates.Playing)]
        private static void AddPower() => DivinePower.Instance?.Add(50f);

        [DebugAction(Category, "Divine power = 0", allowedGameStates = AllowedGameStates.Playing)]
        private static void ClearPower() => DivinePower.Instance?.DebugSet(0f);

        [DebugAction(Category, "Alignment +20 (good)", allowedGameStates = AllowedGameStates.Playing)]
        private static void AlignmentUp() => AlignmentTracker.Instance?.DebugSet(AlignmentTracker.Instance.Value + 20f);

        [DebugAction(Category, "Alignment -20 (evil)", allowedGameStates = AllowedGameStates.Playing)]
        private static void AlignmentDown() => AlignmentTracker.Instance?.DebugSet(AlignmentTracker.Instance.Value - 20f);

        [DebugAction(Category, "Alignment = 0", allowedGameStates = AllowedGameStates.Playing)]
        private static void AlignmentReset() => AlignmentTracker.Instance?.DebugSet(0f);

        [DebugAction(Category, "Unlock all miracles", allowedGameStates = AllowedGameStates.Playing)]
        private static void UnlockAllMiracles() => MiracleTracker.Instance?.DebugUnlockAll();

        [DebugAction(Category, "Faith +20% (colonists)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void FaithUp() => OffsetFaith(0.2f);

        [DebugAction(Category, "Faith -20% (colonists)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void FaithDown() => OffsetFaith(-0.2f);

        [DebugAction(Category, "Prayer request: food", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RequestFood(Pawn p) => ForceRequest(p, "AGM_Request_Food");

        [DebugAction(Category, "Prayer request: heal", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RequestHeal(Pawn p) => ForceRequest(p, "AGM_Request_Heal");

        [DebugAction(Category, "Prayer wish now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void WishNow()
        {
            IncidentDef def = DefDatabase<IncidentDef>.GetNamed("AGM_PrayerWish");
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(def.category, Find.CurrentMap);
            if (!def.Worker.TryExecute(parms))
            {
                Messages.Message("No wish possible right now (needs a shrine and a colonist with 25%+ faith).", MessageTypeDefOf.RejectInput, false);
            }
        }

        [DebugAction(Category, "Record 'colonist downed' (unlocks Heal)", allowedGameStates = AllowedGameStates.Playing)]
        private static void RecordDowned() => MiracleTracker.Instance?.RecordEvent(MiracleTracker.Event_ColonistDowned);

        private static void ForceRequest(Pawn p, string defName)
        {
            if (p.Faith() != null)
            {
                PrayerRequestTracker.Instance?.Create(p, DefDatabase<PrayerRequestDef>.GetNamed(defName));
            }
        }

        private static void OffsetFaith(float amount)
        {
            foreach (Pawn pawn in Find.CurrentMap.mapPawns.FreeColonistsSpawned)
            {
                Need_Faith faith = pawn.Faith();
                if (faith != null)
                {
                    faith.CurLevel += amount;
                }
            }
        }
    }
}
