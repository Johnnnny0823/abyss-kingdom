using System;

namespace AbyssKingdom.Shared.City
{
    /// <summary>
    /// 城建規則。純函式風格：輸入狀態與時間，修改狀態，不依賴系統時鐘。
    /// 伺服器是唯一權威；客戶端只用它來預測顯示（例如倒數、預估產量）。
    /// </summary>
    public static partial class CityLogic
    {
        /// <summary>
        /// 把城市推進到 nowMs：先產資源到升級完成那一刻、完成升級，再產到 nowMs。
        /// 回傳這次是否有升級完成（伺服器可據此推播通知）。
        /// </summary>
        public static UpgradeJob Advance(CityState state, long nowMs)
        {
            return Advance(state, nowMs, out _);
        }

        /// <summary>同上，另外回傳這次完成的訓練（沒有則為 null）。訓練不影響產量，完成時直接入營。</summary>
        public static UpgradeJob Advance(CityState state, long nowMs, out TrainingJob completedTraining)
        {
            completedTraining = null;
            if (state.Training != null && state.Training.FinishAtMs <= nowMs)
            {
                completedTraining = state.Training;
                state.Troops[completedTraining.Troop] = state.GetTroops(completedTraining.Troop) + completedTraining.Count;
                state.Training = null;
            }

            UpgradeJob completed = null;
            if (state.Upgrade != null && state.Upgrade.FinishAtMs <= nowMs)
            {
                Produce(state, state.Upgrade.FinishAtMs);
                completed = state.Upgrade;
                state.Buildings[completed.Building] = completed.TargetLevel;
                state.Upgrade = null;
            }
            Produce(state, nowMs);
            return completed;
        }

        /// <summary>只結算資源產出，不處理升級。時間倒退時不做事。</summary>
        public static void Produce(CityState state, long untilMs)
        {
            long elapsed = untilMs - state.LastSettledAtMs;
            if (elapsed <= 0) return;

            long capacity = CityConfig.Capacity(state.GetLevel(BuildingType.Warehouse));

            foreach (var building in CityConfig.AllBuildings)
            {
                if (!CityConfig.TryGetProducedResource(building, out var resource)) continue;
                long rate = CityConfig.ProductionPerHour(state.GetLevel(building));
                long current = state.GetResource(resource);
                long remainder = state.ProductionRemainder.TryGetValue(resource, out var rem) ? rem : 0;

                if (rate == 0 || current >= capacity)
                {
                    // 滿倉或沒有產量時，不累積餘數（避免清空倉庫後瞬間湧入）
                    state.ProductionRemainder[resource] = 0;
                    continue;
                }

                long total = rate * elapsed + remainder;
                long produced = total / CityConfig.MsPerHour;
                long newRemainder = total % CityConfig.MsPerHour;

                long after = current + produced;
                if (after >= capacity)
                {
                    after = capacity;
                    newRemainder = 0;
                }
                state.Resources[resource] = after;
                state.ProductionRemainder[resource] = newRemainder;
            }

            state.LastSettledAtMs = untilMs;
        }

        /// <summary>檢查能不能升級（不修改狀態）。呼叫前應先 Advance 到現在。</summary>
        public static UpgradeResult CanUpgrade(CityState state, BuildingType building)
        {
            if (!Enum.IsDefined(typeof(BuildingType), building)) return UpgradeResult.UnknownBuilding;
            int current = state.GetLevel(building);
            if (current >= CityConfig.MaxLevel) return UpgradeResult.AlreadyMaxLevel;
            if (state.Upgrade != null) return UpgradeResult.QueueBusy;
            int target = current + 1;
            if (building != BuildingType.Keep && target > state.GetLevel(BuildingType.Keep))
                return UpgradeResult.KeepLevelTooLow;
            foreach (var r in CityConfig.AllResources)
            {
                if (state.GetResource(r) < CityConfig.UpgradeCost(building, target, r))
                    return UpgradeResult.NotEnoughResources;
            }
            return UpgradeResult.Ok;
        }

        /// <summary>開始升級：先結算到現在，檢查條件，扣資源並排入隊列。</summary>
        public static UpgradeResult StartUpgrade(CityState state, BuildingType building, long nowMs)
        {
            Advance(state, nowMs);
            var check = CanUpgrade(state, building);
            if (check != UpgradeResult.Ok) return check;

            int target = state.GetLevel(building) + 1;
            foreach (var r in CityConfig.AllResources)
                state.Resources[r] = state.GetResource(r) - CityConfig.UpgradeCost(building, target, r);

            state.Upgrade = new UpgradeJob
            {
                Building = building,
                TargetLevel = target,
                StartedAtMs = nowMs,
                FinishAtMs = nowMs + CityConfig.UpgradeDurationMs(building, target),
            };
            return UpgradeResult.Ok;
        }

        /// <summary>檢查能不能訓練（不修改狀態）。呼叫前應先 Advance 到現在。</summary>
        public static TrainResult CanTrain(CityState state, TroopType troop, long count)
        {
            if (!Enum.IsDefined(typeof(TroopType), troop)) return TrainResult.UnknownTroop;
            int barracks = state.GetLevel(BuildingType.Barracks);
            if (barracks <= 0) return TrainResult.BarracksRequired;
            if (count <= 0) return TrainResult.InvalidCount;
            if (count > CityConfig.MaxTrainBatch(barracks)) return TrainResult.ExceedsBatchLimit;
            if (state.Training != null) return TrainResult.QueueBusy;
            foreach (var r in CityConfig.AllResources)
            {
                if (state.GetResource(r) < CityConfig.TroopCost(troop, r) * count)
                    return TrainResult.NotEnoughResources;
            }
            return TrainResult.Ok;
        }

        /// <summary>開始訓練：先結算到現在，檢查條件，扣資源並排入訓練隊列。</summary>
        public static TrainResult StartTraining(CityState state, TroopType troop, long count, long nowMs)
        {
            Advance(state, nowMs);
            var check = CanTrain(state, troop, count);
            if (check != TrainResult.Ok) return check;

            foreach (var r in CityConfig.AllResources)
                state.Resources[r] = state.GetResource(r) - CityConfig.TroopCost(troop, r) * count;

            state.Training = new TrainingJob
            {
                Troop = troop,
                Count = count,
                StartedAtMs = nowMs,
                FinishAtMs = nowMs + CityConfig.TroopTrainMsPerUnit(troop) * count,
            };
            return TrainResult.Ok;
        }
    }
}
