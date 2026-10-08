using System;
using System.Collections.Generic;

namespace AbyssKingdom.Shared.Battle
{
    /// <summary>五行。None = 無屬性（例如試煉敵人）。</summary>
    public enum Element
    {
        None = 0,
        Wood = 1,   // 木
        Fire = 2,   // 火
        Earth = 3,  // 土
        Metal = 4,  // 金
        Water = 5,  // 水
    }

    public enum Polarity
    {
        None = 0,
        Yang = 1,  // 陽
        Yin = 2,   // 陰
    }

    /// <summary>隊伍屬性加成（可同時觸發多個，效果相加）。</summary>
    public enum Synergy
    {
        Triad = 1,        // 三合：3 位以上同屬性
        Quintet = 2,      // 五同：5 位同屬性（取代三合）
        ThreeTalents = 3, // 三才：3~4 位且屬性全不同
        FiveComplete = 4, // 五行俱全：5 位、五行到齊
        Chain3 = 5,       // 相生三環
        Chain4 = 6,       // 相生四環
        Chain5 = 7,       // 相生五環
        Harmony = 8,      // 陰陽調和
        PureYang = 9,     // 純陽
        PureYin = 10,     // 純陰
    }

    /// <summary>
    /// 五行陰陽（十天干）：克制規則與隊伍加成。設計見 docs/design/SYSTEMS.md「五行陰陽」。
    /// 克制：木剋土、土剋水、水剋火、火剋金、金剋木。相生：木生火、火生土、土生金、金生水、水生木。
    /// </summary>
    public static class Elements
    {
        public static readonly Element[] All = { Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water };

        public const int SamePolarityOvercome = 125;   // 七殺：同性相剋
        public const int CrossPolarityOvercome = 115;  // 正官：異性相剋
        public const int Overcome = 85;                // 被剋

        /// <summary>a 剋 b？</summary>
        public static bool Overcomes(Element a, Element b)
        {
            return (a == Element.Wood && b == Element.Earth)
                || (a == Element.Earth && b == Element.Water)
                || (a == Element.Water && b == Element.Fire)
                || (a == Element.Fire && b == Element.Metal)
                || (a == Element.Metal && b == Element.Wood);
        }

        /// <summary>a 生 b？</summary>
        public static bool Generates(Element a, Element b)
        {
            return (a == Element.Wood && b == Element.Fire)
                || (a == Element.Fire && b == Element.Earth)
                || (a == Element.Earth && b == Element.Metal)
                || (a == Element.Metal && b == Element.Water)
                || (a == Element.Water && b == Element.Wood);
        }

        /// <summary>攻擊方對目標的五行傷害百分比（100 = 無影響）。</summary>
        public static int Percent(Element attacker, Polarity ap, Element target, Polarity tp)
        {
            if (attacker == Element.None || target == Element.None) return 100;
            if (Overcomes(attacker, target))
                return ap != Polarity.None && ap == tp ? SamePolarityOvercome : CrossPolarityOvercome;
            if (Overcomes(target, attacker)) return Overcome;
            return 100;
        }

        /// <summary>依隊伍成員的屬性算出觸發的加成。</summary>
        public static List<Synergy> Synergies(IList<(Element element, Polarity polarity)> team)
        {
            var result = new List<Synergy>();
            var members = new List<(Element element, Polarity polarity)>();
            foreach (var m in team) if (m.element != Element.None) members.Add(m);
            int n = members.Count;
            if (n < 2) return result;

            var counts = new Dictionary<Element, int>();
            foreach (var m in members) counts[m.element] = (counts.TryGetValue(m.element, out var c) ? c : 0) + 1;
            int maxSame = 0;
            foreach (var c in counts.Values) maxSame = Math.Max(maxSame, c);

            if (maxSame >= 5) result.Add(Synergy.Quintet);
            else if (maxSame >= 3) result.Add(Synergy.Triad);

            if (counts.Count == n && n >= 5) result.Add(Synergy.FiveComplete);
            else if (counts.Count == n && n >= 3) result.Add(Synergy.ThreeTalents);

            int chain = LongestChain(counts.Keys);
            if (chain >= 5) result.Add(Synergy.Chain5);
            else if (chain == 4) result.Add(Synergy.Chain4);
            else if (chain == 3) result.Add(Synergy.Chain3);

            int yang = 0, yin = 0;
            foreach (var m in members)
            {
                if (m.polarity == Polarity.Yang) yang++;
                else if (m.polarity == Polarity.Yin) yin++;
            }
            if (yang > 0 && yin > 0 && Math.Abs(yang - yin) <= 1) result.Add(Synergy.Harmony);
            if (yin == 0 && yang >= 3) result.Add(Synergy.PureYang);
            if (yang == 0 && yin >= 3) result.Add(Synergy.PureYin);
            return result;
        }

        /// <summary>隊伍裡（不重複的）屬性能串成的最長相生鏈。</summary>
        public static int LongestChain(IEnumerable<Element> present)
        {
            var set = new HashSet<Element>(present);
            set.Remove(Element.None);
            if (set.Count == 5) return 5;
            int best = set.Count > 0 ? 1 : 0;
            foreach (var start in set)
            {
                int len = 1;
                var cur = start;
                while (true)
                {
                    var next = Next(cur);
                    if (!set.Contains(next) || next == start) break;
                    len++;
                    cur = next;
                }
                best = Math.Max(best, len);
            }
            return best;
        }

        static Element Next(Element e)
        {
            switch (e)
            {
                case Element.Wood: return Element.Fire;
                case Element.Fire: return Element.Earth;
                case Element.Earth: return Element.Metal;
                case Element.Metal: return Element.Water;
                default: return Element.Wood;
            }
        }

        /// <summary>加成效果：(攻擊 +%, 受傷 -%)。</summary>
        public static (int attack, int damageReduce) Effect(Synergy s)
        {
            switch (s)
            {
                case Synergy.Triad: return (8, 0);
                case Synergy.Quintet: return (18, 8);
                case Synergy.ThreeTalents: return (5, 3);
                case Synergy.FiveComplete: return (12, 8);
                case Synergy.Chain3: return (0, 5);
                case Synergy.Chain4: return (0, 8);
                case Synergy.Chain5: return (0, 12);
                case Synergy.Harmony: return (4, 4);
                case Synergy.PureYang: return (10, 0);
                case Synergy.PureYin: return (0, 10);
                default: return (0, 0);
            }
        }

        /// <summary>把隊伍加成套到整隊每支小隊上（受傷最多減到 50%）。回傳觸發的加成。</summary>
        public static List<Synergy> ApplyTeamSynergy(IList<SquadInput> team)
        {
            var list = new List<(Element, Polarity)>();
            foreach (var s in team) list.Add((s.Element, s.Polarity));
            var synergies = Synergies(list);
            int atk = 0, reduce = 0;
            foreach (var s in synergies)
            {
                var (a, r) = Effect(s);
                atk += a;
                reduce += r;
            }
            foreach (var s in team)
            {
                s.AttackPercent = s.AttackPercent * (100 + atk) / 100;
                s.DamageTakenPercent = Math.Max(50, s.DamageTakenPercent * (100 - reduce) / 100);
            }
            return synergies;
        }
    }
}
