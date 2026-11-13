using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// Numbers for the Hand (grab / carry / place), from <c>Defs/HandOfGod/AGM_HandTuning.xml</c>.
    /// Cost guideline: "how much are you affecting the world?" Items are cheap, living creatures dearer.
    /// </summary>
    public class HandTuningDef : Def
    {
        /// <summary>Lift capacity: base plus a share per living believer (kg).</summary>
        public float baseLiftKg = 35f;
        public float liftPerBelieverKg = 15f;

        /// <summary>Power to pick up: flat plus per kg.</summary>
        public float itemPickupCost = 0.5f;
        public float itemPickupCostPerKg = 0.02f;
        public float pawnPickupCost = 3f;
        public float pawnPickupCostPerKg = 0.03f;

        /// <summary>Power per kg per in-game hour while held.</summary>
        public float itemHoldCostPerKgPerHour = 0.05f;
        public float pawnHoldCostPerKgPerHour = 0.15f;

        /// <summary>Input: hold still this long on a thing to drag it; moving further than this (pixels) starts a gesture instead.</summary>
        public float grabHoldSeconds = 0.25f;
        public float dragThresholdPixels = 12f;

        /// <summary>Alignment: gently carrying a downed person to a bed (once per person per this many hours).</summary>
        public float carryToBedDeed = 1f;
        public float carryToBedCooldownHours = 24f;

        private static HandTuningDef cached;

        public static HandTuningDef Get => cached ??= DefDatabase<HandTuningDef>.GetNamed("AGM_HandTuning");
    }
}
