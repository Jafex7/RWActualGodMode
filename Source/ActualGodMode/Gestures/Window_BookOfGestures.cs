using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>Shows every miracle's gesture; lets the player re-record or reset each one.</summary>
    public class Window_BookOfGestures : Window
    {
        private const float RowHeight = 92f;
        private const float PreviewSize = 80f;

        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(560f, 620f);

        public Window_BookOfGestures()
        {
            doCloseX = true;
            doCloseButton = true;
            absorbInputAroundWindow = true;
            forcePause = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            using (new TextBlock(GameFont.Medium))
            {
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "AGM_BookOfGestures".Translate());
            }
            using (new TextBlock(GameFont.Tiny, ColoredText.SubtleGrayColor))
            {
                Widgets.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, 36f),
                    "AGM_Book_Intro".Translate(AGM_KeyBindingDefOf.AGM_ToggleHandOfGod.MainKeyLabel));
            }

            List<MiracleDef> miracles = DefDatabase<MiracleDef>.AllDefsListForReading.OrderBy(d => d.order).ToList();
            Rect outRect = new Rect(inRect.x, inRect.y + 74f, inRect.width, inRect.height - 74f - CloseButSize.y - 8f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, miracles.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float y = 0f;
            foreach (MiracleDef miracle in miracles)
            {
                DrawRow(new Rect(0f, y, viewRect.width, RowHeight - 4f), miracle);
                y += RowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawRow(Rect row, MiracleDef miracle)
        {
            Widgets.DrawMenuSection(row);
            Rect inner = row.ContractedBy(6f);

            Rect icon = new Rect(inner.x, inner.y + (inner.height - 32f) / 2f, 32f, 32f);
            GUI.DrawTexture(icon, miracle.Icon);

            bool unlocked = MiracleTracker.Instance?.AnyUnlocked(miracle) ?? false;
            bool custom = GestureLibrary.HasCustom(miracle);
            Rect label = new Rect(icon.xMax + 8f, inner.y, 150f, inner.height);
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                string status = custom ? "AGM_Book_Custom".Translate() : "AGM_Book_Default".Translate();
                if (Current.Game != null && !unlocked)
                {
                    status += "\n" + "AGM_Book_NotGranted".Translate();
                }
                Widgets.Label(label, miracle.LabelCap + "\n<size=11><color=#999999>" + status + "</color></size>");
            }

            Rect preview = new Rect(label.xMax + 4f, inner.y + (inner.height - PreviewSize) / 2f, PreviewSize, PreviewSize);
            List<GestureTemplate> templates = GestureLibrary.TemplatesFor(miracle);
            GestureDrawing.DrawPreview(preview, templates.Count > 0 ? templates[0].AsPointLists() : null);

            Rect buttons = new Rect(inner.xMax - 150f, inner.y + 8f, 150f, 30f);
            if (Widgets.ButtonText(buttons, "AGM_Book_Record".Translate()))
            {
                Find.WindowStack.Add(new Dialog_RecordGesture(miracle));
            }
            if (custom)
            {
                buttons.y += 36f;
                if (Widgets.ButtonText(buttons, "AGM_Book_Reset".Translate()))
                {
                    GestureLibrary.ResetToDefault(miracle);
                }
            }
        }
    }

    /// <summary>Shared drawing of gesture shapes inside a box.</summary>
    public static class GestureDrawing
    {
        public static void DrawPreview(Rect box, List<List<Vector2>> strokes, Color? color = null)
        {
            Widgets.DrawBoxSolid(box, new Color(0f, 0f, 0f, 0.35f));
            if (strokes == null || strokes.Count == 0)
            {
                return;
            }
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (List<Vector2> s in strokes)
            {
                foreach (Vector2 p in s)
                {
                    minX = Mathf.Min(minX, p.x);
                    minY = Mathf.Min(minY, p.y);
                    maxX = Mathf.Max(maxX, p.x);
                    maxY = Mathf.Max(maxY, p.y);
                }
            }
            float size = Mathf.Max(Mathf.Max(maxX - minX, maxY - minY), 1e-4f);
            Rect inner = box.ContractedBy(8f);
            float scale = Mathf.Min(inner.width, inner.height) / size;
            Vector2 offset = new Vector2(
                inner.x + (inner.width - (maxX - minX) * scale) / 2f,
                inner.y + (inner.height - (maxY - minY) * scale) / 2f);
            Color c = color ?? InfluenceUtility.InfluenceColor;
            foreach (List<Vector2> s in strokes)
            {
                for (int i = 1; i < s.Count; i++)
                {
                    Vector2 a = offset + (s[i - 1] - new Vector2(minX, minY)) * scale;
                    Vector2 b = offset + (s[i] - new Vector2(minX, minY)) * scale;
                    Widgets.DrawLine(a, b, c, 2f);
                }
                if (s.Count > 0)
                {
                    // Mark each stroke's start so the drawing order is readable.
                    Vector2 start = offset + (s[0] - new Vector2(minX, minY)) * scale;
                    Widgets.DrawBoxSolid(new Rect(start.x - 2.5f, start.y - 2.5f, 5f, 5f), Color.white);
                }
            }
        }
    }
}
