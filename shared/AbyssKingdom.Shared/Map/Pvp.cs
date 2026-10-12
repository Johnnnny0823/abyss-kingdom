using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;

namespace AbyssKingdom.Shared.Map
{
    /// <summary>攻城的數值（測試用，之後統一調平衡）。</summary>
    public static class PvpConfig
    {
        /// <summary>新手護盾：建立帳號（或這個功能上線後第一次登入）起 7 天，不能被攻打（2026-10-08 使用者定案）。</summary>
        public const long NewbieShieldMs = 7 * 24 * 3_600_000L;

        /// <summary>早期版本只發 24 小時；還在護盾中的帳號補足到 7 天。</summary>
        public const long OldNewbieShieldMs = 24 * 3_600_000L;

        /// <summary>每種基礎資源受保護的量（倉庫等級越高保護越多），超過的部分才能被掠奪。</summary>
        public static long Protected(int warehouseLevel) => 1000 + 500L * Math.Max(0, warehouseLevel);

        /// <summary>打贏時，每種資源可掠奪「超過保護量部分」的比例（%）。</summary>
        public const long PlunderPercent = 20;

        /// <summary>守方的攻城紀錄保留多久（正常情況攻方套用完就刪；這是當機後留下的紀錄的上限）。</summary>
        public const long SiegeLogKeepMs = 30L * 24 * 3_600_000;

        /// <summary>城防（城牆）：主堡等級越高越硬。部隊打光了也要先打穿城防。</summary>
        public static SquadInput Wall(int keepLevel)
        {
            long k = Math.Max(1, keepLevel);
            return new SquadInput
            {
                Leader = "Wall", LeaderAttack = 20 * k, LeaderDefense = 30 * k, LeaderHp = 800 * k,
                Troop = TroopType.Shield, Count = 0,
            };
        }
    }

    /// <summary>攻城的結果：先在守方身上結算（扣兵、掠奪），再交給攻方套用。也存在守方的攻城紀錄裡（所以用屬性，才會被存檔）。</summary>
    public sealed class PvpOutcome
    {
        /// <summary>對方有新手護盾，沒有開打。</summary>
        public bool Shielded { get; set; }
        public bool AttackerWon { get; set; }
        /// <summary>精簡過的戰報：只留攻方小隊與攻方的剋制出手（攻方主將累積修練值用得到的部分）。</summary>
        public BattleResult Report { get; set; }
        public Dictionary<TroopType, long> AttackerLosses { get; set; } = new Dictionary<TroopType, long>();
        public Dictionary<TroopType, long> AttackerSurvivors { get; set; } = new Dictionary<TroopType, long>();
        public Dictionary<ResourceType, long> Plunder { get; set; } = new Dictionary<ResourceType, long>();
        public Dictionary<TroopType, long> DefenderWounded { get; set; } = new Dictionary<TroopType, long>();
        public Dictionary<TroopType, long> DefenderDead { get; set; } = new Dictionary<TroopType, long>();
        /// <summary>在守方身上結算的時間（攻城紀錄過期清理用）。</summary>
        public long AtMs { get; set; }
        public GarrisonOutcome Garrison { get; set; }
        public int DefenseBasisPoints { get; set; } = 10_000;
    }

    public static class PvpLogic
    {
        public static bool IsShielded(CityState city, long nowMs) => city.ShieldUntilMs > nowMs;

        /// <summary>
        /// 發新手護盾（每個城市一次）。回傳這次是否有改動。
        /// 拿過舊版 24 小時護盾、而且護盾還在（沒有攻打過別人）的帳號，補足到 7 天。
        /// </summary>
        public static bool GrantNewbieShieldOnce(CityState city, long nowMs)
        {
            if (city.NewbieShieldVersion >= 2) return false;
            if (!city.NewbieShieldGranted)
                city.ShieldUntilMs = Math.Max(city.ShieldUntilMs, nowMs + PvpConfig.NewbieShieldMs);
            else if (city.ShieldUntilMs > nowMs)
                city.ShieldUntilMs += PvpConfig.NewbieShieldMs - PvpConfig.OldNewbieShieldMs;
            city.NewbieShieldGranted = true;
            city.NewbieShieldVersion = 2;
            return true;
        }

        /// <summary>
        /// 守城陣容：兵營裡的部隊。人數最多的兵種由戰力最高、沒有出征的主將帶領，其他兵種由隊長帶；
        /// 最後一定有城防。沒有部隊時只剩城防。
        /// </summary>
        public static List<(SquadInput squad, TroopType? troop)> DefenseSquads(CityState city)
        {
            var list = new List<(SquadInput, TroopType?)>();
            HeroId? best = null;
            long bestPower = -1;
            var busy = new HashSet<HeroId>();
            foreach (var m in city.Surface.Marches) busy.Add(m.Hero);
            foreach (var h in city.Heroes.Keys)
            {
                if (busy.Contains(h)) continue;
                long p = PowerLogic.HeroPower(city, h);
                if (p > bestPower) { bestPower = p; best = h; }
            }

            TroopType main = TroopType.Shield;
            long most = 0;
            foreach (var t in CityConfig.AllTroops)
                if (city.GetTroops(t) > most) { most = city.GetTroops(t); main = t; }

            if (most > 0)
            {
                foreach (var t in CityConfig.AllTroops)
                {
                    long n = city.GetTroops(t);
                    if (n <= 0) continue;
                    SquadInput s;
                    if (t == main && best.HasValue)
                    {
                        s = DomainLogic.HeroSquad(city, best.Value);
                        s.Troop = t;
                        s.Count = n;
                    }
                    else s = new SquadInput { Leader = "Captain", LeaderHp = 1, Troop = t, Count = n };
                    if (t == main) list.Insert(0, (s, t));
                    else list.Add((s, t));
                }
            }
            list.Add((PvpConfig.Wall(city.GetLevel(BuildingType.Keep)), null));
            return list;
        }

        /// <summary>
        /// 在守方城市上結算一次攻城（呼叫者要持有守方的鎖）：開打、守方部隊損失（七成傷兵進醫館）、
        /// 攻方打贏就掠奪資源（受攻方倖存部隊的載重限制）。攻方的狀態不在這裡改，交給 FinishAttack。
        /// </summary>
        public static PvpOutcome ResolveOnDefender(CityState defender, List<(SquadInput squad, TroopType troop)> attackers, ulong seed, long nowMs,
            string battleId = null, GarrisonDefenseInput garrison = null, int defenseBasisPoints = 10_000)
        {
            // 同一場仗重試（伺服器停機後接續）：直接拿回當初的結果，守方不會被打兩次
            if (battleId != null && defender.SiegeLog.TryGetValue(battleId, out var done)) return done;
            if (defenseBasisPoints != 10_000 && defenseBasisPoints != 10_500)
                throw new ArgumentException("Invalid S6 territory defense bonus.");
            var o = Fight(defender, attackers, seed, nowMs, garrison, defenseBasisPoints);
            if (battleId != null && !o.Shielded)
            {
                o.AtMs = nowMs;
                PruneSiegeLog(defender, nowMs);
                defender.SiegeLog[battleId] = o;
            }
            return o;
        }

        /// <summary>攻方已經套用完結果：刪掉守方的這筆攻城紀錄。</summary>
        public static bool ForgetSiege(CityState defender, string battleId) => battleId != null && defender.SiegeLog.Remove(battleId);

        /// <summary>清掉過期的攻城紀錄（當機後沒刪掉的）。</summary>
        public static void PruneSiegeLog(CityState defender, long nowMs)
        {
            var old = new List<string>();
            foreach (var kv in defender.SiegeLog)
                if (nowMs - kv.Value.AtMs > PvpConfig.SiegeLogKeepMs) old.Add(kv.Key);
            foreach (var k in old) defender.SiegeLog.Remove(k);
        }

        /// <summary>精簡戰報：守方小隊與其他出手紀錄不存（攻城紀錄要存進守方存檔，越小越好）。</summary>
        static BattleResult Compact(BattleResult r)
        {
            var c = new BattleResult { Outcome = r.Outcome, Rounds = r.Rounds, Seed = r.Seed, Attackers = r.Attackers };
            foreach (var a in r.Actions)
                if (a.Side == BattleSide.Attacker && a.CounterPercent > 100) c.Actions.Add(a);
            return c;
        }

        static PvpOutcome Fight(CityState defender, List<(SquadInput squad, TroopType troop)> attackers, ulong seed, long nowMs,
            GarrisonDefenseInput garrison, int defenseBasisPoints)
        {
            var o = new PvpOutcome { DefenseBasisPoints = defenseBasisPoints };
            if (IsShielded(defender, nowMs))
            {
                o.Shielded = true;
                foreach (var a in attackers) o.AttackerSurvivors[a.troop] = a.squad.Count;
                return o;
            }

            var def = DefenseSquads(defender);
            var aIn = new List<SquadInput>();
            foreach (var a in attackers) aIn.Add(a.squad);
            var dIn = new List<SquadInput>();
            int ownSquads = def.Count - 1;
            for (int i = 0; i < ownSquads; i++) dIn.Add(def[i].squad);
            if (garrison != null)
            {
                if (garrison.Squads.Count > 3) throw new ArgumentException("One reinforcement has at most three troop squads.");
                var types = new HashSet<TroopType>();
                foreach (var squad in garrison.Squads)
                {
                    if (squad.Count <= 0 || !types.Add(squad.Troop)) throw new ArgumentException("Invalid reinforcement squads.");
                    dIn.Add(squad);
                }
            }
            dIn.Add(def[ownSquads].squad); // Wall always stands behind both owners' troops.
            if (defenseBasisPoints != 10_000)
                for (int i = 0; i < dIn.Count; i++) dIn[i] = dIn[i].WithDefenseBonus(defenseBasisPoints);
            var report = BattleEngine.Run(aIn, dIn, seed,
                garrison == null ? BattleScenario.Standard : BattleScenario.GarrisonDefense);
            o.Report = Compact(report);
            o.AttackerWon = report.Outcome == BattleOutcome.AttackerWins;

            for (int i = 0; i < attackers.Count; i++)
            {
                var t = attackers[i].troop;
                long left = report.Attackers[i].TroopsLeft;
                o.AttackerSurvivors[t] = left;
                o.AttackerLosses[t] = attackers[i].squad.Count - left;
            }

            var defLosses = new Dictionary<TroopType, long>();
            for (int i = 0; i < ownSquads; i++)
            {
                if (!def[i].troop.HasValue) continue;
                var t = def[i].troop.Value;
                long lost = def[i].squad.Count - report.Defenders[i].TroopsLeft;
                defLosses[t] = lost;
                defender.Troops[t] = defender.GetTroops(t) - lost;
            }
            var (w, d2) = CityLogic.TakeCasualties(defender, defLosses);
            o.DefenderWounded = w;
            o.DefenderDead = d2;
            if (garrison != null)
            {
                o.Garrison = new GarrisonOutcome { OwnerId = garrison.OwnerId,
                    ReinforcementId = garrison.ReinforcementId, MarchId = garrison.MarchId };
                for (int i = 0; i < garrison.Squads.Count; i++)
                {
                    var squad = garrison.Squads[i];
                    long left = report.Defenders[ownSquads + i].TroopsLeft;
                    o.Garrison.Survivors[squad.Troop] = left;
                    o.Garrison.Losses[squad.Troop] = squad.Count - left;
                }
            }

            if (o.AttackerWon)
            {
                long load = GatherConfig.Load(o.AttackerSurvivors);
                long protect = PvpConfig.Protected(defender.GetLevel(BuildingType.Warehouse));
                var avail = new Dictionary<ResourceType, long>();
                long total = 0;
                foreach (var r in CityConfig.AllResources)
                {
                    long a = Math.Max(0, defender.GetResource(r) - protect) * PvpConfig.PlunderPercent / 100;
                    avail[r] = a;
                    total += a;
                }
                foreach (var r in CityConfig.AllResources)
                {
                    // 載重不夠時依比例分配
                    long take = total <= load ? avail[r] : avail[r] * load / Math.Max(1, total);
                    if (take <= 0) continue;
                    o.Plunder[r] = take;
                    defender.Resources[r] = defender.GetResource(r) - take;
                }
            }
            return o;
        }

        /// <summary>Apply only on the reinforcement owner's city, after the defender's outcome is durable.</summary>
        public static bool FinishGarrison(CityState owner, GarrisonOutcome outcome, string battleId, long nowMs,
            bool returnRequested)
        {
            if (outcome == null) return true;
            string receipt = "alliance:garrison:" + outcome.ReinforcementId + ":" + battleId;
            if (owner.SocialReceipts.Contains(receipt)) return true;
            var march = owner.Surface.Marches.Find(m => m.Id == outcome.MarchId);
            if (march == null || march.GarrisonSettlementId != battleId) return false;
            foreach (var pair in outcome.Survivors) march.Troops[pair.Key] = pair.Value;
            CityLogic.TakeCasualties(owner, outcome.Losses);
            march.GarrisonSettlementId = null;
            owner.SocialReceipts.Add(receipt);
            if (returnRequested || march.TotalTroops() == 0) MarchLogic.RecallReinforcement(owner, march.Id, nowMs);
            return true;
        }

        /// <summary>
        /// 攻方套用攻城結果（呼叫者要持有攻方的鎖）：倖存部隊、傷兵進自己的醫館、帶著掠奪品回城。
        /// 行軍已經不在（例如剛好被召回）就回傳 null。
        /// </summary>
        public static MarchEvent FinishAttack(CityState attacker, long marchId, PvpOutcome o, long nowMs)
        {
            var m = attacker.Surface.Marches.Find(x => x.Id == marchId);
            if (m == null || !m.Sieging) return null;
            m.Sieging = false;
            m.SiegeTarget = null;
            var ev = new MarchEvent { MarchId = m.Id, Hero = m.Hero, Tile = m.Destination, Shielded = o.Shielded };
            if (!o.Shielded)
            {
                foreach (var kv in o.AttackerSurvivors) m.Troops[kv.Key] = kv.Value;
                var (w, d) = CityLogic.TakeCasualties(attacker, o.AttackerLosses);
                ev.Wounded = w;
                ev.Dead = d;
                if (o.Report != null) DomainLogic.AwardFromBattle(attacker, new List<HeroId> { m.Hero }, o.Report);
                ev.BattleWon = o.AttackerWon;
                ev.BattleLost = !o.AttackerWon;
                foreach (var kv in o.Plunder) m.Carry[kv.Key] = (m.Carry.TryGetValue(kv.Key, out var c) ? c : 0) + kv.Value;
                ev.Carry = new Dictionary<ResourceType, long>(o.Plunder);
            }
            MarchLogic.TurnBack(attacker, m, nowMs);
            return ev;
        }
    }
}
