using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// Who believes in the god. Only believers have the Faith need (and so pray, ask for
    /// help, generate power...). Nobody believes before the Awakening; the chosen one is the
    /// first, and others are convinced by miracles, preaching, or a friend's answered prayer.
    /// </summary>
    public class BelieverTracker : GameComponent
    {
        private HashSet<Pawn> believers = new HashSet<Pawn>();

        /// <summary>
        /// False only when loading a save from before believers existed: until migrated, every
        /// pawn that already had the Faith need keeps it (and becomes a believer).
        /// </summary>
        private bool migrated = true;

        public static BelieverTracker Instance => Current.Game?.GetComponent<BelieverTracker>();

        public bool Migrated => migrated;

        public BelieverTracker(Game game)
        {
        }

        public bool IsBeliever(Pawn pawn) => pawn != null && believers.Contains(pawn);

        /// <summary>Living believers anywhere (they lend the Hand its strength).</summary>
        public int LivingCount => believers.Count(p => p != null && !p.Dead && !p.Destroyed);

        public IEnumerable<Pawn> NonBelieverColonists(Map map) =>
            map.mapPawns.FreeColonistsSpawned.Where(p => !IsBeliever(p) && p.RaceProps.Humanlike);

        /// <summary>Make a pawn a believer, give them the Faith need at the given level.</summary>
        public bool Convert(Pawn pawn, float initialFaith, bool silent = false)
        {
            if (pawn == null || pawn.Dead || !pawn.RaceProps.Humanlike || !believers.Add(pawn))
            {
                return false;
            }
            pawn.needs?.AddOrRemoveNeedsAsAppropriate();
            Need_Faith faith = pawn.Faith();
            if (faith == null)
            {
                believers.Remove(pawn);
                return false;
            }
            faith.CurLevel = initialFaith;
            faith.History.Add("AGM_Cause_Converted".Translate(), 0f);
            if (!silent)
            {
                FaithThoughts.Give(pawn, AGM_ThoughtDefOf.AGM_Converted);
                Messages.Message("AGM_Converted".Translate(pawn.Named("PAWN")), new LookTargets(pawn), MessageTypeDefOf.PositiveEvent);
                GodGuideTracker.Instance?.Notify_Converted(pawn);
            }
            return true;
        }

        /// <summary>Debug/test: make a pawn a non-believer again (removes the Faith need).</summary>
        public void DebugDeconvert(Pawn pawn)
        {
            if (believers.Remove(pawn))
            {
                pawn.needs?.AddOrRemoveNeedsAsAppropriate();
            }
        }

        /// <summary>Convert with a chance; the chance is scaled by the pawn's traits.</summary>
        public bool TryConvert(Pawn pawn, float baseChance, bool byTalk)
        {
            if (pawn == null || IsBeliever(pawn))
            {
                return false;
            }
            float factor = FaithTraitUtility.Factor(pawn, f => byTalk ? f.conversionByTalkFactor : f.conversionFactor);
            if (!Rand.Chance(baseChance * factor))
            {
                return false;
            }
            return Convert(pawn, FaithTuningDef.Get.convertedInitialFaith.RandomInRange);
        }

        /// <summary>Chosen People / skip setting / test setups: every colonist already believes, faith low.</summary>
        public void ConvertAllColonists()
        {
            foreach (Pawn p in PawnsFinder.AllMaps_FreeColonists)
            {
                Convert(p, FaithTuningDef.Get.startingBelieverFaith.RandomInRange, silent: true);
            }
        }

        public override void StartedNewGame()
        {
            if (ActualGodModeMod.Settings.skipAwakening)
            {
                ConvertAllColonists();
                GodGuideTracker.Instance?.Start();
            }
        }

        public override void LoadedGame()
        {
            if (migrated)
            {
                return;
            }
            // Old save: everyone who already had faith keeps it.
            foreach (Pawn p in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
            {
                if (p.needs?.TryGetNeed<Need_Faith>() != null)
                {
                    believers.Add(p);
                }
            }
            migrated = true;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref migrated, "migrated", false);
            Scribe_Collections.Look(ref believers, "believers", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                believers ??= new HashSet<Pawn>();
                believers.RemoveWhere(p => p == null);
            }
        }
    }

    /// <summary>The Faith need only exists for believers.</summary>
    [HarmonyPatch(typeof(Pawn_NeedsTracker), "ShouldHaveNeed")]
    public static class Patch_FaithOnlyForBelievers
    {
        private static readonly AccessTools.FieldRef<Pawn_NeedsTracker, Pawn> PawnRef =
            AccessTools.FieldRefAccess<Pawn_NeedsTracker, Pawn>("pawn");

        public static void Postfix(Pawn_NeedsTracker __instance, NeedDef nd, ref bool __result)
        {
            if (!__result || nd != AGM_DefOf.AGM_Faith)
            {
                return;
            }
            BelieverTracker tracker = BelieverTracker.Instance;
            if (tracker == null)
            {
                __result = false;
                return;
            }
            __result = !tracker.Migrated || tracker.IsBeliever(PawnRef(__instance));
        }
    }

    /// <summary>
    /// A believer talks about the god to a non-believer. Rare; never works on Sceptics,
    /// works more often on those inclined to believe (Zealot). Better speakers convince more.
    /// </summary>
    public class InteractionWorker_Preach : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            FaithTuningDef t = FaithTuningDef.Get;
            BelieverTracker tracker = BelieverTracker.Instance;
            if (tracker == null || !(AwakeningTracker.Instance?.Revealed ?? false))
            {
                return 0f;
            }
            Need_Faith faith = initiator.Faith();
            if (faith == null || faith.CurLevelPercentage < t.preachMinFaith || tracker.IsBeliever(recipient) || !recipient.IsColonist)
            {
                return 0f;
            }
            if (FaithTraitUtility.Factor(recipient, f => f.conversionByTalkFactor) <= 0f)
            {
                return 0f; // talk never convinces them, so believers don't bother
            }
            return t.preachSelectionWeight;
        }

        public override void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks,
            out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            letterText = null;
            letterLabel = null;
            letterDef = null;
            lookTargets = null;
            int social = initiator.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            float chance = FaithTuningDef.Get.preachConvertChance * (1f + social * 0.05f);
            if (BelieverTracker.Instance.TryConvert(recipient, chance, byTalk: true))
            {
                extraSentencePacks.Add(AGM_ThoughtDefOf.AGM_Sentence_PreachConvinced);
            }
        }
    }
}
