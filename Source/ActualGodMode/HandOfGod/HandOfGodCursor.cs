using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// The god's cursor: a flat, featureless golden triangle (the one the Awakening
    /// pawn swears they saw in the sky). Generated in code so it is CPU-readable,
    /// which Waylith's hardware cursor requires. Placeholder until real art.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HandOfGodCursor
    {
        private const int Size = 32;

        public static readonly Vector2 Hotspot = new Vector2(1f, 1f);

        public static readonly Texture2D Texture = Build();

        private static Texture2D Build()
        {
            // Triangle in screen space (y down): tip at top-left.
            Vector2 a = new Vector2(1f, 1f);
            Vector2 b = new Vector2(1f, 24f);
            Vector2 c = new Vector2(17f, 17f);

            Color fill = new Color(1f, 0.88f, 0.45f, 1f);
            Color edge = new Color(0.55f, 0.38f, 0.05f, 1f);
            var pixels = new Color[Size * Size];

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
                        // Soft outer glow.
                        float t = 1f - (d - 0.3f) / 3.7f;
                        col = new Color(1f, 0.85f, 0.4f, 0.45f * t * t);
                    }
                    else
                    {
                        col = Color.clear;
                    }
                    // Texture rows are bottom-up.
                    pixels[(Size - 1 - y) * Size + x] = col;
                }
            }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "AGM_HandOfGodCursor" };
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>Negative inside the triangle, positive outside (approximate, in pixels).</summary>
        private static float SignedDistance(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float da = EdgeDistance(p, a, b);
            float db = EdgeDistance(p, b, c);
            float dc = EdgeDistance(p, c, a);
            return Mathf.Max(da, Mathf.Max(db, dc));
        }

        private static float EdgeDistance(Vector2 p, Vector2 from, Vector2 to)
        {
            Vector2 dir = (to - from).normalized;
            Vector2 normal = new Vector2(dir.y, -dir.x); // outward for counter-clockwise winding in y-down space
            return Vector2.Dot(p - from, normal);
        }
    }
}
