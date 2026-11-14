using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    public abstract class MiracleWorker
    {
        public MiracleDef def;

        public virtual AcceptanceReport CanCastAt(IntVec3 cell, Map map, MiracleStats stats) => true;

        public abstract void Cast(IntVec3 cell, Map map, MiracleStats stats);

        /// <summary>One unit of a channelled cast at this cell (see <see cref="MiracleChannel"/>). False if nothing happened (not charged).</summary>
        public virtual bool ChannelUnit(IntVec3 cell, Map map, MiracleStats stats, MiracleChannelProps channel, bool first) => false;

        public virtual void DrawPreview(IntVec3 cell, Map map, MiracleStats stats)
        {
            if (stats.radius > 0f)
            {
                GenDraw.DrawRadiusRing(cell, stats.radius);
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

    /// <summary>Rain: puts out fires and wets the ground, on <c>amount</c> tiles (closest first; poured tile by tile in Hand of God mode).</summary>
    public class MiracleWorker_Water : MiracleWorker
    {
        public override AcceptanceReport CanCastAt(IntVec3 cell, Map map, MiracleStats stats)
        {
            return !cell.Filled(map);
        }

        public override void Cast(IntVec3 cell, Map map, MiracleStats stats)
        {
            foreach (IntVec3 c in Area(cell, map, stats))
            {
                Soak(c, map, 0.35f);
            }
        }

        /// <summary>The <c>amount</c> closest open tiles (a one-shot cast, e.g. from the toolbar).</summary>
        private static List<IntVec3> Area(IntVec3 cell, Map map, MiracleStats stats)
        {
            var cells = new List<IntVec3>();
            for (int i = 0; i < GenRadial.NumCellsInRadius(GenRadial.MaxRadialPatternRadius - 1f) && cells.Count < stats.amount; i++)
            {
                IntVec3 c = cell + GenRadial.RadialPattern[i];
                if (c.InBounds(map) && !c.Filled(map))
                {
                    cells.Add(c);
                }
            }
            return cells;
        }

        public override void DrawPreview(IntVec3 cell, Map map, MiracleStats stats)
        {
            GenDraw.DrawFieldEdges(Area(cell, map, stats), InfluenceUtility.InfluenceColor);
        }

        /// <summary>Channelled: a pinpoint of water on each tile the cursor crosses.</summary>
        public override bool ChannelUnit(IntVec3 cell, Map map, MiracleStats stats, MiracleChannelProps channel, bool first)
        {
            Soak(cell, map, 0.5f);
            return true;
        }

        private static void Soak(IntVec3 c, Map map, float puddleChance)
        {
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
                if (Rand.Chance(puddleChance))
                {
                    FilthMaker.TryMakeFilth(c, map, ThingDefOf.Filth_Water);
                }
                FleckMaker.Static(c, map, FleckDefOf.WaterSplash, Rand.Range(0.6f, 1.2f));
            }
        }
    }

    /// <summary>Spawns things (crops, logs...) at the target: poured one unit at a time, or all at once.</summary>
    public class MiracleWorker_SpawnThing : MiracleWorker
    {
        public override AcceptanceReport CanCastAt(IntVec3 cell, Map map, MiracleStats stats)
        {
            return cell.Standable(map);
        }

        public override void Cast(IntVec3 cell, Map map, MiracleStats stats)
        {
            if (def.channel != null)
            {
                // The whole charge at once (e.g. a one-shot cast).
                int units = MiracleChannel.BudgetOf(def);
                for (int i = 0; i < units; i++)
                {
                    ChannelUnit(cell, map, stats, def.channel, i == 0);
                }
                DivineFlash(cell, map, 2f);
                return;
            }
            RecordFeedDeed(stats.thingDef, cell, map);
            int remaining = stats.amount;
            while (remaining > 0)
            {
                Thing thing = ThingMaker.MakeThing(stats.thingDef);
                thing.stackCount = System.Math.Min(remaining, stats.thingDef.stackLimit);
                remaining -= thing.stackCount;
                if (thing.def.IsIngestible)
                {
                    MiracleTracker.Instance?.Bless(thing);
                }
                GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near);
            }
            DivineFlash(cell, map, 2f);
        }

        /// <summary>Channelled: a handful of one of the channel's things (raw crops, logs) at the cursor.</summary>
        public override bool ChannelUnit(IntVec3 cell, Map map, MiracleStats stats, MiracleChannelProps channel, bool first)
        {
            ThingDef def = channel.things.NullOrEmpty() ? stats.thingDef : channel.things.RandomElement();
            if (def == null)
            {
                return false;
            }
            if (first)
            {
                RecordFeedDeed(def, cell, map);
            }
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = Mathf.Clamp(channel.countPerUnit, 1, def.stackLimit);
            if (thing.def.IsIngestible)
            {
                MiracleTracker.Instance?.Bless(thing);
            }
            if (!GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near))
            {
                thing.Destroy();
                return false;
            }
            FleckMaker.ThrowMicroSparks(cell.ToVector3Shifted(), map);
            return true;
        }

        /// <summary>Feeding the hungry is a good deed (only when someone nearby actually needs it).</summary>
        private static void RecordFeedDeed(ThingDef def, IntVec3 cell, Map map)
        {
            if (!def.IsNutritionGivingIngestible)
            {
                return;
            }
            bool hungryNearby = map.mapPawns.FreeColonistsSpawned.Any(p =>
                p.Position.InHorDistOf(cell, 8f) && p.needs?.food != null && p.needs.food.CurCategory >= HungerCategory.Hungry);
            if (hungryNearby)
            {
                AlignmentTracker.Instance?.Deed(AlignmentTuningDef.Get.feedHungry, "AGM_Deed_FedHungry".Translate());
            }
        }
    }

    /// <summary>
    /// Mend: heals a single pawn's fresh injuries by stats.amount severity in total, most
    /// bleeding then most painful first. Doesn't tend (doctors still matter: a miracle tend
    /// would block a proper one), and doesn't touch scars, missing parts or disease.
    /// Poured onto one pawn (a unit = <c>countPerUnit</c> severity), so only what heals is paid.
    /// </summary>
    public class MiracleWorker_Heal : MiracleWorker
    {
        public static Pawn TargetAt(IntVec3 cell, Map map) => cell.GetFirstPawn(map);

        public static List<Hediff_Injury> HealableInjuries(Pawn pawn)
        {
            var result = new List<Hediff_Injury>();
            foreach (Hediff h in pawn.health.hediffSet.hediffs)
            {
                // A mended injury stays in the list (at 0) until the next health tick removes it.
                if (h is Hediff_Injury injury && !injury.IsPermanent() && injury.Severity > 0.001f)
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

        public override AcceptanceReport CanCastAt(IntVec3 cell, Map map, MiracleStats stats)
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

        public override void Cast(IntVec3 cell, Map map, MiracleStats stats)
        {
            Pawn pawn = TargetAt(cell, map);
            RecordHealDeed(pawn);
            float remaining = stats.amount;
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

        /// <summary>Channelled: mends a little more of the pawn's worst wounds.</summary>
        public override bool ChannelUnit(IntVec3 cell, Map map, MiracleStats stats, MiracleChannelProps channel, bool first)
        {
            Pawn pawn = TargetAt(cell, map);
            if (pawn == null)
            {
                return false;
            }
            float remaining = Mathf.Max(1, channel.countPerUnit);
            bool healed = false;
            foreach (Hediff_Injury injury in HealableInjuries(pawn))
            {
                if (remaining <= 0f)
                {
                    break;
                }
                float amount = Mathf.Min(remaining, injury.Severity);
                injury.Heal(amount);
                remaining -= amount;
                healed = true;
            }
            if (!healed)
            {
                return false;
            }
            if (first)
            {
                RecordHealDeed(pawn);
                FleckMaker.ThrowMetaIcon(pawn.Position, map, FleckDefOf.HealingCross);
            }
            FleckMaker.ThrowMicroSparks(pawn.DrawPos, map);
            return true;
        }

        /// <summary>Healing is good; healing those who aren't yours (enemies, prisoners, visitors) is mercy.</summary>
        private static void RecordHealDeed(Pawn pawn)
        {
            AlignmentTracker alignment = AlignmentTracker.Instance;
            AlignmentTuningDef t = AlignmentTuningDef.Get;
            if (alignment == null)
            {
                return;
            }
            if (pawn.RaceProps.Animal)
            {
                alignment.Deed(t.healAnimal, "AGM_Deed_HealAnimal".Translate(pawn.LabelShort));
            }
            else if (pawn.IsColonist)
            {
                alignment.Deed(t.healColonist, "AGM_Deed_Heal".Translate(pawn.LabelShort));
            }
            else
            {
                alignment.Deed(t.healMercy, "AGM_Deed_Mercy".Translate(pawn.LabelShort));
            }
        }

        public override void DrawPreview(IntVec3 cell, Map map, MiracleStats stats)
        {
            Pawn pawn = TargetAt(cell, map);
            if (pawn != null)
            {
                GenDraw.DrawFieldEdges(new List<IntVec3> { pawn.Position }, InfluenceUtility.InfluenceColor);
            }
        }
    }
}
