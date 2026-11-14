using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ActualGodMode
{
    /// <summary>
    /// Applies the <see cref="DropRules"/> when the god's hand sets a colonist down on a cell: finds what
    /// they're set on (the top main thing, else filth, else the ground), its <see cref="DropCategoryDef"/>
    /// and the player's action for it, then starts it (work, haul, wear, equip, eat) or says why not.
    /// Also previews the intent next to the cursor before the drop (the held pawn isn't on the map, so
    /// only checks that don't need the map are shown; the drop does the rest).
    /// </summary>
    public static class DropRuleOrder
    {
        public struct Plan
        {
            public DropCategoryDef category;
            /// <summary>What they're set on (null: the ground).</summary>
            public Thing thing;
            public DropAction action;
        }

        /// <summary>What a colonist set down here would act on, and how.</summary>
        public static Plan PlanAt(IntVec3 cell, Map map, Pawn pawn)
        {
            if (!cell.InBounds(map))
            {
                return default;
            }
            List<Thing> things = cell.GetThingList(map);
            for (int pass = 0; pass < 2; pass++)
            {
                bool filthPass = pass == 1;
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    Thing t = things[i];
                    if (t == pawn || !t.def.selectable || (t is Filth) != filthPass)
                    {
                        continue;
                    }
                    DropCategoryDef cat = DropCategoryDef.For(t);
                    if (cat != null)
                    {
                        return new Plan { category = cat, thing = t, action = DropRules.For(cat) };
                    }
                }
            }
            DropCategoryDef ground = DropCategoryDef.ForCell(cell, map);
            return ground == null ? default : new Plan { category = ground, action = DropRules.For(ground) };
        }

        /// <summary>Set down here: act on the rule. Text null = nothing to do (or "just set down").</summary>
        public static HandWorkOrder.Result Execute(Pawn pawn, IntVec3 cell)
        {
            if (!HandWorkOrder.CanTakeOrders(pawn))
            {
                return default;
            }
            Plan plan = PlanAt(cell, pawn.Map, pawn);
            if (plan.category == null)
            {
                return HandWorkOrder.OrderWork(pawn, cell);
            }
            switch (plan.action)
            {
                case DropAction.Ignore:
                    return default;
                case DropAction.Haul:
                    return HandWorkOrder.OrderWorkOn(pawn, plan.thing, wt => wt == WorkTypeDefOf.Hauling);
                case DropAction.Wear:
                    return plan.thing is Apparel apparel ? Wear(pawn, apparel) : default;
                case DropAction.Equip:
                    return Equip(pawn, plan.thing);
                case DropAction.Eat:
                    return Eat(pawn, plan.thing);
                default:
                    return HandWorkOrder.OrderWork(pawn, cell);
            }
        }

        /// <summary>
        /// Whether a held pawn gets the drop-rule preview: a colonist in their right mind. (Not
        /// IsColonistPlayerControlled: that needs the pawn on the map, and a held pawn isn't.)
        /// </summary>
        public static bool ShowsPreview(Pawn held) =>
            held != null && held.IsColonist && held.MentalStateDef == null && !held.Downed && held.workSettings != null;

        /// <summary>
        /// Next to the cursor while holding a colonist: what the rule will do here ("Will wear: parka"),
        /// and a reason in <paramref name="warning"/> when it can already be told it won't work.
        /// </summary>
        public static string Preview(Pawn pawn, IntVec3 cell, Map map, out string warning)
        {
            warning = null;
            Plan plan = PlanAt(cell, map, pawn);
            if (plan.category == null)
            {
                return null;
            }
            string what = plan.thing?.LabelShort ?? (string)"AGM_Drop_TheGround".Translate();
            switch (plan.action)
            {
                case DropAction.Ignore:
                    return "AGM_Drop_Will_Ignore".Translate();
                case DropAction.Haul:
                    if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hauling))
                    {
                        warning = "CannotPrioritizeWorkTypeDisabled".Translate(WorkTypeDefOf.Hauling.gerundLabel);
                    }
                    else if (pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.Hauling) == 0)
                    {
                        warning = "CannotPrioritizeNotAssignedToWorkType".Translate(WorkTypeDefOf.Hauling.gerundLabel);
                    }
                    break;
                case DropAction.Wear:
                    warning = plan.thing is Apparel apparel ? WearProblem(pawn, apparel, onMap: false) : null;
                    break;
                case DropAction.Equip:
                    warning = EquipProblem(pawn, plan.thing, onMap: false);
                    break;
                case DropAction.Eat:
                    warning = EatProblem(pawn, plan.thing, onMap: false);
                    break;
            }
            return ("AGM_Drop_Will_" + plan.action).Translate(what);
        }

        // ---- Wear ----

        private static string WearProblem(Pawn pawn, Apparel apparel, bool onMap)
        {
            if (pawn.apparel == null)
            {
                return "Incapable".Translate().CapitalizeFirst();
            }
            if (onMap && !pawn.CanReach(apparel, PathEndMode.ClosestTouch, Danger.Deadly))
            {
                return "NoPath".Translate().CapitalizeFirst();
            }
            if (apparel.IsBurning())
            {
                return "Burning".Translate();
            }
            if (pawn.apparel.WouldReplaceLockedApparel(apparel))
            {
                return "WouldReplaceLockedApparel".Translate().CapitalizeFirst();
            }
            if (!ApparelUtility.HasPartsToWear(pawn, apparel.def))
            {
                return "CannotWearBecauseOfMissingBodyParts".Translate().CapitalizeFirst();
            }
            return EquipmentUtility.CanEquip(apparel, pawn, out string reason) ? null : reason;
        }

        private static HandWorkOrder.Result Wear(Pawn pawn, Apparel apparel)
        {
            string problem = WearProblem(pawn, apparel, onMap: true);
            if (problem != null)
            {
                return new HandWorkOrder.Result { text = "CannotWear".Translate(apparel.Label, apparel) + ": " + problem };
            }
            apparel.SetForbidden(false);
            bool ordered = pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wear, apparel), JobTag.Misc);
            return new HandWorkOrder.Result { text = "ForceWear".Translate(apparel.LabelShort, apparel), ordered = ordered };
        }

        // ---- Equip ----

        private static string EquipProblem(Pawn pawn, Thing thing, bool onMap)
        {
            if (thing == null || !thing.HasComp<CompEquippable>() || pawn.equipment == null)
            {
                return "Incapable".Translate().CapitalizeFirst();
            }
            if (thing.def.IsWeapon && pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                return "IsIncapableOfViolenceLower".Translate(pawn.LabelShort, pawn);
            }
            if (thing.def.IsRangedWeapon && pawn.WorkTagIsDisabled(WorkTags.Shooting))
            {
                return "IsIncapableOfShootingLower".Translate(pawn);
            }
            if (onMap && !pawn.CanReach(thing, PathEndMode.ClosestTouch, Danger.Deadly))
            {
                return "NoPath".Translate().CapitalizeFirst();
            }
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                return "Incapable".Translate().CapitalizeFirst();
            }
            if (thing.IsBurning())
            {
                return "BurningLower".Translate();
            }
            if (pawn.IsQuestLodger() && !EquipmentUtility.QuestLodgerCanEquip(thing, pawn))
            {
                return "QuestRelated".Translate().CapitalizeFirst();
            }
            if (EquipmentUtility.AlreadyBondedToWeapon(thing, pawn))
            {
                return "BladelinkAlreadyBonded".Translate();
            }
            return EquipmentUtility.CanEquip(thing, pawn, out string reason, checkBonded: false) ? null : reason.CapitalizeFirst();
        }

        private static HandWorkOrder.Result Equip(Pawn pawn, Thing thing)
        {
            string label = thing?.LabelShort ?? "";
            string problem = EquipProblem(pawn, thing, onMap: true);
            if (problem != null)
            {
                return new HandWorkOrder.Result { text = "CannotEquip".Translate(label) + ": " + problem };
            }
            bool Go()
            {
                thing.SetForbidden(false);
                FleckMaker.Static(thing.DrawPos, thing.MapHeld, FleckDefOf.FeedbackEquip);
                // (Set down right on it, the job can finish at once: the weapon is already in hand.)
                return pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Equip, thing), JobTag.Misc);
            }
            // A persona weapon asks first, as with vanilla's right-click.
            string confirm = EquipmentUtility.GetPersonaWeaponConfirmationText(thing, pawn);
            if (!confirm.NullOrEmpty())
            {
                Find.WindowStack.Add(new Dialog_MessageBox(confirm, "Yes".Translate(), () => Go(), "No".Translate()));
                return default;
            }
            bool ordered = Go();
            return new HandWorkOrder.Result { text = "Equip".Translate(label), ordered = ordered };
        }

        // ---- Eat / take ----

        private static string EatProblem(Pawn pawn, Thing food, bool onMap)
        {
            if (food?.def.ingestible == null || !food.IngestibleNow || !pawn.RaceProps.CanEverEat(food.def))
            {
                return "FoodNotSuitable".Translate().CapitalizeFirst();
            }
            if (!food.IsSociallyProper(pawn))
            {
                return "ReservedForPrisoners".Translate().CapitalizeFirst();
            }
            if (!food.def.IsDrug && !food.def.ingestible.nonDrugIngestibleWithoutFoodNeed && !pawn.FoodIsSuitable(food.def))
            {
                return "FoodNotSuitable".Translate().CapitalizeFirst();
            }
            if (food.def.IsDrug && !pawn.DrugIsSuitable(food.def))
            {
                return "DrugNotSuitable".Translate().CapitalizeFirst();
            }
            if (food.def.IsNonMedicalDrug && !pawn.CanTakeDrug(food.def))
            {
                return TraitDefOf.DrugDesire.DataAtDegree(-1).GetLabelCapFor(pawn);
            }
            if (FoodUtility.InappropriateForTitle(food.def, pawn, allowIfStarving: true))
            {
                return "FoodBelowTitleRequirements".Translate(pawn.royalty.MostSeniorTitle.def.GetLabelFor(pawn).CapitalizeFirst()).CapitalizeFirst();
            }
            if (onMap && !pawn.CanReach(food, PathEndMode.OnCell, Danger.Deadly))
            {
                return "NoPath".Translate().CapitalizeFirst();
            }
            return null;
        }

        private static HandWorkOrder.Result Eat(Pawn pawn, Thing food)
        {
            if (food == null)
            {
                return default;
            }
            string label = food.def.ingestible != null && !food.def.ingestible.ingestCommandString.NullOrEmpty()
                ? (string)food.def.ingestible.ingestCommandString.Formatted(food.LabelShort)
                : (string)"ConsumeThing".Translate(food.LabelShort, food);
            string problem = EatProblem(pawn, food, onMap: true);
            int count = problem == null
                ? FoodUtility.GetMaxAmountToPickup(food, pawn, FoodUtility.WillIngestStackCountOf(pawn, food.def, FoodUtility.NutritionForEater(pawn, food)))
                : 0;
            if (problem != null || count <= 0)
            {
                return new HandWorkOrder.Result { text = label + ": " + (problem ?? "FoodNotSuitable".Translate().CapitalizeFirst()) };
            }
            food.SetForbidden(false);
            Job job = JobMaker.MakeJob(JobDefOf.Ingest, food);
            job.count = count;
            bool ordered = pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            return new HandWorkOrder.Result { text = label, ordered = ordered };
        }
    }
}
