using System.Linq;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    public static class MiracleUtility
    {
        /// <summary>What a one-shot cast here costs: the base cost, raised sharply past the edge of influence.</summary>
        public static float CostAt(MiracleDef def, IntVec3 cell, Map map) =>
            map == null || !cell.InBounds(map) ? def.Stats.cost : def.Stats.cost * InfluenceUtility.CostFactor(cell, map);

        /// <summary>
        /// Every check a one-shot cast must pass: unlocked, within reach (soft border), affordable, valid target.
        /// (Poured miracles are normally cast from the hand, unit by unit: <see cref="MiracleChannel"/>.)
        /// </summary>
        public static AcceptanceReport CanCast(MiracleDef def, IntVec3 cell, Map map)
        {
            if (map == null || !cell.InBounds(map) || cell.Fogged(map))
            {
                return false;
            }
            if (!(MiracleTracker.Instance?.IsUnlocked(def) ?? false))
            {
                return "AGM_MiracleLocked".Translate();
            }
            if (!InfluenceUtility.InReach(cell, map))
            {
                return "AGM_OutsideInfluence".Translate();
            }
            float cost = CostAt(def, cell, map);
            if (!(DivinePower.Instance?.CanAfford(cost) ?? false))
            {
                return "AGM_NotEnoughPower".Translate(cost.ToString("F0"));
            }
            return def.Worker.CanCastAt(cell, map, def.Stats);
        }

        public static AcceptanceReport TryCast(MiracleDef def, IntVec3 cell, Map map)
        {
            AcceptanceReport report = CanCast(def, cell, map);
            if (!report.Accepted)
            {
                return report;
            }
            DivinePower.Instance.TrySpend(CostAt(def, cell, map));
            def.Worker.Cast(cell, map, def.Stats);
            NotifyWitnesses(def, cell, map);
            PrayerRequestTracker.Instance?.Notify_MiracleCast(def, cell, map);
            return true;
        }

        /// <summary>Awake colonists who can see the miracle gain a little faith.</summary>
        public static void NotifyWitnesses(MiracleDef def, IntVec3 cell, Map map)
        {
            if (def.witnessFaithGain <= 0f)
            {
                return;
            }
            float radiusSq = def.witnessRadius * def.witnessRadius;
            BelieverTracker believers = BelieverTracker.Instance;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned.ToList())
            {
                if (!p.Awake() || (p.Position - cell).LengthHorizontalSquared > radiusSq || !GenSight.LineOfSight(p.Position, cell, map))
                {
                    continue;
                }
                if (p.Faith() != null)
                {
                    p.Faith().Gain(def.witnessFaithGain, "AGM_Cause_Witnessed".Translate(def.label));
                    FaithThoughts.Give(p, AGM_ThoughtDefOf.AGM_WitnessedMiracle);
                }
                else
                {
                    // Seeing is believing (even for Sceptics, sometimes).
                    believers?.TryConvert(p, FaithTuningDef.Get.witnessConvertChance, byTalk: false);
                }
            }
        }
    }
}
