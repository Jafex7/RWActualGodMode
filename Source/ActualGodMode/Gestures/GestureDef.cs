using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// A default gesture shape. Points are in a 0–1 box with y pointing DOWN (screen space).
    /// Size, position, speed, stroke order and stroke direction don't matter to the recognizer.
    /// Players can replace any miracle's gesture with their own recording (stored in mod settings).
    /// </summary>
    public class GestureDef : Def
    {
        public List<GestureTemplate> templates = new List<GestureTemplate>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors())
            {
                yield return e;
            }
            if (templates.NullOrEmpty())
            {
                yield return "has no templates.";
            }
            foreach (GestureTemplate t in templates)
            {
                if (t.strokes.NullOrEmpty() || t.strokes.Exists(s => s.points.Count < 2))
                {
                    yield return "has a template with an empty stroke (need 2+ points per stroke).";
                }
            }
        }
    }

    /// <summary>One way of drawing a gesture: one or more strokes.</summary>
    public class GestureTemplate : IExposable
    {
        public List<GestureStroke> strokes = new List<GestureStroke>();

        public GestureTemplate()
        {
        }

        public GestureTemplate(List<List<Vector2>> strokes)
        {
            foreach (List<Vector2> s in strokes)
            {
                this.strokes.Add(new GestureStroke { points = new List<Vector2>(s) });
            }
        }

        public List<List<Vector2>> AsPointLists()
        {
            var result = new List<List<Vector2>>();
            foreach (GestureStroke s in strokes)
            {
                result.Add(s.points);
            }
            return result;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref strokes, "strokes", LookMode.Deep);
        }
    }

    public class GestureStroke : IExposable
    {
        public List<Vector2> points = new List<Vector2>();

        public void ExposeData()
        {
            Scribe_Collections.Look(ref points, "points", LookMode.Value);
        }
    }

    /// <summary>A player's recorded replacement gesture (several samples of the same shape).</summary>
    public class CustomGesture : IExposable
    {
        public List<GestureTemplate> samples = new List<GestureTemplate>();

        public void ExposeData()
        {
            Scribe_Collections.Look(ref samples, "samples", LookMode.Deep);
        }
    }
}
