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
    /// Hand of God mode: the god's direct presence on the map. Active while toggled on,
    /// while the hold key is down, or while a miracle is charged. While active it owns
    /// left/right click on the map (selection and designators are suspended).
    ///
    /// Flow: draw a gesture (one or more strokes) â†’ after a short pause it is recognized â†’
    /// the miracle is "charged" in the cursor â†’ Q/E change level â†’ left-click casts,
    /// right-click cancels.
    /// </summary>
    public static class HandOfGodMode
    {
        private const float MinPointSpacing = 3f;
        private const float TrailFadeSeconds = 0.6f;

        /// <summary>Pause after the last stroke before the gesture is recognized (allows multi-stroke symbols). A setting.</summary>
        private static float RecognizeDelaySeconds => ActualGodModeMod.Settings.strokePauseSeconds;

        public static bool Toggled;

        // Drawing
        private static bool drawingStroke;
        private static readonly List<List<Vector2>> strokes = new List<List<Vector2>>();
        private static float lastStrokeEndTime;

        // Feedback trail after recognition
        private static readonly List<List<Vector2>> fadingStrokes = new List<List<Vector2>>();
        private static float fadeStartTime = -100f;
        private static Color fadeColor;

        // Charged miracle
        private static MiracleDef charged;
        private static int chargedLevel;

        // Hand: a press on a grabbable thing becomes a grab unless the mouse moves (then it's a stroke).
        private static Thing pressTarget;
        private static Vector2 pressPos;
        private static float pressTime;

        private static bool cursorApplied;
        private static int glowFrame;

        public static MiracleDef Charged => charged;

        public static bool Active =>
            Find.CurrentMap != null && WorldRendererUtility.DrawingMap
            && (Toggled || charged != null || AGM_KeyBindingDefOf.AGM_HoldHandOfGod.IsDown || Hand?.Holding == true);

        private static GodHand Hand => GodHand.On(Find.CurrentMap);

        private static bool HasPendingGesture => strokes.Count > 0 && !drawingStroke;

        public static void Toggle()
        {
            Toggled = !Toggled;
            if (Toggled)
            {
                Find.DesignatorManager.Deselect();
                Find.Targeter.StopTargeting();
            }
            else
            {
                CancelCharge();
                ClearStrokes();
                Hand?.Place(UI.MouseCell());
            }
            (Toggled ? SoundDefOf.Tick_High : SoundDefOf.Tick_Low).PlayOneShotOnCamera();
        }

        /// <summary>Called from the MapInterface.HandleMapClicks patch while active (during OnGUI).</summary>
        public static void ProcessMapInput()
        {
            Event e = Event.current;
            GodHand hand = Hand;
            if (hand != null && hand.Holding)
            {
                ProcessHoldingInput(e, hand);
                return;
            }
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                    if (charged != null)
                    {
                        TryCastCharged(UI.MouseCell());
                    }
                    else
                    {
                        // On a thing (and not mid-sign): maybe a grab. Moving away makes it a stroke.
                        pressTarget = strokes.Count == 0 ? GodHand.GrabbableAt(UI.MouseCell(), Find.CurrentMap) : null;
                        pressPos = e.mousePosition;
                        pressTime = Time.realtimeSinceStartup;
                        drawingStroke = true;
                        strokes.Add(new List<Vector2> { e.mousePosition });
                    }
                    e.Use();
                    break;

                case EventType.MouseDrag when e.button == 0 && drawingStroke:
                    if (pressTarget != null && (e.mousePosition - pressPos).magnitude > HandTuningDef.Get.dragThresholdPixels)
                    {
                        pressTarget = null;
                    }
                    List<Vector2> current = strokes[strokes.Count - 1];
                    if ((e.mousePosition - current[current.Count - 1]).sqrMagnitude >= MinPointSpacing * MinPointSpacing)
                    {
                        current.Add(e.mousePosition);
                    }
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0 && drawingStroke:
                    if (pressTarget != null)
                    {
                        // A click on a thing: pick it up and carry it until the next click.
                        Thing target = pressTarget;
                        pressTarget = null;
                        ClearStrokes();
                        hand?.TryGrab(target, drag: false);
                        e.Use();
                        break;
                    }
                    drawingStroke = false;
                    lastStrokeEndTime = Time.realtimeSinceStartup;
                    e.Use();
                    break;

                case EventType.MouseDown when e.button == 1:
                    if (charged != null)
                    {
                        CancelCharge();
                        SoundDefOf.CancelMode.PlayOneShotOnCamera();
                    }
                    else if (strokes.Count > 0)
                    {
                        ClearStrokes();
                    }
                    else if (Toggled)
                    {
                        Toggle();
                    }
                    e.Use();
                    break;
            }
        }

        /// <summary>While something is in the hand: release (drag) or click (carry) sets it down.</summary>
        private static void ProcessHoldingInput(Event e, GodHand hand)
        {
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 || e.button == 1:
                    hand.Place(UI.MouseCell());
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0 && hand.dragging:
                    hand.Place(UI.MouseCell());
                    e.Use();
                    break;

                case EventType.MouseDrag when e.button == 0:
                    e.Use();
                    break;
            }
        }

        /// <summary>Held still on a thing long enough: grab it and drag (release to place).</summary>
        private static void CheckHoldToGrab()
        {
            if (pressTarget == null || !drawingStroke)
            {
                pressTarget = null;
                return;
            }
            if (Time.realtimeSinceStartup - pressTime >= HandTuningDef.Get.grabHoldSeconds)
            {
                Thing target = pressTarget;
                pressTarget = null;
                ClearStrokes();
                Hand?.TryGrab(target, drag: true);
            }
        }

        private static void Recognize()
        {
            var drawn = new List<List<Vector2>>(strokes);
            ClearStrokes();
            if (GestureLibrary.PathLength(drawn) < GestureLibrary.MinPathPixels)
            {
                return; // a click, not a gesture
            }

            GestureMatch match = GestureLibrary.Recognize(drawn);
            MiracleTracker tracker = MiracleTracker.Instance;
            if (!match.Recognized)
            {
                StartFade(drawn, ColorLibrary.RedReadable);
                Messages.Message("AGM_Gesture_NotUnderstood".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            if (tracker == null || !tracker.AnyUnlocked(match.miracle))
            {
                StartFade(drawn, ColorLibrary.RedReadable);
                Messages.Message("AGM_Gesture_NotGranted".Translate(match.miracle.label), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            StartFade(drawn, HandOfGodCursor.TrailColor);
            charged = match.miracle;
            chargedLevel = DefaultLevel(charged);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        /// <summary>Highest unlocked level you can afford, else the lowest unlocked.</summary>
        private static int DefaultLevel(MiracleDef miracle)
        {
            MiracleTracker tracker = MiracleTracker.Instance;
            DivinePower dp = DivinePower.Instance;
            for (int i = miracle.levels.Count - 1; i >= 0; i--)
            {
                if (tracker.IsUnlocked(miracle, i) && dp.CanAfford(miracle.levels[i].cost))
                {
                    return i;
                }
            }
            return 0;
        }

        private static void ChangeLevel(int delta)
        {
            MiracleTracker tracker = MiracleTracker.Instance;
            int next = chargedLevel + delta;
            if (next >= 0 && next < charged.levels.Count && tracker.IsUnlocked(charged, next))
            {
                chargedLevel = next;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
        }

        private static void TryCastCharged(IntVec3 cell)
        {
            AcceptanceReport result = MiracleUtility.TryCast(charged, chargedLevel, cell, Find.CurrentMap);
            if (result.Accepted)
            {
                CancelCharge();
            }
            else if (!result.Reason.NullOrEmpty())
            {
                Messages.Message(result.Reason, MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        private static void CancelCharge()
        {
            charged = null;
        }

        private static void ClearStrokes()
        {
            strokes.Clear();
            drawingStroke = false;
        }

        private static void StartFade(List<List<Vector2>> drawn, Color color)
        {
            fadingStrokes.Clear();
            fadingStrokes.AddRange(drawn);
            fadeStartTime = Time.realtimeSinceStartup;
            fadeColor = color;
        }

        /// <summary>Per-frame: recognition timing, cursor swap, previews, glow particles.</summary>
        public static void Update()
        {
            // Recognize after a short pause, or right away if the hold key was released.
            if (HasPendingGesture && (Time.realtimeSinceStartup - lastStrokeEndTime >= RecognizeDelaySeconds || !Active))
            {
                Recognize();
            }

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
            if (active && HandOfGodCursor.NeedsReapply)
            {
                HandOfGodCursor.Apply(); // alignment band changed while the mode was on
            }
            if (!active)
            {
                ClearStrokes();
                return;
            }

            Map map = Find.CurrentMap;
            CheckHoldToGrab();
            GodHand hand = Hand;
            if (hand != null && hand.Holding)
            {
                hand.MoveTo(UI.MouseCell());
                InfluenceUtility.DrawInfluence(map);
            }
            if (charged != null)
            {
                InfluenceUtility.DrawInfluence(map);
                IntVec3 mouse = UI.MouseCell();
                if (mouse.InBounds(map))
                {
                    charged.Worker.DrawPreview(mouse, map, charged.levels[chargedLevel]);
                }
            }

            // Flecks run on game time: spawning them while paused would pile up and burst on unpause.
            if (Find.TickManager.Paused)
            {
                return;
            }
            Vector3 mousePos = UI.MouseMapPosition();
            if (++glowFrame % (drawingStroke || charged != null ? 3 : 10) == 0 && mousePos.ToIntVec3().InBounds(map))
            {
                HandOfGodCursor.ThrowParticles(mousePos, map);
            }
        }

        /// <summary>OnGUI: hotkeys, trail, charged-miracle readout, mode label.</summary>
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
            if (charged != null)
            {
                if (KeyBindingDefOf.Designator_RotateLeft.KeyDownEvent)
                {
                    ChangeLevel(-1);
                    Event.current.Use();
                }
                else if (KeyBindingDefOf.Designator_RotateRight.KeyDownEvent)
                {
                    ChangeLevel(1);
                    Event.current.Use();
                }
            }
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Color gold = HandOfGodCursor.TrailColor;
            foreach (List<Vector2> s in strokes)
            {
                DrawTrail(s, gold);
            }
            float fade = 1f - (Time.realtimeSinceStartup - fadeStartTime) / TrailFadeSeconds;
            if (fade > 0f)
            {
                foreach (List<Vector2> s in fadingStrokes)
                {
                    DrawTrail(s, new Color(fadeColor.r, fadeColor.g, fadeColor.b, fade));
                }
            }

            if (charged != null)
            {
                DrawChargedReadout();
            }
            else if (Active)
            {
                DrawHandReadout();
            }

            if (Active)
            {
                Rect rect = LabelRect();
                using (new TextBlock(GameFont.Medium, TextAnchor.MiddleCenter, gold))
                {
                    Widgets.Label(rect, "AGM_HoG_Active".Translate());
                }
                string hint = Hand?.Holding == true
                    ? "AGM_HoG_HintHolding".Translate()
                    : charged != null
                    ? "AGM_HoG_HintCharged".Translate(KeyBindingDefOf.Designator_RotateLeft.MainKeyLabel, KeyBindingDefOf.Designator_RotateRight.MainKeyLabel)
                    : Toggled
                        ? "AGM_HoG_HintToggled".Translate(AGM_KeyBindingDefOf.AGM_ToggleHandOfGod.MainKeyLabel)
                        : "AGM_HoG_HintHeld".Translate();
                using (new TextBlock(GameFont.Tiny, TextAnchor.MiddleCenter, Color.white))
                {
                    Widgets.Label(new Rect(rect.x, rect.yMax, rect.width, 20f), hint);
                }
            }
        }

        private static void DrawChargedReadout()
        {
            MiracleLevel level = charged.levels[chargedLevel];
            float power = DivinePower.Instance?.Power ?? 0f;
            Vector2 mouse = Event.current.mousePosition;
            Rect icon = new Rect(mouse.x + 22f, mouse.y + 14f, 32f, 32f);
            GUI.DrawTexture(icon, charged.Icon);
            Color color = power >= level.cost ? Color.white : ColorLibrary.RedReadable;
            using (new TextBlock(GameFont.Small, TextAnchor.UpperLeft, color))
            {
                Widgets.Label(new Rect(icon.xMax + 6f, icon.y - 2f, 240f, 22f), charged.LevelLabel(chargedLevel));
                Widgets.Label(new Rect(icon.xMax + 6f, icon.y + 16f, 240f, 22f),
                    "AGM_MiracleCost".Translate(level.cost.ToString("F0"), power.ToString("F0")));
            }
        }

        /// <summary>Next to the cursor: what's held (mass, upkeep), or what's under it and whether it can be lifted.</summary>
        private static void DrawHandReadout()
        {
            GodHand hand = Hand;
            if (hand == null)
            {
                return;
            }
            Thing thing = hand.Holding ? hand.Held : (strokes.Count == 0 ? GodHand.GrabbableAt(UI.MouseCell(), Find.CurrentMap) : null);
            if (thing == null)
            {
                return;
            }
            float capacity = GodHand.Capacity;
            string line1, line2;
            Color color = Color.white;
            if (hand.Holding)
            {
                float mass = GodHand.MassOf(thing);
                line1 = thing.LabelCap;
                line2 = "AGM_Hand_HoldingLine".Translate(mass.ToString("F0"), capacity.ToString("F0"), GodHand.HoldCostPerHour(thing, mass).ToString("F1"));
            }
            else
            {
                int count = GodHand.LiftableCount(thing);
                float mass = thing is Pawn ? GodHand.UnitMass(thing) : GodHand.UnitMass(thing) * Mathf.Max(count, 1);
                line1 = "AGM_Hand_GrabLine".Translate(thing.LabelShort);
                if (count <= 0)
                {
                    color = ColorLibrary.RedReadable;
                    line2 = "AGM_Hand_TooHeavyShort".Translate(GodHand.UnitMass(thing).ToString("F0"), capacity.ToString("F0"));
                }
                else
                {
                    float cost = GodHand.PickupCost(thing, mass);
                    if (!InfluenceUtility.InInfluence(thing.Position, thing.Map) || !DivinePower.Instance.CanAfford(cost))
                    {
                        color = ColorLibrary.RedReadable;
                    }
                    string partial = count < thing.stackCount ? " (" + count + "/" + thing.stackCount + ")" : "";
                    line2 = "AGM_Hand_LiftLine".Translate(mass.ToString("F0"), capacity.ToString("F0"), cost.ToString("F1")) + partial;
                }
            }
            Vector2 mouse = Event.current.mousePosition;
            using (new TextBlock(GameFont.Small, TextAnchor.UpperLeft, color))
            {
                Widgets.Label(new Rect(mouse.x + 24f, mouse.y + 12f, 360f, 22f), line1);
                Widgets.Label(new Rect(mouse.x + 24f, mouse.y + 30f, 360f, 22f), line2);
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

        public static void DrawTrail(List<Vector2> points, Color color, float width = 3f)
        {
            for (int i = 1; i < points.Count; i++)
            {
                Widgets.DrawLine(points[i - 1], points[i], color, width);
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
