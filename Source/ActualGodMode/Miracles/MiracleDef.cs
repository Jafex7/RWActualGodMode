using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// A miracle the god can cast. One miracle, no levels: its numbers (cost, amount, radius...)
    /// are read through <see cref="Stats"/>, which applies the rank bought in its
    /// <see cref="PowerTrackDef"/> (god progression).
    /// It unlocks once its <see cref="requirements"/> are met.
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

        // Base numbers (see Stats).
        public float cost;
        public float radius;
        public int amount;
        public ThingDef thingDef;

        /// <summary>What one unit of <c>amount</c> is called ("tiles", "logs"), for the Powers page.</summary>
        public string unitLabel;

        /// <summary>What unlocks it; empty = available as soon as the god is revealed.</summary>
        public List<MiracleRequirement> requirements = new List<MiracleRequirement>();

        /// <summary>Optional: the miracle is poured from the hand while the click is held (see <see cref="MiracleChannel"/>).</summary>
        public MiracleChannelProps channel;

        public int order;

        [Unsaved(false)]
        private MiracleWorker workerInt;

        [Unsaved(false)]
        private Texture2D iconInt;

        [Unsaved(false)]
        private MiracleStats statsInt;

        [Unsaved(false)]
        private int statsVersion = -1;

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

        /// <summary>The miracle's current numbers: the XML values, raised by the rank bought in its power track.</summary>
        public MiracleStats Stats
        {
            get
            {
                if (statsInt == null || statsVersion != GodProgression.Version)
                {
                    statsInt = StatsAt(GodProgression.Rank(PowerTrackDef.ForMiracle(this)));
                    statsVersion = GodProgression.Version;
                }
                return statsInt;
            }
        }

        /// <summary>The numbers at a given rank of its power track (0 = the XML values).</summary>
        public MiracleStats StatsAt(int rank)
        {
            var s = new MiracleStats { cost = cost, radius = radius, amount = amount, thingDef = thingDef };
            PowerTrackDef track = PowerTrackDef.ForMiracle(this);
            if (track != null && rank > 0)
            {
                PowerRank r = track.ranks[Mathf.Min(rank, track.MaxRank) - 1];
                if (r.amount > 0)
                {
                    s.amount = r.amount;
                }
                if (r.cost > 0f)
                {
                    s.cost = r.cost;
                }
                if (r.radius > 0f)
                {
                    s.radius = r.radius;
                }
            }
            return s;
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
            if (channel != null && amount <= 0)
            {
                yield return "a poured miracle needs an amount (units in a full charge).";
            }
        }
    }

    /// <summary>A miracle's effective numbers (base XML values, raised by its power track).</summary>
    public class MiracleStats
    {
        public float cost;
        public float radius;
        public int amount;
        public ThingDef thingDef;
    }

    /// <summary>
    /// A poured miracle (Hand of God mode, or picked from the Miracles tab): no click cast; the
    /// charge is the miracle's full effect (<c>amount</c>, in units of <see cref="countPerUnit"/>),
    /// paid per unit, poured while the click is held, over as many presses as needed. Right-click
    /// cancels the rest. Per tile: one unit for each new cell the cursor crosses (water). Timed:
    /// units at a steady rhythm, the full charge taking <see cref="pourSeconds"/> of holding.
    /// </summary>
    public class MiracleChannelProps
    {
        public bool perTile;

        /// <summary>Timed: seconds of holding to pour the full charge.</summary>
        public float pourSeconds = 3f;

        /// <summary>Timed spawns: one of these at random (default: the miracle's thingDef), <see cref="countPerUnit"/> at a time.</summary>
        public List<ThingDef> things = new List<ThingDef>();
        public int countPerUnit = 1;
    }
}
