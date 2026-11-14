using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// Heralds: living pylons (Influence branch, endgame). A very faithful colonist, anointed with a
    /// gizmo, carries a small circle of the god's influence wherever they go. The circle is off while
    /// their faith is below the bar, or they're downed, captured or dead. The Heralds power track sets
    /// how many can be anointed at once.
    /// </summary>
    public class HeraldTracker : GameComponent
    {
        private const int CleanupIntervalTicks = 250;

        private List<Pawn> heralds = new List<Pawn>();

        public static HeraldTracker Instance => Current.Game?.GetComponent<HeraldTracker>();

        public HeraldTracker(Game game)
        {
        }

        public bool Any => heralds.Count > 0;

        public int Count => heralds.Count;

        /// <summary>How many heralds the god may have (the Heralds power track).</summary>
        public static int Max => Mathf.RoundToInt(GodProgression.Value(AGM_ProgressionDefOf.AGM_Track_Heralds));

        public bool IsHerald(Pawn p) => heralds.Contains(p);

        public static bool FaithfulEnough(Pawn p) =>
            p.Faith() != null && p.Faith().CurLevelPercentage >= InfluenceTuningDef.Get.heraldMinFaith;

        /// <summary>Carrying the influence right now.</summary>
        public static bool IsActive(Pawn p) =>
            p.Spawned && !p.Dead && !p.Downed && p.IsColonist && !p.IsPrisoner && FaithfulEnough(p);

        public IEnumerable<Pawn> ActiveOn(Map map)
        {
            for (int i = 0; i < heralds.Count; i++)
            {
                if (heralds[i].MapHeld == map && IsActive(heralds[i]))
                {
                    yield return heralds[i];
                }
            }
        }

        public AcceptanceReport CanAnoint(Pawn p)
        {
            if (IsHerald(p))
            {
                return false;
            }
            if (Count >= Max)
            {
                return "AGM_Herald_Full".Translate(Max);
            }
            if (!FaithfulEnough(p))
            {
                return "AGM_Herald_NeedsFaith".Translate(p.LabelShort, InfluenceTuningDef.Get.heraldMinFaith.ToStringPercent());
            }
            return true;
        }

        public bool Anoint(Pawn p)
        {
            if (!CanAnoint(p).Accepted)
            {
                return false;
            }
            heralds.Add(p);
            return true;
        }

        public void Dismiss(Pawn p) => heralds.Remove(p);

        /// <summary>Why a herald isn't carrying the influence right now (null if they are).</summary>
        public static string InactiveReason(Pawn p)
        {
            if (p.Downed)
            {
                return "AGM_Herald_Off_Downed".Translate();
            }
            if (!FaithfulEnough(p))
            {
                return "AGM_Herald_Off_Faith".Translate(InfluenceTuningDef.Get.heraldMinFaith.ToStringPercent());
            }
            return IsActive(p) ? null : (string)"AGM_Herald_Off_Away".Translate();
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % CleanupIntervalTicks == 0)
            {
                // The dead, and those no longer the colony's own (captured, left), stop being heralds.
                heralds.RemoveAll(p => p == null || p.Destroyed || p.Dead || p.Faction != Faction.OfPlayer);
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref heralds, "heralds", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                heralds ??= new List<Pawn>();
                heralds.RemoveAll(p => p == null);
            }
        }
    }

    /// <summary>"Anoint as herald" / "Dismiss herald" on believers, once the Heralds power is bought.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_HeraldGizmo
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            HeraldTracker tracker = HeraldTracker.Instance;
            Pawn pawn = __instance;
            if (tracker == null || !pawn.IsColonistPlayerControlled || pawn.Faith() == null)
            {
                return;
            }
            bool herald = tracker.IsHerald(pawn);
            if (!herald && HeraldTracker.Max <= 0)
            {
                return;
            }
            __result = __result.Append(herald ? DismissGizmo(pawn, tracker) : AnointGizmo(pawn, tracker));
        }

        private static Texture2D Icon => AGM_TempleDefOf.AGM_Pylon.uiIcon;

        private static Gizmo AnointGizmo(Pawn pawn, HeraldTracker tracker)
        {
            var command = new Command_Action
            {
                defaultLabel = "AGM_Herald_Anoint".Translate(),
                defaultDesc = "AGM_Herald_AnointDesc".Translate(pawn.LabelShort, InfluenceTuningDef.Get.heraldMinFaith.ToStringPercent(),
                    tracker.Count, HeraldTracker.Max),
                icon = Icon,
                action = () => tracker.Anoint(pawn),
            };
            AcceptanceReport can = tracker.CanAnoint(pawn);
            if (!can.Accepted)
            {
                command.Disable(can.Reason);
            }
            return command;
        }

        private static Gizmo DismissGizmo(Pawn pawn, HeraldTracker tracker)
        {
            string reason = HeraldTracker.InactiveReason(pawn);
            return new Command_Action
            {
                defaultLabel = "AGM_Herald_Dismiss".Translate(),
                defaultDesc = "AGM_Herald_DismissDesc".Translate(pawn.LabelShort)
                    + "\n\n" + (reason == null ? "AGM_Herald_On".Translate().ToString() : reason),
                icon = Icon,
                defaultIconColor = reason == null ? Color.white : Color.gray,
                action = () => tracker.Dismiss(pawn),
            };
        }
    }
}
