using System.Collections.Generic;
using UnityEngine;

namespace ActualGodMode
{
    /// <summary>
    /// $P point-cloud gesture recognizer (Vatavu, Anthony &amp; Wobbrock, 2012).
    /// Gestures are compared as clouds of points, so stroke order, stroke direction and
    /// the number of strokes don't matter. Not rotation invariant (a "V" differs from a "^").
    /// Distances are in normalized units (gesture scaled to a unit box); lower is better.
    /// </summary>
    public static class PointCloudRecognizer
    {
        public const int SampleCount = 32;

        public struct CloudPoint
        {
            public float x;
            public float y;
            public int strokeId;
        }

        /// <summary>Resample to SampleCount points, scale uniformly to a unit box, center on the centroid.</summary>
        public static CloudPoint[] Normalize(List<List<Vector2>> strokes)
        {
            var raw = new List<CloudPoint>();
            for (int s = 0; s < strokes.Count; s++)
            {
                foreach (Vector2 p in strokes[s])
                {
                    raw.Add(new CloudPoint { x = p.x, y = p.y, strokeId = s });
                }
            }
            if (raw.Count == 0)
            {
                return null;
            }
            CloudPoint[] points = Resample(raw, SampleCount);
            Scale(points);
            TranslateToOrigin(points);
            return points;
        }

        public static float Distance(CloudPoint[] candidate, CloudPoint[] template)
        {
            int n = candidate.Length;
            int step = Mathf.Max(1, Mathf.FloorToInt(Mathf.Pow(n, 0.5f)));
            float min = float.MaxValue;
            for (int i = 0; i < n; i += step)
            {
                min = Mathf.Min(min, CloudDistance(candidate, template, i));
                min = Mathf.Min(min, CloudDistance(template, candidate, i));
            }
            return min;
        }

        private static float CloudDistance(CloudPoint[] a, CloudPoint[] b, int start)
        {
            int n = a.Length;
            var matched = new bool[n];
            float sum = 0f;
            int i = start;
            do
            {
                int index = -1;
                float min = float.MaxValue;
                for (int j = 0; j < n; j++)
                {
                    if (matched[j])
                    {
                        continue;
                    }
                    float dx = a[i].x - b[j].x;
                    float dy = a[i].y - b[j].y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < min)
                    {
                        min = d;
                        index = j;
                    }
                }
                matched[index] = true;
                float weight = 1f - ((i - start + n) % n) / (float)n;
                sum += weight * min;
                i = (i + 1) % n;
            }
            while (i != start);
            return sum;
        }

        private static CloudPoint[] Resample(List<CloudPoint> points, int n)
        {
            float pathLength = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i].strokeId == points[i - 1].strokeId)
                {
                    pathLength += Dist(points[i - 1], points[i]);
                }
            }
            var result = new CloudPoint[n];
            if (pathLength <= 0f)
            {
                // A dot: every sample at the same place.
                for (int i = 0; i < n; i++)
                {
                    result[i] = points[0];
                }
                return result;
            }

            float interval = pathLength / (n - 1);
            float accumulated = 0f;
            int count = 0;
            result[count++] = points[0];
            var work = new List<CloudPoint>(points);
            for (int i = 1; i < work.Count && count < n; i++)
            {
                if (work[i].strokeId != work[i - 1].strokeId)
                {
                    continue;
                }
                float d = Dist(work[i - 1], work[i]);
                if (accumulated + d >= interval && d > 0f)
                {
                    float t = (interval - accumulated) / d;
                    var q = new CloudPoint
                    {
                        x = work[i - 1].x + t * (work[i].x - work[i - 1].x),
                        y = work[i - 1].y + t * (work[i].y - work[i - 1].y),
                        strokeId = work[i].strokeId
                    };
                    result[count++] = q;
                    work.Insert(i, q);
                    accumulated = 0f;
                }
                else
                {
                    accumulated += d;
                }
            }
            // Rounding can leave us one short.
            while (count < n)
            {
                result[count++] = work[work.Count - 1];
            }
            return result;
        }

        private static void Scale(CloudPoint[] points)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (CloudPoint p in points)
            {
                minX = Mathf.Min(minX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxX = Mathf.Max(maxX, p.x);
                maxY = Mathf.Max(maxY, p.y);
            }
            float size = Mathf.Max(maxX - minX, maxY - minY);
            if (size < 1e-6f)
            {
                size = 1f;
            }
            for (int i = 0; i < points.Length; i++)
            {
                points[i].x = (points[i].x - minX) / size;
                points[i].y = (points[i].y - minY) / size;
            }
        }

        private static void TranslateToOrigin(CloudPoint[] points)
        {
            float cx = 0f, cy = 0f;
            foreach (CloudPoint p in points)
            {
                cx += p.x;
                cy += p.y;
            }
            cx /= points.Length;
            cy /= points.Length;
            for (int i = 0; i < points.Length; i++)
            {
                points[i].x -= cx;
                points[i].y -= cy;
            }
        }

        private static float Dist(CloudPoint a, CloudPoint b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }
    }
}
