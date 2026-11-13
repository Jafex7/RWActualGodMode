using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    /// <summary>Tracks which miracles the god has unlocked (permanent), colony events, and blessed food.</summary>
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

        public bool IsUnlocked(MiracleDef def) => def != null && unlocked.Contains(def.defName);

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
                if (IsUnlocked(def) || !def.requirements.TrueForAll(r => r.IsMet()))
                {
                    continue;
                }
                unlocked.Add(def.defName);
                if (def.requirements.Count > 0 && !silent)
                {
                    Find.LetterStack.ReceiveLetter(
                        "AGM_MiracleUnlocked_Label".Translate(def.LabelCap),
                        "AGM_MiracleUnlocked_Text".Translate(def.label, def.description),
                        LetterDefOf.PositiveEvent);
                }
            }
        }

        public void DebugUnlockAll()
        {
            foreach (MiracleDef def in DefDatabase<MiracleDef>.AllDefsListForReading)
            {
                unlocked.Add(def.defName);
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
                // Older saves stored unlocks per level ("AGM_Miracle_Water:0"): keep the miracle.
                var migrated = new HashSet<string>();
                foreach (string key in unlocked)
                {
                    int colon = key.IndexOf(':');
                    migrated.Add(colon >= 0 ? key.Substring(0, colon) : key);
                }
                unlocked = migrated;
                events ??= new HashSet<string>();
                blessedThings ??= new HashSet<int>();
            }
        }
    }
}
