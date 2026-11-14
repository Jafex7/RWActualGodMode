using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// Bottom-bar "Miracles" tab. The Miracles page: pick a miracle to put it in the god's hand,
    /// exactly as if its gesture had been drawn (an accessibility path: same casting, same pouring,
    /// no gestures needed). The Powers page: the god's progression (<see cref="PowersPage"/>).
    /// </summary>
    public class MainTabWindow_Miracles : MainTabWindow
    {
        private const float RowHeight = 40f;
        private const float IconSize = 32f;
        private const float CastButtonWidth = 110f;
        private const float HeaderHeight = 30f;
        private const float MaxPowersHeight = 560f;

        private static bool showPowers;

        private static List<MiracleDef> Miracles =>
            DefDatabase<MiracleDef>.AllDefsListForReading.OrderBy(d => d.order).ToList();

        public override Vector2 RequestedTabSize =>
            new Vector2(600f, HeaderHeight + TabDrawer.TabHeight + 8f + Mathf.Max(Miracles.Count * RowHeight,
                Mathf.Min(PowersPage.ContentHeight, MaxPowersHeight)) + Margin * 2f);

        public override MainTabWindowAnchor Anchor => MainTabWindowAnchor.Left;

        public override void DoWindowContents(Rect inRect)
        {
            DivinePower dp = DivinePower.Instance;
            MiracleTracker tracker = MiracleTracker.Instance;
            if (dp == null || tracker == null)
            {
                return;
            }

            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight),
                "AGM_PowerReadout".Translate(dp.Power.ToString("F1"), dp.MaxPower.ToString("F0")));

            Rect handButton = new Rect(inRect.xMax - 170f, inRect.y, 170f, HeaderHeight - 4f);
            string handLabel = "AGM_HoG_Toggle".Translate() + " (" + AGM_KeyBindingDefOf.AGM_ToggleHandOfGod.MainKeyLabel + ")";
            if (Widgets.ButtonText(handButton, handLabel))
            {
                HandOfGodMode.Toggle();
                Close();
            }
            Rect bookButton = new Rect(handButton.x - 150f - 6f, inRect.y, 150f, HeaderHeight - 4f);
            if (Widgets.ButtonText(bookButton, "AGM_BookOfGestures".Translate()))
            {
                Find.WindowStack.Add(new Window_BookOfGestures());
            }

            Rect page = new Rect(inRect.x, inRect.y + HeaderHeight + TabDrawer.TabHeight + 4f, inRect.width,
                inRect.height - HeaderHeight - TabDrawer.TabHeight - 4f);
            var tabs = new List<TabRecord>
            {
                new TabRecord("AGM_Tab_Miracles".Translate(), () => showPowers = false, () => !showPowers),
                new TabRecord("AGM_Tab_Powers".Translate(), () => showPowers = true, () => showPowers),
            };
            Widgets.DrawMenuSection(page);
            TabDrawer.DrawTabs(page, tabs);
            page = page.ContractedBy(6f);
            if (showPowers)
            {
                PowersPage.Draw(page);
                return;
            }

            float y = page.y;
            foreach (MiracleDef def in Miracles)
            {
                Rect row = new Rect(page.x, y, page.width, RowHeight);
                Widgets.DrawHighlightIfMouseover(row);
                TooltipHandler.TipRegion(row, def.description);

                bool unlocked = tracker.IsUnlocked(def);
                Rect iconRect = new Rect(row.x, row.y + (RowHeight - IconSize) / 2f, IconSize, IconSize);
                GUI.color = unlocked ? Color.white : Color.gray;
                GUI.DrawTexture(iconRect, def.Icon);
                GUI.color = Color.white;

                using (new TextBlock(TextAnchor.MiddleLeft))
                {
                    Widgets.Label(new Rect(iconRect.xMax + 8f, row.y, 200f, RowHeight), def.LabelCap);
                }

                DrawCastButton(new Rect(row.xMax - CastButtonWidth, row.y + 6f, CastButtonWidth, RowHeight - 12f), def, unlocked, dp);
                y += RowHeight;
            }
        }

        private void DrawCastButton(Rect rect, MiracleDef def, bool unlocked, DivinePower dp)
        {
            float cost = def.Stats.cost;
            string label = "AGM_MiracleCastButton".Translate(cost.ToString("F0"));
            if (!unlocked)
            {
                Widgets.ButtonText(rect, label, drawBackground: true, doMouseoverSound: false, active: false);
                TooltipHandler.TipRegion(rect, LockedTip(def));
                return;
            }
            // A poured miracle can be started with less than the full cost (it pours what it can).
            bool affordable = dp.CanAfford(def.channel != null ? MiracleChannel.BaseUnitCost(def) : cost);
            Color textColor = affordable ? Color.white : ColorLibrary.RedReadable;
            if (Widgets.ButtonText(rect, label, drawBackground: true, doMouseoverSound: true, textColor))
            {
                HandOfGodMode.Charge(def);
                Close();
            }
            if (!affordable)
            {
                TooltipHandler.TipRegion(rect, "AGM_NotEnoughPower".Translate(cost.ToString("F0")));
            }
        }

        private static string LockedTip(MiracleDef def)
        {
            var sb = new StringBuilder();
            sb.AppendLine("AGM_MiracleLocked".Translate());
            foreach (MiracleRequirement req in def.requirements)
            {
                sb.AppendLine((req.IsMet() ? "  ✓ " : "  ✗ ") + req.Description);
            }
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>The Miracles tab only appears once the god is revealed (a shrine exists or power was gained).</summary>
    public class MainButtonWorker_Miracles : MainButtonWorker_ToggleTab
    {
        public override bool Visible => base.Visible && (DivinePower.Instance?.Revealed ?? false);
    }
}
