using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>Bottom-bar "Miracles" tab: pick a miracle level, then click the map to cast it.</summary>
    public class MainTabWindow_Miracles : MainTabWindow
    {
        private const float RowHeight = 40f;
        private const float IconSize = 32f;
        private const float LevelButtonWidth = 72f;
        private const float HeaderHeight = 30f;

        private static List<MiracleDef> Miracles =>
            DefDatabase<MiracleDef>.AllDefsListForReading.OrderBy(d => d.order).ToList();

        public override Vector2 RequestedTabSize =>
            new Vector2(420f, HeaderHeight + Miracles.Count * RowHeight + Margin * 2f);

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

            float y = inRect.y + HeaderHeight;
            foreach (MiracleDef def in Miracles)
            {
                Rect row = new Rect(inRect.x, y, inRect.width, RowHeight);
                Widgets.DrawHighlightIfMouseover(row);
                TooltipHandler.TipRegion(row, def.description);

                Rect iconRect = new Rect(row.x, row.y + (RowHeight - IconSize) / 2f, IconSize, IconSize);
                GUI.color = tracker.AnyUnlocked(def) ? Color.white : Color.gray;
                GUI.DrawTexture(iconRect, def.Icon);
                GUI.color = Color.white;

                using (new TextBlock(TextAnchor.MiddleLeft))
                {
                    Widgets.Label(new Rect(iconRect.xMax + 8f, row.y, 110f, RowHeight), def.LabelCap);
                }

                float x = row.xMax - def.levels.Count * (LevelButtonWidth + 4f);
                for (int i = 0; i < def.levels.Count; i++)
                {
                    DrawLevelButton(new Rect(x, row.y + 6f, LevelButtonWidth, RowHeight - 12f), def, i, dp, tracker);
                    x += LevelButtonWidth + 4f;
                }
                y += RowHeight;
            }
        }

        private void DrawLevelButton(Rect rect, MiracleDef def, int index, DivinePower dp, MiracleTracker tracker)
        {
            MiracleLevel level = def.levels[index];
            bool unlocked = tracker.IsUnlocked(def, index);
            bool affordable = dp.CanAfford(level.cost);
            string label = level.label + " (" + level.cost.ToString("F0") + ")";

            if (!unlocked)
            {
                Widgets.ButtonText(rect, label, drawBackground: true, doMouseoverSound: false, active: false);
                TooltipHandler.TipRegion(rect, LockedTip(level));
                return;
            }
            Color textColor = affordable ? Color.white : ColorLibrary.RedReadable;
            if (Widgets.ButtonText(rect, label, drawBackground: true, doMouseoverSound: true, textColor))
            {
                Find.DesignatorManager.Select(new Designator_Miracle(def, index));
                Close();
            }
            if (!affordable)
            {
                TooltipHandler.TipRegion(rect, "AGM_NotEnoughPower".Translate(level.cost.ToString("F0")));
            }
        }

        private static string LockedTip(MiracleLevel level)
        {
            var sb = new StringBuilder();
            sb.AppendLine("AGM_MiracleLocked".Translate());
            foreach (MiracleRequirement req in level.requirements)
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
