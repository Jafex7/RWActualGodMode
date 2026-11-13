using System;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// A kind of prayer a colonist can make: what condition triggers it, which miracle
    /// answers it, how long the god has, and the faith reward or penalty.
    /// Needs-driven requests are created by PrayerRequestTracker; wishes (isWish) come
    /// from the storyteller via IncidentWorker_PrayerWish.
    /// </summary>
    public class PrayerRequestDef : Def
    {
        public Type workerClass = typeof(PrayerRequestWorker);
        public MiracleDef miracle;

        public bool isWish;
        public bool urgent;

        /// <summary>Mean days between requests while the condition holds (scaled by traits).</summary>
        public float mtbDays = 2f;

        public float deadlineHours = 24f;
        public float answerRadius = 8f;
        public float minFaith = 0.15f;

        public float reward = 0.1f;
        public float witnessReward = 0.03f;

        /// <summary>Wishes: every colonist on the map shares in the reward.</summary>
        public bool rewardAllColonists;

        public float ignoredPenalty = 0.06f;

        [MustTranslate] public string letterLabel;
        [MustTranslate] public string letterText;
        [MustTranslate] public string alertLine;

        [Unsaved(false)]
        private PrayerRequestWorker workerInt;

        public PrayerRequestWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (PrayerRequestWorker)Activator.CreateInstance(workerClass);
                    workerInt.def = this;
                }
                return workerInt;
            }
        }
    }

    /// <summary>One active prayer.</summary>
    public class PrayerRequest : IExposable
    {
        public Pawn pawn;
        public PrayerRequestDef def;
        public Map map;
        public int startTick;
        public int deadlineTick;

        public int TicksLeft => deadlineTick - Find.TickManager.TicksGame;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Defs.Look(ref def, "def");
            Scribe_References.Look(ref map, "map");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref deadlineTick, "deadlineTick");
        }
    }
}
