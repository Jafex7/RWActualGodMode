using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    /// <summary>Tracks which miracle levels the god has unlocked. Unlocks are permanent.</summary>
    public class MiracleTracker : GameComponent
    {
        private const int CheckIntervalTicks = 250;

        private HashSet<string> unlocked = new HashSet<string>();

        public static MiracleTracker Instance => Current.Game?.GetComponent<MiracleTracker>();

        public MiracleTracker(Game game)
        {
        }

        private static string Key(MiracleDef def, int level) => def.defName + ":" + level;

        public bool IsUnlocked(MiracleDef def, int level) => unlocked.Contains(Key(def, level));

        public bool AnyUnlocked(MiracleDef def) => IsUnlocked(def, 0);

        public override void StartedNewGame() => CheckUnlocks();

        public override void LoadedGame() => CheckUnlocks();

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % CheckIntervalTicks == 0)
            {
                CheckUnlocks();
            }
        }

        public void CheckUnlocks()
        {
            foreach (MiracleDef def in DefDatabase<MiracleDef>.AllDefsListForReading)
            {
                for (int i = 0; i < def.levels.Count; i++)
                {
                    if (IsUnlocked(def, i))
                    {
                        continue;
                    }
                    if (i > 0 && !IsUnlocked(def, i - 1))
                    {
                        break;
                    }
                    MiracleLevel level = def.levels[i];
                    if (!level.requirements.TrueForAll(r => r.IsMet()))
                    {
                        break;
                    }
                    unlocked.Add(Key(def, i));
                    if (level.requirements.Count > 0)
                    {
                        Find.LetterStack.ReceiveLetter(
                            "AGM_MiracleUnlocked_Label".Translate(def.LevelLabel(i)),
                            "AGM_MiracleUnlocked_Text".Translate(def.LevelLabel(i), def.description),
                            LetterDefOf.PositiveEvent);
                    }
                }
            }
        }

        public void DebugUnlockAll()
        {
            foreach (MiracleDef def in DefDatabase<MiracleDef>.AllDefsListForReading)
            {
                for (int i = 0; i < def.levels.Count; i++)
                {
                    unlocked.Add(Key(def, i));
                }
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref unlocked, "unlocked", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && unlocked == null)
            {
                unlocked = new HashSet<string>();
            }
        }
    }
}
