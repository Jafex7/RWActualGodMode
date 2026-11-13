using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>Draws the Divine Power readout in the bottom-right controls, above the date.</summary>
    [HarmonyPatch(typeof(GlobalControlsUtility), nameof(GlobalControlsUtility.DoDate))]
    public static class Patch_DivinePowerReadout
    {
        private const float RowHeight = 26f;

        public static void Postfix(float leftX, float width, ref float curBaseY)
        {
            DivinePower dp = DivinePower.Instance;
            if (dp == null || !dp.Revealed)
            {
                return;
            }
            Rect rect = new Rect(leftX, curBaseY - RowHeight, width, RowHeight);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(rect, "AGM_PowerReadout".Translate(dp.Power.ToString("F1"), dp.MaxPower.ToString("F0")));
            Text.Anchor = TextAnchor.UpperLeft;
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
                TooltipHandler.TipRegion(rect, "AGM_PowerReadout_Tip".Translate(dp.LifetimePower.ToString("F0")));
            }
            curBaseY -= RowHeight;

            AlignmentTracker alignment = AlignmentTracker.Instance;
            if (alignment == null)
            {
                return;
            }
            Rect aRect = new Rect(leftX, curBaseY - RowHeight, width, RowHeight);
            string sign = alignment.Value > 0f ? "+" : "";
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(aRect, "AGM_AlignmentReadout".Translate(AlignmentTracker.BandLabel(alignment.Band), sign + alignment.Value.ToString("F0"))
                .Colorize(HandOfGodCursor.TrailColor));
            Text.Anchor = TextAnchor.UpperLeft;
            if (Mouse.IsOver(aRect))
            {
                Widgets.DrawHighlight(aRect);
                string ledger = alignment.Deeds.TooltipSection("AGM_Alignment_LedgerHeader", asPercent: false);
                TooltipHandler.TipRegion(aRect, "AGM_AlignmentReadout_Tip".Translate() + (ledger != null ? "\n\n" + ledger : ""));
            }
            curBaseY -= RowHeight;
        }
    }
}
