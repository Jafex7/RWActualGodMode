using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// A colonist's belief in the god. Decays very slowly on its own; raised by worship,
    /// miracles, gratitude and answered prayers. Hardship drains a hidden patience buffer
    /// first, and only lowers faith once patience runs out.
    /// </summary>
    public class Need_Faith : Need
    {
        public const float LowThreshold = 0.25f;
        public const float HighThreshold = 0.75f;

        private const int IntervalTicks = 150;

        private float patience = -1f;
        private float personalPatience = 1f;

        private float moodSum;
        private int moodSamples;
        private bool hardshipToday;
        private int lastDay = -1;

        /// <summary>Tick of this pawn's last prayer request (for cooldowns).</summary>
        public int lastRequestTick = -999999;

        public Need_Faith(Pawn pawn) : base(pawn)
        {
            threshPercents = new List<float> { LowThreshold, HighThreshold };
        }

        private static FaithTuningDef Tuning => FaithTuningDef.Get;

        public bool IsPraying =>
            pawn.jobs?.curDriver is JobDriver_PrayAtShrine driver && driver.IsPraying;

        public override int GUIChangeArrow => IsPraying ? 1 : 0;

        public float MaxPatience =>
            Tuning.basePatience * personalPatience * FaithTraitUtility.Factor(pawn, f => f.patienceFactor);

        public float Patience => Mathf.Max(patience, 0f);

        public float PatiencePercent => MaxPatience > 0f ? Patience / MaxPatience : 0f;

        public override void SetInitialLevel()
        {
            CurLevelPercentage = Rand.Range(0.15f, 0.45f);
            personalPatience = Tuning.personalPatienceRange.RandomInRange;
            patience = MaxPatience;
        }

        public override void NeedInterval()
        {
            if (IsFrozen)
            {
                return;
            }
            if (patience < 0f)
            {
                patience = MaxPatience;
            }

            float dayFraction = (float)IntervalTicks / GenDate.TicksPerDay;

            float decayPerDay = def.fallPerDay * ActualGodModeMod.Settings.faithDecayMultiplier
                * FaithTraitUtility.Factor(pawn, f => f.decayFactor);
            CurLevel -= decayPerDay * dayFraction;

            float hardshipPerDay = FaithHardship.OngoingPerDay(pawn);
            float mood = pawn.needs?.mood?.CurLevelPercentage ?? 0.5f;
            if (hardshipPerDay > 0f)
            {
                Hardship(hardshipPerDay * dayFraction);
            }
            else if (mood >= Tuning.patienceRegenMinMood)
            {
                patience = Mathf.Min(MaxPatience, patience + Tuning.patienceRegenPerDay * dayFraction);
            }

            moodSum += mood;
            moodSamples++;
            int day = GenDate.DaysPassed;
            if (day != lastDay)
            {
                if (lastDay >= 0)
                {
                    EvaluateGratitude();
                }
                lastDay = day;
                moodSum = 0f;
                moodSamples = 0;
                hardshipToday = false;
            }
        }

        private void EvaluateGratitude()
        {
            if (hardshipToday || moodSamples == 0)
            {
                return;
            }
            if (moodSum / moodSamples >= Tuning.gratitudeMinAverageMood)
            {
                Gain(Tuning.gratitudePerDay * FaithTraitUtility.Factor(pawn, f => f.gratitudeFactor));
            }
        }

        /// <summary>Raise faith. Positive amounts are scaled by the pawn's traits.</summary>
        public void Gain(float amount)
        {
            if (amount > 0f)
            {
                amount *= FaithTraitUtility.Factor(pawn, f => f.gainFactor);
            }
            CurLevel += amount;
        }

        /// <summary>Hardship: drains patience first, then faith. Scaled by the pawn's traits.</summary>
        public void Hardship(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }
            if (patience < 0f)
            {
                patience = MaxPatience;
            }
            amount *= FaithTraitUtility.Factor(pawn, f => f.hardshipFactor);
            float absorbed = Mathf.Min(patience, amount);
            patience -= absorbed;
            CurLevel -= amount - absorbed;
            hardshipToday = true;
        }

        /// <summary>A direct loss that patience doesn't absorb (e.g. an ignored prayer).</summary>
        public void Lose(float amount)
        {
            CurLevel -= amount * FaithTraitUtility.Factor(pawn, f => f.hardshipFactor);
        }

        public void DebugSetPatience(float value) => patience = value;

        public override string GetTipString()
        {
            string tip = base.GetTipString();
            string patienceLabel = PatiencePercent >= 0.66f ? "AGM_Patience_High".Translate()
                : PatiencePercent >= 0.33f ? "AGM_Patience_Medium".Translate()
                : "AGM_Patience_Low".Translate();
            return tip + "\n\n" + "AGM_Patience_Tip".Translate(patienceLabel);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref patience, "patience", -1f);
            Scribe_Values.Look(ref personalPatience, "personalPatience", 1f);
            Scribe_Values.Look(ref moodSum, "moodSum", 0f);
            Scribe_Values.Look(ref moodSamples, "moodSamples", 0);
            Scribe_Values.Look(ref hardshipToday, "hardshipToday", false);
            Scribe_Values.Look(ref lastDay, "lastDay", -1);
            Scribe_Values.Look(ref lastRequestTick, "lastRequestTick", -999999);
        }
    }
}
