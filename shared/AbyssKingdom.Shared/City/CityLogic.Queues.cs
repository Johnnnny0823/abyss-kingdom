using System;

namespace AbyssKingdom.Shared.City
{
    /// <summary>
    /// 隊列操作：取消、免費加速、加速道具。
    /// 加速只把完成時間往前移，不在這裡完成工程——完成交給下一次 Advance，
    /// 這樣伺服器能用同一個地方偵測「完成」並推播通知。
    /// </summary>
    public static partial class CityLogic
    {
        /// <summary>
        /// 取消隊列。建築：退還該級成本的一定比例。
        /// 訓練：已訓練完的士兵照算入營，尚未訓練的部分退還一定比例資源。
        /// 治療：傷兵回到醫館，退還一定比例資源。
        /// </summary>
        public static CancelResult Cancel(CityState state, QueueType queue, long nowMs)
        {
            if (!Enum.IsDefined(typeof(QueueType), queue)) return CancelResult.UnknownQueue;
            Advance(state, nowMs);

            if (queue == QueueType.Building)
            {
                var job = state.Upgrade;
                if (job == null) return CancelResult.NothingToCancel;
                foreach (var r in CityConfig.AllResources)
                    Refund(state, r, CityConfig.UpgradeCost(job.Building, job.TargetLevel, r));
                state.Upgrade = null;
                return CancelResult.Ok;
            }

            if (queue == QueueType.Healing)
            {
                var heal = state.Healing;
                if (heal == null) return CancelResult.NothingToCancel;
                foreach (var kv in heal.Troops) state.Wounded[kv.Key] = (state.Wounded.TryGetValue(kv.Key, out var w) ? w : 0) + kv.Value;
                foreach (var r in CityConfig.AllResources) Refund(state, r, HealCost(heal.Troops, r));
                state.Healing = null;
                return CancelResult.Ok;
            }

            var training = state.Training;
            if (training == null) return CancelResult.NothingToCancel;
            long done = TrainedSoFar(training, nowMs);
            long rest = training.Count - done;
            state.Troops[training.Troop] = state.GetTroops(training.Troop) + done;
            foreach (var r in CityConfig.AllResources)
                Refund(state, r, CityConfig.TroopCost(training.Troop, r) * rest);
            state.Training = null;
            return CancelResult.Ok;
        }

        /// <summary>訓練中已完成幾名：剩餘時間換算成「還沒練完的人數」（無條件進位），其餘算完成。加速後也正確。</summary>
        public static long TrainedSoFar(TrainingJob job, long nowMs)
        {
            long perUnit = CityConfig.TroopTrainMsPerUnit(job.Troop);
            long remaining = Math.Max(0, job.FinishAtMs - nowMs);
            long remainingUnits = Math.Min(job.Count, (remaining + perUnit - 1) / perUnit);
            return job.Count - remainingUnits;
        }

        /// <summary>剩餘時間在免費門檻內時，立即完成（完成時間設為現在）。</summary>
        public static SpeedupResult SpeedupFree(CityState state, QueueType queue, long nowMs)
        {
            if (!Enum.IsDefined(typeof(QueueType), queue)) return SpeedupResult.UnknownQueue;
            Advance(state, nowMs);
            long? finish = FinishAt(state, queue);
            if (finish == null) return SpeedupResult.NothingInQueue;
            if (finish.Value - nowMs > CityConfig.FreeSpeedupMs) return SpeedupResult.NotFreeYet;
            SetFinishAt(state, queue, nowMs);
            return SpeedupResult.Ok;
        }

        /// <summary>使用 count 個加速道具，縮短隊列剩餘時間（最多縮到現在）。</summary>
        public static SpeedupResult UseSpeedupItem(CityState state, QueueType queue, ItemType item, long count, long nowMs)
        {
            if (!Enum.IsDefined(typeof(QueueType), queue)) return SpeedupResult.UnknownQueue;
            if (!ItemConfig.TryGetSpeedup(item, out var category, out var msEach)) return SpeedupResult.UnknownItem;
            if (count <= 0 || count > 1000) return SpeedupResult.InvalidCount;
            if (!ItemConfig.CanApply(category, queue)) return SpeedupResult.ItemNotApplicable;
            if (state.GetItemCount(item) < count) return SpeedupResult.NotEnoughItems;

            Advance(state, nowMs);
            long? finish = FinishAt(state, queue);
            if (finish == null) return SpeedupResult.NothingInQueue;

            state.Items[item] = state.GetItemCount(item) - count;
            SetFinishAt(state, queue, Math.Max(nowMs, finish.Value - msEach * count));
            return SpeedupResult.Ok;
        }

        /// <summary>發放新手道具（每個城市只發一次）。回傳這次是否有發放。</summary>
        public static bool GrantStarterItemsOnce(CityState state)
        {
            if (state.StarterItemsGranted) return false;
            foreach (var item in ItemConfig.StarterItems)
                state.Items[item.Key] = state.GetItemCount(item.Key) + item.Value;
            state.StarterItemsGranted = true;
            return true;
        }

        static void Refund(CityState state, ResourceType r, long cost)
        {
            if (cost <= 0) return;
            state.Resources[r] = state.GetResource(r) + cost * CityConfig.CancelRefundPercent / 100;
        }

        static long? FinishAt(CityState state, QueueType queue)
        {
            if (queue == QueueType.Building) return state.Upgrade?.FinishAtMs;
            if (queue == QueueType.Healing) return state.Healing?.FinishAtMs;
            return state.Training?.FinishAtMs;
        }

        static void SetFinishAt(CityState state, QueueType queue, long finishAtMs)
        {
            if (queue == QueueType.Building) state.Upgrade.FinishAtMs = finishAtMs;
            else if (queue == QueueType.Healing) state.Healing.FinishAtMs = finishAtMs;
            else state.Training.FinishAtMs = finishAtMs;
        }
    }
}
