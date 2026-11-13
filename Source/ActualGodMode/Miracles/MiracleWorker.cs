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
                GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near);
            }
            DivineFlash(cell, map, 2f);
        }
    }
}
