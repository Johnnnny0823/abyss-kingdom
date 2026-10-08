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
        /// <summary>新手護盾：建立帳號（或這個功能上線後第一次登入）起 24 小時，不能被攻打。</summary>
        public const long NewbieShieldMs = 24 * 3_600_000L;

        /// <summary>每種基礎資源受保護的量（倉庫等級越高保護越多），超過的部分才能被掠奪。</summary>
        public static long Protected(int warehouseLevel) => 1000 + 500L * Math.Max(0, warehouseLevel);

        /// <summary>打贏時，每種資源可掠奪「超過保護量部分」的比例（%）。</summary>
        public const long PlunderPercent = 20;

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

    /// <summary>攻城的結果：先在守方身上結算（扣兵、掠奪），再交給攻方套用。</summary>
    public sealed class PvpOutcome
    {
        public bool Shielded;            // 對方有新手護盾，沒有開打
        public bool AttackerWon;
        public BattleResult Report;
        public Dictionary<TroopType, long> AttackerLosses = new Dictionary<TroopType, long>();
        public Dictionary<TroopType, long> AttackerSurvivors = new Dictionary<TroopType, long>();
        public Dictionary<ResourceType, long> Plunder = new Dictionary<ResourceType, long>();
        public Dictionary<TroopType, long> DefenderWounded = new Dictionary<TroopType, long>();
        public Dictionary<TroopType, long> DefenderDead = new Dictionary<TroopType, long>();
    }

    public static class PvpLogic
    {
        public static bool IsShielded(CityState city, long nowMs) => city.ShieldUntilMs > nowMs;

        /// <summary>發新手護盾（每個城市一次）。回傳這次是否有發。</summary>
        public static bool GrantNewbieShieldOnce(CityState city, long nowMs)
        {
            if (city.NewbieShieldGranted) return false;
            city.NewbieShieldGranted = true;
            city.ShieldUntilMs = Math.Max(city.ShieldUntilMs, nowMs + PvpConfig.NewbieShieldMs);
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
        public static PvpOutcome ResolveOnDefender(CityState defender, List<(SquadInput squad, TroopType troop)> attackers, ulong seed, long nowMs)
        {
            var o = new PvpOutcome();
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
            foreach (var d in def) dIn.Add(d.squad);
            var report = BattleEngine.Run(aIn, dIn, seed);
            o.Report = report;
            o.AttackerWon = report.Outcome == BattleOutcome.AttackerWins;

            for (int i = 0; i < attackers.Count; i++)
            {
                var t = attackers[i].troop;
                long left = report.Attackers[i].TroopsLeft;
                o.AttackerSurvivors[t] = left;
                o.AttackerLosses[t] = attackers[i].squad.Count - left;
            }

            var defLosses = new Dictionary<TroopType, long>();
            for (int i = 0; i < def.Count; i++)
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

        /// <summary>
        /// 攻方套用攻城結果（呼叫者要持有攻方的鎖）：倖存部隊、傷兵進自己的醫館、帶著掠奪品回城。
        /// 行軍已經不在（例如剛好被召回）就回傳 null。
        /// </summary>
        public static MarchEvent FinishAttack(CityState attacker, long marchId, PvpOutcome o, long nowMs)
        {
            var m = attacker.Surface.Marches.Find(x => x.Id == marchId);
            if (m == null || !m.Sieging) return null;
            m.Sieging = false;
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
