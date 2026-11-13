using System.Linq;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    public enum GuideStep
    {
        NotStarted,
        ResearchShrine,
        BuildShrine,
        FirstPrayers,
        ConvinceOthers,
        AnswerPrayer,
        Done
    }

    /// <summary>
    /// "The god's path": optional guidance after the Revelation. Each step completes on its own
    /// when the player gets there (nothing is blocked); a letter introduces what comes next.
    /// Hints can be turned off in the mod settings (steps still advance silently).
    /// </summary>
    public class GodGuideTracker : GameComponent
    {
        private const float FirstPrayersPower = 5f;

        private GuideStep step = GuideStep.NotStarted;
        private bool converted;
        private bool answered;

        public static GodGuideTracker Instance => Current.Game?.GetComponent<GodGuideTracker>();

        public GuideStep Step => step;

        private static bool ShowHints => ActualGodModeMod.Settings.showGuidance;

        public GodGuideTracker(Game game)
        {
        }

        public void Start()
        {
            if (step == GuideStep.NotStarted)
            {
                step = GuideStep.ResearchShrine;
            }
        }

        public void Notify_Converted(Pawn pawn) => converted = true;

        public void Notify_PrayerAnswered() => answered = true;

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 == 0)
            {
                CheckProgress();
            }
        }

        public void CheckProgress()
        {
            if (step != GuideStep.NotStarted && step != GuideStep.Done && IsComplete(step))
            {
                Advance();
            }
        }

        private bool IsComplete(GuideStep s)
        {
            switch (s)
            {
                case GuideStep.ResearchShrine:
                    return AGM_AwakeningDefOf.AGM_ShrineResearch.IsFinished;
                case GuideStep.BuildShrine:
                    return ShrineUtility.AnyShrineExists();
                case GuideStep.FirstPrayers:
                    return (DivinePower.Instance?.LifetimePower ?? 0f) >= FirstPrayersPower;
                case GuideStep.ConvinceOthers:
                    return converted || !AnyNonBelievers();
                case GuideStep.AnswerPrayer:
                    return answered;
                default:
                    return false;
            }
        }

        private static bool AnyNonBelievers()
        {
            BelieverTracker tracker = BelieverTracker.Instance;
            return tracker != null && Find.Maps.Any(m => m.IsPlayerHome && tracker.NonBelieverColonists(m).Any());
        }

        /// <summary>Complete the current step (public for the self-test and debug actions).</summary>
        public void Advance()
        {
            GuideStep finished = step;
            step = finished + 1;
            if (step == GuideStep.ConvinceOthers && !AnyNonBelievers())
            {
                step = GuideStep.AnswerPrayer; // everyone already believes
            }
            if (ShowHints)
            {
                SendLetter(finished);
            }
        }

        private void SendLetter(GuideStep finished)
        {
            Pawn speaker = AwakeningTracker.Instance?.Chosen
                ?? PawnsFinder.AllMaps_FreeColonistsSpawned.FirstOrDefault(p => p.Faith() != null);
            NamedArgument name = (speaker?.LabelShort ?? "AGM_Someone".Translate().ToString()).Named("PAWN");
            string key;
            switch (finished)
            {
                case GuideStep.ResearchShrine:
                    if (ShrineUtility.AnyShrineExists())
                    {
                        return;
                    }
                    key = "AGM_Guide_ResearchDone";
                    break;
                case GuideStep.BuildShrine:
                    key = "AGM_Guide_ShrineBuilt";
                    break;
                case GuideStep.FirstPrayers:
                    key = step == GuideStep.ConvinceOthers ? "AGM_Guide_FirstPowerConvince" : "AGM_Guide_FirstPower";
                    break;
                case GuideStep.ConvinceOthers:
                    key = "AGM_Guide_Converted";
                    break;
                case GuideStep.AnswerPrayer:
                    key = "AGM_Guide_Answered";
                    break;
                default:
                    return;
            }
            Find.LetterStack.ReceiveLetter(
                (key + "_Label").Translate(),
                (key + "_Text").Translate(name, AGM_KeyBindingDefOf.AGM_ToggleHandOfGod.MainKeyLabel.Named("KEY")),
                LetterDefOf.PositiveEvent,
                speaker != null ? new LookTargets(speaker) : LookTargets.Invalid);
        }

        public string CurrentHint() =>
            ("AGM_Guide_Step_" + step).Translate(AGM_KeyBindingDefOf.AGM_ToggleHandOfGod.MainKeyLabel.Named("KEY"));

        public void DebugReset()
        {
            step = GuideStep.NotStarted;
            converted = false;
            answered = false;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref step, "step", GuideStep.NotStarted);
            Scribe_Values.Look(ref converted, "converted");
            Scribe_Values.Look(ref answered, "answered");
        }
    }

    /// <summary>Shows the current step of the god's path.</summary>
    public class Alert_GodGuide : Alert
    {
        public Alert_GodGuide()
        {
            defaultPriority = AlertPriority.Medium;
        }

        public override string GetLabel() => "AGM_Guide_AlertLabel".Translate();

        public override TaggedString GetExplanation() => GodGuideTracker.Instance?.CurrentHint() ?? "";

        public override AlertReport GetReport()
        {
            GodGuideTracker g = GodGuideTracker.Instance;
            return g != null && ActualGodModeMod.Settings.showGuidance
                && g.Step != GuideStep.NotStarted && g.Step != GuideStep.Done;
        }
    }
}
