using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    /// <summary>Tracks which miracle levels the god has unlocked. Unlocks are permanent.</summary>
    public class MiracleTracker : GameComponent
    {
        private const int CheckIntervalTicks = 250;

        // Colony history events (unlock requirements, context-aware whispers)
        public const string Event_ColonistDowned = "ColonistDowned";
        public const string Event_ColonistDied = "ColonistDied";
        public const string Event_Raid = "Raid";
        public const string Event_Fire = "Fire";
        public const string Event_Rain = "Rain";
        public const string Event_Starvation = "Starvation";

        private HashSet<string> unlocked = new HashSet<string>();

        /// <summary>Things that have happened in this game (for "survived/witnessed X" unlocks).</summary>
        private HashSet<string> events = new HashSet<string>();

        /// <summary>Thing IDs of food made by miracles (blessed).</summary>
        private HashSet<int> blessedThings = new HashSet<int>();

        public bool HasEvent(string key) => events.Contains(key);

        public void RecordEvent(string key)
        {
            if (events.Add(key))
            {
                CheckUnlocks();
            }
        }

        public bool IsBlessed(Thing t) => t != null && blessedThings.Contains(t.thingIDNumber);

        public void Bless(Thing t)
        {
            if (t != null)
            {
                blessedThings.Add(t.thingIDNumber);
            }
        }

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

        /// <summary>False until the first check after the god is revealed; that check unlocks silently.</summary>
        private bool caughtUp;

        public void CheckUnlocks()
        {
            // Nothing unlocks before the god is known (Revelation, Chosen People, or the skip setting).
            if (!(AwakeningTracker.Instance?.Revealed ?? false))
            {
                return;
            }
            bool silent = !caughtUp;
            caughtUp = true;
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
                    if (level.requirements.Count > 0 && !silent)
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
            Scribe_Values.Look(ref caughtUp, "caughtUp");
            Scribe_Collections.Look(ref unlocked, "unlocked", LookMode.Value);
            Scribe_Collections.Look(ref events, "events", LookMode.Value);
            Scribe_Collections.Look(ref blessedThings, "blessedThings", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                unlocked ??= new HashSet<string>();
                events ??= new HashSet<string>();
                blessedThings ??= new HashSet<int>();
            }
        }
    }
}
