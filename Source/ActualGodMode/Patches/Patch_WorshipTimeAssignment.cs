using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// Vanilla JobGiver_GetRest and JobGiver_Work throw NotImplementedException for unknown
    /// time assignments. While they evaluate a pawn on the Worship block, we make the timetable
    /// report "Recreation" instead, so vanilla logic runs unchanged (work priority 2, sleep only
    /// when exhausted). Joy is already handled by Worship's allowJoy=false.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_WorshipTimeAssignment
    {
        [ThreadStatic]
        private static TimeAssignmentDef substitute;

        private static void Begin(Pawn pawn)
        {
            if (pawn?.timetable != null && pawn.timetable.CurrentAssignment == AGM_DefOf.AGM_Worship)
            {
                substitute = TimeAssignmentDefOf.Joy;
            }
        }

        private static void End()
        {
            substitute = null;
        }

        [HarmonyPatch(typeof(JobGiver_GetRest), nameof(JobGiver_GetRest.GetPriority))]
        [HarmonyPrefix]
        public static void GetRest_Prefix(Pawn pawn) => Begin(pawn);

        [HarmonyPatch(typeof(JobGiver_GetRest), nameof(JobGiver_GetRest.GetPriority))]
        [HarmonyFinalizer]
        public static void GetRest_Finalizer() => End();

        [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.GetPriority))]
        [HarmonyPrefix]
        public static void Work_Prefix(Pawn pawn) => Begin(pawn);

        [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.GetPriority))]
        [HarmonyFinalizer]
        public static void Work_Finalizer() => End();

        [HarmonyPatch(typeof(Pawn_TimetableTracker), nameof(Pawn_TimetableTracker.CurrentAssignment), MethodType.Getter)]
        [HarmonyPostfix]
        public static void CurrentAssignment_Postfix(ref TimeAssignmentDef __result)
        {
            if (substitute != null && __result == AGM_DefOf.AGM_Worship)
            {
                __result = substitute;
            }
        }
    }

    /// <summary>Adds the Worship button to the schedule tab's assignment selector.</summary>
    [HarmonyPatch(typeof(TimeAssignmentSelector), nameof(TimeAssignmentSelector.DrawTimeAssignmentSelectorGrid))]
    public static class Patch_TimeAssignmentSelector
    {
        private static readonly Action<Rect, TimeAssignmentDef> DrawSelectorFor =
            AccessTools.MethodDelegate<Action<Rect, TimeAssignmentDef>>(
                AccessTools.Method(typeof(TimeAssignmentSelector), "DrawTimeAssignmentSelectorFor"));

        public static void Postfix(Rect rect)
        {
            // Mirror vanilla layout: one row of half-width buttons, Royalty adds Meditate as the 5th.
            rect.yMax -= 2f;
            Rect button = rect;
            button.xMax = button.center.x;
            button.yMax = button.center.y;
            int index = ModsConfig.RoyaltyActive ? 5 : 4;
            button.x += button.width * index;
            DrawSelectorFor(button, AGM_DefOf.AGM_Worship);
        }
    }
}
