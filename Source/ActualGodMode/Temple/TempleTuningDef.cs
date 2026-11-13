using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    /// <summary>One temple tier: what the room needs, and what the shrine gets.</summary>
    public class TempleTier
    {
        [MustTranslate] public string label;
        public float minImpressiveness;
        public float minHolyFurniture;
        public int prayerSlots = 2;
        public float powerCapBonus;
    }

    /// <summary>Temple tiers and holy furniture rules. All values in AGM_TempleTuning.xml.</summary>
    public class TempleTuningDef : Def
    {
        /// <summary>Tier 0 is "outdoors / no temple"; higher tiers in increasing order.</summary>
        public List<TempleTier> tiers = new List<TempleTier>();

        private static TempleTuningDef cached;

        public static TempleTuningDef Get => cached ??= DefDatabase<TempleTuningDef>.GetNamed("AGM_TempleTuning");

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors())
            {
                yield return e;
            }
            if (tiers.Count < 1)
            {
                yield return "needs at least the outdoors tier.";
            }
        }
    }

    /// <summary>Marks a building as holy furniture for temples. Vanilla/DLC defs get it via XML patch.</summary>
    public class HolyFurnitureExtension : DefModExtension
    {
        /// <summary>How many "holy pieces" it counts as (a grand sculpture counts double).</summary>
        public float holiness = 1f;
    }

    /// <summary>The result of judging the room around a shrine.</summary>
    public struct TempleInfo
    {
        public int tierIndex;
        public bool enclosed;
        public bool conflictingUse;
        public float impressiveness;
        public float holyFurniture;
        public Room room;

        public TempleTier Tier => TempleTuningDef.Get.tiers[tierIndex];

        public TempleTier NextTier =>
            tierIndex + 1 < TempleTuningDef.Get.tiers.Count ? TempleTuningDef.Get.tiers[tierIndex + 1] : null;
    }

    public static class TempleUtility
    {
        public static TempleInfo Evaluate(Thing shrine)
        {
            var info = new TempleInfo();
            Room room = shrine.Spawned ? shrine.GetRoom() : null;
            info.room = room;
            if (room == null || room.PsychologicallyOutdoors || room.TouchesMapEdge || !room.ProperRoom)
            {
                return info;
            }
            info.enclosed = true;
            info.impressiveness = room.GetStat(RoomStatDefOf.Impressiveness);

            foreach (Thing t in room.ContainedAndAdjacentThings)
            {
                if (!room.ContainsCell(t.Position))
                {
                    continue;
                }
                if (t is Building_Bed || t is Building_WorkTable)
                {
                    info.conflictingUse = true;
                }
                HolyFurnitureExtension holy = t.def.GetModExtension<HolyFurnitureExtension>();
                if (holy != null)
                {
                    info.holyFurniture += holy.holiness;
                }
            }
            if (info.conflictingUse)
            {
                return info; // a bedroom or workshop isn't a temple
            }

            List<TempleTier> tiers = TempleTuningDef.Get.tiers;
            for (int i = tiers.Count - 1; i >= 1; i--)
            {
                if (info.impressiveness >= tiers[i].minImpressiveness && info.holyFurniture >= tiers[i].minHolyFurniture)
                {
                    info.tierIndex = i;
                    break;
                }
            }
            return info;
        }
    }

    /// <summary>A room containing the shrine is a temple (shows in the room stats readout).</summary>
    public class RoomRoleWorker_Temple : RoomRoleWorker
    {
        public override float GetScore(Room room)
        {
            return room.ContainsThing(AGM_DefOf.AGM_Shrine) ? 100000f : 0f;
        }
    }
}
