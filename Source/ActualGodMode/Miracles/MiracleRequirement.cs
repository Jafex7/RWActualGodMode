using System.Collections.Generic;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// A composable unlock condition for a miracle level. New kinds (survived a raid,
    /// witnessed lightning, sacrifices, alignment range...) are added as subclasses.
    /// </summary>
    public abstract class MiracleRequirement
    {
        public abstract bool IsMet();

        public abstract string Description { get; }
    }

    /// <summary>At least N colonists whose faith is at or above a threshold.</summary>
    public class Req_Believers : MiracleRequirement
    {
        public int count = 1;
        public float minFaith = Need_Faith.LowThreshold;

        public override bool IsMet() => CountBelievers(minFaith) >= count;

        public override string Description =>
            "AGM_Req_Believers".Translate(count, minFaith.ToStringPercent(), CountBelievers(minFaith));

        public static int CountBelievers(float minFaith)
        {
            int n = 0;
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                foreach (Pawn p in maps[m].mapPawns.FreeColonistsSpawned)
                {
                    Need_Faith f = p.Faith();
                    if (f != null && f.CurLevelPercentage >= minFaith)
                    {
                        n++;
                    }
                }
            }
            return n;
        }
    }

    /// <summary>The god has received at least this much power in total.</summary>
    public class Req_LifetimePower : MiracleRequirement
    {
        public float amount;

        public override bool IsMet() => (DivinePower.Instance?.LifetimePower ?? 0f) >= amount;

        public override string Description =>
            "AGM_Req_LifetimePower".Translate(amount.ToString("F0"), (DivinePower.Instance?.LifetimePower ?? 0f).ToString("F0"));
    }
}
