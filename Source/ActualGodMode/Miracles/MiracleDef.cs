using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// A miracle the god can cast. Each miracle has one or more levels with a fixed cost
    /// and effect size. Levels unlock in order once their requirements are met.
    /// </summary>
    public class MiracleDef : Def
    {
        public Type workerClass;

        /// <summary>Default gesture that casts this miracle in Hand of God mode (players can re-record it).</summary>
        public GestureDef gesture;

        /// <summary>Icon taken from this thing's UI icon (placeholder until custom art).</summary>
        public ThingDef iconThing;
        public string iconPath;

        /// <summary>Faith gained by awake colonists who see the miracle happen.</summary>
        public float witnessFaithGain = 0.03f;
        public float witnessRadius = 12f;

        public List<MiracleLevel> levels = new List<MiracleLevel>();

        /// <summary>Optional: holding the click in Hand of God mode pours the miracle under the cursor (see <see cref="MiracleChannel"/>).</summary>
        public MiracleChannelProps channel;

        public int order;

        [Unsaved(false)]
        private MiracleWorker workerInt;

        [Unsaved(false)]
        private Texture2D iconInt;

        public MiracleWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (MiracleWorker)Activator.CreateInstance(workerClass);
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public Texture2D Icon
        {
            get
            {
                if (iconInt == null)
                {
                    if (!iconPath.NullOrEmpty())
                    {
                        iconInt = ContentFinder<Texture2D>.Get(iconPath);
                    }
                    else if (iconThing != null)
                    {
                        iconInt = iconThing.uiIcon;
                    }
                    else
                    {
                        iconInt = BaseContent.BadTex;
                    }
                }
                return iconInt;
            }
        }

        public string LevelLabel(int index) => (label + " " + levels[index].label).CapitalizeFirst();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors())
            {
                yield return e;
            }
            if (workerClass == null || !typeof(MiracleWorker).IsAssignableFrom(workerClass))
            {
                yield return "workerClass must be a MiracleWorker subclass.";
            }
            if (levels.NullOrEmpty())
            {
                yield return "has no levels.";
            }
        }
    }

    public class MiracleLevel
    {
        /// <summary>Shown after the miracle's label, e.g. "I", "II".</summary>
        public string label = "I";
        public float cost;
        public float radius;
        public int amount;
        public ThingDef thingDef;
        public List<MiracleRequirement> requirements = new List<MiracleRequirement>();
    }

    /// <summary>
    /// How a miracle is channelled in Hand of God mode. Per tile: one unit for each new cell the
    /// cursor crosses (water). Timed: units at a fixed rhythm at the cursor (logs, food crops).
    /// Pour-only: no click cast; the charge is the level's <c>amount</c> poured over as many presses
    /// as needed (right-click cancels). Otherwise a quick click is the normal cast and holding
    /// streams for up to <see cref="maxSeconds"/>.
    /// </summary>
    public class MiracleChannelProps
    {
        public bool pourOnly;

        /// <summary>Pour-only, timed: seconds of holding to pour the full charge.</summary>
        public float pourSeconds = 3f;

        /// <summary>Stream: longest it lasts (real seconds of holding).</summary>
        public float maxSeconds = 2f;

        /// <summary>A press released sooner than this is a click (the normal cast).</summary>
        public float clickSeconds = 0.2f;

        public bool perTile;
        public float unitsPerSecond = 6f;

        /// <summary>Stream: power per unit; if 0, the level's cost spread over its area, times <see cref="markup"/>. (Pour-only: level cost / units.)</summary>
        public float unitCost;
        public float markup = 1.5f;

        /// <summary>Timed spawns: one of these (default: the level's thingDef), <see cref="countPerUnit"/> at a time.</summary>
        public List<ThingDef> things = new List<ThingDef>();
        public int countPerUnit = 1;
    }
}
