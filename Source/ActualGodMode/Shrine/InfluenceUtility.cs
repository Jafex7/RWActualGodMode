using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>Numbers for the soft border of influence, from <c>Defs/Shrine/AGM_InfluenceTuning.xml</c>.</summary>
    public class InfluenceTuningDef : Def
    {
        public float maxReachOutside = 10f;
        public float costGrowthCells = 2.5f;
        public float liftFalloffCells = 3f;

        /// <summary>Heralds (living pylons): influence radius, and the faith they need to carry it.</summary>
        public float heraldRadius = 4.9f;
        public float heraldMinFaith = 0.9f;

        private static InfluenceTuningDef cached;

        public static InfluenceTuningDef Get => cached ??= DefDatabase<InfluenceTuningDef>.GetNamed("AGM_InfluenceTuning");
    }

    /// <summary>
    /// Where the god can act: a radius around the Shrine, each tended Pylon, and each active Herald
    /// (a very faithful pawn carrying a small circle with them). Later: holy objects.
    ///
    /// The border is soft: just outside, the god can still act, but costs climb (and the
    /// Hand weakens) sharply with distance, until nothing reaches past <c>maxReachOutside</c>.
    /// The Long reach power track stretches all three distances.
    /// </summary>
    public static class InfluenceUtility
    {
        public static readonly Color InfluenceColor = new Color(1f, 0.85f, 0.4f);

        public static bool InInfluence(IntVec3 cell, Map map) => DistanceOutside(cell, map) <= 0f;

        /// <summary>Cells beyond the nearest influence edge (0 inside). Infinity if the god has no influence here.</summary>
        public static float DistanceOutside(IntVec3 cell, Map map)
        {
            float best = float.PositiveInfinity;
            Thing shrine = ShrineUtility.ShrineOn(map);
            CompShrine comp = shrine?.TryGetComp<CompShrine>();
            if (comp != null)
            {
                best = Beyond(cell, shrine.Position, comp.Props.influenceRadius);
            }
            // Tended pylons extend the god's reach.
            foreach (Thing pylon in map.listerThings.ThingsOfDef(AGM_TempleDefOf.AGM_Pylon))
            {
                CompPylon p = pylon.TryGetComp<CompPylon>();
                if (p != null && p.Active)
                {
                    best = Mathf.Min(best, Beyond(cell, pylon.Position, p.Radius));
                }
            }
            HeraldTracker heralds = HeraldTracker.Instance;
            if (heralds != null && heralds.Any)
            {
                float radius = InfluenceTuningDef.Get.heraldRadius;
                foreach (Pawn herald in heralds.ActiveOn(map))
                {
                    best = Mathf.Min(best, Beyond(cell, herald.Position, radius));
                }
            }
            return best;
        }

        /// <summary>Long reach: how much further the soft border stretches (1 = the tuning's numbers).</summary>
        public static float ReachScale
        {
            get
            {
                PowerTrackDef track = AGM_ProgressionDefOf.AGM_Track_LongReach;
                return track.baseValue > 0f ? GodProgression.Value(track) / track.baseValue : 1f;
            }
        }

        public static float MaxReachOutside => InfluenceTuningDef.Get.maxReachOutside * ReachScale;

        private static float Beyond(IntVec3 cell, IntVec3 center, float radius) =>
            Mathf.Max(0f, (cell - center).LengthHorizontal - radius);

        /// <summary>Inside, or within the soft border.</summary>
        public static bool InReach(IntVec3 cell, Map map) => DistanceOutside(cell, map) <= MaxReachOutside;

        /// <summary>Cost multiplier for acting here: 1 inside, climbing sharply outside. Infinity out of reach.</summary>
        public static float CostFactor(IntVec3 cell, Map map)
        {
            float d = DistanceOutside(cell, map);
            if (d <= 0f)
            {
                return 1f;
            }
            float scale = ReachScale;
            return d > MaxReachOutside ? float.PositiveInfinity : Mathf.Exp(d / (InfluenceTuningDef.Get.costGrowthCells * scale));
        }

        /// <summary>How much of the Hand's strength reaches here: 1 inside, falling fast outside, 0 out of reach.</summary>
        public static float LiftFactor(IntVec3 cell, Map map)
        {
            float d = DistanceOutside(cell, map);
            if (d <= 0f)
            {
                return 1f;
            }
            float scale = ReachScale;
            return d > MaxReachOutside ? 0f : Mathf.Exp(-d / (InfluenceTuningDef.Get.liftFalloffCells * scale));
        }

        public static void DrawInfluence(Map map)
        {
            Thing shrine = ShrineUtility.ShrineOn(map);
            CompShrine comp = shrine?.TryGetComp<CompShrine>();
            if (comp != null)
            {
                GenDraw.DrawRadiusRing(shrine.Position, comp.Props.influenceRadius, InfluenceColor);
            }
            foreach (Thing pylon in map.listerThings.ThingsOfDef(AGM_TempleDefOf.AGM_Pylon))
            {
                CompPylon p = pylon.TryGetComp<CompPylon>();
                if (p != null && p.Active)
                {
                    GenDraw.DrawRadiusRing(pylon.Position, p.Radius, InfluenceColor);
                }
            }
            if (HeraldTracker.Instance != null)
            {
                foreach (Pawn herald in HeraldTracker.Instance.ActiveOn(map))
                {
                    GenDraw.DrawRadiusRing(herald.Position, InfluenceTuningDef.Get.heraldRadius, InfluenceColor);
                }
            }
        }
    }
}
