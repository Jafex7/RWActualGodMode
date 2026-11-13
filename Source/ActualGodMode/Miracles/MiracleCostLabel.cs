using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>Cursor readout of a miracle's cost at the cell under the mouse (raised past the influence edge).</summary>
    public static class MiracleCostLabel
    {
        /// <param name="rect">Title line; the cost goes on the line below, then the outside-influence factor if any.</param>
        /// <param name="title">Optional title (e.g. the miracle and level).</param>
        /// <returns>The y of the next free line.</returns>
        public static float Draw(Rect rect, string title, MiracleLevel level, IntVec3 cell, Map map)
        {
            float power = DivinePower.Instance?.Power ?? 0f;
            bool reach = map != null && cell.InBounds(map) && InfluenceUtility.InReach(cell, map);
            float cost = MiracleUtility.CostAt(level, cell, map);
            Color color = reach && power >= cost ? Color.white : ColorLibrary.RedReadable;
            using (new TextBlock(GameFont.Small, TextAnchor.UpperLeft, color))
            {
                if (!title.NullOrEmpty())
                {
                    Widgets.Label(rect, title);
                }
                rect.y += 18f;
                Widgets.Label(rect, reach
                    ? (string)"AGM_MiracleCost".Translate(cost.ToString("F0"), power.ToString("F0"))
                    : (string)"AGM_OutsideInfluence".Translate());
            }
            float factor = reach ? InfluenceUtility.CostFactor(cell, map) : 1f;
            if (factor > 1.01f)
            {
                rect.y += 18f;
                using (new TextBlock(GameFont.Small, TextAnchor.UpperLeft, new Color(1f, 0.75f, 0.4f)))
                {
                    Widgets.Label(rect, "AGM_OutsideCostFactor".Translate(factor.ToString("F1")));
                }
            }
            return rect.y + 18f;
        }
    }
}
