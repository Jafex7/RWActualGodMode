using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Waylith.PlatformSpecific;

namespace ActualGodMode
{
    public enum HandStyle
    {
        Good,
        Neutral,
        Evil
    }

    /// <summary>
    /// The god's cursor: a flat, featureless triangle (the one the Awakening pawn swears they
    /// saw in the sky), tinted by alignment: golden when good, pale blue when neutral, dark red
    /// when evil. The gesture trail and cursor particles follow the same style. Placeholder art.
    ///
    /// Waylith note: a Texture2D built in code loses its CPU pixels (residency evicts the CPU
    /// copy and there is no file to reload from), and the hardware cursor reads CPU pixels, so it
    /// showed as a black square. We keep our own RGBA buffers and hand them straight to Waylith's
    /// HardwareCursor, which is what Cursor.SetCursor does internally.
    /// </summary>
    public static class HandOfGodCursor
    {
        public const int Size = 32;
        private const int HotX = 1;
        private const int HotY = 1;

        private static readonly Dictionary<HandStyle, byte[]> Buffers = new Dictionary<HandStyle, byte[]>();
        private static readonly Dictionary<HandStyle, object> Keys = new Dictionary<HandStyle, object>();

        private static HandStyle? applied;

        public static HandStyle CurrentStyle
        {
            get
            {
                AlignmentTracker a = AlignmentTracker.Instance;
                if (a == null)
                {
                    return HandStyle.Good;
                }
                return a.IsGood ? HandStyle.Good : a.IsEvil ? HandStyle.Evil : HandStyle.Neutral;
            }
        }

        /// <summary>RGBA32 pixels of the current style, rows top-down.</summary>
        public static byte[] Rgba => BufferFor(CurrentStyle);

        public static Color TrailColor
        {
            get
            {
                switch (CurrentStyle)
                {
                    case HandStyle.Evil: return new Color(0.85f, 0.15f, 0.1f);
                    case HandStyle.Neutral: return new Color(0.7f, 0.85f, 1f);
                    default: return InfluenceUtility.InfluenceColor;
                }
            }
        }

        public static bool NeedsReapply => applied.HasValue && applied.Value != CurrentStyle;

        public static void Apply()
        {
            HandStyle style = CurrentStyle;
            if (!Keys.TryGetValue(style, out object key))
            {
                key = new object();
                Keys[style] = key;
            }
            HardwareCursor.SetImage(key, BufferFor(style), Size, Size, HotX, HotY);
            applied = style;
        }

        /// <summary>Back to RimWorld's own cursor (or the system cursor if that pref is off).</summary>
        public static void Restore()
        {
            applied = null;
            // Clear the stub's "last texture" cache first, or re-setting the vanilla cursor is a no-op.
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            if (Prefs.CustomCursorEnabled)
            {
                CustomCursor.Activate();
            }
        }

        public static void Notify_AlignmentChanged()
        {
            if (applied.HasValue)
            {
                Apply();
            }
        }

        /// <summary>Particles at the cursor, in the current style (game-time flecks: don't call while paused).</summary>
        public static void ThrowParticles(Vector3 loc, Map map)
        {
            switch (CurrentStyle)
            {
                case HandStyle.Evil:
                    FleckMaker.ThrowMicroSparks(loc, map);
                    if (Rand.Chance(0.5f))
                    {
                        FleckMaker.ThrowSmoke(loc, map, 0.4f);
                    }
                    break;
                case HandStyle.Neutral:
                    FleckMaker.ThrowAirPuffUp(loc, map);
                    break;
                default:
                    FleckMaker.ThrowMicroSparks(loc, map);
                    break;
            }
        }

        private static byte[] BufferFor(HandStyle style)
        {
            if (!Buffers.TryGetValue(style, out byte[] buffer))
            {
                buffer = Build(style);
                Buffers[style] = buffer;
            }
            return buffer;
        }

        private static byte[] Build(HandStyle style)
        {
            Color fill, edge, glow;
            switch (style)
            {
                case HandStyle.Evil:
                    fill = new Color(0.55f, 0.08f, 0.06f);
                    edge = new Color(0.15f, 0.02f, 0.02f);
                    glow = new Color(0.9f, 0.2f, 0.1f);
                    break;
                case HandStyle.Neutral:
                    fill = new Color(0.78f, 0.9f, 1f);
                    edge = new Color(0.2f, 0.33f, 0.5f);
                    glow = new Color(0.6f, 0.8f, 1f);
                    break;
                default:
                    fill = new Color(1f, 0.88f, 0.45f);
                    edge = new Color(0.55f, 0.38f, 0.05f);
                    glow = new Color(1f, 0.85f, 0.4f);
                    break;
            }

            // Triangle in screen space (y down): tip at top-left.
            Vector2 a = new Vector2(1f, 1f);
            Vector2 b = new Vector2(1f, 24f);
            Vector2 c = new Vector2(17f, 17f);
            var bytes = new byte[Size * Size * 4];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = Mathf.Max(EdgeDistance(p, a, b), Mathf.Max(EdgeDistance(p, b, c), EdgeDistance(p, c, a)));
                    Color col;
                    if (d <= -1.2f)
                    {
                        col = fill;
                    }
                    else if (d <= 0.3f)
                    {
                        col = edge;
                    }
                    else if (d <= 4f)
                    {
                        float t = 1f - (d - 0.3f) / 3.7f;
                        col = new Color(glow.r, glow.g, glow.b, 0.45f * t * t);
                    }
                    else
                    {
                        col = Color.clear;
                    }
                    int i = (y * Size + x) * 4;
                    bytes[i] = (byte)(col.r * 255f);
                    bytes[i + 1] = (byte)(col.g * 255f);
                    bytes[i + 2] = (byte)(col.b * 255f);
                    bytes[i + 3] = (byte)(col.a * 255f);
                }
            }
            return bytes;
        }

        private static float EdgeDistance(Vector2 p, Vector2 from, Vector2 to)
        {
            Vector2 dir = (to - from).normalized;
            // Outward normal for this vertex order (a→b→c is clockwise on screen, y down).
            Vector2 normal = new Vector2(-dir.y, dir.x);
            return Vector2.Dot(p - from, normal);
        }
    }
}
