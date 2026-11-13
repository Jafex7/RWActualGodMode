using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// Creates needs-driven prayer requests, answers them when the right miracle is cast
    /// near the requester, and applies the penalty when one is ignored past its deadline.
    /// </summary>
    public class PrayerRequestTracker : GameComponent
    {
        private const int CheckIntervalTicks = 250;

        /// <summary>Minimum days between two requests from the same pawn (scaled by traits).</summary>
        private const float CooldownDays = 2f;

        private List<PrayerRequest> active = new List<PrayerRequest>();

        public static PrayerRequestTracker Instance => Current.Game?.GetComponent<PrayerRequestTracker>();

        public IReadOnlyList<PrayerRequest> Active => active;

        public PrayerRequestTracker(Game game)
        {
        }

        public bool HasRequest(Pawn pawn) => active.Any(r => r.pawn == pawn);

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0)
            {
                return;
            }
            UpdateActive();
            if (DivinePower.Instance?.Revealed ?? false)
            {
                TryCreateRequests();
            }
        }

        public void UpdateActive()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                PrayerRequest r = active[i];
                if (r.pawn == null || r.pawn.Dead || r.pawn.Destroyed || r.def == null)
                {
                    active.RemoveAt(i);
                }
                else if (!r.def.Worker.StillNeeded(r))
                {
                    active.RemoveAt(i); // the need passed on its own
                }
                else if (r.TicksLeft <= 0)
                {
                    active.RemoveAt(i);
                    Expire(r);
                }
            }
        }

        private void TryCreateRequests()
        {
            MiracleTracker miracles = MiracleTracker.Instance;
            int now = Find.TickManager.TicksGame;
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    Need_Faith faith = pawn.Faith();
                    if (faith == null || HasRequest(pawn))
                    {
                        continue;
                    }
                    float frequency = FaithTraitUtility.Factor(pawn, f => f.requestFrequencyFactor);
                    if (frequency <= 0f || now - faith.lastRequestTick < CooldownDays / frequency * GenDate.TicksPerDay)
                    {
                        continue;
                    }
                    foreach (PrayerRequestDef def in DefDatabase<PrayerRequestDef>.AllDefsListForReading)
                    {
                        if (def.isWish || faith.CurLevelPercentage < def.minFaith)
                        {
                            continue;
                        }
                        if (def.miracle != null && !miracles.AnyUnlocked(def.miracle))
                        {
                            continue; // they don't know to ask for it yet
                        }
                        if (def.Worker.CanRequest(pawn) && Rand.MTBEventOccurs(def.mtbDays / frequency, GenDate.TicksPerDay, CheckIntervalTicks))
                        {
                            Create(pawn, def);
                            break;
                        }
                    }
                }
            }
        }

        public PrayerRequest Create(Pawn pawn, PrayerRequestDef def)
        {
            var request = new PrayerRequest
            {
                pawn = pawn,
                def = def,
                map = pawn.Map,
                startTick = Find.TickManager.TicksGame,
                deadlineTick = Find.TickManager.TicksGame + def.Worker.DeadlineTicks(pawn)
            };
            active.Add(request);
            Need_Faith faith = pawn.Faith();
            if (faith != null)
            {
                faith.lastRequestTick = request.startTick;
            }

            string hours = (request.TicksLeft / (float)GenDate.TicksPerHour).ToString("F0");
            Find.LetterStack.ReceiveLetter(
                def.letterLabel.Formatted(pawn.Named("PAWN")),
                def.letterText.Formatted(pawn.Named("PAWN"), hours.Named("HOURS")),
                def.urgent ? LetterDefOf.NegativeEvent : LetterDefOf.NeutralEvent,
                new LookTargets(pawn));
            return request;
        }

        public void Notify_MiracleCast(MiracleDef miracle, IntVec3 cell, Map map)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                PrayerRequest r = active[i];
                if (r.def.Worker.IsAnsweredBy(r, miracle, cell, map))
                {
                    active.RemoveAt(i);
                    Answer(r, cell, map);
                }
            }
        }

        private void Answer(PrayerRequest r, IntVec3 cell, Map map)
        {
            r.pawn.Faith()?.Gain(r.def.reward, "AGM_Cause_PrayerAnswered".Translate(r.def.label));
            FaithThoughts.Give(r.pawn, AGM_ThoughtDefOf.AGM_PrayerAnswered);
            float radiusSq = 12f * 12f;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p == r.pawn)
                {
                    continue;
                }
                bool shares = r.def.rewardAllColonists
                    || ((p.Position - cell).LengthHorizontalSquared <= radiusSq && GenSight.LineOfSight(p.Position, cell, map));
                if (shares)
                {
                    p.Faith()?.Gain(r.def.witnessReward, "AGM_Cause_SharedAnswer".Translate(r.pawn.LabelShort));
                }
            }
            Messages.Message("AGM_Request_Answered".Translate(r.pawn.Named("PAWN")), new LookTargets(r.pawn), MessageTypeDefOf.PositiveEvent);
        }

        private void Expire(PrayerRequest r)
        {
            r.pawn.Faith()?.Lose(r.def.ignoredPenalty, "AGM_Cause_PrayerIgnored".Translate(r.def.label));
            FaithThoughts.Give(r.pawn, AGM_ThoughtDefOf.AGM_PrayerIgnored);
            Messages.Message("AGM_Request_Ignored".Translate(r.pawn.Named("PAWN")), new LookTargets(r.pawn), MessageTypeDefOf.NegativeEvent);
        }

        public string AlertExplanation()
        {
            var sb = new StringBuilder();
            foreach (PrayerRequest r in active.OrderBy(r => r.TicksLeft))
            {
                string left = r.TicksLeft.ToStringTicksToPeriod(allowSeconds: false, shortForm: true);
                sb.AppendLine("  • " + r.def.alertLine.Formatted(r.pawn.Named("PAWN")) + " (" + left + ")");
            }
            return sb.ToString().TrimEnd();
        }

        public void DebugExpire(PrayerRequest r) => r.deadlineTick = Find.TickManager.TicksGame;

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref active, "active", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                active ??= new List<PrayerRequest>();
                active.RemoveAll(r => r.pawn == null || r.def == null);
            }
        }
    }

    /// <summary>Lists unanswered prayers with time left. Critical when any is urgent.</summary>
    public class Alert_PrayerRequests : Alert
    {
        public Alert_PrayerRequests()
        {
            defaultPriority = AlertPriority.Medium;
        }

        public override AlertPriority Priority =>
            (PrayerRequestTracker.Instance?.Active.Any(r => r.def.urgent) ?? false) ? AlertPriority.Critical : AlertPriority.Medium;

        public override string GetLabel() =>
            "AGM_Alert_Prayers".Translate(PrayerRequestTracker.Instance?.Active.Count ?? 0);

        public override TaggedString GetExplanation() =>
            "AGM_Alert_Prayers_Desc".Translate(PrayerRequestTracker.Instance?.AlertExplanation() ?? "");

        public override AlertReport GetReport()
        {
            PrayerRequestTracker tracker = PrayerRequestTracker.Instance;
            if (tracker == null || tracker.Active.Count == 0)
            {
                return false;
            }
            return AlertReport.CulpritsAre(tracker.Active.Select(r => r.pawn).ToList());
        }
    }

    /// <summary>Storyteller event: a believer makes a non-urgent wish (rain for the crops, a sign...).</summary>
    public class IncidentWorker_PrayerWish : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            return TryPick((Map)parms.target, out _, out _);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!TryPick((Map)parms.target, out Pawn pawn, out PrayerRequestDef wish))
            {
                return false;
            }
            PrayerRequestTracker.Instance.Create(pawn, wish);
            return true;
        }

        private static bool TryPick(Map map, out Pawn pawn, out PrayerRequestDef wish)
        {
            pawn = null;
            wish = null;
            PrayerRequestTracker tracker = PrayerRequestTracker.Instance;
            MiracleTracker miracles = MiracleTracker.Instance;
            if (map == null || tracker == null || miracles == null || ShrineUtility.ShrineOn(map) == null)
            {
                return false;
            }
            var options = new List<(Pawn, PrayerRequestDef)>();
            foreach (PrayerRequestDef def in DefDatabase<PrayerRequestDef>.AllDefsListForReading)
            {
                if (!def.isWish || (def.miracle != null && !miracles.AnyUnlocked(def.miracle)))
                {
                    continue;
                }
                foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                {
                    Need_Faith f = p.Faith();
                    if (f != null && f.CurLevelPercentage >= def.minFaith && !tracker.HasRequest(p) && def.Worker.CanRequest(p))
                    {
                        options.Add((p, def));
                    }
                }
            }
            if (options.Count == 0)
            {
                return false;
            }
            (pawn, wish) = options.RandomElement();
            return true;
        }
    }
}
