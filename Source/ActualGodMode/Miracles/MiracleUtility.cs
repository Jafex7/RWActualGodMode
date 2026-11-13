using RimWorld;
using Verse;

namespace ActualGodMode
{
    public static class MiracleUtility
    {
        /// <summary>Every check a cast must pass: unlocked, inside influence, affordable, valid target.</summary>
        public static AcceptanceReport CanCast(MiracleDef def, int levelIndex, IntVec3 cell, Map map)
        {
            if (map == null || !cell.InBounds(map) || cell.Fogged(map))
            {
                return false;
            }
            if (!(MiracleTracker.Instance?.IsUnlocked(def, levelIndex) ?? false))
            {
                return "AGM_MiracleLocked".Translate();
            }
            if (!InfluenceUtility.InInfluence(cell, map))
            {
                return "AGM_OutsideInfluence".Translate();
            }
            MiracleLevel level = def.levels[levelIndex];
            if (!(DivinePower.Instance?.CanAfford(level.cost) ?? false))
            {
                return "AGM_NotEnoughPower".Translate(level.cost.ToString("F0"));
            }
            return def.Worker.CanCastAt(cell, map, level);
        }

        public static AcceptanceReport TryCast(MiracleDef def, int levelIndex, IntVec3 cell, Map map)
        {
            AcceptanceReport report = CanCast(def, levelIndex, cell, map);
            if (!report.Accepted)
            {
                return report;
            }
            MiracleLevel level = def.levels[levelIndex];
            DivinePower.Instance.TrySpend(level.cost);
            def.Worker.Cast(cell, map, level);
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
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (!p.Awake() || (p.Position - cell).LengthHorizontalSquared > radiusSq)
                {
                    continue;
                }
                if (GenSight.LineOfSight(p.Position, cell, map))
                {
                    p.Faith()?.Gain(def.witnessFaithGain);
                }
            }
        }
    }
}
