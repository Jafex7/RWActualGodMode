using HarmonyLib;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// While the god's hand is out (Hand of God mode, a charged miracle, or something held), the
    /// map's hover tooltips on pawns and things are hidden: they would cover the hand's own readout
    /// next to the cursor (miracle cost, charge, what can be lifted).
    /// </summary>
    [HarmonyPatch(typeof(TooltipGiverList), nameof(TooltipGiverList.DispenseAllThingTooltips))]
    public static class Patch_HideThingTooltips
    {
        public static bool Prefix() => !HandOfGodMode.Active;
    }
}
