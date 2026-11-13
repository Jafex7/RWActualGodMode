using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// All faith dynamics numbers in one XML def (AGM_FaithTuning), so balance can be
    /// tweaked without code. Amounts are fractions of the faith bar (0.05 = 5%).
    /// </summary>
    public class FaithTuningDef : Def
    {
        // Patience (hardship buffer)
        public float basePatience = 0.25f;
        public FloatRange personalPatienceRange = new FloatRange(0.8f, 1.2f);
        public float patienceRegenPerDay = 0.1f;
        public float patienceRegenMinMood = 0.5f;

        // Gratitude
        public float gratitudeMinAverageMood = 0.55f;
        public float gratitudePerDay = 0.02f;

        // Ongoing hardship, drained per day while the condition lasts
        public float malnutritionPerDay = 0.05f;
        public float malnutritionPerDayPerSeverity = 0.15f;
        public float temperatureMinSeverity = 0.2f;
        public float temperaturePerDay = 0.08f;
        public float sicknessPerDay = 0.05f;
        public float sicknessMaxPerDay = 0.1f;
        public float miseryPerDay = 0.04f;

        // Hardship events, drained once
        public float mentalBreak = 0.08f;
        public float colonistDied = 0.05f;
        public float colonistDiedCloseExtra = 0.1f;
        public int closeOpinion = 20;
        public float downed = 0.04f;

        // Blessings
        public float blessedFoodGain = 0.04f;

        private static FaithTuningDef cached;

        public static FaithTuningDef Get => cached ??= DefDatabase<FaithTuningDef>.GetNamed("AGM_FaithTuning");
    }
}
