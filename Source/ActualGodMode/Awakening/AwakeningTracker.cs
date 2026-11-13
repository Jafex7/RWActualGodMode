using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace ActualGodMode
{
    public enum AwakeningStage
    {
        Dormant,
        Whispers,
        Trance,
        AwaitingRevelation,
        Revealed
    }

    /// <summary>Awakening pacing. All values in AGM_AwakeningTuning.xml.</summary>
    public class AwakeningTuningDef : Def
    {
        public int startDay = 3;
        public float beginMtbDays = 1f;
        public int whispersNeeded = 3;
        public FloatRange firstWhisperDelayDays = new FloatRange(0.1f, 0.3f);
        public FloatRange whisperIntervalDays = new FloatRange(0.6f, 1.0f);
        public FloatRange tranceDelayDays = new FloatRange(0.3f, 0.6f);
        public float revelationDelayHours = 6f;
        public float revelationFaithGain = 0.2f;
        public float zealotWeight = 3f;
        public float scepticWeight = 0.2f;

        private static AwakeningTuningDef cached;

        public static AwakeningTuningDef Get => cached ??= DefDatabase<AwakeningTuningDef>.GetNamed("AGM_AwakeningTuning");
    }

    /// <summary>
    /// The mod's entry point: the god slowly makes itself known to one colonist.
    /// Dormant → Whispers (odd talk in the social log) → Trance (draws the god's sign in the dirt)
    /// → Revelation (unlocks the Shrine research). Skipped by the setting or the Chosen People scenario.
    /// </summary>
    public class AwakeningTracker : GameComponent
    {
        private const int CheckIntervalTicks = 250;

        private AwakeningStage stage = AwakeningStage.Dormant;
        private Pawn chosen;
        private int whispersDone;
        private int nextEventTick = -1;
        private bool revealedByScenario;

        public static AwakeningTracker Instance => Current.Game?.GetComponent<AwakeningTracker>();

        public AwakeningStage Stage => stage;
        public Pawn Chosen => chosen;
        public int WhispersDone => whispersDone;
        public int NextEventTick => nextEventTick;

        public bool Revealed =>
            stage == AwakeningStage.Revealed || revealedByScenario || ActualGodModeMod.Settings.skipAwakening;

        private static AwakeningTuningDef Tuning => AwakeningTuningDef.Get;

        private static int Now => Find.TickManager.TicksGame;

        public AwakeningTracker(Game game)
        {
        }

        public override void GameComponentTick()
        {
            if (Now % CheckIntervalTicks != 0 || Revealed)
            {
                return;
            }
            switch (stage)
            {
                case AwakeningStage.Dormant:
                    if (GenDate.DaysPassed >= Tuning.startDay && Rand.MTBEventOccurs(Tuning.beginMtbDays, GenDate.TicksPerDay, CheckIntervalTicks))
                    {
                        Begin(null);
                    }
                    break;

                case AwakeningStage.Whispers:
                    if (!ChosenValid())
                    {
                        ResetToDormant();
                    }
                    else if (Now >= nextEventTick)
                    {
                        Whisper();
                    }
                    break;

                case AwakeningStage.Trance:
                    if (!ChosenValid())
                    {
                        ResetToDormant();
                    }
                    else if (Now >= nextEventTick && !(chosen.MentalStateDef == AGM_AwakeningDefOf.AGM_DivineTrance))
                    {
                        TryStartTrance();
                    }
                    break;

                case AwakeningStage.AwaitingRevelation:
                    if (Now >= nextEventTick)
                    {
                        Reveal();
                    }
                    break;
            }
        }

        private bool ChosenValid() => chosen != null && !chosen.Dead && chosen.IsColonist && chosen.Faith() != null;

        /// <summary>Start the Awakening. Pass null to pick the chosen pawn by weight.</summary>
        public bool Begin(Pawn pawn)
        {
            pawn ??= PickChosen();
            if (pawn == null)
            {
                return false;
            }
            chosen = pawn;
            stage = AwakeningStage.Whispers;
            whispersDone = 0;
            nextEventTick = Now + DaysToTicks(Tuning.firstWhisperDelayDays.RandomInRange);
            return true;
        }

        private static Pawn PickChosen()
        {
            List<Pawn> candidates = PawnsFinder.AllMaps_FreeColonistsSpawned.Where(p => p.Faith() != null && !p.Downed).ToList();
            if (candidates.Count == 0)
            {
                return null;
            }
            TraitDef zealot = DefDatabase<TraitDef>.GetNamed("AGM_Zealot");
            TraitDef sceptic = DefDatabase<TraitDef>.GetNamed("AGM_Sceptic");
            return candidates.RandomElementByWeight(p =>
                p.story.traits.HasTrait(zealot) ? Tuning.zealotWeight
                : p.story.traits.HasTrait(sceptic) ? Tuning.scepticWeight
                : 1f);
        }

        /// <summary>The chosen one says something strange: a social log entry (or a speech mote if alone).</summary>
        public void Whisper()
        {
            Pawn recipient = chosen.Spawned
                ? chosen.Map.mapPawns.FreeColonistsSpawned.Where(p => p != chosen).OrderBy(p => p.Position.DistanceToSquared(chosen.Position)).FirstOrDefault()
                : null;
            if (recipient != null)
            {
                Find.PlayLog.Add(new PlayLogEntry_Interaction(AGM_AwakeningDefOf.AGM_StrangeTalk, chosen, recipient, null));
                recipient.needs?.mood?.thoughts?.memories?.TryGainMemory(AGM_AwakeningDefOf.AGM_HeardStrangeTalk, chosen);
            }
            else if (chosen.Spawned)
            {
                MoteMaker.ThrowText(chosen.DrawPos, chosen.Map, ("AGM_Whisper_Alone" + Rand.RangeInclusive(1, 4)).Translate(), 6f);
            }

            if (whispersDone == 0)
            {
                Messages.Message("AGM_Whisper_First".Translate(chosen.Named("PAWN")), new LookTargets(chosen), MessageTypeDefOf.NeutralEvent);
            }
            whispersDone++;
            if (whispersDone >= Tuning.whispersNeeded)
            {
                stage = AwakeningStage.Trance;
                nextEventTick = Now + DaysToTicks(Tuning.tranceDelayDays.RandomInRange);
            }
            else
            {
                nextEventTick = Now + DaysToTicks(Tuning.whisperIntervalDays.RandomInRange);
            }
        }

        /// <summary>Put the chosen one into the divine trance (only when safe: awake, spawned, not drafted or downed).</summary>
        public bool TryStartTrance()
        {
            if (!ChosenValid() || !chosen.Spawned || chosen.Downed || chosen.Drafted || !chosen.Awake() || chosen.InMentalState)
            {
                nextEventTick = Now + GenDate.TicksPerHour;
                return false;
            }
            stage = AwakeningStage.Trance;
            return chosen.mindState.mentalStateHandler.TryStartMentalState(AGM_AwakeningDefOf.AGM_DivineTrance, forced: true, forceWake: true);
        }

        /// <summary>Called by the drawing job when the sign is finished.</summary>
        public void Notify_SignDrawn(Pawn pawn)
        {
            if (pawn != chosen || stage != AwakeningStage.Trance)
            {
                return;
            }
            stage = AwakeningStage.AwaitingRevelation;
            nextEventTick = Now + UnityEngine.Mathf.RoundToInt(Tuning.revelationDelayHours * GenDate.TicksPerHour);
        }

        /// <summary>The trance ended before the sign was drawn (interrupted): try again later.</summary>
        public void Notify_TranceEndedEarly(Pawn pawn)
        {
            if (pawn == chosen && stage == AwakeningStage.Trance)
            {
                nextEventTick = Now + DaysToTicks(0.25f);
            }
        }

        public void Reveal()
        {
            stage = AwakeningStage.Revealed;
            Pawn p = ChosenValid() ? chosen : PawnsFinder.AllMaps_FreeColonistsSpawned.FirstOrDefault();
            p?.Faith()?.Gain(Tuning.revelationFaithGain, "AGM_Cause_Revelation".Translate());
            string name = p != null ? p.LabelShort : "AGM_Someone".Translate().ToString();
            Find.LetterStack.ReceiveLetter(
                "AGM_Revelation_Label".Translate(),
                p != null ? "AGM_Revelation_Text".Translate(p.Named("PAWN")) : "AGM_Revelation_TextNoPawn".Translate(),
                LetterDefOf.PositiveEvent,
                p != null ? new LookTargets(p) : LookTargets.Invalid);
        }

        /// <summary>Scenario or debug: the god is already known; no Awakening needed.</summary>
        public void MarkRevealedByScenario() => revealedByScenario = true;

        public void ResetToDormant()
        {
            stage = AwakeningStage.Dormant;
            revealedByScenario = false;
            chosen = null;
            whispersDone = 0;
            nextEventTick = -1;
        }

        private static int DaysToTicks(float days) => UnityEngine.Mathf.RoundToInt(days * GenDate.TicksPerDay);

        public override void ExposeData()
        {
            Scribe_Values.Look(ref stage, "stage", AwakeningStage.Dormant);
            Scribe_References.Look(ref chosen, "chosen");
            Scribe_Values.Look(ref whispersDone, "whispersDone");
            Scribe_Values.Look(ref nextEventTick, "nextEventTick", -1);
            Scribe_Values.Look(ref revealedByScenario, "revealedByScenario");
        }
    }

    [DefOf]
    public static class AGM_AwakeningDefOf
    {
        public static MentalStateDef AGM_DivineTrance;
        public static JobDef AGM_DrawDivineSign;
        public static ThingDef AGM_Filth_DivineSign;
        public static InteractionDef AGM_StrangeTalk;
        public static ThoughtDef AGM_HeardStrangeTalk;
        public static ResearchProjectDef AGM_ShrineResearch;

        static AGM_AwakeningDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AGM_AwakeningDefOf));
        }
    }
}
