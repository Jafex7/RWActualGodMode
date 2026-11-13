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

        [DebugAction(Category, "Faith +20% (colonists)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void FaithUp() => OffsetFaith(0.2f);

        [DebugAction(Category, "Faith -20% (colonists)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void FaithDown() => OffsetFaith(-0.2f);

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
