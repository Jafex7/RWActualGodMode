using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    public enum PowerBranch
    {
        Miracles,
        Hand,
        Influence,
    }

    /// <summary>
    /// One track of the god's progression (Powers page): a few ranks bought in order with power from
    /// the pool. A Miracles-branch track improves its <see cref="miracle"/> (each rank sets a new
    /// charge amount and cost, read through <see cref="MiracleDef.Stats"/>); other tracks carry a
    /// number per rank (<see cref="GodProgression.Value"/>) or simply open an ability at rank 1.
    /// </summary>
    public class PowerTrackDef : Def
    {
        public PowerBranch branch;
        public int order;

        /// <summary>Miracles branch: the miracle the ranks improve.</summary>
        public MiracleDef miracle;

        /// <summary>Other branches: the number before any rank, and how to describe a value ({0}).</summary>
        public float baseValue;
        public string effectKey;
        public string valueFormat = "F0";

        public List<PowerRank> ranks = new List<PowerRank>();

        /// <summary>What must be true before rank 1 can be bought (e.g. another track's rank, a colony event).</summary>
        public List<MiracleRequirement> requirements = new List<MiracleRequirement>();

        public ThingDef iconThing;
        public string iconPath;

        [Unsaved(false)]
        private Texture2D iconInt;

        private static Dictionary<MiracleDef, PowerTrackDef> byMiracle;

        public int MaxRank => ranks.Count;

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
                    else if (miracle != null)
                    {
                        iconInt = miracle.Icon;
                    }
                    else
                    {
                        iconInt = BaseContent.BadTex;
                    }
                }
                return iconInt;
            }
        }

        /// <summary>The track that improves this miracle, if any.</summary>
        public static PowerTrackDef ForMiracle(MiracleDef def)
        {
            if (byMiracle == null)
            {
                byMiracle = new Dictionary<MiracleDef, PowerTrackDef>();
                foreach (PowerTrackDef t in DefDatabase<PowerTrackDef>.AllDefsListForReading)
                {
                    if (t.miracle != null)
                    {
                        byMiracle[t.miracle] = t;
                    }
                }
            }
            return def != null && byMiracle.TryGetValue(def, out PowerTrackDef track) ? track : null;
        }

        public float ValueAt(int rank) => rank > 0 ? ranks[Mathf.Min(rank, MaxRank) - 1].value : baseValue;

        /// <summary>What the track gives at this rank, or null when there's nothing to say (an ability not yet bought).</summary>
        public string EffectAt(int rank)
        {
            if (miracle != null)
            {
                MiracleStats s = miracle.StatsAt(rank);
                return "AGM_Power_MiracleEffect".Translate(s.amount, miracle.unitLabel, s.cost.ToString("0.#"),
                    (s.cost / Mathf.Max(1, s.amount)).ToString("0.##"));
            }
            if (!effectKey.NullOrEmpty())
            {
                return effectKey.Translate(ValueAt(rank).ToString(valueFormat));
            }
            return null;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors())
            {
                yield return e;
            }
            if (ranks.NullOrEmpty())
            {
                yield return "a power track needs at least one rank.";
                yield break;
            }
            for (int i = 0; i < ranks.Count; i++)
            {
                if (ranks[i].price <= 0f)
                {
                    yield return $"rank {i + 1} has no price.";
                }
                if (miracle != null && (ranks[i].amount <= 0 || ranks[i].cost <= 0f))
                {
                    yield return $"rank {i + 1} of a miracle track needs an amount and a cost.";
                }
            }
            if (miracle != null && miracle.unitLabel.NullOrEmpty())
            {
                yield return $"{miracle.defName} needs a unitLabel to describe its ranks.";
            }
        }
    }

    /// <summary>One rank of a <see cref="PowerTrackDef"/>.</summary>
    public class PowerRank
    {
        /// <summary>Power spent to buy this rank.</summary>
        public float price;

        // Miracles branch: the miracle's numbers at this rank (0 = unchanged).
        public int amount;
        public float cost;
        public float radius;

        // Other branches: the track's number at this rank.
        public float value;
    }
}
