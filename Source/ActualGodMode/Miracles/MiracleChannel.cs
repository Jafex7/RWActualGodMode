using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// A miracle being poured under the cursor while the click is held (Hand of God mode).
    /// Each unit (a watered tile, a handful of crops) is checked and paid for where it lands,
    /// with the soft-border cost factor. Ends on release, after <see cref="MiracleChannelProps.maxSeconds"/>,
    /// or when power runs out. Times are real seconds passed in by the caller (works while paused).
    /// </summary>
    public class MiracleChannel
    {
        private const int MaxUnitsPerUpdate = 3;

        public readonly MiracleDef def;
        public readonly int levelIndex;
        public readonly Map map;
        public readonly float startTime;

        public float spent;
        public int units;
        public bool finished;
        public string endReason;

        private IntVec3 lastCell;
        private float nextUnitTime;
        private readonly HashSet<IntVec3> done = new HashSet<IntVec3>();

        public MiracleChannel(MiracleDef def, int levelIndex, Map map, IntVec3 startCell, float now)
        {
            this.def = def;
            this.levelIndex = levelIndex;
            this.map = map;
            startTime = now;
            nextUnitTime = now;
            lastCell = startCell;
        }

        public MiracleChannelProps Props => def.channel;

        public MiracleLevel Level => def.levels[levelIndex];

        /// <summary>Power per unit before the soft-border factor.</summary>
        public static float BaseUnitCost(MiracleDef def, MiracleLevel level)
        {
            MiracleChannelProps ch = def.channel;
            if (ch.unitCost > 0f)
            {
                return ch.unitCost;
            }
            int cells = level.radius > 0f ? GenRadial.NumCellsInRadius(level.radius) : 1;
            return level.cost / Mathf.Max(1, cells) * ch.markup;
        }

        public static float UnitCostAt(MiracleDef def, MiracleLevel level, IntVec3 cell, Map map) =>
            BaseUnitCost(def, level) * (map != null && cell.InBounds(map) ? InfluenceUtility.CostFactor(cell, map) : 1f);

        /// <summary>0 at the start, 1 when time is up.</summary>
        public float Progress(float now) => Mathf.Clamp01((now - startTime) / Props.maxSeconds);

        public void Update(IntVec3 cell, float now)
        {
            if (finished)
            {
                return;
            }
            if (Props.perTile)
            {
                IntVec3 from = lastCell.IsValid && lastCell.InBounds(map) ? lastCell : cell;
                if (cell.InBounds(map))
                {
                    foreach (IntVec3 c in GenSight.PointsOnLineOfSight(from, cell))
                    {
                        TryUnit(c);
                        if (finished)
                        {
                            break;
                        }
                    }
                    lastCell = cell;
                }
            }
            else
            {
                float interval = 1f / Mathf.Max(0.01f, Props.unitsPerSecond);
                for (int i = 0; i < MaxUnitsPerUpdate && now >= nextUnitTime && !finished; i++)
                {
                    TryUnit(cell);
                    nextUnitTime += interval;
                }
                if (now >= nextUnitTime)
                {
                    nextUnitTime = now; // a long frame: don't save up a burst
                }
            }
            if (!finished && now - startTime >= Props.maxSeconds)
            {
                finished = true;
            }
        }

        private void TryUnit(IntVec3 c)
        {
            if (Props.perTile && done.Contains(c))
            {
                return; // going back over a tile is free
            }
            if (!c.InBounds(map) || c.Fogged(map) || !InfluenceUtility.InReach(c, map) || !def.Worker.CanCastAt(c, map, Level).Accepted)
            {
                return;
            }
            float cost = UnitCostAt(def, Level, c, map);
            if (!(DivinePower.Instance?.CanAfford(cost) ?? false))
            {
                finished = true;
                endReason = "AGM_Channel_OutOfPower".Translate();
                return;
            }
            if (!def.Worker.ChannelUnit(c, map, Level, Props, units == 0))
            {
                return;
            }
            DivinePower.Instance.TrySpend(cost);
            spent += cost;
            units++;
            done.Add(c);
            if (units == 1)
            {
                MiracleUtility.NotifyWitnesses(def, c, map);
            }
            PrayerRequestTracker.Instance?.Notify_MiracleCast(def, c, map);
        }
    }
}
