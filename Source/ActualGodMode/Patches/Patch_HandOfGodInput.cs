using HarmonyLib;
using RimWorld;

namespace ActualGodMode
{
    /// <summary>
    /// While Hand of God mode is active it owns map clicks: designators, targeting and
    /// selection are skipped. UI windows and camera controls are unaffected.
    /// </summary>
    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.HandleMapClicks))]
    public static class Patch_HandleMapClicks
    {
        public static bool Prefix()
        {
            if (!HandOfGodMode.Active)
            {
                return true;
            }
            HandOfGodMode.ProcessMapInput();
            return false;
        }
    }

    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.HandleLowPriorityInput))]
    public static class Patch_HandleLowPriorityInput
    {
        public static bool Prefix() => !HandOfGodMode.Active;
    }
}
