using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ActualGodMode
{
    /// <summary>
    /// The "Powers" page of the Miracles window: the god's progression tracks, grouped by branch
    /// (Miracles / Hand / Influence), each with its ranks, what the next rank gives, and a Buy button.
    /// </summary>
    public static class PowersPage
    {
        private const float BranchHeaderHeight = 30f;
        private const float RowHeight = 50f;
        private const float IconSize = 32f;
        private const float BuyButtonWidth = 110f;
        private const float PipSize = 10f;

        private static Vector2 scroll;

        private static List<PowerTrackDef> Tracks =>
            DefDatabase<PowerTrackDef>.AllDefsListForReading.OrderBy(t => t.branch).ThenBy(t => t.order).ToList();

        public static float ContentHeight
        {
            get
            {
                List<PowerTrackDef> tracks = Tracks;
                return tracks.Select(t => t.branch).Distinct().Count() * BranchHeaderHeight + tracks.Count * RowHeight;
            }
        }

        public static void Draw(Rect rect)
        {
            GodProgression progression = GodProgression.Instance;
            if (progression == null)
            {
                return;
            }
            List<PowerTrackDef> tracks = Tracks;
            Rect view = new Rect(0f, 0f, rect.width - 16f, ContentHeight);
            Widgets.BeginScrollView(rect, ref scroll, view);
            float y = 0f;
            PowerBranch? current = null;
            foreach (PowerTrackDef track in tracks)
            {
                if (track.branch != current)
                {
                    current = track.branch;
                    DrawBranchHeader(new Rect(0f, y, view.width, BranchHeaderHeight), track.branch);
                    y += BranchHeaderHeight;
                }
                DrawTrack(new Rect(0f, y, view.width, RowHeight), track, progression);
                y += RowHeight;
            }
            Widgets.EndScrollView();
        }

        private static void DrawBranchHeader(Rect rect, PowerBranch branch)
        {
            using (new TextBlock(GameFont.Medium, TextAnchor.LowerLeft))
            {
                Widgets.Label(rect, ("AGM_Branch_" + branch).Translate());
            }
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width, Widgets.SeparatorLineColor);
        }

        private static void DrawTrack(Rect row, PowerTrackDef track, GodProgression progression)
        {
            Widgets.DrawHighlightIfMouseover(row);
            int rank = progression.RankOf(track);
            Rect buy = new Rect(row.xMax - BuyButtonWidth, row.y + (RowHeight - 30f) / 2f, BuyButtonWidth, 30f);
            TooltipHandler.TipRegion(new Rect(row.x, row.y, row.width - BuyButtonWidth - 4f, row.height), TrackTip(track));

            Rect icon = new Rect(row.x + 2f, row.y + (RowHeight - IconSize) / 2f, IconSize, IconSize);
            GUI.DrawTexture(icon, track.Icon);

            float textX = icon.xMax + 8f;
            float textWidth = buy.x - textX - 6f;
            Rect nameRect = new Rect(textX, row.y + 3f, textWidth, 24f);
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                Widgets.Label(nameRect, track.LabelCap);
            }
            if (track.MaxRank > 1)
            {
                float nameWidth = Text.CalcSize(track.LabelCap).x;
                DrawPips(new Vector2(textX + nameWidth + 8f, nameRect.center.y - PipSize / 2f), rank, track.MaxRank);
            }

            string line = EffectLine(track, rank);
            if (line != null)
            {
                using (new TextBlock(GameFont.Tiny, TextAnchor.UpperLeft, Color.gray))
                {
                    Widgets.Label(new Rect(textX, row.y + 27f, textWidth, 20f), line.Truncate(textWidth));
                }
            }

            DrawBuyButton(buy, track, rank, progression);
        }

        private static void DrawPips(Vector2 at, int rank, int max)
        {
            for (int i = 0; i < max; i++)
            {
                Rect pip = new Rect(at.x + i * (PipSize + 3f), at.y, PipSize, PipSize);
                Widgets.DrawBoxSolidWithOutline(pip, i < rank ? new Color(0.95f, 0.82f, 0.4f) : new Color(0.15f, 0.15f, 0.15f), Color.gray);
            }
        }

        /// <summary>"Now: X → next: Y", "Now: X (all ranks)", or for an ability its state.</summary>
        private static string EffectLine(PowerTrackDef track, int rank)
        {
            string now = track.EffectAt(rank);
            if (now == null)
            {
                return rank > 0 ? "AGM_Power_Owned".Translate().ToString() : null;
            }
            if (rank >= track.MaxRank)
            {
                return "AGM_Power_EffectMaxed".Translate(now);
            }
            return "AGM_Power_EffectNext".Translate(now, track.EffectAt(rank + 1));
        }

        private static void DrawBuyButton(Rect rect, PowerTrackDef track, int rank, GodProgression progression)
        {
            if (rank >= track.MaxRank)
            {
                Widgets.ButtonText(rect, "AGM_Power_Maxed".Translate(), drawBackground: true, doMouseoverSound: false, active: false);
                return;
            }
            float price = track.ranks[rank].price;
            string label = "AGM_Power_Buy".Translate(price.ToString("F0"));
            AcceptanceReport report = progression.CanBuy(track);
            if (!report.Accepted)
            {
                Widgets.ButtonText(rect, label, drawBackground: true, doMouseoverSound: false, active: false);
                if (!report.Reason.NullOrEmpty())
                {
                    TooltipHandler.TipRegion(rect, report.Reason);
                }
                return;
            }
            if (Widgets.ButtonText(rect, label))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "AGM_Power_ConfirmBuy".Translate(price.ToString("F0"), track.label, rank + 1),
                    () => Buy(track, progression)));
            }
        }

        private static void Buy(PowerTrackDef track, GodProgression progression)
        {
            if (progression.TryBuy(track))
            {
                SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
                Messages.Message("AGM_Power_Bought".Translate(track.LabelCap, progression.RankOf(track)), MessageTypeDefOf.PositiveEvent, historical: false);
            }
        }

        private static string TrackTip(PowerTrackDef track)
        {
            var sb = new StringBuilder(track.description);
            if (track.requirements.Count > 0)
            {
                sb.AppendLine().AppendLine().AppendLine("AGM_Power_Requires".Translate());
                foreach (MiracleRequirement req in track.requirements)
                {
                    sb.AppendLine((req.IsMet() ? "  ✓ " : "  ✗ ") + req.Description);
                }
            }
            return sb.ToString().TrimEnd();
        }
    }
}
