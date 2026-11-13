using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ActualGodMode
{
    /// <summary>
    /// Pawn walks to a pray spot, faces the shrine and prays, generating Divine Power.
    /// Target A: pray spot cell. Target B: the shrine.
    /// job.ignoreJoyTimeAssignment = true means the job came from the Worship schedule block.
    /// </summary>
    public class JobDriver_PrayAtShrine : JobDriver
    {
        private const TargetIndex SpotInd = TargetIndex.A;
        private const TargetIndex ShrineInd = TargetIndex.B;

        private bool praying;

        public bool IsPraying => praying;

        public Thing Shrine => job.GetTarget(ShrineInd).Thing;

        private bool FromSchedule => job.ignoreJoyTimeAssignment;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(SpotInd), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(ShrineInd);
            this.FailOn(() => !pawn.HasFaith());

            yield return Toils_Goto.GotoCell(SpotInd, PathEndMode.OnCell);

            Toil pray = ToilMaker.MakeToil("Pray");
            pray.initAction = () => praying = true;
            pray.defaultCompleteMode = ToilCompleteMode.Delay;
            pray.defaultDuration = job.def.joyDuration;
            pray.handlingFacing = true;
            pray.socialMode = RandomSocialMode.Off;
            pray.tickIntervalAction = delta =>
            {
                Thing shrine = Shrine;
                pawn.rotationTracker.FaceCell(shrine.Position);
                shrine.TryGetComp<CompShrine>()?.DoPrayerTick(pawn, delta);

                if (FromSchedule)
                {
                    if (pawn.GetTimeAssignment() != AGM_DefOf.AGM_Worship)
                    {
                        EndJobWith(JobCondition.Succeeded);
                    }
                }
                else
                {
                    JoyUtility.JoyTickCheckEnd(pawn, delta, JoyTickFullJoyAction.EndJob, 1f, shrine as Building);
                }
            };
            pray.AddFinishAction(() => praying = false);
            yield return pray;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref praying, "praying", false);
        }
    }
}
