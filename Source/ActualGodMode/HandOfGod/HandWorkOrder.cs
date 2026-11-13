using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace ActualGodMode
{
    /// <summary>
    /// Setting a colonist down on something puts them to work on it, like the vanilla
    /// right-click "Prioritize ..." order but with no menu: the first work the pawn is
    /// assigned to (in their own priority order) that has a job on that cell wins.
    /// The main things on the cell come first (bench bills, blueprints, plants, items to haul;
    /// an undesignated tree gets marked for cutting), then filth, then the cell itself (sowing).
    /// If only unassigned/disabled work fits, the player gets a warning instead.
    /// </summary>
    public static class HandWorkOrder
    {
        public struct Result
        {
            public Job job;
            public WorkGiver_Scanner giver;
            public LocalTargetInfo target;
            /// <summary>"Prioritize cutting oak tree" when a job was found, else why not (or null: nothing there).</summary>
            public string text;
            public bool ordered;
        }

        public static bool CanTakeOrders(Pawn pawn) =>
            pawn != null && pawn.Spawned && pawn.IsColonistPlayerControlled && !pawn.Downed && !pawn.InMentalState
            && !pawn.Drafted && pawn.workSettings != null && pawn.workSettings.EverWork
            && pawn.thinker?.TryGetMainTreeThinkNode<JobGiver_Work>() != null;

        /// <summary>Find and start work at the cell. Returns what happened (text null = nothing to do there).</summary>
        public static Result OrderWork(Pawn pawn, IntVec3 cell)
        {
            if (!CanTakeOrders(pawn) || !cell.InBounds(pawn.Map))
            {
                return default;
            }
            Map map = pawn.Map;
            Result result = Find(pawn, cell, mainThings: true);
            if (result.job == null && TryDesignateTree(pawn, cell, map, out Plant tree))
            {
                Result withTree = Find(pawn, cell, mainThings: true);
                if (withTree.job == null)
                {
                    map.designationManager.RemoveAllDesignationsOn(tree); // didn't help: leave it as it was
                }
                if (withTree.text != null)
                {
                    result = withTree;
                }
            }
            if (result.job == null)
            {
                Result rest = Find(pawn, cell, mainThings: false);
                if (result.text == null) // what they were set on wins, even as a warning
                {
                    result = rest;
                }
            }
            if (result.job != null)
            {
                result.job.workGiverDef = result.giver.def;
                result.ordered = pawn.jobs.TryTakeOrderedJobPrioritizedWork(result.job, result.giver, cell);
                if (result.ordered && result.giver.def.forceFleck != null)
                {
                    FleckMaker.Static(cell, map, result.giver.def.forceFleck);
                }
            }
            return result;
        }

        /// <summary>
        /// The first assigned work with a job here; else a reason built from unassigned work that would fit.
        /// <paramref name="mainThings"/>: buildings, blueprints, plants, items, pawns; otherwise filth and the cell.
        /// </summary>
        public static Result Find(Pawn pawn, IntVec3 cell, bool mainThings)
        {
            List<LocalTargetInfo> targets = Targets(pawn, cell, mainThings);
            foreach (LocalTargetInfo target in targets)
            {
                foreach (WorkGiver wg in pawn.workSettings.WorkGiversInOrderNormal)
                {
                    if (wg is WorkGiver_Scanner scanner && TryJob(pawn, scanner, target, out Job job) && Allowed(pawn, scanner, target) == null)
                    {
                        return new Result
                        {
                            job = job,
                            giver = scanner,
                            target = target,
                            text = target.HasThing
                                ? "PrioritizeGeneric".Translate(scanner.PostProcessedGerund(job), target.Thing.Label).CapitalizeFirst()
                                : "PrioritizeGenericSimple".Translate(scanner.PostProcessedGerund(job)).CapitalizeFirst(),
                        };
                    }
                }
            }
            // Nothing assigned fits: is there work here they just aren't assigned to / can't do?
            foreach (LocalTargetInfo target in targets)
            {
                foreach (WorkTypeDef wt in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                {
                    foreach (WorkGiverDef def in wt.workGiversByPriority)
                    {
                        if (def.Worker is WorkGiver_Scanner scanner && TryJob(pawn, scanner, target, out Job job))
                        {
                            string what = "CannotGenericWork".Translate(def.verb, target.HasThing ? target.Thing.Label : "AreaLower".Translate().ToString());
                            return new Result { target = target, text = what + ": " + (Allowed(pawn, scanner, target) ?? "") };
                        }
                    }
                }
            }
            return default;
        }

        private static List<LocalTargetInfo> Targets(Pawn pawn, IntVec3 cell, bool mainThings)
        {
            var list = new List<LocalTargetInfo>();
            List<Thing> things = cell.GetThingList(pawn.Map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (things[i] != pawn && (things[i] is Filth) != mainThings)
                {
                    list.Add(things[i]);
                }
            }
            if (!mainThings)
            {
                list.Add(cell);
            }
            return list;
        }

        /// <summary>Same checks vanilla's work-giver float menu makes before offering a job.</summary>
        private static bool TryJob(Pawn pawn, WorkGiver_Scanner scanner, LocalTargetInfo target, out Job job)
        {
            job = null;
            if (!scanner.def.directOrderable)
            {
                return false;
            }
            try
            {
                if (target.HasThing)
                {
                    Thing t = target.Thing;
                    bool potential = scanner.PotentialWorkThingRequest.Accepts(t)
                        || (scanner.PotentialWorkThingsGlobal(pawn)?.Contains(t) ?? false);
                    if (!potential || scanner.ShouldSkip(pawn, forced: true) || !scanner.HasJobOnThing(pawn, t, forced: true))
                    {
                        return false;
                    }
                    job = scanner.JobOnThing(pawn, t, forced: true);
                }
                else
                {
                    if (scanner.ShouldSkip(pawn, forced: true) || !scanner.PotentialWorkCellsGlobal(pawn).Contains(target.Cell)
                        || !scanner.HasJobOnCell(pawn, target.Cell, forced: true))
                    {
                        return false;
                    }
                    job = scanner.JobOnCell(pawn, target.Cell, forced: true);
                }
            }
            catch (System.Exception e)
            {
                Log.WarningOnce($"[Actual God Mode] Work giver {scanner.def.defName} threw while checking a hand order: {e.Message}", scanner.def.GetHashCode());
                job = null;
            }
            return job != null;
        }

        /// <summary>Null if the pawn may take this work now, else why not (vanilla wording).</summary>
        private static string Allowed(Pawn pawn, WorkGiver_Scanner scanner, LocalTargetInfo target)
        {
            WorkTypeDef workType = scanner.def.workType;
            PawnCapacityDef missing = scanner.MissingRequiredCapacity(pawn);
            if (missing != null)
            {
                return "CannotMissingHealthActivities".Translate(missing.label);
            }
            if (pawn.WorkTagIsDisabled(scanner.def.workTags) || pawn.WorkTypeIsDisabled(workType))
            {
                return "CannotPrioritizeWorkTypeDisabled".Translate(workType.gerundLabel);
            }
            if (pawn.workSettings.GetPriority(workType) == 0)
            {
                return "CannotPrioritizeNotAssignedToWorkType".Translate(workType.gerundLabel);
            }
            if (target.HasThing ? target.Thing.IsForbidden(pawn) : target.Cell.IsForbidden(pawn))
            {
                return "CannotPrioritizeCellForbidden".Translate();
            }
            bool reach = target.HasThing ? pawn.CanReach(target.Thing, scanner.PathEndMode, Danger.Deadly) : pawn.CanReach(target.Cell, PathEndMode.Touch, Danger.Deadly);
            return reach ? null : (string)"NoPath".Translate().CapitalizeFirst();
        }

        /// <summary>B&amp;W-style: drop a woodcutter on a tree and they chop it. Only undesignated trees, only if they cut plants.</summary>
        private static bool TryDesignateTree(Pawn pawn, IntVec3 cell, Map map, out Plant tree)
        {
            tree = cell.GetPlant(map);
            if (tree == null || !tree.def.plant.IsTree || map.designationManager.AllDesignationsOn(tree).Any())
            {
                return false;
            }
            map.designationManager.AddDesignation(new Designation(tree, DesignationDefOf.CutPlant));
            return true;
        }
    }
}
