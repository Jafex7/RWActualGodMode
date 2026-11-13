using Verse;

namespace ActualGodMode
{
    public static class FaithUtility
    {
        public static Need_Faith Faith(this Pawn pawn)
        {
            return pawn?.needs?.TryGetNeed<Need_Faith>();
        }

        public static bool HasFaith(this Pawn pawn)
        {
            return pawn.Faith() != null;
        }
    }
}
