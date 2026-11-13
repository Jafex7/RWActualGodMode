using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>One line of a pawn's faith history: what changed their faith on a given day.</summary>
    public class FaithLogEntry : IExposable
    {
        public int day;
        public string label;
        public float delta;
        public float absorbed;

        public void ExposeData()
        {
            Scribe_Values.Look(ref day, "day");
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref delta, "delta");
            Scribe_Values.Look(ref absorbed, "absorbed");
        }
    }

    /// <summary>
    /// Per-pawn faith history shown in the Faith tooltip. Entries with the same cause on the
    /// same day are merged, so small continuous effects (prayer, hunger) read as one line.
    /// </summary>
    public class FaithLog : IExposable
    {
        private const int MaxEntries = 30;
        private const int MaxAgeDays = 7;
        private const int ShownInTooltip = 10;
        private const float MinShown = 0.0005f;

        private List<FaithLogEntry> entries = new List<FaithLogEntry>();

        public IReadOnlyList<FaithLogEntry> Entries => entries;

        public void Add(string label, float delta, float absorbed = 0f)
        {
            if (label.NullOrEmpty())
            {
                label = "AGM_Cause_Unknown".Translate();
            }
            int day = GenDate.DaysPassed;
            foreach (FaithLogEntry e in entries)
            {
                if (e.day == day && e.label == label)
                {
                    e.delta += delta;
                    e.absorbed += absorbed;
                    return;
                }
            }
            entries.Insert(0, new FaithLogEntry { day = day, label = label, delta = delta, absorbed = absorbed });
            entries.RemoveAll(e => day - e.day > MaxAgeDays);
            if (entries.Count > MaxEntries)
            {
                entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            }
        }

        public string TooltipSection()
        {
            var sb = new StringBuilder();
            int shown = 0;
            int today = GenDate.DaysPassed;
            foreach (FaithLogEntry e in entries)
            {
                if (Mathf.Abs(e.delta) < MinShown && e.absorbed < MinShown)
                {
                    continue;
                }
                if (shown++ >= ShownInTooltip)
                {
                    break;
                }
                int ago = today - e.day;
                string when = ago == 0 ? "AGM_Log_Today".Translate()
                    : ago == 1 ? "AGM_Log_Yesterday".Translate()
                    : "AGM_Log_DaysAgo".Translate(ago);
                string line = "  " + when + ": " + e.label;
                if (Mathf.Abs(e.delta) >= MinShown)
                {
                    string amount = (e.delta > 0f ? "+" : "") + e.delta.ToStringPercent("0.#");
                    line += " " + amount.Colorize(e.delta > 0f ? ColorLibrary.Green : ColorLibrary.RedReadable);
                }
                if (e.absorbed >= MinShown)
                {
                    line += " " + "AGM_Log_Absorbed".Translate(e.absorbed.ToStringPercent("0.#")).Resolve().Colorize(ColoredText.SubtleGrayColor);
                }
                sb.AppendLine(line);
            }
            if (shown == 0)
            {
                return null;
            }
            return "AGM_Log_Header".Translate() + "\n" + sb.ToString().TrimEnd();
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && entries == null)
            {
                entries = new List<FaithLogEntry>();
            }
        }
    }

    [DefOf]
    public static class AGM_ThoughtDefOf
    {
        public static ThoughtDef AGM_PrayerAnswered;
        public static ThoughtDef AGM_PrayerIgnored;
        public static ThoughtDef AGM_WitnessedMiracle;
        public static ThoughtDef AGM_AteBlessedMeal;
        public static ThoughtDef AGM_FaithShaken;

        static AGM_ThoughtDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AGM_ThoughtDefOf));
        }
    }

    public static class FaithThoughts
    {
        public static void Give(Pawn pawn, ThoughtDef def)
        {
            pawn?.needs?.mood?.thoughts?.memories?.TryGainMemory(def);
        }
    }
}
