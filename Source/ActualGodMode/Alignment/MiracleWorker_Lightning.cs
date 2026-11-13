using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace ActualGodMode
{
    /// <summary>
    /// Smite: a real weather lightning strike (flash, bolt, thunder, flame explosion r1.9).
    /// What it means depends on who's standing there:
    ///  enemies fighting you → neutral; helpless (downed/fleeing) enemies → evil;
    ///  your own followers → evil, except "justice" against one in an aggressive break, and
    ///  "punishment" of one in another break (ends the break; witnesses are cowed);
    ///  prisoners, visitors, colony animals → evil.
    /// Under an evil god, witnesses gain faith out of fear.
    /// </summary>
    public class MiracleWorker_Lightning : MiracleWorker
    {
        public override AcceptanceReport CanCastAt(IntVec3 cell, Map map, MiracleLevel level)
        {
            RoofDef roof = map.roofGrid.RoofAt(cell);
            if (roof != null && roof.isThickRoof)
            {
                return "AGM_Lightning_ThickRoof".Translate();
            }
            return true;
        }

        public override void Cast(IntVec3 cell, Map map, MiracleLevel level)
        {
            float radius = level.radius > 0f ? level.radius : 1.9f;
            List<Pawn> targets = GenRadial.RadialDistinctThingsAround(cell, map, radius, useCenter: true).OfType<Pawn>().ToList();
            bool punished = false;
            AlignmentTracker alignment = AlignmentTracker.Instance;
            AlignmentTuningDef t = AlignmentTuningDef.Get;

            foreach (Pawn p in targets)
            {
                if (p.Dead)
                {
                    continue;
                }
                switch (Classify(p))
                {
                    case SmiteTarget.HelplessEnemy:
                        alignment?.Deed(t.smiteHelplessEnemy, "AGM_Deed_SmiteHelpless".Translate(p.LabelShort));
                        break;
                    case SmiteTarget.OwnFollower:
                        alignment?.Deed(t.smiteOwnFollower, "AGM_Deed_SmiteFollower".Translate(p.LabelShort));
                        break;
                    case SmiteTarget.Justice:
                        alignment?.LogNeutral("AGM_Deed_Justice".Translate(p.LabelShort));
                        break;
                    case SmiteTarget.Punishment:
                        alignment?.Deed(t.punishBreak, "AGM_Deed_Punish".Translate(p.LabelShort));
                        p.MentalState?.RecoverFromState();
                        punished = true;
                        break;
                    case SmiteTarget.Prisoner:
                        alignment?.Deed(t.smitePrisoner, "AGM_Deed_SmitePrisoner".Translate(p.LabelShort));
                        break;
                    case SmiteTarget.Visitor:
                        alignment?.Deed(t.smiteVisitor, "AGM_Deed_SmiteVisitor".Translate(p.LabelShort));
                        break;
                    case SmiteTarget.ColonyAnimal:
                        alignment?.Deed(t.smiteColonyAnimal, "AGM_Deed_SmiteAnimal".Translate(p.LabelShort));
                        break;
                }
            }

            WeatherEvent_LightningStrike strike = new WeatherEvent_LightningStrike(map, cell);
            map.weatherManager.eventHandler.AddEvent(strike);

            AffectWitnesses(cell, map, targets, punished);
        }

        public enum SmiteTarget
        {
            Other,
            Enemy,
            HelplessEnemy,
            OwnFollower,
            Justice,
            Punishment,
            Prisoner,
            Visitor,
            ColonyAnimal
        }

        public static SmiteTarget Classify(Pawn p)
        {
            if (p.IsPrisonerOfColony || p.IsSlaveOfColony)
            {
                return SmiteTarget.Prisoner;
            }
            if (p.IsColonist)
            {
                if (p.InAggroMentalState)
                {
                    return SmiteTarget.Justice;
                }
                return p.InMentalState ? SmiteTarget.Punishment : SmiteTarget.OwnFollower;
            }
            if (p.HostileTo(Faction.OfPlayer))
            {
                bool fleeing = p.MentalStateDef == MentalStateDefOf.PanicFlee || p.CurJobDef == JobDefOf.Flee || p.CurJobDef == JobDefOf.FleeAndCower;
                return p.Downed || fleeing ? SmiteTarget.HelplessEnemy : SmiteTarget.Enemy;
            }
            if (p.Faction == Faction.OfPlayer && p.RaceProps.Animal)
            {
                return SmiteTarget.ColonyAnimal;
            }
            if (p.RaceProps.Humanlike && p.Faction != null && p.Faction != Faction.OfPlayer)
            {
                return SmiteTarget.Visitor;
            }
            return SmiteTarget.Other;
        }

        /// <summary>Witnessing the god's wrath: fear-faith under an evil god; punishment cows the onlookers.</summary>
        private static void AffectWitnesses(IntVec3 cell, Map map, List<Pawn> targets, bool punished)
        {
            bool evil = AlignmentTracker.Instance?.IsEvil ?? false;
            if (!evil && !punished)
            {
                return;
            }
            float radiusSq = 15f * 15f;
            foreach (Pawn w in map.mapPawns.FreeColonistsSpawned)
            {
                if (targets.Contains(w) || !w.Awake() || (w.Position - cell).LengthHorizontalSquared > radiusSq || !GenSight.LineOfSight(w.Position, cell, map))
                {
                    continue;
                }
                if (evil && w.Faith() != null)
                {
                    w.Faith().Gain(AlignmentTuningDef.Get.fearFaithGain, "AGM_Cause_FearedWrath".Translate());
                    FaithThoughts.Give(w, AGM_AlignmentDefOf.AGM_FearedWrath);
                }
                if (punished)
                {
                    HealthUtility.AdjustSeverity(w, AGM_AlignmentDefOf.AGM_WontBeNext, 1f);
                }
            }
        }

        public override void DrawPreview(IntVec3 cell, Map map, MiracleLevel level)
        {
            GenDraw.DrawRadiusRing(cell, level.radius > 0f ? level.radius : 1.9f);
        }
    }

    [DefOf]
    public static class AGM_AlignmentDefOf
    {
        public static ThoughtDef AGM_FearedWrath;
        public static HediffDef AGM_WontBeNext;
        public static MiracleDef AGM_Miracle_Lightning;

        static AGM_AlignmentDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AGM_AlignmentDefOf));
        }
    }
}
