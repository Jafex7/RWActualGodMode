using UnityEngine;
using Verse;
using Waylith.PlatformSpecific;

namespace ActualGodMode
{
    /// <summary>
    /// The god's cursor: a flat, featureless golden triangle (the one the Awakening
    /// pawn swears they saw in the sky). Placeholder until real art.
    ///
    /// Waylith note: a Texture2D built in code loses its CPU pixels (residency evicts
    /// the CPU copy and there is no file to reload from), and the hardware cursor reads
    /// CPU pixels, so it showed as a black square. We keep our own RGBA buffer and hand
    /// it straight to Waylith's HardwareCursor, which is what Cursor.SetCursor does internally.
    /// </summary>
    public static class HandOfGodCursor
    {
        public const int Size = 32;
        private const int HotX = 1;
        private const int HotY = 1;

        private static readonly object CacheKey = new object();

        /// <summary>RGBA32 pixels, rows top-down.</summary>
        public static readonly byte[] Rgba = Build();

        public static void Apply()
        {
            HardwareCursor.SetImage(CacheKey, Rgba, Size, Size, HotX, HotY);
        }

        /// <summary>Back to RimWorld's own cursor (or the system cursor if that pref is off).</summary>
        public static void Restore()
        {
            // Clear the stub's "last texture" cache first, or re-setting the vanilla cursor is a no-op.
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            if (Prefs.CustomCursorEnabled)
            {
                CustomCursor.Activate();
            }
        }

        private static byte[] Build()
        {
            // Triangle in screen space (y down): tip at top-left.
            Vector2 a = new Vector2(1f, 1f);
            Vector2 b = new Vector2(1f, 24f);
            Vector2 c = new Vector2(17f, 17f);

            Color fill = new Color(1f, 0.88f, 0.45f, 1f);
            Color edge = new Color(0.55f, 0.38f, 0.05f, 1f);
            var bytes = new byte[Size * Size * 4];

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = SignedDistance(p, a, b, c);
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
                        col = new Color(1f, 0.85f, 0.4f, 0.45f * t * t);
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

        /// <summary>Negative inside the triangle, positive outside (approximate, in pixels).</summary>
        private static float SignedDistance(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            return Mathf.Max(EdgeDistance(p, a, b), Mathf.Max(EdgeDistance(p, b, c), EdgeDistance(p, c, a)));
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
