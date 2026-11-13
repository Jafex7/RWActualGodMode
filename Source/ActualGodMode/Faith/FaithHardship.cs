using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ActualGodMode
{
    /// <summary>What counts as hardship, and the event hooks that report it.</summary>
    public static class FaithHardship
    {
        /// <summary>Ongoing hardship per day from the pawn's current condition, one entry per cause.</summary>
        public static void AddOngoingCauses(Pawn pawn, List<(string label, float perDay)> into)
        {
            FaithTuningDef t = FaithTuningDef.Get;
            HediffSet hediffs = pawn.health?.hediffSet;
            if (hediffs == null)
            {
                return;
            }

            Hediff malnutrition = hediffs.GetFirstHediffOfDef(HediffDefOf.Malnutrition);
            if (malnutrition != null)
            {
                into.Add(("AGM_Cause_Hunger".Translate(), t.malnutritionPerDay + t.malnutritionPerDayPerSeverity * malnutrition.Severity));
            }

            Hediff cold = hediffs.GetFirstHediffOfDef(HediffDefOf.Hypothermia);
            Hediff heat = hediffs.GetFirstHediffOfDef(HediffDefOf.Heatstroke);
            if ((cold != null && cold.Severity >= t.temperatureMinSeverity) || (heat != null && heat.Severity >= t.temperatureMinSeverity))
            {
                into.Add(("AGM_Cause_Temperature".Translate(), t.temperaturePerDay));
            }

            float sickness = 0f;
            List<Hediff> list = hediffs.hediffs;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].def.makesSickThought && list[i].Visible)
                {
                    sickness += t.sicknessPerDay;
                }
            }
            if (sickness > 0f)
            {
                into.Add(("AGM_Cause_Sickness".Translate(), Mathf.Min(sickness, t.sicknessMaxPerDay)));
            }

            Need_Mood mood = pawn.needs?.mood;
            if (mood != null && pawn.mindState?.mentalBreaker != null
                && mood.CurLevel < pawn.mindState.mentalBreaker.BreakThresholdMinor)
            {
                into.Add(("AGM_Cause_Misery".Translate(), t.miseryPerDay));
            }
        }

        public static bool IsBeliever(Pawn pawn) => pawn != null && pawn.IsColonist && pawn.Faith() != null;
    }

    [HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    public static class Patch_FaithOnMentalBreak
    {
        private static readonly AccessTools.FieldRef<MentalStateHandler, Pawn> PawnRef =
            AccessTools.FieldRefAccess<MentalStateHandler, Pawn>("pawn");

        public static void Postfix(MentalStateHandler __instance, bool __result, bool causedByMood)
        {
            Pawn pawn = PawnRef(__instance);
            if (__result && causedByMood && FaithHardship.IsBeliever(pawn))
            {
                pawn.Faith().Hardship(FaithTuningDef.Get.mentalBreak, "AGM_Cause_MentalBreak".Translate());
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_FaithOnColonistDeath
    {
        public static void Prefix(Pawn __instance, out bool __state)
        {
            __state = __instance.IsColonist && !__instance.Dead;
        }

        public static void Postfix(Pawn __instance, bool __state)
        {
            if (!__state || !__instance.Dead)
            {
                return;
            }
            MiracleTracker.Instance?.RecordEvent(MiracleTracker.Event_ColonistDied);
            FaithTuningDef t = FaithTuningDef.Get;
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn mourner in map.mapPawns.FreeColonistsSpawned)
                {
                    if (!FaithHardship.IsBeliever(mourner))
                    {
                        continue;
                    }
                    float amount = t.colonistDied;
                    if (mourner.relations != null && mourner.relations.OpinionOf(__instance) >= t.closeOpinion)
                    {
                        amount += t.colonistDiedCloseExtra;
                    }
                    mourner.Faith().Hardship(amount, "AGM_Cause_Death".Translate(__instance.LabelShort));
                }
            }
        }
    }

    /// <summary>Records that the colony survived a raid (for whispers and future unlocks).</summary>
    [HarmonyPatch(typeof(IncidentWorker_Raid), "TryExecuteWorker")]
    public static class Patch_RecordRaid
    {
        public static void Postfix(IncidentWorker_Raid __instance, IncidentParms parms, bool __result)
        {
            if (__result && __instance is IncidentWorker_RaidEnemy && parms?.target is Map map && map.IsPlayerHome)
            {
                MiracleTracker.Instance?.RecordEvent(MiracleTracker.Event_Raid);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    public static class Patch_FaithOnDowned
    {
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn> PawnRef =
            AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");

        public static void Postfix(Pawn_HealthTracker __instance)
        {
            Pawn pawn = PawnRef(__instance);
            if (pawn == null || !pawn.IsColonist)
            {
                return;
            }
            MiracleTracker.Instance?.RecordEvent(MiracleTracker.Event_ColonistDowned);
            if (FaithHardship.IsBeliever(pawn))
            {
                pawn.Faith().Hardship(FaithTuningDef.Get.downed, "AGM_Cause_Downed".Translate());
            }
        }
    }

    /// <summary>Eating food a miracle made gives the eater extra faith. Blessing survives stack splits.</summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    public static class Patch_BlessedFoodIngested
    {
        public static void Prefix(Thing __instance, out bool __state)
        {
            __state = MiracleTracker.Instance?.IsBlessed(__instance) ?? false;
        }

        public static void Postfix(Pawn ingester, bool __state)
        {
            if (__state && FaithHardship.IsBeliever(ingester))
            {
                ingester.Faith().Gain(FaithTuningDef.Get.blessedFoodGain, "AGM_Cause_BlessedMeal".Translate());
                FaithThoughts.Give(ingester, AGM_ThoughtDefOf.AGM_AteBlessedMeal);
            }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.SplitOff))]
    public static class Patch_BlessedFoodSplit
    {
        public static void Postfix(Thing __instance, Thing __result)
        {
            MiracleTracker tracker = MiracleTracker.Instance;
            if (tracker != null && __result != __instance && tracker.IsBlessed(__instance))
            {
                tracker.Bless(__result);
            }
        }
    }

    /// <summary>Blessed and ordinary food never merge into one stack (the blessing would spread or vanish).</summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.CanStackWith))]
    public static class Patch_BlessedFoodStacking
    {
        public static void Postfix(Thing __instance, Thing other, ref bool __result)
        {
            MiracleTracker tracker = MiracleTracker.Instance;
            if (__result && tracker != null && tracker.IsBlessed(__instance) != tracker.IsBlessed(other))
            {
                __result = false;
            }
        }
    }

    /// <summary>A meal cooked with any blessed ingredient is blessed (read before the ingredients are used up).</summary>
    [HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
    public static class Patch_BlessedFoodCooked
    {
        public static void Postfix(ref IEnumerable<Thing> __result, List<Thing> ingredients)
        {
            MiracleTracker tracker = MiracleTracker.Instance;
            if (tracker != null && ingredients != null && ingredients.Any(tracker.IsBlessed))
            {
                __result = BlessIngestibles(__result, tracker);
            }
        }

        private static IEnumerable<Thing> BlessIngestibles(IEnumerable<Thing> products, MiracleTracker tracker)
        {
            foreach (Thing product in products)
            {
                if (product.def.IsIngestible)
                {
                    tracker.Bless(product);
                }
                yield return product;
            }
        }
    }
}
