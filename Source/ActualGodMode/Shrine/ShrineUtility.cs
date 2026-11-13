using System.Collections.Generic;
using Verse;

namespace ActualGodMode
{
    public static class ShrineUtility
    {
        public static Thing ShrineOn(Map map)
        {
            if (map == null)
            {
                return null;
            }
            List<Thing> shrines = map.listerThings.ThingsOfDef(AGM_DefOf.AGM_Shrine);
            return shrines.Count > 0 ? shrines[0] : null;
        }

        public static bool AnyShrineExists()
        {
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                if (ShrineOn(maps[i]) != null)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>The god has one central Shrine per map.</summary>
    public class PlaceWorker_OneShrinePerMap : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            ThingDef shrine = AGM_DefOf.AGM_Shrine;
            if (HasOther(map, shrine, thingToIgnore)
                || HasOther(map, shrine.blueprintDef, thingToIgnore)
                || HasOther(map, shrine.frameDef, thingToIgnore))
            {
                return "AGM_OnlyOneShrine".Translate();
            }
            return true;
        }

        private static bool HasOther(Map map, ThingDef def, Thing ignore)
        {
            if (def == null)
            {
                return false;
            }
            foreach (Thing t in map.listerThings.ThingsOfDef(def))
            {
                if (t != ignore)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
