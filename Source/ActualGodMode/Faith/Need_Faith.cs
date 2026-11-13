using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// A colonist's belief in the god. Decays very slowly on its own;
    /// raised by worship (and later by miracles, gratitude, answered prayers).
    /// </summary>
    public class Need_Faith : Need
    {
        public const float LowThreshold = 0.25f;
        public const float HighThreshold = 0.75f;

        private const int IntervalTicks = 150;

        public Need_Faith(Pawn pawn) : base(pawn)
        {
            threshPercents = new List<float> { LowThreshold, HighThreshold };
        }

        public bool IsPraying =>
            pawn.jobs?.curDriver is JobDriver_PrayAtShrine driver && driver.IsPraying;

        public override int GUIChangeArrow => IsPraying ? 1 : 0;

        public override void SetInitialLevel()
        {
            CurLevelPercentage = Rand.Range(0.15f, 0.45f);
        }

        public override void NeedInterval()
        {
            if (IsFrozen)
            {
                return;
            }
            float decayPerDay = def.fallPerDay * ActualGodModeMod.Settings.faithDecayMultiplier;
            CurLevel -= decayPerDay * IntervalTicks / GenDate.TicksPerDay;
        }

        public void Gain(float amount)
        {
            CurLevel += amount;
        }
    }
}
