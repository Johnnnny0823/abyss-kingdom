using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Shared.Heroes
{
    /// <summary>四境（設計見 docs/design/SYSTEMS.md 第 2 節）。玩家看到的是「四股索」：崩索／結索／墜索／錨索（客戶端 DisplayNames）。</summary>
    public enum Domain
    {
        Power = 0,       // 震境：力
        Technique = 1,   // 巧境：技
        Speed = 2,       // 迅境：速
        Durability = 3,  // 磐境：韌
    }

    /// <summary>通達幾境決定稱號（2026-10-08 使用者定案：每多一境一個稱號）。</summary>
    public enum DomainTier
    {
        Unbound = 0,       // 尚未繫索
        Roped = 1,         // 繫索者
        Delver = 2,        // 下探者
        Deepwalker = 3,    // 深行者
        AbyssReacher = 4,  // 抵淵者（四境皆通，頂點）
    }

    public enum DomainResult
    {
        Ok = 0,
        UnknownHero = 1,
        HeroNotOwned = 2,
        AlreadyMastered = 3,
        NotReady = 4,
        NotScroll = 5,
        InvalidCount = 6,
        NotEnoughItems = 7,
        TrialFailed = 8,
    }

    public sealed class DomainTrialResult
    {
        public HeroId Hero { get; set; }
        public Domain Domain { get; set; }
        public bool Success { get; set; }
        public BattleResult Report { get; set; }
    }

    public static class DomainConfig
    {
        public static readonly Domain[] All = (Domain[])Enum.GetValues(typeof(Domain));

        /// <summary>通達下一境所需修練值：已通達越多越難（1000、2000、3000、4000）。</summary>
        public static long XpToMaster(int masteredCount) => 1000L * (masteredCount + 1);

        /// <summary>一場戰鬥每一境最多獲得的修練值（避免單場暴衝）。</summary>
        public const long MaxXpPerBattle = 400;

        // 通達後的戰鬥加成
        public const long PowerAttackPercent = 115;
        public const long DurabilityDamageTakenPercent = 85;
        public const int SpeedBonus = 2;
        public const int TechniqueCounterBonus = 15;

        public static DomainTier Tier(int masteredCount) => (DomainTier)Math.Max(0, Math.Min(4, masteredCount));
    }

    public static class DomainLogic
    {
        public static long Xp(HeroState h, Domain d) => h.DomainXp.TryGetValue(d, out var v) ? v : 0;

        public static bool IsMastered(HeroState h, Domain d) => h.Mastered.Contains(d);

        /// <summary>修練值已滿、可以挑戰通達試煉。</summary>
        public static bool ReadyForTrial(HeroState h, Domain d) =>
            !IsMastered(h, d) && Xp(h, d) >= DomainConfig.XpToMaster(h.Mastered.Count);

        /// <summary>增加修練值；已通達的境不再累積，未通達的最多累積到門檻（滿了就等試煉）。</summary>
        public static void AddXp(HeroState h, Domain d, long amount)
        {
            if (amount <= 0 || IsMastered(h, d)) return;
            long cap = DomainConfig.XpToMaster(h.Mastered.Count);
            h.DomainXp[d] = Math.Min(cap, Xp(h, d) + amount);
        }

        /// <summary>把已通達的四境加成套到小隊上。</summary>
        public static void ApplyBonuses(HeroState h, SquadInput s)
        {
            if (IsMastered(h, Domain.Power)) s.AttackPercent = s.AttackPercent * DomainConfig.PowerAttackPercent / 100;
            if (IsMastered(h, Domain.Durability)) s.DamageTakenPercent = s.DamageTakenPercent * DomainConfig.DurabilityDamageTakenPercent / 100;
            if (IsMastered(h, Domain.Speed)) s.SpeedBonus += DomainConfig.SpeedBonus;
            if (IsMastered(h, Domain.Technique)) s.CounterBonus += DomainConfig.TechniqueCounterBonus;
        }

        /// <summary>
        /// 依戰鬥表現發放修練值（表現為主）：
        /// 震 = 造成傷害 ÷ 50；巧 = 打出克制的次數 × 25；迅 = 搶先出手回合 × 40；磐 = 承受傷害 ÷ 50（倒下只算一半）。
        /// </summary>
        public static void AwardFromBattle(CityState city, IList<HeroId> team, BattleResult report, BattleSide side = BattleSide.Attacker)
        {
            var squads = side == BattleSide.Attacker ? report.Attackers : report.Defenders;
            for (int i = 0; i < team.Count && i < squads.Count; i++)
            {
                if (!city.Heroes.TryGetValue(team[i], out var hero)) continue;
                var s = squads[i];
                long counters = 0;
                foreach (var a in report.Actions)
                    if (a.Side == side && a.Actor == i && a.CounterPercent > 100) counters++;
                long cap = DomainConfig.MaxXpPerBattle;
                AddXp(hero, Domain.Power, Math.Min(cap, s.DamageDealt / 50));
                AddXp(hero, Domain.Technique, Math.Min(cap, counters * 25));
                AddXp(hero, Domain.Speed, Math.Min(cap, s.FirstStrikes * 40));
                AddXp(hero, Domain.Durability, Math.Min(cap, s.DamageTaken / (s.LeaderStanding ? 50 : 100)));
            }
        }

        /// <summary>使用境書定向補修練值（材料為輔）。</summary>
        public static DomainResult UseScroll(CityState city, HeroId id, ItemType item, long count)
        {
            if (!HeroConfig.TryGet(id, out _)) return DomainResult.UnknownHero;
            if (!city.Heroes.TryGetValue(id, out var hero)) return DomainResult.HeroNotOwned;
            if (!ItemConfig.TryGetDomainScroll(item, out var domain, out var xpEach)) return DomainResult.NotScroll;
            if (count <= 0 || count > 1000) return DomainResult.InvalidCount;
            if (city.GetItemCount(item) < count) return DomainResult.NotEnoughItems;
            if (IsMastered(hero, domain)) return DomainResult.AlreadyMastered;
            city.Items[item] = city.GetItemCount(item) - count;
            AddXp(hero, domain, xpEach * count);
            return DomainResult.Ok;
        }

        /// <summary>
        /// 通達試煉（夢魘試煉）：主將單獨帶兵挑戰一場主題戰鬥。難度隨已通達數量提高。
        ///  震境：打破重甲——敵人不會攻擊但極硬，20 回合內擊倒才算過。
        ///  巧境：逆境——敵人是剋制你的兵種。
        ///  迅境：搶先——敵人攻擊很高但脆弱，誰先倒下誰輸。
        ///  磐境：撐住——敵人打不死，主將撐過 20 回合不倒就算過。
        /// </summary>
        public static DomainResult Trial(CityState city, HeroId id, Domain domain, ulong seed, out DomainTrialResult result)
        {
            result = null;
            if (!HeroConfig.TryGet(id, out var def)) return DomainResult.UnknownHero;
            if (!city.Heroes.TryGetValue(id, out var hero)) return DomainResult.HeroNotOwned;
            if (IsMastered(hero, domain)) return DomainResult.AlreadyMastered;
            if (!ReadyForTrial(hero, domain)) return DomainResult.NotReady;

            var ours = HeroSquad(city, id);
            var enemy = TrialEnemy(domain, def.Specialty, hero.Mastered.Count);
            var report = BattleEngine.Run(new List<SquadInput> { ours }, new List<SquadInput> { enemy }, seed);
            bool success = domain == Domain.Durability
                ? report.Attackers[0].LeaderStanding
                : report.Outcome == BattleOutcome.AttackerWins;

            result = new DomainTrialResult { Hero = id, Domain = domain, Success = success, Report = report };
            if (success) hero.Mastered.Add(domain);
            return DomainResult.Ok;
        }

        /// <summary>主將帶「統率」數量的專精兵種，並套上已通達的四境加成（遠征與試煉共用）。</summary>
        public static SquadInput HeroSquad(CityState city, HeroId id)
        {
            HeroConfig.TryGet(id, out var def);
            var s = PowerLogic.TotalStats(city, id);
            var squad = new SquadInput
            {
                Leader = id.ToString(),
                LeaderAttack = s.Attack, LeaderDefense = s.Defense, LeaderHp = s.Hp,
                Troop = def.Specialty,
                Count = s.Leadership,
                Element = def.Element,
                Polarity = def.Polarity,
            };
            if (city.Heroes.TryGetValue(id, out var hero)) ApplyBonuses(hero, squad);
            return squad;
        }

        /// <summary>試煉敵人。tier = 已通達數量（0~3），越後面越強。數值見 DomainTests 的難度測試。</summary>
        public static SquadInput TrialEnemy(Domain domain, TroopType heroTroop, int tier)
        {
            long t = tier;
            var e = new SquadInput
            {
                Leader = "Trial" + domain,
                Troop = TroopType.Shield,
                Count = 220 + 140 * t,
                LeaderAttack = 100 + 70 * t,
                LeaderDefense = 100 + 60 * t,
                LeaderHp = 1500 + 1200 * t,
            };
            switch (domain)
            {
                case Domain.Power:
                    // 不還手的重甲：只考驗 20 回合內的總輸出
                    e.Troop = TroopType.Shield;
                    e.AttackPercent = 0;
                    e.LeaderDefense *= 3;
                    e.LeaderHp *= 2;
                    e.Count = 800 + 300 * t;
                    break;
                case Domain.Technique:
                    e.Troop = CounterOf(heroTroop); // 敵人剋制你
                    break;
                case Domain.Speed:
                    // 同兵種（不受克制影響）但出手更快、攻擊更高、比較脆：誰先倒下誰輸
                    e.Troop = heroTroop;
                    e.SpeedBonus = 5;
                    e.AttackPercent = 150;
                    e.LeaderHp /= 2;
                    break;
                case Domain.Durability:
                    // 打不死的敵人：只考驗主將能不能撐過 20 回合
                    e.Troop = TroopType.Crossbow;
                    e.Count = 220 + 60 * t;
                    e.AttackPercent = 20 + 3 * t;
                    e.DamageTakenPercent = 0;
                    break;
            }
            return e;
        }

        /// <summary>回傳剋制 t 的兵種。</summary>
        public static TroopType CounterOf(TroopType t)
        {
            foreach (var x in CityConfig.AllTroops) if (BattleConfig.Counters(x, t)) return x;
            return t;
        }
    }
}
