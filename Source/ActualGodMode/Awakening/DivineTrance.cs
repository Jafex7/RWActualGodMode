using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ActualGodMode
{
    /// <summary>
    /// A short, harmless trance: the chosen one walks to an open spot nearby and scratches
    /// the god's sign (the cursor triangle) into the ground, then comes back to themselves.
    /// </summary>
    public class MentalState_DivineTrance : MentalState
    {
        public bool drawn;

        public override void PostEnd()
        {
            base.PostEnd();
            if (!drawn)
            {
                AwakeningTracker.Instance?.Notify_TranceEndedEarly(pawn);
            }
        }

        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);
            // Recover here rather than from the drawing job: ending the state ends the job,
            // which must not happen while the job is still cleaning up its own toil.
            if (drawn && !pawn.Dead)
            {
                RecoverFromState();
            }
        }

        public override RandomSocialMode SocialModeMax() => RandomSocialMode.Off;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref drawn, "drawn");
        }
    }

    /// <summary>In the trance: find an open patch of ground and draw the sign.</summary>
    public class JobGiver_DrawDivineSign : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn.MentalState is MentalState_DivineTrance trance && trance.drawn)
            {
                return null;
            }
            Map map = pawn.Map;
            if (!CellFinder.TryFindRandomCellNear(pawn.Position, map, 10,
                    c => c.Standable(map) && c.GetFirstItem(map) == null && c.GetFirstBuilding(map) == null
                         && !c.Roofed(map) && pawn.CanReach(c, PathEndMode.OnCell, Danger.Some),
                    out IntVec3 cell))
            {
                cell = pawn.Position;
            }
            return JobMaker.MakeJob(AGM_AwakeningDefOf.AGM_DrawDivineSign, cell);
        }
    }

    public class JobDriver_DrawDivineSign : JobDriver
    {
        private const int DrawTicks = 900;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

            Toil draw = Toils_General.Wait(DrawTicks);
            draw.WithProgressBarToilDelay(TargetIndex.A);
            draw.handlingFacing = true;
            draw.tickIntervalAction = delta => pawn.rotationTracker.FaceCell(pawn.Position + IntVec3.South);
            draw.AddFinishAction(() =>
            {
                if (ticksLeftThisToil > 0)
                {
                    return; // interrupted before finishing
                }
                PlaceSign(pawn.Position, Map);
                if (pawn.MentalState is MentalState_DivineTrance trance)
                {
                    trance.drawn = true; // the trance ends itself on its next tick
                    AwakeningTracker.Instance?.Notify_SignDrawn(pawn);
                }
            });
            yield return draw;
        }

        /// <summary>
        /// Spawn the sign directly on this cell (or the nearest walkable one). Not via FilthMaker:
        /// vanilla filth rules vary by ground (soil accepts only "Unnatural" filth, rough stone only
        /// "Terrain") and roll random chances, and a scripted mark must always appear.
        /// </summary>
        private static void PlaceSign(IntVec3 at, Map map)
        {
            ThingDef def = AGM_AwakeningDefOf.AGM_Filth_DivineSign;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, 3f, useCenter: true))
            {
                if (c.InBounds(map) && c.Walkable(map) && c.GetFirstThing(map, def) == null && c.GetEdifice(map) == null)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(def), c, map);
                    return;
                }
            }
            TerrainDef terrain = at.GetTerrain(map);
            Log.Warning($"[Actual God Mode] Couldn't find a cell to draw the divine sign near {at} " +
                        $"(terrain {terrain.defName}, accepts {terrain.filthAcceptanceMask}, roofed {at.Roofed(map)}, walkable {at.Walkable(map)}, " +
                        $"things: {string.Join(",", at.GetThingList(map).Select(t => t.def.defName))})");
        }
    }

    /// <summary>The Shrine research stays hidden and locked until the Awakening's Revelation.</summary>
    [HarmonyPatch(typeof(ResearchProjectDef), nameof(ResearchProjectDef.IsHidden), MethodType.Getter)]
    public static class Patch_ShrineResearchHidden
    {
        public static void Postfix(ResearchProjectDef __instance, ref bool __result)
        {
            if (!__result && __instance == AGM_AwakeningDefOf.AGM_ShrineResearch && !(AwakeningTracker.Instance?.Revealed ?? false))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(ResearchProjectDef), nameof(ResearchProjectDef.PrerequisitesCompleted), MethodType.Getter)]
    public static class Patch_ShrineResearchLocked
    {
        public static void Postfix(ResearchProjectDef __instance, ref bool __result)
        {
            if (__result && __instance == AGM_AwakeningDefOf.AGM_ShrineResearch && !(AwakeningTracker.Instance?.Revealed ?? false))
            {
                __result = false;
            }
        }
    }

    /// <summary>Scenario part: the colony starts already knowing the god, with a Shrine and its research done.</summary>
    public class ScenPart_StartWithShrine : ScenPart
    {
        public override void PostGameStart()
        {
            AwakeningTracker.Instance?.MarkRevealedByScenario();
            Find.ResearchManager.FinishProject(AGM_AwakeningDefOf.AGM_ShrineResearch, doCompletionDialog: false);
            Map map = Find.AnyPlayerHomeMap;
            if (map != null)
            {
                AGM_TestScenarios.EnsureShrine(map);
            }
            // It just happened to all of them: everyone believes, a little.
            BelieverTracker.Instance?.ConvertAllColonists();
            GodGuideTracker.Instance?.Start();
        }

        public override string Summary(Scenario scen) => "AGM_ScenPart_StartWithShrine".Translate();
    }
}
