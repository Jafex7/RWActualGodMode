using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ActualGodMode
{
    /// <summary>
    /// Smite: a bolt from the sky (flash, bolt, thunder) that burns everyone in its radius, without
    /// setting fires. Enemies take a multiple of the damage (<see cref="LightningExtension"/>).
    /// What it means depends on who's standing there: see <see cref="WrathJudgement"/>.
    /// A charge holds <c>amount</c> bolts, one per click (each judged on its own).
    /// </summary>
    public class MiracleWorker_Lightning : MiracleWorker
    {
        private LightningExtension Props => def.GetModExtension<LightningExtension>() ?? LightningExtension.Default;

        /// <summary>The burn a bolt deals to this pawn (enemies of the colony take more).</summary>
        public float DamageTo(Pawn p) =>
            Props.damage * (p.HostileTo(Faction.OfPlayer) ? Props.enemyDamageFactor : 1f);

        public override AcceptanceReport CanCastAt(IntVec3 cell, Map map, MiracleStats stats)
        {
            RoofDef roof = map.roofGrid.RoofAt(cell);
            if (roof != null && roof.isThickRoof)
            {
                return "AGM_Lightning_ThickRoof".Translate();
            }
            return true;
        }

        public override void Cast(IntVec3 cell, Map map, MiracleStats stats)
        {
            float radius = stats.radius > 0f ? stats.radius : 1.9f;
            List<Pawn> targets = GenRadial.RadialDistinctThingsAround(cell, map, radius, useCenter: true).OfType<Pawn>().ToList();
            bool punished = false;
            foreach (Pawn p in targets)
            {
                if (!p.Dead)
                {
                    punished |= WrathJudgement.Judge(p, "AGM_Deed_Smite_");
                }
            }

            map.weatherManager.eventHandler.AddEvent(new WeatherEvent_DivineBolt(map, cell));
            DamageDef damageDef = Props.damageDef ?? DamageDefOf.Burn;
            foreach (Pawn p in targets)
            {
                if (p.Spawned && !p.Dead)
                {
                    p.TakeDamage(new DamageInfo(damageDef, DamageTo(p), Props.armorPenetration));
                }
            }

            WrathJudgement.AffectWitnesses(cell, map, targets, punished);
        }

        public override bool ChannelUnit(IntVec3 cell, Map map, MiracleStats stats, MiracleChannelProps channel, bool first)
        {
            Cast(cell, map, stats);
            return true;
        }

        public override void DrawPreview(IntVec3 cell, Map map, MiracleStats stats)
        {
            GenDraw.DrawRadiusRing(cell, stats.radius > 0f ? stats.radius : 1.9f);
        }
    }

    /// <summary>Lightning's damage numbers (a mod extension on the miracle def).</summary>
    public class LightningExtension : DefModExtension
    {
        public static readonly LightningExtension Default = new LightningExtension();

        public float damage = 10f;
        public float enemyDamageFactor = 3f;
        public float armorPenetration = -1f;
        public DamageDef damageDef;
    }

    /// <summary>
    /// The look and sound of a vanilla lightning strike (sky flash, bolt, thunder, smoke and sparks)
    /// without its flame explosion: the miracle deals its own damage and starts no fires.
    /// </summary>
    [StaticConstructorOnStartup]
    public class WeatherEvent_DivineBolt : WeatherEvent_LightningFlash
    {
        private static readonly Material LightningMat = MatLoader.LoadMat("Weather/LightningBolt");

        private readonly IntVec3 strikeLoc;
        private Mesh boltMesh;

        public WeatherEvent_DivineBolt(Map map, IntVec3 strikeLoc) : base(map)
        {
            this.strikeLoc = strikeLoc;
        }

        public override void FireEvent()
        {
            SoundDefOf.Thunder_OffMap.PlayOneShotOnCamera(map);
            boltMesh = LightningBoltMeshPool.RandomBoltMesh;
            if (!strikeLoc.Fogged(map))
            {
                Vector3 loc = strikeLoc.ToVector3Shifted();
                FleckMaker.Static(loc, map, FleckDefOf.ExplosionFlash, 6f);
                for (int i = 0; i < 4; i++)
                {
                    FleckMaker.ThrowSmoke(loc, map, 1.5f);
                    FleckMaker.ThrowMicroSparks(loc, map);
                    FleckMaker.ThrowLightningGlow(loc, map, 1.5f);
                }
            }
            SoundDefOf.Thunder_OnMap.PlayOneShot(SoundInfo.InMap(new TargetInfo(strikeLoc, map)));
        }

        public override void WeatherEventDraw()
        {
            Graphics.DrawMesh(boltMesh, strikeLoc.ToVector3ShiftedWithAltitude(AltitudeLayer.Weather), Quaternion.identity,
                FadedMaterialPool.FadedVersionOf(LightningMat, LightningBrightness), 0);
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
