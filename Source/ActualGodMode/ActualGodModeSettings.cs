using UnityEngine;
using Verse;

namespace ActualGodMode
{
    public class ActualGodModeSettings : ModSettings
    {
        // Faith
        public float faithDecayMultiplier = 1f;

        // Entry
        public bool skipAwakening = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref faithDecayMultiplier, "faithDecayMultiplier", 1f);
            Scribe_Values.Look(ref skipAwakening, "skipAwakening", false);
        }

        public void DoWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            faithDecayMultiplier = listing.SliderLabeled(
                "AGM_Setting_FaithDecay".Translate(faithDecayMultiplier.ToStringPercent()),
                faithDecayMultiplier, 0f, 3f,
                tooltip: "AGM_Setting_FaithDecay_Tip".Translate());

            listing.CheckboxLabeled(
                "AGM_Setting_SkipAwakening".Translate(), ref skipAwakening,
                "AGM_Setting_SkipAwakening_Tip".Translate());

            listing.End();
        }
    }
}
