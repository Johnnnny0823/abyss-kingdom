using System;
using System.Collections.Generic;

namespace AbyssKingdom.Shared.City
{
    public enum HealResult
    {
        Ok = 0,
        NothingToHeal = 1,
        NotEnoughWounded = 2,
        QueueBusy = 3,
        NotEnoughResources = 4,
    }

    /// <summary>醫館的治療工程：一次治療一批傷兵（可混兵種），完成後回到兵營。</summary>
    public class HealingJob
    {
        public Dictionary<TroopType, long> Troops { get; set; } = new Dictionary<TroopType, long>();
        public long StartedAtMs { get; set; }
        public long FinishAtMs { get; set; }

        public long Total()
        {
            long n = 0;
            foreach (var v in Troops.Values) n += v;
            return n;
        }
    }

    /// <summary>
    /// 傷兵與醫館。戰鬥中倒下的士兵，一定比例變成傷兵送進醫館（受醫館容量限制），其餘陣亡。
    /// 傷兵花資源與時間治療後回到兵營。
    /// </summary>
    public static partial class CityLogic
    {
        public static long WoundedTotal(CityState state)
        {
            long n = 0;
            foreach (var v in state.Wounded.Values) n += v;
            return n;
        }

        /// <summary>醫館還能收多少傷兵（治療中的也佔位）。</summary>
        public static long HospitalFree(CityState state)
        {
            long used = WoundedTotal(state) + (state.Healing?.Total() ?? 0);
            return Math.Max(0, CityConfig.HospitalCapacity(state.GetLevel(BuildingType.Hospital)) - used);
        }

        /// <summary>
        /// 戰鬥損失：losses 中 WoundedPercent% 送醫館（容量不夠就只收得下的部分），其餘陣亡。
        /// 回傳 (傷兵, 陣亡) 各兵種數量。
        /// </summary>
        public static (Dictionary<TroopType, long> wounded, Dictionary<TroopType, long> dead) TakeCasualties(CityState state, Dictionary<TroopType, long> losses)
        {
            var wounded = new Dictionary<TroopType, long>();
            var dead = new Dictionary<TroopType, long>();
            long free = HospitalFree(state);
            foreach (var t in CityConfig.AllTroops)
            {
                if (!losses.TryGetValue(t, out var lost) || lost <= 0) continue;
                long w = Math.Min(free, lost * CityConfig.WoundedPercent / 100);
                free -= w;
                if (w > 0)
                {
                    wounded[t] = w;
                    state.Wounded[t] = (state.Wounded.TryGetValue(t, out var cur) ? cur : 0) + w;
                }
                if (lost - w > 0) dead[t] = lost - w;
            }
            return (wounded, dead);
        }

        public static long HealDurationMs(Dictionary<TroopType, long> troops)
        {
            long ms = 0;
            foreach (var kv in troops) ms += CityConfig.HealMsPerUnit(kv.Key) * kv.Value;
            return ms;
        }

        public static long HealCost(Dictionary<TroopType, long> troops, ResourceType r)
        {
            long c = 0;
            foreach (var kv in troops) c += CityConfig.HealCost(kv.Key, r) * kv.Value;
            return c;
        }

        public static HealResult CanHeal(CityState state, Dictionary<TroopType, long> troops)
        {
            long total = 0;
            foreach (var kv in troops ?? new Dictionary<TroopType, long>())
            {
                if (kv.Value < 0) return HealResult.NothingToHeal;
                if ((state.Wounded.TryGetValue(kv.Key, out var w) ? w : 0) < kv.Value) return HealResult.NotEnoughWounded;
                total += kv.Value;
            }
            if (total <= 0) return HealResult.NothingToHeal;
            if (state.Healing != null) return HealResult.QueueBusy;
            foreach (var r in CityConfig.AllResources)
                if (state.GetResource(r) < HealCost(troops, r)) return HealResult.NotEnoughResources;
            return HealResult.Ok;
        }

        public static HealResult StartHealing(CityState state, Dictionary<TroopType, long> troops, long nowMs)
        {
            Advance(state, nowMs);
            var check = CanHeal(state, troops);
            if (check != HealResult.Ok) return check;
            var job = new HealingJob { StartedAtMs = nowMs };
            foreach (var kv in troops)
            {
                if (kv.Value <= 0) continue;
                job.Troops[kv.Key] = kv.Value;
                state.Wounded[kv.Key] -= kv.Value;
            }
            foreach (var r in CityConfig.AllResources)
                state.Resources[r] = state.GetResource(r) - HealCost(job.Troops, r);
            job.FinishAtMs = nowMs + HealDurationMs(job.Troops);
            state.Healing = job;
            return HealResult.Ok;
        }

        /// <summary>治療完成：傷兵回到兵營。回傳完成的工程（沒有則 null）。</summary>
        static HealingJob CompleteHealing(CityState state, long nowMs)
        {
            var job = state.Healing;
            if (job == null || job.FinishAtMs > nowMs) return null;
            foreach (var kv in job.Troops) state.Troops[kv.Key] = state.GetTroops(kv.Key) + kv.Value;
            state.Healing = null;
            return job;
        }
    }
}
