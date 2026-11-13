using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ActualGodMode
{
    public class OfferingBowlExtension : DefModExtension
    {
        /// <summary>Divine Power per 1 silver of market value offered.</summary>
        public float powerPerValue = 0.1f;
    }

    /// <summary>
    /// A storage building: pawns haul whatever its filter allows into it. The player offers the
    /// contents to the god (gizmo), turning their market value into Divine Power.
    /// </summary>
    public class Building_OfferingBowl : Building_Storage
    {
        private OfferingBowlExtension Ext => def.GetModExtension<OfferingBowlExtension>() ?? new OfferingBowlExtension();

        public List<Thing> Contents => slotGroup?.HeldThings?.ToList() ?? new List<Thing>();

        public float ContentsValue => Contents.Sum(t => t.MarketValue * t.stackCount);

        public float PowerFromContents => ContentsValue * Ext.powerPerValue;

        /// <summary>Consume everything in the bowl. Returns the power gained (capped by the god's maximum).</summary>
        public float Offer()
        {
            List<Thing> contents = Contents;
            if (contents.Count == 0)
            {
                return 0f;
            }
            DivinePower dp = DivinePower.Instance;
            float before = dp.Power;
            float power = PowerFromContents;
            foreach (Thing t in contents)
            {
                t.Destroy();
            }
            dp.Add(power);
            float gained = dp.Power - before;

            FleckMaker.ThrowLightningGlow(DrawPos, Map, 2f);
            for (int i = 0; i < 6; i++)
            {
                FleckMaker.ThrowMicroSparks(DrawPos, Map);
            }
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            string msg = gained < power - 0.05f
                ? "AGM_Offering_DoneCapped".Translate(gained.ToString("F1"), (power - gained).ToString("F1"))
                : "AGM_Offering_Done".Translate(gained.ToString("F1"));
            Messages.Message(msg, this, MessageTypeDefOf.PositiveEvent, historical: false);
            return gained;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
            {
                yield return g;
            }
            var offer = new Command_Action
            {
                defaultLabel = "AGM_Offering_Command".Translate(),
                defaultDesc = "AGM_Offering_CommandDesc".Translate(PowerFromContents.ToString("F1")),
                icon = def.uiIcon,
                action = () => Offer()
            };
            if (Contents.Count == 0)
            {
                offer.Disable("AGM_Offering_Empty".Translate());
            }
            yield return offer;
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string mine = "AGM_Offering_Inspect".Translate(ContentsValue.ToString("F0"), PowerFromContents.ToString("F1"));
            return s.NullOrEmpty() ? mine : s + "\n" + mine;
        }
    }
}
