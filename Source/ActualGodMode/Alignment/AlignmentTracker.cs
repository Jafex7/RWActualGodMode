using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    public enum AlignmentBand
    {
        Wrathful,
        Stern,
        Neutral,
        Kind,
        Benevolent
    }

    /// <summary>Alignment rules and numbers. All in AGM_AlignmentTuning.xml.</summary>
    public class AlignmentTuningDef : Def
    {
        public float benevolentAt = 60f;
        public float kindAt = 20f;
        public float sternAt = -20f;
        public float wrathfulAt = -60f;

        /// <summary>Points per day alignment drifts back toward 0.</summary>
        public float driftPerDay = 1.5f;

        // Deeds (points)
        public float healColonist = 1f;
        public float healMercy = 3f;
        public float healAnimal = 0.5f;
        public float feedHungry = 1f;
        public float prayerAnswered = 2f;
        public float wishGranted = 1f;
        public float smiteHelplessEnemy = -5f;
        public float smiteOwnFollower = -10f;
        public float punishBreak = -4f;
        public float smitePrisoner = -6f;
        public float smiteVisitor = -8f;
        public float smiteColonyAnimal = -3f;

        // Effects
        public float kindGratitudeFactor = 1.5f;
        public float benevolentGratitudeFactor = 2f;
        public float kindPatienceRegenFactor = 1.5f;
        public float benevolentPatienceRegenFactor = 2f;
        public float fearFaithGain = 0.03f;

        private static AlignmentTuningDef cached;

        public static AlignmentTuningDef Get => cached ??= DefDatabase<AlignmentTuningDef>.GetNamed("AGM_AlignmentTuning");
    }

    /// <summary>
    /// The god's alignment, from -100 (evil) to +100 (good), moved by deeds and slowly drifting
    /// back toward neutral. Neglect (ignored prayers) costs faith, not alignment.
    /// </summary>
    public class AlignmentTracker : GameComponent
    {
        public const float Max = 100f;

        private float value;
        private AlignmentBand lastBand = AlignmentBand.Neutral;
        private FaithLog deeds = new FaithLog();

        public static AlignmentTracker Instance => Current.Game?.GetComponent<AlignmentTracker>();

        public float Value => value;

        public FaithLog Deeds => deeds;

        public AlignmentBand Band => BandFor(value);

        public bool IsGood => Band >= AlignmentBand.Kind;

        public bool IsEvil => Band <= AlignmentBand.Stern;

        public AlignmentTracker(Game game)
        {
        }

        public static AlignmentBand BandFor(float v)
        {
            AlignmentTuningDef t = AlignmentTuningDef.Get;
            if (v >= t.benevolentAt) return AlignmentBand.Benevolent;
            if (v >= t.kindAt) return AlignmentBand.Kind;
            if (v <= t.wrathfulAt) return AlignmentBand.Wrathful;
            if (v <= t.sternAt) return AlignmentBand.Stern;
            return AlignmentBand.Neutral;
        }

        public static string BandLabel(AlignmentBand band) => ("AGM_Alignment_" + band).Translate();

        /// <summary>Record a deed. Positive is good, negative evil.</summary>
        public void Deed(float amount, string label)
        {
            if (Mathf.Approximately(amount, 0f))
            {
                return;
            }
            float before = value;
            value = Mathf.Clamp(value + amount, -Max, Max);
            deeds.Add(label, value - before);
            CheckBandChange();
        }

        /// <summary>Record a morally neutral act in the ledger (e.g. justice) without moving alignment.</summary>
        public void LogNeutral(string label) => deeds.Add(label, 0f, 0.0001f);

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % GenDate.TicksPerHour != 0 || Mathf.Approximately(value, 0f))
            {
                return;
            }
            float drift = AlignmentTuningDef.Get.driftPerDay / GenDate.HoursPerDay;
            value = value > 0f ? Mathf.Max(0f, value - drift) : Mathf.Min(0f, value + drift);
            CheckBandChange();
        }

        private void CheckBandChange()
        {
            AlignmentBand band = Band;
            if (band == lastBand)
            {
                return;
            }
            bool towardGood = band > lastBand;
            lastBand = band;
            Find.LetterStack.ReceiveLetter(
                "AGM_Alignment_Changed_Label".Translate(BandLabel(band)),
                ("AGM_Alignment_Changed_" + band).Translate(),
                band == AlignmentBand.Neutral ? LetterDefOf.NeutralEvent : towardGood ? LetterDefOf.PositiveEvent : LetterDefOf.NegativeEvent);
            HandOfGodCursor.Notify_AlignmentChanged();
        }

        /// <summary>Gratitude multiplier for a good god.</summary>
        public float GratitudeFactor =>
            Band == AlignmentBand.Benevolent ? AlignmentTuningDef.Get.benevolentGratitudeFactor
            : Band == AlignmentBand.Kind ? AlignmentTuningDef.Get.kindGratitudeFactor
            : 1f;

        /// <summary>Patience refill multiplier for a good god.</summary>
        public float PatienceRegenFactor =>
            Band == AlignmentBand.Benevolent ? AlignmentTuningDef.Get.benevolentPatienceRegenFactor
            : Band == AlignmentBand.Kind ? AlignmentTuningDef.Get.kindPatienceRegenFactor
            : 1f;

        public void DebugSet(float v)
        {
            value = Mathf.Clamp(v, -Max, Max);
            CheckBandChange();
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref value, "value");
            Scribe_Values.Look(ref lastBand, "lastBand", AlignmentBand.Neutral);
            Scribe_Deep.Look(ref deeds, "deeds");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && deeds == null)
            {
                deeds = new FaithLog();
            }
        }
    }
}
