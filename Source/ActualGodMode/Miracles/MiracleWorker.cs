using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    public abstract class MiracleWorker
    {
        public MiracleDef def;

        public virtual AcceptanceReport CanCastAt(IntVec3 cell, Map map, MiracleLevel level) => true;

        public abstract void Cast(IntVec3 cell, Map map, MiracleLevel level);

        public virtual void DrawPreview(IntVec3 cell, Map map, MiracleLevel level)
        {
            if (level.radius > 0f)
            {
                GenDraw.DrawRadiusRing(cell, level.radius);
            }
        }

        protected static void DivineFlash(IntVec3 cell, Map map, float size)
        {
            FleckMaker.ThrowLightningGlow(cell.ToVector3Shifted(), map, size);
            for (int i = 0; i < 4; i++)
            {
                FleckMaker.ThrowMicroSparks(cell.ToVector3Shifted(), map);
            }
        }
    }

    /// <summary>Rain on an area: puts out fires and wets the ground.</summary>
    public class MiracleWorker_Water : MiracleWorker
    {
        public override AcceptanceReport CanCastAt(IntVec3 cell, Map map, MiracleLevel level)
        {
            return !cell.Filled(map);
        }

        public override void Cast(IntVec3 cell, Map map, MiracleLevel level)
        {
            foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, level.radius, useCenter: true))
            {
                if (!c.InBounds(map))
                {
                    continue;
                }
                List<Thing> things = c.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    if (things[i] is Fire)
                    {
                        things[i].Destroy();
                    }
                }
                if (!c.Filled(map))
                {
                    if (Rand.Chance(0.35f))
                    {
                        FilthMaker.TryMakeFilth(c, map, ThingDefOf.Filth_Water);
                    }
                    FleckMaker.Static(c, map, FleckDefOf.WaterSplash, Rand.Range(0.6f, 1.2f));
                }
            }
        }
    }

    /// <summary>Spawns a stack of things (food, wood...) at the target.</summary>
    public class MiracleWorker_SpawnThing : MiracleWorker
    {
        public override AcceptanceReport CanCastAt(IntVec3 cell, Map map, MiracleLevel level)
        {
            return cell.Standable(map);
        }

        public override void Cast(IntVec3 cell, Map map, MiracleLevel level)
        {
            int remaining = level.amount;
            while (remaining > 0)
            {
                Thing thing = ThingMaker.MakeThing(level.thingDef);
                thing.stackCount = System.Math.Min(remaining, level.thingDef.stackLimit);
                remaining -= thing.stackCount;
                if (thing.def.IsIngestible)
                {
                    MiracleTracker.Instance?.Bless(thing);
                }
                GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near);
            }
            DivineFlash(cell, map, 2f);
        }
    }

    /// <summary>
    /// Mend: heals a single pawn's fresh injuries by level.amount severity in total, most
    /// bleeding then most painful first. Doesn't tend (doctors still matter: a miracle tend
    /// would block a proper one), and doesn't touch scars, missing parts or disease.
    /// </summary>
    public class MiracleWorker_Heal : MiracleWorker
    {
        public static Pawn TargetAt(IntVec3 cell, Map map) => cell.GetFirstPawn(map);

        public static List<Hediff_Injury> HealableInjuries(Pawn pawn)
        {
            var result = new List<Hediff_Injury>();
            foreach (Hediff h in pawn.health.hediffSet.hediffs)
            {
                if (h is Hediff_Injury injury && !injury.IsPermanent())
                {
                    result.Add(injury);
                }
            }
            result.Sort((a, b) =>
            {
                int byBleeding = b.BleedRate.CompareTo(a.BleedRate);
                return byBleeding != 0 ? byBleeding : b.PainOffset.CompareTo(a.PainOffset);
            });
            return result;
        }

        public override AcceptanceReport CanCastAt(IntVec3 cell, Map map, MiracleLevel level)
        {
            Pawn pawn = TargetAt(cell, map);
            if (pawn == null)
            {
                return "AGM_Heal_NoPawn".Translate();
            }
            if (HealableInjuries(pawn).Count == 0)
            {
                return "AGM_Heal_NothingToHeal".Translate(pawn.LabelShort);
            }
            return true;
        }

        public override void Cast(IntVec3 cell, Map map, MiracleLevel level)
        {
            Pawn pawn = TargetAt(cell, map);
            float remaining = level.amount;
            foreach (Hediff_Injury injury in HealableInjuries(pawn))
            {
                if (remaining <= 0f)
                {
                    break;
                }
                float amount = UnityEngine.Mathf.Min(remaining, injury.Severity);
                injury.Heal(amount);
                remaining -= amount;
            }
            FleckMaker.ThrowMetaIcon(pawn.Position, map, FleckDefOf.HealingCross);
            DivineFlash(pawn.Position, map, 1.5f);
        }

        public override void DrawPreview(IntVec3 cell, Map map, MiracleLevel level)
        {
            Pawn pawn = TargetAt(cell, map);
            if (pawn != null)
            {
                GenDraw.DrawFieldEdges(new List<IntVec3> { pawn.Position }, InfluenceUtility.InfluenceColor);
            }
        }
    }
}
