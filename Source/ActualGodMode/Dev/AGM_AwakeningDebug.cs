using LudeonTK;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    /// <summary>Dev-mode controls to step through the Awakening one phase at a time.</summary>
    public static class AGM_AwakeningDebug
    {
        private const string Category = "Actual God Mode - Awakening";

        [DebugAction(Category, "Status", allowedGameStates = AllowedGameStates.Playing, displayPriority = 100)]
        private static void Status()
        {
            AwakeningTracker t = AwakeningTracker.Instance;
            string next = t.NextEventTick < 0 ? "-" : (t.NextEventTick - Find.TickManager.TicksGame).ToStringTicksToPeriod();
            Messages.Message($"Awakening: {t.Stage}, chosen: {t.Chosen?.LabelShort ?? "none"}, whispers: {t.WhispersDone}, next step in: {next}, revealed: {t.Revealed}",
                MessageTypeDefOf.SilentInput, false);
        }

        [DebugAction(Category, "1. Begin (click the chosen pawn)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 90)]
        private static void Begin(Pawn p)
        {
            AwakeningTracker.Instance.ResetToDormant();
            if (!AwakeningTracker.Instance.Begin(p))
            {
                Messages.Message("That pawn can't be chosen (needs to be a colonist with faith).", MessageTypeDefOf.RejectInput, false);
            }
        }

        [DebugAction(Category, "2. Whisper now", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 80)]
        private static void WhisperNow()
        {
            AwakeningTracker t = AwakeningTracker.Instance;
            if (t.Stage != AwakeningStage.Whispers)
            {
                Messages.Message("Not in the Whispers stage (use 'Begin' first).", MessageTypeDefOf.RejectInput, false);
                return;
            }
            t.Whisper();
        }

        [DebugAction(Category, "3. Trance now", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 70)]
        private static void TranceNow()
        {
            AwakeningTracker t = AwakeningTracker.Instance;
            if (t.Chosen == null)
            {
                Messages.Message("No chosen pawn (use 'Begin' first).", MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!t.TryStartTrance())
            {
                Messages.Message("The chosen pawn can't enter a trance right now (asleep, drafted, downed or already in a mental state).", MessageTypeDefOf.RejectInput, false);
            }
        }

        [DebugAction(Category, "4. Revelation now", allowedGameStates = AllowedGameStates.Playing, displayPriority = 60)]
        private static void RevealNow() => AwakeningTracker.Instance.Reveal();

        [DebugAction(Category, "Reset to dormant", allowedGameStates = AllowedGameStates.Playing, displayPriority = 10)]
        private static void Reset() => AwakeningTracker.Instance.ResetToDormant();
    }
}
