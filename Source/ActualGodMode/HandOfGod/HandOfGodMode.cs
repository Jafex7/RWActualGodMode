using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ActualGodMode
{
    [DefOf]
    public static class AGM_KeyBindingDefOf
    {
        public static KeyBindingDef AGM_ToggleHandOfGod;
        public static KeyBindingDef AGM_HoldHandOfGod;

        static AGM_KeyBindingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AGM_KeyBindingDefOf));
        }
    }

    /// <summary>
    /// Hand of God mode: the god's direct presence on the map. Active while toggled on
    /// or while the hold key is down. While active it owns left-click/drag on the map
    /// (vanilla selection and designators are suspended); UI and camera keep working.
    ///
    /// M3 spike: cursor swap, glow, stroke capture and trail drawing. Gesture
    /// recognition and grabbing build on this.
    /// </summary>
    public static class HandOfGodMode
    {
        private const float MinPointSpacing = 3f;
        private const float TrailFadeSeconds = 0.6f;

        public static bool Toggled;

        private static bool drawing;
        private static readonly List<Vector2> stroke = new List<Vector2>();
        private static float strokeStartTime;

        private static readonly List<Vector2> fadingStroke = new List<Vector2>();
        private static float fadeStartTime = -100f;

        private static bool cursorApplied;
        private static int glowFrame;

        public static bool Active =>
            Find.CurrentMap != null && WorldRendererUtility.DrawingMap
            && (Toggled || AGM_KeyBindingDefOf.AGM_HoldHandOfGod.IsDown);

        public static void Toggle()
        {
            Toggled = !Toggled;
            if (Toggled)
            {
                Find.DesignatorManager.Deselect();
                Find.Targeter.StopTargeting();
            }
            (Toggled ? SoundDefOf.Tick_High : SoundDefOf.Tick_Low).PlayOneShotOnCamera();
        }

        /// <summary>Called from the MapInterface.HandleMapClicks patch while active (during OnGUI).</summary>
        public static void ProcessMapInput()
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                    drawing = true;
                    stroke.Clear();
                    stroke.Add(e.mousePosition);
                    strokeStartTime = Time.realtimeSinceStartup;
                    e.Use();
                    break;

                case EventType.MouseDrag when e.button == 0 && drawing:
                    if ((e.mousePosition - stroke[stroke.Count - 1]).sqrMagnitude >= MinPointSpacing * MinPointSpacing)
                    {
                        stroke.Add(e.mousePosition);
                    }
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0 && drawing:
                    FinishStroke();
                    e.Use();
                    break;

                case EventType.MouseDown when e.button == 1 && Toggled:
                    Toggle();
                    e.Use();
                    break;
            }
        }

        private static void FinishStroke()
        {
            drawing = false;
            float length = 0f;
            for (int i = 1; i < stroke.Count; i++)
            {
                length += (stroke[i] - stroke[i - 1]).magnitude;
            }
            float seconds = Time.realtimeSinceStartup - strokeStartTime;
            Messages.Message("AGM_HoG_StrokeDebug".Translate(stroke.Count, length.ToString("F0"), seconds.ToString("F2")),
                MessageTypeDefOf.SilentInput, historical: false);

            fadingStroke.Clear();
            fadingStroke.AddRange(stroke);
            fadeStartTime = Time.realtimeSinceStartup;
            stroke.Clear();
        }

        /// <summary>Per-frame: cursor swap and glow particles.</summary>
        public static void Update()
        {
            bool active = Active;
            if (active != cursorApplied)
            {
                cursorApplied = active;
                if (active)
                {
                    HandOfGodCursor.Apply();
                }
                else
                {
                    HandOfGodCursor.Restore();
                }
            }
            if (!active)
            {
                if (drawing)
                {
                    drawing = false;
                    stroke.Clear();
                }
                return;
            }

            // Flecks run on game time: spawning them while paused would pile up and burst on unpause.
            if (Find.TickManager.Paused)
            {
                return;
            }
            Map map = Find.CurrentMap;
            Vector3 mousePos = UI.MouseMapPosition();
            if (++glowFrame % (drawing ? 3 : 10) == 0 && mousePos.ToIntVec3().InBounds(map))
            {
                FleckMaker.ThrowMicroSparks(mousePos, map);
            }
        }

        /// <summary>OnGUI: hotkey, trail and mode indicator.</summary>
        public static void OnGUI()
        {
            if (Find.CurrentMap == null)
            {
                return;
            }
            if (AGM_KeyBindingDefOf.AGM_ToggleHandOfGod.KeyDownEvent)
            {
                Toggle();
                Event.current.Use();
            }
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Color gold = InfluenceUtility.InfluenceColor;
            if (drawing)
            {
                DrawTrail(stroke, gold);
            }
            float fade = 1f - (Time.realtimeSinceStartup - fadeStartTime) / TrailFadeSeconds;
            if (fade > 0f)
            {
                DrawTrail(fadingStroke, new Color(gold.r, gold.g, gold.b, fade));
            }

            if (Active)
            {
                Rect rect = LabelRect();
                using (new TextBlock(GameFont.Medium, TextAnchor.MiddleCenter, gold))
                {
                    Widgets.Label(rect, "AGM_HoG_Active".Translate());
                }
                using (new TextBlock(GameFont.Tiny, TextAnchor.MiddleCenter, Color.white))
                {
                    Widgets.Label(new Rect(rect.x, rect.yMax, rect.width, 20f),
                        Toggled ? "AGM_HoG_HintToggled".Translate(AGM_KeyBindingDefOf.AGM_ToggleHandOfGod.MainKeyLabel)
                                : "AGM_HoG_HintHeld".Translate());
                }
            }
        }

        /// <summary>
        /// Where the "Hand of God" label goes. Horizontal position is a setting; vertical is
        /// either automatic (just below the colonist bar) or a setting, for UI mods that overlap.
        /// </summary>
        private static Rect LabelRect()
        {
            const float width = 400f;
            ActualGodModeSettings s = ActualGodModeMod.Settings;
            float y;
            if (s.handLabelAutoY)
            {
                y = 8f;
                ColonistBar bar = Find.ColonistBar;
                if (bar != null && bar.DrawLocs != null)
                {
                    foreach (Vector2 loc in bar.DrawLocs)
                    {
                        y = Mathf.Max(y, loc.y + bar.Size.y + 18f * bar.Scale + 6f);
                    }
                }
            }
            else
            {
                y = s.handLabelY * UI.screenHeight;
            }
            float x = s.handLabelX * UI.screenWidth - width / 2f;
            return new Rect(Mathf.Clamp(x, 0f, UI.screenWidth - width), y, width, 30f);
        }

        private static void DrawTrail(List<Vector2> points, Color color)
        {
            for (int i = 1; i < points.Count; i++)
            {
                Widgets.DrawLine(points[i - 1], points[i], color, 3f);
            }
        }
    }

    /// <summary>Hooks the mode into the game's per-frame update and GUI loops.</summary>
    public class HandOfGodComponent : GameComponent
    {
        public HandOfGodComponent(Game game)
        {
        }

        public override void GameComponentUpdate() => HandOfGodMode.Update();

        public override void GameComponentOnGUI() => HandOfGodMode.OnGUI();
    }
}
