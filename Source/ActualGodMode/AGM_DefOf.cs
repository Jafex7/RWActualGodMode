using RimWorld;
using Verse;

namespace ActualGodMode
{
    [DefOf]
    public static class AGM_DefOf
    {
        public static NeedDef AGM_Faith;
        public static TimeAssignmentDef AGM_Worship;
        public static JobDef AGM_PrayAtShrine;
        public static ThingDef AGM_Shrine;

        static AGM_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AGM_DefOf));
        }
    }
}
