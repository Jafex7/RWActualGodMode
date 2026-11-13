using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ActualGodMode
{
    /// <summary>
    /// How the god's harm to a pawn is judged, whatever the means (Lightning, a drop into
    /// danger...): enemies fighting you → neutral ("just"); helpless (downed/fleeing) enemies →
    /// evil; your own followers → evil, except "justice" against one in an aggressive break and
    /// "punishment" of one in another break (ends it; onlookers are cowed); prisoners, visitors,
    /// colony animals → evil. Under an evil god, onlookers gain faith out of fear.
    ///
    /// Ledger lines come from keys <c>{keyPrefix}{Target}</c>, e.g. <c>AGM_Deed_Smite_OwnFollower</c>.
    /// </summary>
    public static class WrathJudgement
    {
        public enum Target
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

        public static Target Classify(Pawn p)
        {
            if (p.IsPrisonerOfColony || p.IsSlaveOfColony)
            {
                return Target.Prisoner;
            }
            if (p.IsColonist)
            {
                if (p.InAggroMentalState)
                {
                    return Target.Justice;
                }
                return p.InMentalState ? Target.Punishment : Target.OwnFollower;
            }
            if (p.HostileTo(Faction.OfPlayer))
            {
                bool fleeing = p.MentalStateDef == MentalStateDefOf.PanicFlee || p.CurJobDef == JobDefOf.Flee || p.CurJobDef == JobDefOf.FleeAndCower;
                return p.Downed || fleeing ? Target.HelplessEnemy : Target.Enemy;
            }
            if (p.Faction == Faction.OfPlayer && p.RaceProps.Animal)
            {
                return Target.ColonyAnimal;
            }
            if (p.RaceProps.Humanlike && p.Faction != null && p.Faction != Faction.OfPlayer)
            {
                return Target.Visitor;
            }
            return Target.Other;
        }

        /// <summary>
        /// Record the deed for harming this pawn. <paramref name="factor"/> scales the evil.
        /// <paramref name="logEnemy"/>: also note just harm to an enemy in the ledger (neutral).
        /// Returns true if it was a punishment (the break is ended).
        /// </summary>
        public static bool Judge(Pawn p, string keyPrefix, float factor = 1f, bool logEnemy = false)
        {
            AlignmentTracker alignment = AlignmentTracker.Instance;
            AlignmentTuningDef t = AlignmentTuningDef.Get;
            Target target = Classify(p);
            string label = (keyPrefix + target).Translate(p.LabelShort);
            switch (target)
            {
                case Target.Enemy:
                    if (logEnemy)
                    {
                        alignment?.LogNeutral(label);
                    }
                    return false;
                case Target.Justice:
                    alignment?.LogNeutral(label);
                    return false;
                case Target.HelplessEnemy:
                    alignment?.Deed(t.smiteHelplessEnemy * factor, label);
                    return false;
                case Target.OwnFollower:
                    alignment?.Deed(t.smiteOwnFollower * factor, label);
                    return false;
                case Target.Punishment:
                    alignment?.Deed(t.punishBreak * factor, label);
                    p.MentalState?.RecoverFromState();
                    return true;
                case Target.Prisoner:
                    alignment?.Deed(t.smitePrisoner * factor, label);
                    return false;
                case Target.Visitor:
                    alignment?.Deed(t.smiteVisitor * factor, label);
                    return false;
                case Target.ColonyAnimal:
                    alignment?.Deed(t.smiteColonyAnimal * factor, label);
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>Onlookers of the god's wrath: fear-faith under an evil god; a punishment cows them.</summary>
        public static void AffectWitnesses(IntVec3 cell, Map map, ICollection<Pawn> victims, bool punished)
        {
            bool evil = AlignmentTracker.Instance?.IsEvil ?? false;
            if (!evil && !punished)
            {
                return;
            }
            float radiusSq = 15f * 15f;
            foreach (Pawn w in map.mapPawns.FreeColonistsSpawned)
            {
                if (victims.Contains(w) || !w.Awake() || (w.Position - cell).LengthHorizontalSquared > radiusSq || !GenSight.LineOfSight(w.Position, cell, map))
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
    }
}
