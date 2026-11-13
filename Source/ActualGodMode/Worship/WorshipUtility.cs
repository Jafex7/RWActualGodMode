using RimWorld;
using Verse;
using Verse.AI;

namespace ActualGodMode
{
    public static class WorshipUtility
    {
        public static Job TryMakePrayJob(Pawn pawn, bool fromSchedule)
        {
            if (!pawn.HasFaith() || pawn.Downed || !pawn.Spawned)
            {
                return null;
            }
            Thing shrine = ShrineUtility.ShrineOn(pawn.Map);
            CompShrine comp = shrine?.TryGetComp<CompShrine>();
            if (comp == null || shrine.IsForbidden(pawn))
            {
                return null;
            }
            foreach (IntVec3 spot in comp.PraySpots)
            {
                if (pawn.CanReserveAndReach(spot, PathEndMode.OnCell, Danger.Some))
                {
                    Job job = JobMaker.MakeJob(AGM_DefOf.AGM_PrayAtShrine, spot, shrine);
                    job.ignoreJoyTimeAssignment = fromSchedule;
                    return job;
                }
            }
            return null;
        }
    }

    /// <summary>Pray during the Worship schedule block.</summary>
    public class JobGiver_Worship : ThinkNode_JobGiver
    {
        public override float GetPriority(Pawn pawn)
        {
            if (pawn.GetTimeAssignment() == AGM_DefOf.AGM_Worship && pawn.HasFaith())
            {
                return 9f;
            }
            return 0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return WorshipUtility.TryMakePrayJob(pawn, fromSchedule: true);
        }
    }

    /// <summary>Pray at the shrine as recreation.</summary>
    public class JoyGiver_PrayAtShrine : JoyGiver
    {
        public override bool CanBeGivenTo(Pawn pawn)
        {
            return pawn.HasFaith() && base.CanBeGivenTo(pawn);
        }

        public override Job TryGiveJob(Pawn pawn)
        {
            return WorshipUtility.TryMakePrayJob(pawn, fromSchedule: false);
        }
    }
}
