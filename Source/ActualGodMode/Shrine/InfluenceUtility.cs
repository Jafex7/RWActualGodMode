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

        private static InfluenceTuningDef cached;

        public static InfluenceTuningDef Get => cached ??= DefDatabase<InfluenceTuningDef>.GetNamed("AGM_InfluenceTuning");
    }

    /// <summary>
    /// Where the god can act: a radius around the Shrine and each tended Pylon.
    /// Later: very faithful pawns, holy objects.
    ///
    /// The border is soft: just outside, the god can still act, but costs climb (and the
    /// Hand weakens) sharply with distance, until nothing reaches past <c>maxReachOutside</c>.
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
                    best = Mathf.Min(best, Beyond(cell, pylon.Position, p.Props.influenceRadius));
                }
            }
            return best;
        }

        private static float Beyond(IntVec3 cell, IntVec3 center, float radius) =>
            Mathf.Max(0f, (cell - center).LengthHorizontal - radius);

        /// <summary>Inside, or within the soft border.</summary>
        public static bool InReach(IntVec3 cell, Map map) => DistanceOutside(cell, map) <= InfluenceTuningDef.Get.maxReachOutside;

        /// <summary>Cost multiplier for acting here: 1 inside, climbing sharply outside. Infinity out of reach.</summary>
        public static float CostFactor(IntVec3 cell, Map map)
        {
            float d = DistanceOutside(cell, map);
            if (d <= 0f)
            {
                return 1f;
            }
            InfluenceTuningDef t = InfluenceTuningDef.Get;
            return d > t.maxReachOutside ? float.PositiveInfinity : Mathf.Exp(d / t.costGrowthCells);
        }

        /// <summary>How much of the Hand's strength reaches here: 1 inside, falling fast outside, 0 out of reach.</summary>
        public static float LiftFactor(IntVec3 cell, Map map)
        {
            float d = DistanceOutside(cell, map);
            if (d <= 0f)
            {
                return 1f;
            }
            InfluenceTuningDef t = InfluenceTuningDef.Get;
            return d > t.maxReachOutside ? 0f : Mathf.Exp(-d / t.liftFalloffCells);
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
                    GenDraw.DrawRadiusRing(pylon.Position, p.Props.influenceRadius, InfluenceColor);
                }
            }
        }
    }
}
