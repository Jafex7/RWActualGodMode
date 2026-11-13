using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace ActualGodMode
{
    /// <summary>
    /// Smite: a real weather lightning strike (flash, bolt, thunder, flame explosion r1.9).
    /// What it means depends on who's standing there: see <see cref="WrathJudgement"/>.
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
            foreach (Pawn p in targets)
            {
                if (!p.Dead)
                {
                    punished |= WrathJudgement.Judge(p, "AGM_Deed_Smite_");
                }
            }

            WeatherEvent_LightningStrike strike = new WeatherEvent_LightningStrike(map, cell);
            map.weatherManager.eventHandler.AddEvent(strike);

            WrathJudgement.AffectWitnesses(cell, map, targets, punished);
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
