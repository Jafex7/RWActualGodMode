using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    public struct GestureMatch
    {
        public MiracleDef miracle;
        public float distance;
        public MiracleDef runnerUp;
        public float runnerUpDistance;

        public bool Recognized => miracle != null && distance <= GestureLibrary.Threshold;

        /// <summary>0–1 confidence for display (1 = perfect, 0 = at the threshold).</summary>
        public float Confidence => miracle == null ? 0f : Mathf.Clamp01(1f - distance / GestureLibrary.Threshold);
    }

    /// <summary>
    /// Which gesture casts which miracle: the player's recording if they made one,
    /// otherwise the miracle's default GestureDef. Matching uses the $P recognizer.
    /// </summary>
    public static class GestureLibrary
    {
        /// <summary>
        /// Max $P distance (normalized, 32 points) for a gesture to count. Calibrated by the self-test:
        /// simulated drawings of the right sign scored ≤ ~1.0, the closest wrong sign ~1.8, junk ≥ ~2.1.
        /// Scaled by the player's gesture tolerance setting.
        /// </summary>
        public const float BaseThreshold = 1.5f;

        /// <summary>Total screen path length below which a stroke is treated as a click, not a gesture.</summary>
        public const float MinPathPixels = 25f;

        private static readonly Dictionary<MiracleDef, List<PointCloudRecognizer.CloudPoint[]>> cache =
            new Dictionary<MiracleDef, List<PointCloudRecognizer.CloudPoint[]>>();

        public static float Threshold => BaseThreshold * ActualGodModeMod.Settings.gestureTolerance;

        public static IEnumerable<MiracleDef> MiraclesWithGestures =>
            DefDatabase<MiracleDef>.AllDefsListForReading.Where(d => d.gesture != null || HasCustom(d)).OrderBy(d => d.order);

        public static bool HasCustom(MiracleDef miracle) =>
            ActualGodModeMod.Settings.customGestures.TryGetValue(miracle.defName, out CustomGesture g) && !g.samples.NullOrEmpty();

        /// <summary>Templates used for matching (and drawn in the Book of Gestures).</summary>
        public static List<GestureTemplate> TemplatesFor(MiracleDef miracle)
        {
            if (ActualGodModeMod.Settings.customGestures.TryGetValue(miracle.defName, out CustomGesture custom) && !custom.samples.NullOrEmpty())
            {
                return custom.samples;
            }
            return miracle.gesture?.templates ?? new List<GestureTemplate>();
        }

        public static void SetCustom(MiracleDef miracle, List<GestureTemplate> samples)
        {
            ActualGodModeMod.Settings.customGestures[miracle.defName] = new CustomGesture { samples = samples };
            ActualGodModeMod.Instance.WriteSettings();
            cache.Remove(miracle);
        }

        public static void ResetToDefault(MiracleDef miracle)
        {
            ActualGodModeMod.Settings.customGestures.Remove(miracle.defName);
            ActualGodModeMod.Instance.WriteSettings();
            cache.Remove(miracle);
        }

        public static float PathLength(List<List<Vector2>> strokes)
        {
            float length = 0f;
            foreach (List<Vector2> s in strokes)
            {
                for (int i = 1; i < s.Count; i++)
                {
                    length += (s[i] - s[i - 1]).magnitude;
                }
            }
            return length;
        }

        /// <summary>Best and second-best miracle for a drawing. Pass <paramref name="exclude"/> to ignore one (recorder conflict check).</summary>
        public static GestureMatch Recognize(List<List<Vector2>> strokes, MiracleDef exclude = null)
        {
            var match = new GestureMatch { distance = float.MaxValue, runnerUpDistance = float.MaxValue };
            PointCloudRecognizer.CloudPoint[] candidate = PointCloudRecognizer.Normalize(strokes);
            if (candidate == null)
            {
                return match;
            }
            foreach (MiracleDef miracle in MiraclesWithGestures)
            {
                if (miracle == exclude)
                {
                    continue;
                }
                float best = float.MaxValue;
                foreach (PointCloudRecognizer.CloudPoint[] template in NormalizedTemplates(miracle))
                {
                    best = Mathf.Min(best, PointCloudRecognizer.Distance(candidate, template));
                }
                if (best < match.distance)
                {
                    match.runnerUp = match.miracle;
                    match.runnerUpDistance = match.distance;
                    match.miracle = miracle;
                    match.distance = best;
                }
                else if (best < match.runnerUpDistance)
                {
                    match.runnerUp = miracle;
                    match.runnerUpDistance = best;
                }
            }
            return match;
        }

        /// <summary>Distance between two drawings (recorder consistency check).</summary>
        public static float DistanceBetween(List<List<Vector2>> a, List<List<Vector2>> b)
        {
            return PointCloudRecognizer.Distance(PointCloudRecognizer.Normalize(a), PointCloudRecognizer.Normalize(b));
        }

        private static List<PointCloudRecognizer.CloudPoint[]> NormalizedTemplates(MiracleDef miracle)
        {
            if (!cache.TryGetValue(miracle, out var list))
            {
                list = TemplatesFor(miracle).Select(t => PointCloudRecognizer.Normalize(t.AsPointLists())).Where(c => c != null).ToList();
                cache[miracle] = list;
            }
            return list;
        }
    }
}
