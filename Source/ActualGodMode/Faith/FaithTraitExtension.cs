using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// Attach to any TraitDef (ours, vanilla via XML patch, or other mods') to change how a
    /// pawn with that trait relates to the god. Factors from all of a pawn's traits multiply,
    /// so combinations (e.g. Lazy + Zealot) work without special cases.
    /// </summary>
    public class FaithTraitExtension : DefModExtension
    {
        public List<FaithTraitDegree> degrees = new List<FaithTraitDegree>();
    }

    public class FaithTraitDegree
    {
        /// <summary>Trait degree this applies to (0 for single-degree traits).</summary>
        public int degree;

        public float decayFactor = 1f;
        public float gainFactor = 1f;
        public float hardshipFactor = 1f;
        public float patienceFactor = 1f;
        public float gratitudeFactor = 1f;
        public float requestFrequencyFactor = 1f;
        public float prayerOutputFactor = 1f;

        /// <summary>Chance to be converted by seeing the god act (miracles, answered prayers).</summary>
        public float conversionFactor = 1f;

        /// <summary>Chance to be converted by a believer's preaching. 0 = talk never works.</summary>
        public float conversionByTalkFactor = 1f;
    }

    public static class FaithTraitUtility
    {
        public static float Factor(Pawn pawn, Func<FaithTraitDegree, float> selector)
        {
            float factor = 1f;
            List<Trait> traits = pawn?.story?.traits?.allTraits;
            if (traits == null)
            {
                return factor;
            }
            for (int i = 0; i < traits.Count; i++)
            {
                FaithTraitExtension ext = traits[i].def.GetModExtension<FaithTraitExtension>();
                if (ext == null)
                {
                    continue;
                }
                foreach (FaithTraitDegree d in ext.degrees)
                {
                    if (d.degree == traits[i].Degree)
                    {
                        factor *= selector(d);
                    }
                }
            }
            return factor;
        }
    }
}
