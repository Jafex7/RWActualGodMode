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
    /// Smite: a pinpoint bolt from the sky (flash, bolt, thunder) onto one pawn, without
    /// setting fires: a smite that downs an unarmored person; the colony's own only get a sting.
    /// What it means depends on who's standing there: see <see cref="WrathJudgement"/>.
    /// A charge holds <c>amount</c> bolts, one per click (each judged on its own).
    /// </summary>
    public class MiracleWorker_Lightning : MiracleWorker
    {
        private LightningExtension Props => def.GetModExtension<LightningExtension>() ?? LightningExtension.Default;

        /// <summary>The colony's own (colonists, slaves, animals, prisoners): a stinging punishment, not a smite.</summary>
        public static bool IsOwn(Pawn p) => p.Faction == Faction.OfPlayer || p.IsPrisonerOfColony;

        /// <summary>
        /// The damage a bolt deals this pawn, before armor: the full smite, or the light punishment for
        /// the colony's own; fire resistance (genes, hediffs) takes part of it off.
        /// </summary>
        public float DamageTo(Pawn p) => (IsOwn(p) ? Props.ownDamage : Props.damage) * ResistFactor(p);

        /// <summary>1 normally; down to <c>fireResistedFactor</c> for a pawn immune to fire.</summary>
        public float ResistFactor(Pawn p)
        {
            float worst = 1f;
            foreach (DamageDef fire in new[] { DamageDefOf.Flame, DamageDefOf.Burn })
            {
                var probe = new DamageInfo(fire, 1f);
                float f = p.health.FactorForDamage(probe);
                if (ModsConfig.BiotechActive && p.genes != null)
                {
                    f *= p.genes.FactorForDamage(probe);
                }
                worst = Mathf.Min(worst, f);
            }
            return Mathf.Lerp(Props.fireResistedFactor, 1f, Mathf.Clamp01(worst));
        }

        /// <summary>
        /// Pinpoint: the one pawn drawn closest to the cell's centre (within <c>targetReach</c>, so a pawn
        /// walking between cells can still be hit), or nobody.
        /// </summary>
        public Pawn TargetAt(IntVec3 cell, Map map)
        {
            Vector3 center = cell.ToVector3Shifted();
            Pawn best = null;
            float bestDist = Props.targetReach;
            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(new TargetInfo(cell, map)).Append(cell))
            {
                if (!c.InBounds(map))
                {
                    continue;
                }
                foreach (Thing t in c.GetThingList(map))
                {
                    if (t is Pawn p && !p.Dead)
                    {
                        Vector3 d = p.DrawPos - center;
                        float dist = new Vector2(d.x, d.z).magnitude;
                        if (dist <= bestDist)
                        {
                            best = p;
                            bestDist = dist;
                        }
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// The bolt's damage, in several hits on the outer body (never straight into the brain or heart).
        /// The colony's own take one light hit on the torso that never scars. Mechs are also hit by an EMP.
        /// </summary>
        public void Smite(Pawn p)
        {
            LightningExtension props = Props;
            DamageDef damageDef = props.damageDef ?? DamageDefOf.Burn;
            float total = DamageTo(p);
            if (IsOwn(p))
            {
                var before = new HashSet<Hediff>(p.health.hediffSet.hediffs);
                var dinfo = new DamageInfo(damageDef, total, props.armorPenetration, hitPart: p.RaceProps.body.corePart);
                p.TakeDamage(dinfo);
                foreach (Hediff h in p.health.hediffSet.hediffs)
                {
                    HediffComp_GetsPermanent permanent = before.Contains(h) ? null : h.TryGetComp<HediffComp_GetsPermanent>();
                    if (permanent != null)
                    {
                        permanent.isPermanentInt = false;
                        permanent.permanentDamageThreshold = 9999f;
                    }
                }
            }
            else
            {
                int hits = Mathf.Max(1, props.hits);
                float dealt = 0f;
                // Vanilla may kill a non-colony pawn the moment damage downs them (often 50%+): a smite
                // is meant to bring them down, not finish them, so it uses its own (low) chance.
                float vanillaDeathChance = p.health.overrideDeathOnDownedChance;
                if (props.deathOnDownedChance >= 0f && !p.RaceProps.IsMechanoid)
                {
                    p.health.overrideDeathOnDownedChance = props.deathOnDownedChance;
                }
                try
                {
                    for (int i = 0; i < hits && !p.Dead; i++)
                    {
                        var dinfo = new DamageInfo(damageDef, total / hits, props.armorPenetration);
                        dinfo.SetBodyRegion(BodyPartHeight.Undefined, BodyPartDepth.Outside);
                        dealt += p.TakeDamage(dinfo).totalDamageDealt;
                    }
                }
                finally
                {
                    p.health.overrideDeathOnDownedChance = vanillaDeathChance;
                }
                // The shock: pain on top of the burns, as strong as the share of the bolt that got through.
                if (!p.Dead && p.RaceProps.IsFlesh && props.damage > 0f && dealt > 0f)
                {
                    Hediff shock = p.health.GetOrAddHediff(AGM_AlignmentDefOf.AGM_LightningShock);
                    shock.Severity = Mathf.Max(shock.Severity, Mathf.Clamp01(dealt / props.damage));
                }
            }
            if (!p.Dead && p.RaceProps.IsMechanoid && props.empDamage > 0f)
            {
                p.TakeDamage(new DamageInfo(DamageDefOf.EMP, props.empDamage));
            }
        }

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
            Pawn target = TargetAt(cell, map);
            var targets = new List<Pawn>();
            if (target != null)
            {
                targets.Add(target);
            }
            bool punished = false;
            foreach (Pawn p in targets)
            {
                if (!p.Dead)
                {
                    punished |= WrathJudgement.Judge(p, "AGM_Deed_Smite_");
                }
            }

            map.weatherManager.eventHandler.AddEvent(new WeatherEvent_DivineBolt(map, target?.Position ?? cell));
            if (target != null && target.Spawned && !target.Dead)
            {
                Smite(target);
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
            Pawn target = TargetAt(cell, map);
            if (target != null)
            {
                GenDraw.DrawTargetHighlight(target);
            }
            else
            {
                GenDraw.DrawFieldEdges(new List<IntVec3> { cell });
            }
        }
    }

    /// <summary>Lightning's damage numbers (a mod extension on the miracle def).</summary>
    public class LightningExtension : DefModExtension
    {
        public static readonly LightningExtension Default = new LightningExtension();

        /// <summary>Total damage to anyone not the colony's own, split into <c>hits</c> on the outer body.</summary>
        public float damage = 50f;
        public int hits = 5;
        /// <summary>The colony's own: one light hit on the torso, never a scar.</summary>
        public float ownDamage = 10f;
        public float armorPenetration = 0.2f;
        /// <summary>Damage factor for a fire-immune pawn (partly fire-resistant pawns fall in between).</summary>
        public float fireResistedFactor = 0.5f;
        /// <summary>EMP damage added on mechanoids (stuns them).</summary>
        public float empDamage = 50f;
        /// <summary>Chance a non-colony person downed by the bolt dies on the spot (-1 = vanilla's, often 50%+).</summary>
        public float deathOnDownedChance = 0.1f;
        /// <summary>How far from the cell's centre (in cells) a pawn can be drawn and still be the target.</summary>
        public float targetReach = 0.9f;
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
        public static HediffDef AGM_LightningShock;
        public static MiracleDef AGM_Miracle_Lightning;

        static AGM_AlignmentDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AGM_AlignmentDefOf));
        }
    }
}
