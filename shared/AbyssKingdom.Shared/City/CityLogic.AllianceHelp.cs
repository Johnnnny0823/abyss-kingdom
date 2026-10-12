using System;
using System.Collections.Generic;

namespace AbyssKingdom.Shared.City
{
    public enum AllianceHelpResult
    {
        Ok, NoQueue, WrongQueue, NotRequested, AlreadyHelped, SelfHelp, HelpLimit, Finished
    }

    /// <summary>Lives on the owner city, so retrying a cross-player write cannot shorten a queue twice.</summary>
    public class AllianceHelpProgress
    {
        public long QueueId { get; set; }
        public long OriginalDurationMs { get; set; }
        public long ReducedMs { get; set; }
        public int MaxCount { get; set; }
        public string AllianceId { get; set; }
        public long OwnerMembershipEpoch { get; set; }
        public HashSet<string> HelpedBy { get; set; } = new HashSet<string>();
    }

    public static partial class CityLogic
    {
        private static AllianceHelpProgress NewHelpProgress(CityState state, long durationMs)
        {
            if (state.NextAllianceQueueId <= 0) state.NextAllianceQueueId = 1;
            return new AllianceHelpProgress
            {
                QueueId = state.NextAllianceQueueId++, OriginalDurationMs = durationMs,
                MaxCount = Math.Min(10, 2 + state.GetLevel(BuildingType.Keep))
            };
        }

        public static AllianceHelpResult RequestAllianceHelp(CityState state, QueueType queue,
            string allianceId, long membershipEpoch, long nowMs, out long queueId)
        {
            queueId = 0;
            if (queue != QueueType.Building && queue != QueueType.Healing) return AllianceHelpResult.NoQueue;
            long finish = queue == QueueType.Building ? state.Upgrade?.FinishAtMs ?? 0 : state.Healing?.FinishAtMs ?? 0;
            if (finish == 0) return AllianceHelpResult.NoQueue;
            if (finish <= nowMs) return AllianceHelpResult.Finished;
            AllianceHelpProgress progress;
            if (queue == QueueType.Building)
            {
                var job = state.Upgrade;
                if (job == null) return AllianceHelpResult.NoQueue;
                progress = job.Help ??= NewHelpProgress(state,
                    CityConfig.UpgradeDurationMs(job.Building, job.TargetLevel));
            }
            else
            {
                var job = state.Healing;
                if (job == null) return AllianceHelpResult.NoQueue;
                progress = job.Help ??= NewHelpProgress(state, HealDurationMs(job.Troops));
            }
            progress.AllianceId = allianceId;
            progress.OwnerMembershipEpoch = membershipEpoch;
            queueId = progress.QueueId;
            return AllianceHelpResult.Ok;
        }

        public static AllianceHelpResult ApplyAllianceHelp(CityState state, QueueType queue, long queueId,
            string ownerId, string helperId, string allianceId, long ownerEpoch, long nowMs, out long shortenedMs)
        {
            shortenedMs = 0;
            if (helperId == ownerId) return AllianceHelpResult.SelfHelp;
            if (queue != QueueType.Building && queue != QueueType.Healing) return AllianceHelpResult.NoQueue;
            var progress = queue == QueueType.Building ? state.Upgrade?.Help : state.Healing?.Help;
            long finish = queue == QueueType.Building ? state.Upgrade?.FinishAtMs ?? 0 : state.Healing?.FinishAtMs ?? 0;
            if (progress == null) return AllianceHelpResult.NoQueue;
            if (progress.QueueId != queueId) return AllianceHelpResult.WrongQueue;
            if (progress.AllianceId != allianceId || progress.OwnerMembershipEpoch != ownerEpoch)
                return AllianceHelpResult.NotRequested;
            if (finish <= nowMs) return AllianceHelpResult.Finished;
            if (progress.HelpedBy.Contains(helperId)) return AllianceHelpResult.AlreadyHelped;
            if (progress.HelpedBy.Count >= progress.MaxCount) return AllianceHelpResult.HelpLimit;

            long perHelp = Math.Min(60_000, Math.Max(1_000, progress.OriginalDurationMs / 100));
            long remainingBudget = progress.OriginalDurationMs / 5 - progress.ReducedMs;
            shortenedMs = Math.Min(perHelp, Math.Min(remainingBudget, finish - nowMs));
            if (shortenedMs <= 0) return AllianceHelpResult.HelpLimit;
            progress.HelpedBy.Add(helperId);
            progress.ReducedMs += shortenedMs;
            if (queue == QueueType.Building) state.Upgrade.FinishAtMs -= shortenedMs;
            else state.Healing.FinishAtMs -= shortenedMs;
            return AllianceHelpResult.Ok;
        }
    }
}
