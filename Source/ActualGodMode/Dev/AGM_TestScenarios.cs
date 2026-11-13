using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// Scenario builders shared by the headless self-test and dev-mode debug actions, so the
    /// player can recreate any test setup on their own save. Add one per milestone.
    /// </summary>
    public static class AGM_TestScenarios
    {
        private const string Category = "Actual God Mode";

        /// <summary>Returns the map's shrine, placing one near the colonists if there isn't one.</summary>
        public static Thing EnsureShrine(Map map)
        {
            Thing existing = ShrineUtility.ShrineOn(map);
            if (existing != null)
            {
                return existing;
            }
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned.ToList();
            IntVec3 origin = colonists.Count > 0 ? colonists[0].Position : map.Center;
            if (!CellFinder.TryFindRandomCellNear(origin, map, 12,
                    c => c.Standable(map) && GenAdj.CellsAdjacent8Way(new TargetInfo(c, map)).All(a => a.InBounds(map) && a.Standable(map)),
                    out IntVec3 cell))
            {
                return null;
            }
            Thing shrine = GenSpawn.Spawn(ThingMaker.MakeThing(AGM_DefOf.AGM_Shrine, ThingDefOf.BlocksGranite), cell, map);
            shrine.SetFaction(Faction.OfPlayer);
            return shrine;
        }

        /// <summary>Shrine, full power, every miracle unlocked, colonists at the given faith.</summary>
        public static Thing SetUpGodColony(Map map, float faith = 0.6f)
        {
            Thing shrine = EnsureShrine(map);
            BelieverTracker.Instance?.ConvertAllColonists();
            AwakeningTracker.Instance?.MarkRevealedByScenario();
            if (!AGM_AwakeningDefOf.AGM_ShrineResearch.IsFinished)
            {
                Find.ResearchManager.FinishProject(AGM_AwakeningDefOf.AGM_ShrineResearch, doCompletionDialog: false);
            }
            MiracleTracker.Instance?.RecordEvent(MiracleTracker.Event_ColonistDowned);
            MiracleTracker.Instance?.DebugUnlockAll();
            DivinePower dp = DivinePower.Instance;
            dp?.Add(dp.MaxPower);
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                Need_Faith f = p.Faith();
                if (f != null)
                {
                    f.CurLevel = faith;
                }
            }
            return shrine;
        }

        [DebugAction(Category, "Set up god test colony", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 100)]
        private static void SetUpGodColonyAction()
        {
            Thing shrine = SetUpGodColony(Find.CurrentMap);
            if (shrine == null)
            {
                Messages.Message("Couldn't find room for a shrine near the colonists.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.CameraDriver.JumpToCurrentMapLoc(shrine.Position);
            Messages.Message("God test colony ready: shrine, full power, all miracles, colonists at 60% faith.", MessageTypeDefOf.TaskCompletion, false);
        }

        /// <summary>
        /// Builds a roofed stone room around the shrine with floor and holy furniture aiming at the
        /// given tier (1 chapel, 2 temple, 3 grand temple). Returns the resulting temple info.
        /// </summary>
        public static TempleInfo BuildTestTemple(Map map, int tier)
        {
            Thing shrine = SetUpGodColony(map);
            if (shrine == null)
            {
                return default;
            }
            int half = tier >= 3 ? 5 : tier == 2 ? 4 : 3;
            CellRect inner = CellRect.CenteredOn(shrine.Position, half);
            CellRect outer = inner.ExpandedBy(1);
            IntVec3 doorCell = new IntVec3(shrine.Position.x, 0, outer.minZ);

            // Clear the area (keep the shrine), move pawns out of the way.
            foreach (IntVec3 c in outer.ClipInsideMap(map))
            {
                foreach (Thing t in c.GetThingList(map).ToList())
                {
                    if (t == shrine)
                    {
                        continue;
                    }
                    if (t is Pawn p)
                    {
                        IntVec3 dest = CellFinder.RandomClosewalkCellNear(outer.CenterCell + new IntVec3(0, 0, -half - 4), map, 4);
                        p.Position = dest;
                        p.Notify_Teleported();
                    }
                    else if (t.def.destroyable)
                    {
                        t.Destroy();
                    }
                }
            }

            TerrainDef floor = DefDatabase<TerrainDef>.GetNamed(tier >= 3 ? "TileMarble" : tier == 2 ? "FlagstoneGranite" : "WoodPlankFloor");
            foreach (IntVec3 c in inner.ClipInsideMap(map))
            {
                map.terrainGrid.SetTerrain(c, floor);
                map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            }
            foreach (IntVec3 c in outer.EdgeCells)
            {
                if (!c.InBounds(map))
                {
                    continue;
                }
                Thing wall = c == doorCell
                    ? ThingMaker.MakeThing(ThingDefOf.Door, ThingDefOf.BlocksGranite)
                    : ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.BlocksGranite);
                wall.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(wall, c, map);
                map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            }

            // Furniture along the walls, away from the prayer spots.
            var spots = inner.EdgeCells.Where(c => c.InBounds(map) && (c - shrine.Position).LengthHorizontal > 3f && c.x != doorCell.x).ToList();
            var pieces = new List<(ThingDef def, ThingDef stuff, QualityCategory? quality)>();
            ThingDef marble = ThingDefOf.BlocksGranite;
            ThingDef sculptureLarge = DefDatabase<ThingDef>.GetNamed("SculptureLarge");
            ThingDef sculptureGrand = DefDatabase<ThingDef>.GetNamed("SculptureGrand");
            pieces.Add((AGM_TempleDefOf.AGM_Idol, marble, null));
            if (tier >= 2)
            {
                pieces.Add((AGM_TempleDefOf.AGM_IncenseBurner, ThingDefOf.Steel, null));
                pieces.Add((AGM_TempleDefOf.AGM_IncenseBurner, ThingDefOf.Steel, null));
                pieces.Add((sculptureLarge, marble, QualityCategory.Excellent));
            }
            if (tier >= 3)
            {
                pieces.Add((AGM_TempleDefOf.AGM_Idol, marble, null));
                pieces.Add((sculptureGrand, marble, QualityCategory.Masterwork));
                pieces.Add((sculptureLarge, marble, QualityCategory.Masterwork));
            }
            for (int i = 0; i < pieces.Count && i < spots.Count; i++)
            {
                (ThingDef def, ThingDef stuff, QualityCategory? quality) = pieces[i];
                IntVec3 at = spots[(i * spots.Count) / pieces.Count];
                if (def.size.x > 1 || def.size.z > 1)
                {
                    at = spots.FirstOrDefault(c => GenAdj.OccupiedRect(c, Rot4.North, def.size).Cells.All(o => inner.Contains(o) && o.GetFirstBuilding(map) == null && (o - shrine.Position).LengthHorizontal > 3f));
                    if (!at.IsValid)
                    {
                        continue;
                    }
                }
                Thing thing = ThingMaker.MakeThing(def, stuff);
                if (quality.HasValue)
                {
                    thing.TryGetComp<CompQuality>()?.SetQuality(quality.Value, ArtGenerationContext.Colony);
                }
                thing.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(thing, at, map);
            }

            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();
            CompShrine comp = shrine.TryGetComp<CompShrine>();
            comp.InvalidateTemple();
            return comp.Temple;
        }

        [DebugAction(Category, "Build test temple: chapel", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 95)]
        private static void BuildChapel() => ReportTemple(BuildTestTemple(Find.CurrentMap, 1));

        [DebugAction(Category, "Build test temple: temple", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 94)]
        private static void BuildTemple() => ReportTemple(BuildTestTemple(Find.CurrentMap, 2));

        [DebugAction(Category, "Build test temple: grand temple", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 93)]
        private static void BuildGrandTemple() => ReportTemple(BuildTestTemple(Find.CurrentMap, 3));

        private static void ReportTemple(TempleInfo t)
        {
            Messages.Message($"Test temple built: {t.Tier.label} (impressiveness {t.impressiveness:F0}, holy furniture {t.holyFurniture:0.#}, prayer slots {t.Tier.prayerSlots}).",
                MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction(Category, "Wound pawn (for Heal / prayer)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void WoundPawn(Pawn p)
        {
            for (int i = 0; i < 3; i++)
            {
                p.TakeDamage(new DamageInfo(DamageDefOf.Cut, 6f));
            }
        }

        [DebugAction(Category, "Make pawn hungry", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void MakeHungry(Pawn p)
        {
            if (p.needs?.food != null)
            {
                p.needs.food.CurLevelPercentage = 0.1f;
            }
        }

        /// <summary>
        /// The Hand: a light stack, a stack too heavy to lift whole, a bed, and a downed
        /// colonist (anesthetic) near the shrine. Fills divine power.
        /// </summary>
        public static Building_Bed SetUpHandTest(Map map)
        {
            Thing shrine = SetUpGodColony(map);
            IntVec3 center = shrine.Position;
            Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
            steel.stackCount = 40;
            GenPlace.TryPlaceThing(steel, center + new IntVec3(3, 0, 0), map, ThingPlaceMode.Near);
            ThingDef heavyDef = DefDatabase<ThingDef>.AllDefs.FirstOrDefault(d => d.category == ThingCategory.Item && d.EverHaulable
                && d.stackLimit > 1 && !d.MadeFromStuff && d.GetStatValueAbstract(StatDefOf.Mass) * d.stackLimit > GodHand.Capacity * 1.2f);
            if (heavyDef != null)
            {
                Thing heavy = ThingMaker.MakeThing(heavyDef);
                heavy.stackCount = heavyDef.stackLimit;
                GenPlace.TryPlaceThing(heavy, center + new IntVec3(-3, 0, 0), map, ThingPlaceMode.Near);
            }
            var bed = (Building_Bed)GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog),
                CellFinder.RandomClosewalkCellNear(center + new IntVec3(0, 0, -4), map, 3), map);
            bed.SetFaction(Faction.OfPlayer);
            Pawn downed = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => !p.Dead && !p.Downed);
            downed?.health.AddHediff(HediffDefOf.Anesthetic);
            DivinePower.Instance.DebugSet(DivinePower.Instance.MaxPower);
            return bed;
        }

        [DebugAction(Category, "Set up hand test (items, bed, downed colonist)", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 92)]
        private static void SetUpHandTestAction()
        {
            SetUpHandTest(Find.CurrentMap);
            Messages.Message($"Hand test ready: lift capacity {GodHand.Capacity:F0} kg. A colonist is under anesthetic: carry them to the bed.",
                MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
