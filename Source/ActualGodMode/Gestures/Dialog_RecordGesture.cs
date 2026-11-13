using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ActualGodMode
{
    /// <summary>
    /// Record a new gesture for a miracle: draw it a few times, the dialog checks the
    /// drawings agree with each other and don't look like another miracle's gesture.
    /// </summary>
    public class Dialog_RecordGesture : Window
    {
        public const int RequiredSamples = 3;
        private const float CanvasSize = 320f;
        private const float MinPointSpacing = 3f;

        private readonly MiracleDef miracle;
        private readonly List<List<List<Vector2>>> samples = new List<List<List<Vector2>>>();
        private readonly List<List<Vector2>> current = new List<List<Vector2>>();
        private bool drawing;

        private string consistencyWarning;
        private string conflictWarning;

        public override Vector2 InitialSize => new Vector2(720f, 520f);

        public Dialog_RecordGesture(MiracleDef miracle)
        {
            this.miracle = miracle;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = true;
            closeOnClickedOutside = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            using (new TextBlock(GameFont.Medium))
            {
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "AGM_Rec_Title".Translate(miracle.LabelCap));
            }

            Rect canvas = new Rect(inRect.x, inRect.y + 40f, CanvasSize, CanvasSize);
            DoCanvas(canvas);

            Rect side = new Rect(canvas.xMax + 16f, canvas.y, inRect.width - CanvasSize - 16f, CanvasSize);
            DoSidePanel(side);

            DoButtons(new Rect(inRect.x, inRect.yMax - 36f, inRect.width, 36f));
        }

        private void DoCanvas(Rect canvas)
        {
            Widgets.DrawBoxSolid(canvas, new Color(0f, 0f, 0f, 0.5f));
            Widgets.DrawBox(canvas);
            if (current.Count == 0)
            {
                using (new TextBlock(TextAnchor.MiddleCenter, ColoredText.SubtleGrayColor))
                {
                    Widgets.Label(canvas, "AGM_Rec_DrawHere".Translate(samples.Count + 1, RequiredSamples));
                }
            }

            Event e = Event.current;
            Vector2 local = e.mousePosition;
            if (e.type == EventType.MouseDown && e.button == 0 && canvas.Contains(local))
            {
                drawing = true;
                current.Add(new List<Vector2> { local });
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && drawing)
            {
                Vector2 clamped = new Vector2(Mathf.Clamp(local.x, canvas.x, canvas.xMax), Mathf.Clamp(local.y, canvas.y, canvas.yMax));
                List<Vector2> stroke = current[current.Count - 1];
                if ((clamped - stroke[stroke.Count - 1]).sqrMagnitude >= MinPointSpacing * MinPointSpacing)
                {
                    stroke.Add(clamped);
                }
                e.Use();
            }
            else if (e.type == EventType.MouseUp && drawing)
            {
                drawing = false;
                e.Use();
            }

            foreach (List<Vector2> s in current)
            {
                HandOfGodMode.DrawTrail(s, InfluenceUtility.InfluenceColor);
            }
        }

        private void DoSidePanel(Rect side)
        {
            float y = side.y;
            using (new TextBlock(GameFont.Small))
            {
                Widgets.Label(new Rect(side.x, y, side.width, 24f), "AGM_Rec_Samples".Translate(samples.Count, RequiredSamples));
            }
            y += 26f;
            float box = 72f;
            for (int i = 0; i < RequiredSamples; i++)
            {
                Rect r = new Rect(side.x + i * (box + 6f), y, box, box);
                GestureDrawing.DrawPreview(r, i < samples.Count ? samples[i] : null);
            }
            y += box + 10f;

            var sb = new StringBuilder();
            if (!consistencyWarning.NullOrEmpty())
            {
                sb.AppendLine(consistencyWarning.Colorize(ColorLibrary.RedReadable));
            }
            if (!conflictWarning.NullOrEmpty())
            {
                sb.AppendLine(conflictWarning.Colorize(ColorLibrary.RedReadable));
            }
            if (samples.Count >= RequiredSamples && consistencyWarning.NullOrEmpty() && conflictWarning.NullOrEmpty())
            {
                sb.AppendLine("AGM_Rec_LooksGood".Translate().Colorize(ColorLibrary.Green));
            }
            sb.AppendLine();
            sb.Append("AGM_Rec_Tips".Translate());
            using (new TextBlock(GameFont.Tiny))
            {
                Widgets.Label(new Rect(side.x, y, side.width, side.yMax - y), sb.ToString());
            }
        }

        private void DoButtons(Rect row)
        {
            float w = 150f;
            Rect r = new Rect(row.x, row.y, w, row.height);
            if (Widgets.ButtonText(r, "AGM_Rec_Clear".Translate()))
            {
                current.Clear();
            }
            r.x += w + 8f;
            bool canAdd = samples.Count < RequiredSamples && GestureLibrary.PathLength(current) >= GestureLibrary.MinPathPixels && !drawing;
            if (Widgets.ButtonText(r, "AGM_Rec_AddSample".Translate(), active: canAdd) && canAdd)
            {
                samples.Add(new List<List<Vector2>>(current));
                current.Clear();
                Validate();
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            r.x += w + 8f;
            if (Widgets.ButtonText(r, "AGM_Rec_RemoveLast".Translate(), active: samples.Count > 0) && samples.Count > 0)
            {
                samples.RemoveAt(samples.Count - 1);
                Validate();
            }

            Rect save = new Rect(row.xMax - w, row.y, w, row.height);
            bool canSave = samples.Count >= RequiredSamples;
            string saveLabel = canSave && (!consistencyWarning.NullOrEmpty() || !conflictWarning.NullOrEmpty())
                ? "AGM_Rec_SaveAnyway".Translate()
                : "AGM_Rec_Save".Translate();
            if (Widgets.ButtonText(save, saveLabel, active: canSave) && canSave)
            {
                var templates = new List<GestureTemplate>();
                foreach (List<List<Vector2>> s in samples)
                {
                    templates.Add(new GestureTemplate(s));
                }
                GestureLibrary.SetCustom(miracle, templates);
                Messages.Message("AGM_Rec_Saved".Translate(miracle.label), MessageTypeDefOf.TaskCompletion, historical: false);
                Close();
            }
        }

        private void Validate()
        {
            consistencyWarning = null;
            conflictWarning = null;

            float worst = 0f;
            for (int i = 0; i < samples.Count; i++)
            {
                for (int j = i + 1; j < samples.Count; j++)
                {
                    worst = Mathf.Max(worst, GestureLibrary.DistanceBetween(samples[i], samples[j]));
                }
            }
            if (worst > GestureLibrary.Threshold * 0.9f)
            {
                consistencyWarning = "AGM_Rec_Inconsistent".Translate();
            }

            foreach (List<List<Vector2>> s in samples)
            {
                GestureMatch other = GestureLibrary.Recognize(s, exclude: miracle);
                if (other.miracle != null && other.distance <= GestureLibrary.Threshold)
                {
                    conflictWarning = "AGM_Rec_Conflict".Translate(other.miracle.label);
                    break;
                }
            }
        }
    }
}
