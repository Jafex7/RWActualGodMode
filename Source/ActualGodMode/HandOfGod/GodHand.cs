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
    /// drops what is held. Carrying a downed person to a bed is a small good deed; dropping
    /// someone into danger (fire, hostiles) is judged like a Lightning strike.
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

        /// <summary>How much the god can lift (kg): the Strength power track (believers speed up power, not strength).</summary>
        public static float Capacity => GodProgression.Value(AGM_ProgressionDefOf.AGM_Track_Strength);

        /// <summary>Light touch: lifting and holding costs scale with this (1 = full price).</summary>
        private static float TouchFactor => GodProgression.Value(AGM_ProgressionDefOf.AGM_Track_LightTouch) / 100f;

        /// <summary>Capacity at a cell: full inside the influence, fading fast past its edge.</summary>
        public static float CapacityAt(IntVec3 cell, Map map) => Capacity * InfluenceUtility.LiftFactor(cell, map);

        /// <summary>Could the hand hold this (mass) here?</summary>
        public static bool CanHoldAt(float mass, IntVec3 cell, Map map) =>
            cell.InBounds(map) && InfluenceUtility.InReach(cell, map) && mass <= CapacityAt(cell, map);

        public static float UnitMass(Thing t) =>
            t is Pawn p ? p.GetStatValue(StatDefOf.Mass) + MassUtility.GearAndInventoryMass(p) : t.GetStatValue(StatDefOf.Mass);

        public static float MassOf(Thing t) => t is Pawn ? UnitMass(t) : UnitMass(t) * t.stackCount;

        /// <summary>Power to pick it up where it lies (dearer past the influence edge).</summary>
        public static float PickupCost(Thing t, float mass) =>
            (t is Pawn ? T.pawnPickupCost + T.pawnPickupCostPerKg * mass : T.itemPickupCost + T.itemPickupCostPerKg * mass)
            * TouchFactor * (t.Spawned ? InfluenceUtility.CostFactor(t.Position, t.Map) : 1f);

        /// <summary>Power per hour to hold it over this cell (dearer past the influence edge).</summary>
        public float HoldCostAt(Thing t, IntVec3 cell) =>
            HoldCostPerHour(t, MassOf(t)) * (cell.IsValid && cell.InBounds(map) ? InfluenceUtility.CostFactor(cell, map) : 1f);

        public static float HoldCostPerHour(Thing t, float mass) =>
            (t is Pawn ? T.pawnHoldCostPerKgPerHour : T.itemHoldCostPerKgPerHour) * mass * TouchFactor;

        /// <summary>Items always; the living (Lift living things), and then enemies (Lift enemies), are bought powers.</summary>
        public static AcceptanceReport CanLiftKind(Thing t)
        {
            if (t is Pawn p)
            {
                if (!GodProgression.Has(AGM_ProgressionDefOf.AGM_Track_LiftLiving))
                {
                    return "AGM_Hand_CantLiftLiving".Translate(p.LabelShort);
                }
                if (p.HostileTo(Faction.OfPlayer) && !GodProgression.Has(AGM_ProgressionDefOf.AGM_Track_LiftEnemies))
                {
                    return "AGM_Hand_CantLiftEnemies".Translate(p.LabelShort);
                }
            }
            return true;
        }

        /// <summary>Pawns and haulable items. No buildings, plants or filth.</summary>
        public static bool IsGrabbable(Thing t) =>
            t != null && t.Spawned && !t.Destroyed
            && (t is Pawn || (t.def.category == ThingCategory.Item && t.def.EverHaulable));

        /// <summary>
        /// The thing the hand would take at this cell: a pawn it may lift first, else the top item, else a pawn
        /// it may not lift yet (so the cursor can say why).
        /// </summary>
        public static Thing GrabbableAt(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map))
            {
                return null;
            }
            List<Thing> things = cell.GetThingList(map);
            Thing notYet = null;
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (things[i] is Pawn && IsGrabbable(things[i]))
                {
                    if (CanLiftKind(things[i]).Accepted)
                    {
                        return things[i];
                    }
                    notYet ??= things[i];
                }
            }
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (!(things[i] is Pawn) && IsGrabbable(things[i]))
                {
                    return things[i];
                }
            }
            return notYet;
        }

        /// <summary>How many of the thing the hand would take (a heavy stack is taken in part).</summary>
        public static int LiftableCount(Thing t)
        {
            float capacity = CapacityAt(t.Position, t.Map);
            if (t is Pawn)
            {
                return UnitMass(t) <= capacity ? 1 : 0;
            }
            float unit = UnitMass(t);
            return unit <= 0.0001f ? t.stackCount : Mathf.Min(t.stackCount, Mathf.FloorToInt(capacity / unit));
        }

        public AcceptanceReport CanGrab(Thing t)
        {
            if (Holding || !IsGrabbable(t))
            {
                return false;
            }
            if (!InfluenceUtility.InReach(t.Position, t.Map))
            {
                return "AGM_Hand_OutOfInfluence".Translate();
            }
            AcceptanceReport kind = CanLiftKind(t);
            if (!kind.Accepted)
            {
                return kind;
            }
            int count = LiftableCount(t);
            if (count <= 0)
            {
                return "AGM_Hand_TooHeavy".Translate(t.LabelShort, UnitMass(t).ToString("F0"), CapacityAt(t.Position, t.Map).ToString("F0"));
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
            if (t is Pawn lifted)
            {
                React(lifted);
            }
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
            if (CanHoldAt(MassOf(Held), cell, map))
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

        /// <summary>
        /// A lifted person reacts by the god's nature: joy (good), surprise (neutral), fear (evil).
        /// Enemies are always afraid. Flavor only: a small, non-stacking mood memory and a shout.
        /// </summary>
        private static void React(Pawn pawn)
        {
            if (!pawn.RaceProps.Humanlike || pawn.Map == null)
            {
                return;
            }
            AlignmentTracker alignment = AlignmentTracker.Instance;
            string key;
            ThoughtDef thought;
            if (pawn.HostileTo(Faction.OfPlayer) || alignment?.IsEvil == true)
            {
                key = "AGM_Hand_ShoutFear";
                thought = AGM_HandDefOf.AGM_LiftedFear;
            }
            else if (alignment?.IsGood == true)
            {
                key = "AGM_Hand_ShoutJoy";
                thought = AGM_HandDefOf.AGM_LiftedJoy;
            }
            else
            {
                key = "AGM_Hand_ShoutSurprise";
                thought = AGM_HandDefOf.AGM_LiftedStartled;
            }
            MoteMaker.ThrowText(pawn.DrawPos + new Vector3(0f, 0f, 0.6f), pawn.Map, key.Translate(), 2.5f);
            if (pawn.needs?.mood != null)
            {
                FaithThoughts.Give(pawn, thought);
            }
        }

        /// <summary>
        /// Set the held thing down at the cell (or the last cell inside the influence).
        /// <paramref name="order"/>: a deliberate drop, so a colonist set on work starts it.
        /// </summary>
        public bool Place(IntVec3 cell, bool order = false)
        {
            Thing t = Held;
            if (t == null)
            {
                return false;
            }
            dragging = false;
            IntVec3 target = CanHoldAt(MassOf(t), cell, map) ? cell : lastCell;
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
                if (order && JudgeDangerousDrop(pawn))
                {
                    return true;
                }
                if (!TryTuckIntoBed(pawn) && order && target == cell)
                {
                    OrderWork(pawn, cell);
                }
            }
            return true;
        }

        public enum DropDanger
        {
            None,
            Fire,
            Hostiles
        }

        /// <summary>
        /// Is setting this pawn down here throwing them into danger? Fire on or next to the cell, or
        /// hostiles to them close by (in sight). A drafted colonist among enemies is being sent to
        /// fight, not thrown to them.
        /// </summary>
        public static DropDanger DangerAt(IntVec3 cell, Map map, Pawn pawn, out int hostiles)
        {
            hostiles = 0;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, 1.5f, useCenter: true))
            {
                if (c.InBounds(map) && c.ContainsStaticFire(map))
                {
                    return DropDanger.Fire;
                }
            }
            if (pawn.Drafted)
            {
                return DropDanger.None;
            }
            float radiusSq = T.dangerHostileRadius * T.dangerHostileRadius;
            foreach (Pawn other in map.mapPawns.AllPawnsSpawned)
            {
                if (other != pawn && !other.Downed && !other.Dead && !other.IsPrisoner
                    && (other.Position - cell).LengthHorizontalSquared <= radiusSq
                    && other.HostileTo(pawn) && GenSight.LineOfSight(other.Position, cell, map))
                {
                    hostiles++;
                }
            }
            return hostiles > 0 ? DropDanger.Hostiles : DropDanger.None;
        }

        /// <summary>A deliberate drop into danger is the god's doing: judge it like a strike. True if it was dangerous.</summary>
        private bool JudgeDangerousDrop(Pawn pawn)
        {
            if (DangerAt(pawn.Position, map, pawn, out _) == DropDanger.None)
            {
                return false;
            }
            bool punished = WrathJudgement.Judge(pawn, "AGM_Deed_Drop_", AlignmentTuningDef.Get.dangerDropFactor, logEnemy: true);
            WrathJudgement.AffectWitnesses(pawn.Position, map, new List<Pawn> { pawn }, punished);
            return true;
        }

        /// <summary>Set down on a bed: a downed person is laid in it (and that's a kindness).</summary>
        private bool TryTuckIntoBed(Pawn pawn)
        {
            Building_Bed bed = pawn.Position.GetFirstThing<Building_Bed>(map);
            if (bed == null || !RestUtility.CanUseBedNow(bed, pawn, checkSocialProperness: false))
            {
                return false;
            }
            bool wasDowned = pawn.Downed;
            pawn.jobs.Notify_TuckedIntoBed(bed);
            pawn.mindState.Notify_TuckedIntoBed();
            if (!wasDowned || takenFromBed || !pawn.RaceProps.Humanlike || pawn.HostileTo(Faction.OfPlayer))
            {
                return true;
            }
            int now = Find.TickManager.TicksGame;
            if (lastBedDeedTick.TryGetValue(pawn, out int last) && now - last < T.carryToBedCooldownHours * GenDate.TicksPerHour)
            {
                return true;
            }
            lastBedDeedTick[pawn] = now;
            AlignmentTracker.Instance?.Deed(T.carryToBedDeed, "AGM_Deed_CarriedToBed".Translate(pawn.LabelShort));
            return true;
        }

        /// <summary>Set down on work: start it, or say why they can't.</summary>
        private void OrderWork(Pawn pawn, IntVec3 cell)
        {
            HandWorkOrder.Result r = HandWorkOrder.OrderWork(pawn, cell);
            if (r.text.NullOrEmpty())
            {
                return;
            }
            if (r.ordered)
            {
                MoteMaker.ThrowText(pawn.DrawPos + new Vector3(0f, 0f, 0.6f), map, r.text, 3f);
            }
            else
            {
                Messages.Message(pawn.LabelShort + ": " + r.text, pawn, MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        /// <summary>Spend the holding cost for a number of ticks. False (and drops) when power runs out.</summary>
        public bool ChargeHold(int ticks)
        {
            Thing t = Held;
            if (t == null)
            {
                return true;
            }
            float cost = HoldCostAt(t, lastCell) * ticks / GenDate.TicksPerHour;
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

    [DefOf]
    public static class AGM_HandDefOf
    {
        public static ThoughtDef AGM_LiftedJoy;
        public static ThoughtDef AGM_LiftedStartled;
        public static ThoughtDef AGM_LiftedFear;

        static AGM_HandDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AGM_HandDefOf));
        }
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
