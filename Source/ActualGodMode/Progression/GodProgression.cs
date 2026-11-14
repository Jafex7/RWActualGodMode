using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// The god's own progression: which rank of each <see cref="PowerTrackDef"/> has been bought.
    /// Ranks are paid from the power pool, in order. A rank can't cost more than the pool can hold,
    /// so the deeper ranks wait for a greater temple (which raises the cap).
    /// </summary>
    public class GodProgression : GameComponent
    {
        private Dictionary<string, int> ranks = new Dictionary<string, int>();

        /// <summary>Changes whenever ranks change or another game is loaded (cached miracle stats compare against it).</summary>
        public static int Version { get; private set; }

        public static GodProgression Instance => Current.Game?.GetComponent<GodProgression>();

        public GodProgression(Game game)
        {
            Version++;
        }

        public int RankOf(PowerTrackDef track) =>
            track != null && ranks.TryGetValue(track.defName, out int r) ? r : 0;

        public static int Rank(PowerTrackDef track) => Instance?.RankOf(track) ?? 0;

        /// <summary>The track's number at its current rank (base value when none bought).</summary>
        public static float Value(PowerTrackDef track) => track.ValueAt(Rank(track));

        /// <summary>True once rank 1 is bought (abilities).</summary>
        public static bool Has(PowerTrackDef track) => Rank(track) > 0;

        public AcceptanceReport CanBuy(PowerTrackDef track)
        {
            int rank = RankOf(track);
            if (rank >= track.MaxRank)
            {
                return "AGM_Power_Maxed".Translate();
            }
            if (!track.requirements.TrueForAll(r => r.IsMet()))
            {
                string missing = string.Join("\n", track.requirements.Where(r => !r.IsMet()).Select(r => "  - " + r.Description));
                return "AGM_Power_Locked".Translate(missing);
            }
            DivinePower dp = DivinePower.Instance;
            float price = track.ranks[rank].price;
            if (dp == null)
            {
                return false;
            }
            if (price > dp.MaxPower)
            {
                return "AGM_Power_OverCap".Translate(price.ToString("F0"), dp.MaxPower.ToString("F0"));
            }
            if (!dp.CanAfford(price))
            {
                return "AGM_NotEnoughPower".Translate(price.ToString("F0"));
            }
            return true;
        }

        public bool TryBuy(PowerTrackDef track)
        {
            if (!CanBuy(track).Accepted)
            {
                return false;
            }
            int rank = RankOf(track);
            DivinePower.Instance.TrySpend(track.ranks[rank].price);
            ranks[track.defName] = rank + 1;
            Version++;
            return true;
        }

        public void DebugSetRank(PowerTrackDef track, int rank)
        {
            ranks[track.defName] = UnityEngine.Mathf.Clamp(rank, 0, track.MaxRank);
            Version++;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref ranks, "ranks", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ranks ??= new Dictionary<string, int>();
                Version++;
            }
        }
    }
}
