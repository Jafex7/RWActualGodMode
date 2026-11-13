using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ActualGodMode
{
    /// <summary>
    /// The Hand: the god picks up a pawn or an item, carries it and sets it down. The held
    /// thing is despawned into this map component's container (so it still ticks and counts as
    /// on the map), drawn under the cursor each frame, and dropped on release.
    ///
    /// Costs power to pick up and per hour held (by mass, more for living creatures); lift
    /// capacity grows with believers. Leaving the god's influence, or running out of power,
    /// drops what is held. Carrying a downed person to a bed is a small good deed.
    /// </summary>
    public class GodHand : MapComponent, IThingHolder
    {
        private ThingOwner<Thing> held;
        private IntVec3 lastCell = IntVec3.Invalid;
        private bool takenFromBed;

        /// <summary>True while the mouse is held down from the grab: releasing it places the thing.</summary>
        public bool dragging;

        /// <summary>When each pawn last earned the carry-to-bed deed (not saved: a soft anti-farming guard).</summary>
        private static readonly Dictionary<Pawn, int> lastBedDeedTick = new Dictionary<Pawn, int>();

        public GodHand(Map map) : base(map)
        {
            held = new ThingOwner<Thing>(this);
        }

        public static GodHand On(Map map) => map?.GetComponent<GodHand>();

        public Thing Held => held.Count > 0 ? held[0] : null;
        public bool Holding => held.Count > 0;
        public IntVec3 LastCell => lastCell;

        private static HandTuningDef T => HandTuningDef.Get;

        // ---- Rules -------------------------------------------------------------------------

        /// <summary>How much the god can lift (kg): grows with believers.</summary>
        public static float Capacity => T.baseLiftKg + T.liftPerBelieverKg * (BelieverTracker.Instance?.LivingCount ?? 0);

        public static float UnitMass(Thing t) =>
            t is Pawn p ? p.GetStatValue(StatDefOf.Mass) + MassUtility.GearAndInventoryMass(p) : t.GetStatValue(StatDefOf.Mass);

        public static float MassOf(Thing t) => t is Pawn ? UnitMass(t) : UnitMass(t) * t.stackCount;

        public static float PickupCost(Thing t, float mass) =>
            t is Pawn ? T.pawnPickupCost + T.pawnPickupCostPerKg * mass : T.itemPickupCost + T.itemPickupCostPerKg * mass;

        public static float HoldCostPerHour(Thing t, float mass) =>
            (t is Pawn ? T.pawnHoldCostPerKgPerHour : T.itemHoldCostPerKgPerHour) * mass;

        /// <summary>Pawns and haulable items. No buildings, plants or filth.</summary>
        public static bool IsGrabbable(Thing t) =>
            t != null && t.Spawned && !t.Destroyed
            && (t is Pawn || (t.def.category == ThingCategory.Item && t.def.EverHaulable));

        /// <summary>The thing the hand would take at this cell: a pawn first, else the top item.</summary>
        public static Thing GrabbableAt(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map))
            {
                return null;
            }
            List<Thing> things = cell.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (things[i] is Pawn && IsGrabbable(things[i]))
                {
                    return things[i];
                }
            }
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (IsGrabbable(things[i]))
                {
                    return things[i];
                }
            }
            return null;
        }

        /// <summary>How many of the thing the hand would take (a heavy stack is taken in part).</summary>
        public static int LiftableCount(Thing t)
        {
            if (t is Pawn)
            {
                return UnitMass(t) <= Capacity ? 1 : 0;
            }
            float unit = UnitMass(t);
            return unit <= 0.0001f ? t.stackCount : Mathf.Min(t.stackCount, Mathf.FloorToInt(Capacity / unit));
        }

        public AcceptanceReport CanGrab(Thing t)
        {
            if (Holding || !IsGrabbable(t))
            {
                return false;
            }
            if (!InfluenceUtility.InInfluence(t.Position, t.Map))
            {
                return "AGM_Hand_OutOfInfluence".Translate();
            }
            int count = LiftableCount(t);
            if (count <= 0)
            {
                return "AGM_Hand_TooHeavy".Translate(t.LabelShort, UnitMass(t).ToString("F0"), Capacity.ToString("F0"));
            }
            float cost = PickupCost(t, t is Pawn ? UnitMass(t) : UnitMass(t) * count);
            if (!DivinePower.Instance.CanAfford(cost))
            {
                return "AGM_NotEnoughPower".Translate(cost.ToString("F1"));
            }
            return true;
        }

        // ---- Actions -----------------------------------------------------------------------

        public bool TryGrab(Thing t, bool drag)
        {
            AcceptanceReport report = CanGrab(t);
            if (!report.Accepted)
            {
                if (!report.Reason.NullOrEmpty())
                {
                    Messages.Message(report.Reason, MessageTypeDefOf.RejectInput, historical: false);
                }
                return false;
            }
            int count = LiftableCount(t);
            float mass = t is Pawn ? UnitMass(t) : UnitMass(t) * count;
            DivinePower.Instance.TrySpend(PickupCost(t, mass));

            lastCell = t.Position;
            takenFromBed = t is Pawn p && p.InBed();
            Find.Selector.Deselect(t);
            Thing taken;
            if (t is Pawn)
            {
                t.DeSpawn();
                taken = t;
            }
            else
            {
                taken = t.SplitOff(count); // the whole stack despawns, a part splits off unspawned
            }
            if (!held.TryAdd(taken, canMergeWithExistingStacks: false))
            {
                Log.Error("[Actual God Mode] The hand failed to hold " + taken.ToStringSafe() + "; putting it back.");
                GenPlace.TryPlaceThing(taken, lastCell, map, ThingPlaceMode.Near);
                return false;
            }
            dragging = drag;
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            return true;
        }

        /// <summary>The hand moved over this cell. Outside the influence the held thing slips free.</summary>
        public void MoveTo(IntVec3 cell)
        {
            if (!Holding)
            {
                return;
            }
            if (cell.InBounds(map) && InfluenceUtility.InInfluence(cell, map))
            {
                lastCell = cell;
            }
            else
            {
                Thing t = Held;
                Place(lastCell);
                Messages.Message("AGM_Hand_Slipped".Translate(t.LabelShort), t, MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        /// <summary>Set the held thing down at the cell (or the last cell inside the influence).</summary>
        public bool Place(IntVec3 cell)
        {
            Thing t = Held;
            if (t == null)
            {
                return false;
            }
            dragging = false;
            IntVec3 target = cell.InBounds(map) && InfluenceUtility.InInfluence(cell, map) ? cell : lastCell;
            if (!target.IsValid || !target.InBounds(map))
            {
                target = map.Center;
            }
            if (!held.TryDrop(t, target, map, ThingPlaceMode.Near, out Thing result))
            {
                // Nowhere near to put it: try anywhere reachable-ish rather than lose it.
                if (!held.TryDrop(t, CellFinder.RandomClosewalkCellNear(target, map, 12), map, ThingPlaceMode.Near, out result))
                {
                    Log.Warning("[Actual God Mode] The hand could not set down " + t.ToStringSafe());
                    return false;
                }
            }
            if (result is Pawn pawn)
            {
                pawn.Rotation = Rot4.South;
                TryTuckIntoBed(pawn);
            }
            return true;
        }

        /// <summary>Set down on a bed: a downed person is laid in it (and that's a kindness).</summary>
        private void TryTuckIntoBed(Pawn pawn)
        {
            Building_Bed bed = pawn.Position.GetFirstThing<Building_Bed>(map);
            if (bed == null || !RestUtility.CanUseBedNow(bed, pawn, checkSocialProperness: false))
            {
                return;
            }
            bool wasDowned = pawn.Downed;
            pawn.jobs.Notify_TuckedIntoBed(bed);
            pawn.mindState.Notify_TuckedIntoBed();
            if (!wasDowned || takenFromBed || !pawn.RaceProps.Humanlike || pawn.HostileTo(Faction.OfPlayer))
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            if (lastBedDeedTick.TryGetValue(pawn, out int last) && now - last < T.carryToBedCooldownHours * GenDate.TicksPerHour)
            {
                return;
            }
            lastBedDeedTick[pawn] = now;
            AlignmentTracker.Instance?.Deed(T.carryToBedDeed, "AGM_Deed_CarriedToBed".Translate(pawn.LabelShort));
        }

        /// <summary>Spend the holding cost for a number of ticks. False (and drops) when power runs out.</summary>
        public bool ChargeHold(int ticks)
        {
            Thing t = Held;
            if (t == null)
            {
                return true;
            }
            float cost = HoldCostPerHour(t, MassOf(t)) * ticks / GenDate.TicksPerHour;
            if (DivinePower.Instance.TrySpend(cost))
            {
                return true;
            }
            DivinePower.Instance.DebugSet(0f);
            Place(lastCell);
            Messages.Message("AGM_Hand_NoPower".Translate(t.LabelShort), t, MessageTypeDefOf.NegativeEvent, historical: false);
            return false;
        }

        // ---- Engine hooks ------------------------------------------------------------------

        public override void MapComponentTick()
        {
            if (!Holding)
            {
                return;
            }
            held.DoTick();
            if (Holding && Held.Destroyed)
            {
                held.Clear();
            }
            ChargeHold(1);
        }

        /// <summary>Per frame: draw the held thing under the cursor and the cell it would land on.</summary>
        public override void MapComponentUpdate()
        {
            if (!Holding || Find.CurrentMap != map)
            {
                return;
            }
            Vector3 pos = UI.MouseMapPosition();
            pos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            pos.z += 0.25f;
            Held.DrawNowAt(pos);
            if (lastCell.IsValid)
            {
                GenDraw.DrawTargetHighlight(new LocalTargetInfo(lastCell));
            }
        }

        public override void FinalizeInit()
        {
            // A thing still in the hand from a save: set it down where it was.
            if (Holding)
            {
                Place(lastCell);
            }
        }

        public override void ExposeData()
        {
            Scribe_Deep.Look(ref held, "held", this);
            Scribe_Values.Look(ref lastCell, "lastCell", IntVec3.Invalid);
            Scribe_Values.Look(ref takenFromBed, "takenFromBed");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && held == null)
            {
                held = new ThingOwner<Thing>(this);
            }
        }

        public ThingOwner GetDirectlyHeldThings() => held;

        public void GetChildHolders(List<IThingHolder> outChildren) =>
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, held);

        public IThingHolder ParentHolder => map;
    }

    /// <summary>A colonist in the god's hand is still in the colony: don't end the game while they're lifted.</summary>
    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.CheckOrUpdateGameOver))]
    public static class Patch_GameOverWhileHeld
    {
        public static bool Prefix(ref bool ___gameEnding)
        {
            foreach (Map map in Find.Maps)
            {
                if (GodHand.On(map)?.Held is Pawn p && p.IsFreeColonist)
                {
                    ___gameEnding = false;
                    return false;
                }
            }
            return true;
        }
    }
}
