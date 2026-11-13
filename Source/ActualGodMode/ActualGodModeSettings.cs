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

        // Hand of God label position (for UI mods that overlap it)
        public float handLabelX = 0.5f;
        public bool handLabelAutoY = true;
        public float handLabelY = 0.12f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref faithDecayMultiplier, "faithDecayMultiplier", 1f);
            Scribe_Values.Look(ref skipAwakening, "skipAwakening", false);
            Scribe_Values.Look(ref handLabelX, "handLabelX", 0.5f);
            Scribe_Values.Look(ref handLabelAutoY, "handLabelAutoY", true);
            Scribe_Values.Look(ref handLabelY, "handLabelY", 0.12f);
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

            listing.GapLine();
            listing.Label("AGM_Setting_HandLabel".Translate());
            handLabelX = listing.SliderLabeled(
                "AGM_Setting_HandLabelX".Translate(handLabelX.ToStringPercent()), handLabelX, 0f, 1f);
            listing.CheckboxLabeled(
                "AGM_Setting_HandLabelAutoY".Translate(), ref handLabelAutoY,
                "AGM_Setting_HandLabelAutoY_Tip".Translate());
            if (!handLabelAutoY)
            {
                handLabelY = listing.SliderLabeled(
                    "AGM_Setting_HandLabelY".Translate(handLabelY.ToStringPercent()), handLabelY, 0f, 0.95f);
            }

            listing.End();
        }
    }
}
