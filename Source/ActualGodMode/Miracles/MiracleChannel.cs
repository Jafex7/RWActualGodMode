using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// A poured miracle's charge (see <see cref="MiracleChannelProps"/>). The charge is the full
    /// effect (<see cref="Budget"/> units); each unit (a watered tile, a log, a crop) is checked and
    /// paid for where it lands, with the soft-border cost factor. Press pours, release pauses,
    /// it ends when all is poured. Times are real seconds passed in by the caller (works while paused).
    /// </summary>
    public class MiracleChannel
    {
        private const int MaxUnitsPerUpdate = 5;

        public readonly MiracleDef def;
        public readonly Map map;

        public float spent;
        public int units;
        public bool finished;
        public bool holding;

        /// <summary>Why pouring stopped (e.g. out of power), shown once by the caller.</summary>
        public string message;

        /// <summary>With <see cref="MiracleChannelProps.lockTarget"/>: the pawn the charge went to first.</summary>
        public Pawn target;

        private IntVec3 lastCell = IntVec3.Invalid;
        private float nextUnitTime;
        private readonly HashSet<IntVec3> done = new HashSet<IntVec3>();

        public MiracleChannel(MiracleDef def, Map map)
        {
            this.def = def;
            this.map = map;
        }

        public MiracleChannelProps Props => def.channel;

        /// <summary>Units in a full charge.</summary>
        public int Budget => BudgetOf(def);

        public int Remaining => Budget - units;

        public static int BudgetOf(MiracleDef def) =>
            Mathf.Max(1, def.Stats.amount / Mathf.Max(1, def.channel.countPerUnit));

        /// <summary>Power per unit before the soft-border factor: the full cost spread over the charge.</summary>
        public static float BaseUnitCost(MiracleDef def) => def.Stats.cost / BudgetOf(def);

        public static float UnitCostAt(MiracleDef def, IntVec3 cell, Map map) =>
            BaseUnitCost(def) * (map != null && cell.InBounds(map) ? InfluenceUtility.CostFactor(cell, map) : 1f);

        /// <summary>Share of the charge used.</summary>
        public float Progress => (float)units / Budget;

        private float UnitsPerSecond => Budget / Mathf.Max(0.1f, Props.pourSeconds);

        /// <summary>The click went down at this cell: start (or go on) pouring.</summary>
        public void Press(IntVec3 cell, float now)
        {
            holding = true;
            lastCell = cell;
            nextUnitTime = now;
            if (Props.perClick)
            {
                TryUnit(cell); // one shot per click
                holding = false;
            }
        }

        public void Release()
        {
            holding = false;
        }

        public void Update(IntVec3 cell, float now)
        {
            if (finished || !holding || Props.perClick)
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
        }

        private void TryUnit(IntVec3 c)
        {
            if (Props.perTile && done.Contains(c))
            {
                return; // tiles already watered don't count twice
            }
            if (!c.InBounds(map) || c.Fogged(map) || !InfluenceUtility.InReach(c, map))
            {
                return;
            }
            Pawn pawn = Props.lockTarget ? c.GetFirstPawn(map) : null;
            if (Props.lockTarget && (pawn == null || (target != null && pawn != target)))
            {
                return; // off the pawn the charge is on: paused
            }
            AcceptanceReport valid = def.Worker.CanCastAt(c, map, def.Stats);
            if (!valid.Accepted)
            {
                if (target != null)
                {
                    finished = true; // its pawn needs nothing more: the rest isn't used (or paid)
                    message = valid.Reason;
                }
                else if (Props.perClick)
                {
                    message = valid.Reason;
                }
                return;
            }
            float cost = UnitCostAt(def, c, map);
            if (!(DivinePower.Instance?.CanAfford(cost) ?? false))
            {
                message = "AGM_Channel_OutOfPower".Translate();
                holding = false; // the rest of the charge waits for more power
                return;
            }
            if (!def.Worker.ChannelUnit(c, map, def.Stats, Props, units == 0))
            {
                return;
            }
            DivinePower.Instance.TrySpend(cost);
            spent += cost;
            units++;
            if (Props.lockTarget)
            {
                target = pawn;
            }
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
