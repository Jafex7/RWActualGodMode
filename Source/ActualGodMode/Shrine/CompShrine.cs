using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    public class CompProperties_Shrine : CompProperties
    {
        /// <summary>Radius in which pray spots are chosen.</summary>
        public float praySpotRadius = 2.9f;

        /// <summary>How many pawns can pray at once (later raised by temple tier).</summary>
        public int baseSlots = 2;

        /// <summary>Divine Power one pawn at 100% faith generates per full day of prayer.</summary>
        public float powerPerDayAtFullFaith = 60f;

        /// <summary>Faith multiplier floor, so a doubter still contributes a little.</summary>
        public float minFaithFactor = 0.1f;

        /// <summary>Output bonus per additional pawn praying at the same time.</summary>
        public float groupBonusPerExtraWorshipper = 0.1f;

        /// <summary>Faith a pawn gains per full day of prayer.</summary>
        public float faithGainPerDay = 0.25f;

        public CompProperties_Shrine()
        {
            compClass = typeof(CompShrine);
        }
    }

    public class CompShrine : ThingComp
    {
        private readonly List<IntVec3> praySpots = new List<IntVec3>();
        private int praySpotsCachedTick = -1;

        private int worshippersCachedTick = -1;
        private int worshippersCached;

        public CompProperties_Shrine Props => (CompProperties_Shrine)props;

        public int Slots => Props.baseSlots;

        /// <summary>Cells pawns may pray on, nearest first. Its count is the worshipper cap.</summary>
        public List<IntVec3> PraySpots
        {
            get
            {
                int tick = Find.TickManager.TicksGame;
                if (praySpotsCachedTick != tick)
                {
                    praySpotsCachedTick = tick;
                    RecalculatePraySpots();
                }
                return praySpots;
            }
        }

        /// <summary>Number of pawns currently in the praying toil at this shrine.</summary>
        public int CurrentWorshippers
        {
            get
            {
                int tick = Find.TickManager.TicksGame;
                if (worshippersCachedTick != tick)
                {
                    worshippersCachedTick = tick;
                    worshippersCached = CountWorshippers();
                }
                return worshippersCached;
            }
        }

        public float GroupFactor =>
            1f + Props.groupBonusPerExtraWorshipper * Mathf.Max(0, CurrentWorshippers - 1);

        public float PowerPerTickFor(Pawn pawn)
        {
            Need_Faith faith = pawn.Faith();
            if (faith == null)
            {
                return 0f;
            }
            float faithFactor = Mathf.Max(faith.CurLevelPercentage, Props.minFaithFactor);
            return Props.powerPerDayAtFullFaith / GenDate.TicksPerDay * faithFactor * GroupFactor;
        }

        /// <summary>Called by the prayer job driver while a pawn prays here.</summary>
        public void DoPrayerTick(Pawn pawn, int delta)
        {
            DivinePower.Instance?.Add(PowerPerTickFor(pawn) * delta);
            pawn.Faith()?.Gain(Props.faithGainPerDay / GenDate.TicksPerDay * delta);
        }

        private void RecalculatePraySpots()
        {
            praySpots.Clear();
            Map map = parent.Map;
            if (map == null)
            {
                return;
            }
            CellRect occupied = parent.OccupiedRect();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, Props.praySpotRadius, useCenter: false))
            {
                if (praySpots.Count >= Slots)
                {
                    break;
                }
                if (!cell.InBounds(map) || occupied.Contains(cell) || !cell.Standable(map))
                {
                    continue;
                }
                if (!GenSight.LineOfSight(cell, parent.Position, map, skipFirstCell: true))
                {
                    continue;
                }
                praySpots.Add(cell);
            }
        }

        private int CountWorshippers()
        {
            if (parent.Map == null)
            {
                return 0;
            }
            int count = 0;
            IReadOnlyList<Pawn> pawns = parent.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i].jobs?.curDriver is JobDriver_PrayAtShrine driver && driver.IsPraying && driver.Shrine == parent)
                {
                    count++;
                }
            }
            return count;
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            GenDraw.DrawFieldEdges(PraySpots, new Color(1f, 0.85f, 0.4f));
        }

        public override string CompInspectStringExtra()
        {
            var sb = new StringBuilder();
            sb.Append("AGM_Shrine_Worshippers".Translate(CurrentWorshippers, Slots));
            int blocked = Slots - PraySpots.Count;
            if (blocked > 0)
            {
                sb.AppendLine();
                sb.Append("AGM_Shrine_SpotsBlocked".Translate(blocked));
            }
            if (CurrentWorshippers > 1)
            {
                sb.AppendLine();
                sb.Append("AGM_Shrine_GroupBonus".Translate((GroupFactor - 1f).ToStringPercent()));
            }
            return sb.ToString();
        }
    }
}
