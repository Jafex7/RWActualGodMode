using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// A miracle being poured under the cursor while the click is held (Hand of God mode).
    /// Each unit (a watered tile, a log, a handful of crops) is checked and paid for where it
    /// lands, with the soft-border cost factor. Times are real seconds passed in by the caller
    /// (works while paused).
    ///
    /// Two kinds (<see cref="MiracleChannelProps.pourOnly"/>):
    /// - Pour-only (water, wood): the charge is the level's full effect (<c>amount</c> units, paid
    ///   per unit). Releasing pauses; pressing again continues; it ends when all is poured.
    /// - Stream (food): a quick click is the normal cast, holding streams for up to <c>maxSeconds</c>
    ///   and releasing ends it.
    /// </summary>
    public class MiracleChannel
    {
        private const int MaxUnitsPerUpdate = 5;

        public readonly MiracleDef def;
        public readonly int levelIndex;
        public readonly Map map;

        public float spent;
        public int units;
        public bool finished;
        public bool holding;

        /// <summary>Why pouring stopped (e.g. out of power), shown once by the caller.</summary>
        public string message;

        private float heldSeconds;
        private float lastUpdate;
        private IntVec3 lastCell = IntVec3.Invalid;
        private float nextUnitTime;
        private readonly HashSet<IntVec3> done = new HashSet<IntVec3>();

        public MiracleChannel(MiracleDef def, int levelIndex, Map map)
        {
            this.def = def;
            this.levelIndex = levelIndex;
            this.map = map;
        }

        public MiracleChannelProps Props => def.channel;

        public MiracleLevel Level => def.levels[levelIndex];

        public bool PourOnly => Props.pourOnly;

        /// <summary>Units in a full charge (pour-only); unlimited for a stream.</summary>
        public int Budget => PourOnly ? BudgetOf(def, Level) : int.MaxValue;

        public int Remaining => Budget - units;

        public static int BudgetOf(MiracleDef def, MiracleLevel level) =>
            Mathf.Max(1, level.amount / Mathf.Max(1, def.channel.countPerUnit));

        /// <summary>Power per unit before the soft-border factor.</summary>
        public static float BaseUnitCost(MiracleDef def, MiracleLevel level)
        {
            MiracleChannelProps ch = def.channel;
            if (ch.pourOnly)
            {
                return level.cost / BudgetOf(def, level);
            }
            if (ch.unitCost > 0f)
            {
                return ch.unitCost;
            }
            int cells = level.radius > 0f ? GenRadial.NumCellsInRadius(level.radius) : 1;
            return level.cost / Mathf.Max(1, cells) * ch.markup;
        }

        public static float UnitCostAt(MiracleDef def, MiracleLevel level, IntVec3 cell, Map map) =>
            BaseUnitCost(def, level) * (map != null && cell.InBounds(map) ? InfluenceUtility.CostFactor(cell, map) : 1f);

        /// <summary>How used up it is: share of the charge (pour-only) or of the time limit (stream).</summary>
        public float Progress => PourOnly ? (float)units / Budget : Mathf.Clamp01(heldSeconds / Props.maxSeconds);

        private float UnitsPerSecond => PourOnly ? Budget / Mathf.Max(0.1f, Props.pourSeconds) : Props.unitsPerSecond;

        /// <summary>The click went down at this cell: start (or continue) pouring.</summary>
        public void Press(IntVec3 cell, float now)
        {
            holding = true;
            lastCell = cell;
            lastUpdate = now;
            nextUnitTime = now;
        }

        public void Release()
        {
            holding = false;
        }

        public void Update(IntVec3 cell, float now)
        {
            if (finished || !holding)
            {
                return;
            }
            heldSeconds += Mathf.Max(0f, now - lastUpdate);
            lastUpdate = now;
            if (Props.perTile)
            {
                IntVec3 from = lastCell.IsValid && lastCell.InBounds(map) ? lastCell : cell;
                if (cell.InBounds(map))
                {
                    foreach (IntVec3 c in GenSight.PointsOnLineOfSight(from, cell))
                    {
                        TryUnit(c);
                        if (finished || !holding)
                        {
                            break;
                        }
                    }
                    lastCell = cell;
                }
            }
            else
            {
                float interval = 1f / Mathf.Max(0.01f, UnitsPerSecond);
                for (int i = 0; i < MaxUnitsPerUpdate && now >= nextUnitTime && !finished && holding; i++)
                {
                    TryUnit(cell);
                    nextUnitTime += interval;
                }
                if (now >= nextUnitTime)
                {
                    nextUnitTime = now; // a long frame: don't save up a burst
                }
            }
            if (PourOnly ? units >= Budget : heldSeconds >= Props.maxSeconds)
            {
                finished = true;
            }
        }

        private void TryUnit(IntVec3 c)
        {
            if (Props.perTile && done.Contains(c))
            {
                return; // tiles already watered don't count twice
            }
            if (!c.InBounds(map) || c.Fogged(map) || !InfluenceUtility.InReach(c, map) || !def.Worker.CanCastAt(c, map, Level).Accepted)
            {
                return;
            }
            float cost = UnitCostAt(def, Level, c, map);
            if (!(DivinePower.Instance?.CanAfford(cost) ?? false))
            {
                message = "AGM_Channel_OutOfPower".Translate();
                if (PourOnly)
                {
                    holding = false; // the rest of the charge waits for more power
                }
                else
                {
                    finished = true;
                }
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
            if (units >= Budget)
            {
                finished = true;
            }
        }
    }
}
