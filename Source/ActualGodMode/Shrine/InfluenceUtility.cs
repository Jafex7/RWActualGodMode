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
            if (comp == null)
            {
                return false;
            }
            float r = comp.Props.influenceRadius;
            return (cell - shrine.Position).LengthHorizontalSquared <= r * r;
        }

        public static void DrawInfluence(Map map)
        {
            Thing shrine = ShrineUtility.ShrineOn(map);
            CompShrine comp = shrine?.TryGetComp<CompShrine>();
            if (comp != null)
            {
                GenDraw.DrawRadiusRing(shrine.Position, comp.Props.influenceRadius, InfluenceColor);
            }
        }
    }
}
