using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>Decides when a prayer is made, how long the god has, and what answers it.</summary>
    public class PrayerRequestWorker
    {
        public PrayerRequestDef def;

        /// <summary>Is this pawn in the situation that makes them pray for this?</summary>
        public virtual bool CanRequest(Pawn pawn) => false;

        /// <summary>False once the need has passed on its own (request quietly withdrawn).</summary>
        public virtual bool StillNeeded(PrayerRequest request) => CanRequest(request.pawn);

        public virtual int DeadlineTicks(Pawn pawn) => Mathf.RoundToInt(def.deadlineHours * GenDate.TicksPerHour);

        public virtual bool IsAnsweredBy(PrayerRequest request, MiracleDef miracle, IntVec3 cell, Map map)
        {
            Pawn p = request.pawn;
            return miracle == def.miracle && p.Spawned && p.Map == map
                && p.Position.InHorDistOf(cell, def.answerRadius);
        }
    }

    /// <summary>Hungry or starving.</summary>
    public class PrayerRequestWorker_Hungry : PrayerRequestWorker
    {
        public override bool CanRequest(Pawn pawn)
        {
            Need_Food food = pawn.needs?.food;
            return food != null && food.CurCategory >= HungerCategory.Hungry;
        }
    }

    /// <summary>Freezing.</summary>
    public class PrayerRequestWorker_Cold : PrayerRequestWorker
    {
        public override bool CanRequest(Pawn pawn)
        {
            Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Hypothermia);
            return h != null && h.Severity >= 0.04f;
        }
    }

    /// <summary>A fire is burning near them (or on them).</summary>
    public class PrayerRequestWorker_Fire : PrayerRequestWorker
    {
        private const float Radius = 8f;

        public override bool CanRequest(Pawn pawn)
        {
            if (!pawn.Spawned)
            {
                return false;
            }
            if (pawn.IsBurning())
            {
                return true;
            }
            foreach (Thing fire in pawn.Map.listerThings.ThingsOfDef(ThingDefOf.Fire))
            {
                if (fire.Position.InHorDistOf(pawn.Position, Radius))
                {
                    return true;
                }
            }
            return false;
        }

        public override bool IsAnsweredBy(PrayerRequest request, MiracleDef miracle, IntVec3 cell, Map map)
        {
            // Water anywhere near the pawn or near any fire close to them counts.
            if (miracle != def.miracle || request.pawn.Map != map)
            {
                return false;
            }
            return base.IsAnsweredBy(request, miracle, cell, map) || cell.InHorDistOf(request.pawn.Position, Radius + def.answerRadius);
        }
    }

    /// <summary>Bleeding or downed by injuries. Urgent: deadline follows their bleed-out time.</summary>
    public class PrayerRequestWorker_Wounded : PrayerRequestWorker
    {
        public override bool CanRequest(Pawn pawn)
        {
            HediffSet set = pawn.health.hediffSet;
            return set.BleedRateTotal > 0.1f || (pawn.Downed && MiracleWorker_Heal.HealableInjuries(pawn).Count > 0);
        }

        public override int DeadlineTicks(Pawn pawn)
        {
            int normal = base.DeadlineTicks(pawn);
            if (pawn.health.hediffSet.BleedRateTotal > 0.01f)
            {
                int bleedOut = HealthUtility.TicksUntilDeathDueToBloodLoss(pawn);
                return Mathf.Clamp(Mathf.RoundToInt(bleedOut * 0.8f), GenDate.TicksPerHour, normal);
            }
            return normal;
        }
    }

    /// <summary>An enemy or a manhunting animal is close. Urgent; answered by Lightning near the pawn.</summary>
    public class PrayerRequestWorker_Hunted : PrayerRequestWorker
    {
        private const float ThreatRadius = 10f;

        public static Pawn Threat(Pawn pawn)
        {
            if (!pawn.Spawned)
            {
                return null;
            }
            float radiusSq = ThreatRadius * ThreatRadius;
            IReadOnlyList<Pawn> all = pawn.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn other = all[i];
                if (other != pawn && !other.Downed && !other.Dead && other.HostileTo(pawn)
                    && (other.Position - pawn.Position).LengthHorizontalSquared <= radiusSq)
                {
                    return other;
                }
            }
            return null;
        }

        public override bool CanRequest(Pawn pawn) => Threat(pawn) != null;

        public override bool IsAnsweredBy(PrayerRequest request, MiracleDef miracle, IntVec3 cell, Map map)
        {
            return miracle == def.miracle && request.pawn.Map == map && cell.InHorDistOf(request.pawn.Position, ThreatRadius + 4f);
        }
    }

    /// <summary>Wish: rain for the crops. Answered by Water cast on a growing zone.</summary>
    public class PrayerRequestWorker_RainForCrops : PrayerRequestWorker
    {
        public override bool CanRequest(Pawn pawn)
        {
            if (!pawn.Spawned)
            {
                return false;
            }
            foreach (Zone zone in pawn.Map.zoneManager.AllZones)
            {
                if (zone is Zone_Growing)
                {
                    return true;
                }
            }
            return false;
        }

        public override bool StillNeeded(PrayerRequest request) => true;

        public override bool IsAnsweredBy(PrayerRequest request, MiracleDef miracle, IntVec3 cell, Map map)
        {
            return miracle == def.miracle && map == request.map && map.zoneManager.ZoneAt(cell) is Zone_Growing;
        }
    }

    /// <summary>Wish: a sign that the god is there. Any miracle on the map answers it.</summary>
    public class PrayerRequestWorker_Sign : PrayerRequestWorker
    {
        public override bool CanRequest(Pawn pawn) => pawn.Spawned;

        public override bool StillNeeded(PrayerRequest request) => true;

        public override bool IsAnsweredBy(PrayerRequest request, MiracleDef miracle, IntVec3 cell, Map map) => map == request.map;
    }
}
