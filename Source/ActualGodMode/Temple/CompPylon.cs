using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ActualGodMode
{
    public class CompProperties_Pylon : CompProperties
    {
        public float influenceRadius = 7.9f;

        /// <summary>Days a full tending lasts before the pylon goes dark.</summary>
        public float tendingLastsDays = 4f;

        /// <summary>Believers come to tend it below this level.</summary>
        public float tendBelow = 0.5f;

        public int tendTicks = 400;

        /// <summary>Faith hardship for believers on the map if enemies destroy it.</summary>
        public float desecrationHardship = 0.06f;

        public CompProperties_Pylon()
        {
            compClass = typeof(CompPylon);
        }
    }

    /// <summary>
    /// Extends the god's influence while tended. Tending wears off over days; a believer must
    /// come and tend it. If enemies destroy it, believers on the map feel it as hardship.
    /// </summary>
    public class CompPylon : ThingComp
    {
        private float tended = 1f;

        public CompProperties_Pylon Props => (CompProperties_Pylon)props;

        public float Tended => tended;

        public bool Active => tended > 0f;

        /// <summary>Pylon strength (power track): wider radius, tending lasts longer (1 = base).</summary>
        public static float StrengthFactor => GodProgression.Value(AGM_ProgressionDefOf.AGM_Track_PylonStrength) / 100f;

        public float Radius => Props.influenceRadius * StrengthFactor;

        public bool NeedsTending => tended < Props.tendBelow;

        public override void CompTickRare()
        {
            tended = Mathf.Max(0f, tended - 250f / (Props.tendingLastsDays * StrengthFactor * GenDate.TicksPerDay));
        }

        public void Tend() => tended = 1f;

        public void DebugSetTended(float value) => tended = Mathf.Clamp01(value);

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (mode != DestroyMode.KillFinalize || previousMap == null)
            {
                return; // deconstructed or moved, not desecrated
            }
            foreach (Pawn p in previousMap.mapPawns.FreeColonistsSpawned)
            {
                p.Faith()?.Hardship(Props.desecrationHardship, "AGM_Cause_PylonDestroyed".Translate());
            }
            Messages.Message("AGM_Pylon_Destroyed".Translate(), new TargetInfo(parent.Position, previousMap), MessageTypeDefOf.NegativeEvent);
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            GenDraw.DrawRadiusRing(parent.Position, Radius, Active ? InfluenceUtility.InfluenceColor : Color.gray);
        }

        public override string CompInspectStringExtra()
        {
            return Active
                ? "AGM_Pylon_Tended".Translate(tended.ToStringPercent())
                : "AGM_Pylon_Dark".Translate();
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref tended, "tended", 1f);
        }
    }

    /// <summary>
    /// While placing a pylon (or a shrine): show the god's current influence and the area the
    /// new one would cover, so it can be placed strategically.
    /// </summary>
    public class PlaceWorker_ShowInfluence : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            InfluenceUtility.DrawInfluence(map);
            float radius = def.GetCompProperties<CompProperties_Pylon>()?.influenceRadius * CompPylon.StrengthFactor
                ?? def.GetCompProperties<CompProperties_Shrine>()?.influenceRadius
                ?? 0f;
            if (radius > 0f)
            {
                GenDraw.DrawRadiusRing(center, radius, Color.white);
            }
        }
    }

    /// <summary>Believers tend pylons that are wearing off (Basic work).</summary>
    public class WorkGiver_TendPylon : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) =>
            pawn.Map.listerThings.ThingsOfDef(AGM_TempleDefOf.AGM_Pylon);

        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            !pawn.HasFaith() || pawn.Map.listerThings.ThingsOfDef(AGM_TempleDefOf.AGM_Pylon).Count == 0;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompPylon comp = t.TryGetComp<CompPylon>();
            if (comp == null || !pawn.HasFaith() || t.IsForbidden(pawn) || t.Faction != pawn.Faction)
            {
                return false;
            }
            if (!forced && !comp.NeedsTending)
            {
                return false;
            }
            return pawn.CanReserve(t, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            JobMaker.MakeJob(AGM_TempleDefOf.AGM_TendPylon, t);
    }

    public class JobDriver_TendPylon : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            CompPylon comp = job.targetA.Thing.TryGetComp<CompPylon>();
            Toil tend = Toils_General.Wait(comp?.Props.tendTicks ?? 400);
            tend.WithProgressBarToilDelay(TargetIndex.A);
            tend.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            tend.AddFinishAction(() =>
            {
                if (ticksLeftThisToil <= 0)
                {
                    job.targetA.Thing?.TryGetComp<CompPylon>()?.Tend();
                }
            });
            yield return tend;
        }
    }

    [DefOf]
    public static class AGM_TempleDefOf
    {
        public static ThingDef AGM_Pylon;
        public static ThingDef AGM_OfferingBowl;
        public static ThingDef AGM_Idol;
        public static ThingDef AGM_IncenseBurner;
        public static JobDef AGM_TendPylon;

        static AGM_TempleDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AGM_TempleDefOf));
        }
    }
}
