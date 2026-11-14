using RimWorld;

namespace ActualGodMode
{
    /// <summary>Power tracks that code reads (Hand and Influence branches).</summary>
    [DefOf]
    public static class AGM_ProgressionDefOf
    {
        public static PowerTrackDef AGM_Track_Strength;
        public static PowerTrackDef AGM_Track_LightTouch;
        public static PowerTrackDef AGM_Track_LiftLiving;
        public static PowerTrackDef AGM_Track_LiftEnemies;

        static AGM_ProgressionDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AGM_ProgressionDefOf));
        }
    }
}
