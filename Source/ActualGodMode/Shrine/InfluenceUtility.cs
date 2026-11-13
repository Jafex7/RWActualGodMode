using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// Where the god can act. For now: a radius around the Shrine.
    /// Later: Pylons, very faithful pawns, holy objects.
    /// </summary>
    public static class InfluenceUtility
    {
        public static readonly Color InfluenceColor = new Color(1f, 0.85f, 0.4f);

        public static bool InInfluence(IntVec3 cell, Map map)
        {
            Thing shrine = ShrineUtility.ShrineOn(map);
            CompShrine comp = shrine?.TryGetComp<CompShrine>();
            if (comp != null && Within(cell, shrine.Position, comp.Props.influenceRadius))
            {
                return true;
            }
            // Tended pylons extend the god's reach.
            foreach (Thing pylon in map.listerThings.ThingsOfDef(AGM_TempleDefOf.AGM_Pylon))
            {
                CompPylon p = pylon.TryGetComp<CompPylon>();
                if (p != null && p.Active && Within(cell, pylon.Position, p.Props.influenceRadius))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Within(IntVec3 cell, IntVec3 center, float radius) =>
            (cell - center).LengthHorizontalSquared <= radius * radius;

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
