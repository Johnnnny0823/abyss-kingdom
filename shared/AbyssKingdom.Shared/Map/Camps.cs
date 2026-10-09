using System.Collections.Generic;
using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Expedition;
using AbyssKingdom.Shared.Heroes;

namespace AbyssKingdom.Shared.Map
{
    /// <summary>地表怪物營地（數值為測試用，之後統一調平衡；見 CampTests 的難度測試）。</summary>
    public static class CampConfig
    {
        /// <summary>被擊敗後多久重新出現。</summary>
        public const long RespawnMs = 10 * 60_000;

        /// <summary>營地強度對應遠征第幾關：Lv.1 ≈ 第 2 關、Lv.2 ≈ 第 4 關、Lv.3 ≈ 第 7 關。</summary>
        public static int StageOf(int level) => level <= 1 ? 2 : level == 2 ? 4 : 7;

        /// <summary>魔物陣容：Lv.N 有 N 支小隊（沿用遠征的魔物數值公式）。</summary>
        public static List<EnemySquad> Lineup(int level, int tile) =>
            ExpeditionConfig.Lineup(StageOf(level), level, tile % 3, 100);

        public static Dictionary<ResourceType, long> ResourceLoot(int level) => new Dictionary<ResourceType, long>
        {
            { ResourceType.Wood, 300L * level },
            { ResourceType.Stone, 300L * level },
            { ResourceType.Food, 300L * level },
            { ResourceType.Crystal, 80L * level },
        };

        public static Dictionary<ItemType, long> ItemLoot(int level, int tile) => new Dictionary<ItemType, long>
        {
            { ItemType.HeroExpSmall, 2L * level },
            { ItemType.DomainScrollPower + tile % 4, 1 },
        };
    }

    public static partial class MarchLogic
    {
        /// <summary>
        /// 這支行軍的戰鬥陣容：人數最多的兵種由主將親自帶領（含主將屬性與四印加成），
        /// 其他兵種各成一隊、由隊長（象徵性 1 點生命）帶領，部隊打光就倒下。
        /// </summary>
        public static List<(SquadInput squad, TroopType troop)> Squads(CityState city, March m)
        {
            var list = new List<(SquadInput, TroopType)>();
            TroopType main = TroopType.Shield;
            long most = -1;
            foreach (var kv in m.Troops)
                if (kv.Value > most) { most = kv.Value; main = kv.Key; }
            HeroConfig.TryGet(m.Hero, out var def);

            var hero = DomainLogic.HeroSquad(city, m.Hero);
            hero.Troop = main;
            hero.Count = m.Troops.TryGetValue(main, out var n) ? n : 0;
            list.Add((hero, main));
            foreach (var t in CityConfig.AllTroops)
            {
                if (t == main || !m.Troops.TryGetValue(t, out var c) || c <= 0) continue;
                list.Add((new SquadInput
                {
                    Leader = "Captain", LeaderAttack = 0, LeaderDefense = 0, LeaderHp = 1,
                    Troop = t, Count = c, Element = def.Element, Polarity = def.Polarity,
                }, t));
            }
            return list;
        }

        /// <summary>抵達怪物營地：開打。部隊損失依比例變成傷兵或陣亡；打贏拿戰利品。打完都自動回城。</summary>
        static MarchEvent FightCamp(CityState city, March m, Feature camp, IFieldRegistry world, string owner)
        {
            var ev = new MarchEvent { MarchId = m.Id, Hero = m.Hero, Tile = m.Destination };
            if (!world.CampAvailable(m.Destination, m.ArriveAt))
            {
                ev.CampGone = true;
                m.Stationed = true;
                return ev;
            }

            var ours = Squads(city, m);
            var inputs = new List<SquadInput>();
            foreach (var s in ours) inputs.Add(s.squad);
            var theirs = new List<SquadInput>();
            foreach (var e in CampConfig.Lineup(camp.Level, m.Destination)) theirs.Add(ExpeditionLogic.EnemySquadInput(e));
            var report = BattleEngine.Run(inputs, theirs, world.NextSeed());

            // 打贏要先搶到「擊敗權」：兩支部隊同時抵達時只有一方算數，另一方當作營地已經不在
            bool won = report.Outcome == BattleOutcome.AttackerWins;
            if (won && !world.TryDefeatCamp(m.Destination, m.ArriveAt))
            {
                ev.CampGone = true;
                m.Stationed = true;
                return ev;
            }
            DomainLogic.AwardFromBattle(city, new List<HeroId> { m.Hero }, report);

            var losses = new Dictionary<TroopType, long>();
            for (int i = 0; i < ours.Count; i++)
            {
                var t = ours[i].troop;
                long left = report.Attackers[i].TroopsLeft;
                losses[t] = m.Troops[t] - left;
                m.Troops[t] = left;
            }
            var (wounded, dead) = CityLogic.TakeCasualties(city, losses);
            ev.Wounded = wounded;
            ev.Dead = dead;

            if (won)
            {
                ev.BattleWon = true;
                foreach (var kv in CampConfig.ResourceLoot(camp.Level)) Add(m.Carry, kv.Key, kv.Value);
                foreach (var kv in CampConfig.ItemLoot(camp.Level, m.Destination)) Add(m.LootItems, kv.Key, kv.Value);
                ev.Carry = new Dictionary<ResourceType, long>(m.Carry);
                ev.LootItems = new Dictionary<ItemType, long>(m.LootItems);
            }
            else ev.BattleLost = true;

            TurnBack(city, m, m.ArriveAt);
            return ev;
        }

        static void Add<TKey>(Dictionary<TKey, long> d, TKey k, long v) => d[k] = (d.TryGetValue(k, out var c) ? c : 0) + v;
    }
}
