using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Shared.Battle
{
    /// <summary>
    /// 確定性亂數（SplitMix64）。戰鬥只能用這個，種子由伺服器決定，同種子必得同結果。
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _state;

        public DeterministicRandom(ulong seed) { _state = seed; }

        public ulong NextULong()
        {
            ulong z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>回傳 [min, maxInclusive] 的整數。</summary>
        public long Range(long min, long maxInclusive)
        {
            ulong span = (ulong)(maxInclusive - min + 1);
            return min + (long)(NextULong() % span);
        }
    }

    public enum BattleSide { Attacker = 0, Defender = 1 }

    public enum BattleOutcome { AttackerWins = 0, DefenderWins = 1, Draw = 2 }

    /// <summary>一支參戰小隊：領軍者（主將或魔物首領）＋同一種部隊。</summary>
    public sealed class SquadInput
    {
        /// <summary>識別用（例如 HeroId 或魔物 id），戰鬥本身不使用。</summary>
        public string Leader { get; set; }
        public long LeaderAttack { get; set; }
        public long LeaderDefense { get; set; }
        public long LeaderHp { get; set; }
        public TroopType Troop { get; set; }
        public long Count { get; set; }

        // ───── 四境加成（預設值 = 無加成） ─────
        /// <summary>震境：攻擊力百分比（100 = 無加成）。</summary>
        public long AttackPercent { get; set; } = 100;
        /// <summary>磐境：承受傷害百分比（100 = 無加成，85 = 少受 15%）。</summary>
        public long DamageTakenPercent { get; set; } = 100;
        /// <summary>迅境：出手速度加成。</summary>
        public int SpeedBonus { get; set; }
        /// <summary>巧境：克制倍率加成；被克制時也減少同樣的懲罰。</summary>
        public int CounterBonus { get; set; }
    }

    /// <summary>每支小隊的戰後結果。DamageDealt / DamageTaken / FirstStrikes 之後用來發放「四境」修練值。</summary>
    public sealed class SquadResult
    {
        public long TroopsStart { get; set; }
        public long TroopsLeft { get; set; }
        public bool LeaderStanding { get; set; }
        public long DamageDealt { get; set; }
        public long DamageTaken { get; set; }
        public long FirstStrikes { get; set; }
    }

    /// <summary>戰報中的一次攻擊。客戶端依此播放戰鬥。</summary>
    public sealed class BattleAction
    {
        public int Round { get; set; }
        public BattleSide Side { get; set; }
        public int Actor { get; set; }
        public int Target { get; set; }
        public long Damage { get; set; }
        public long TroopsKilled { get; set; }
        /// <summary>100 = 無克制，130 = 克制，80 = 被克制。</summary>
        public int CounterPercent { get; set; }
    }

    public sealed class BattleResult
    {
        public BattleOutcome Outcome { get; set; }
        public int Rounds { get; set; }
        public ulong Seed { get; set; }
        public List<SquadResult> Attackers { get; set; } = new List<SquadResult>();
        public List<SquadResult> Defenders { get; set; } = new List<SquadResult>();
        public List<BattleAction> Actions { get; set; } = new List<BattleAction>();
    }

    /// <summary>部隊與戰鬥的數值（測試用，之後統一調平衡）。</summary>
    public static class BattleConfig
    {
        public const int MaxRounds = 20;
        public const int MaxSquadsPerSide = 5;
        public const int CounterBonusPercent = 130;
        public const int CounteredPercent = 80;
        public const int RandomMinPercent = 90;
        public const int RandomMaxPercent = 110;

        public static long TroopAttack(TroopType t) => t == TroopType.Crossbow ? 14 : t == TroopType.Rider ? 12 : 10;
        public static long TroopDefense(TroopType t) => t == TroopType.Shield ? 14 : t == TroopType.Rider ? 10 : 8;
        public static long TroopHp(TroopType t) => t == TroopType.Shield ? 60 : t == TroopType.Rider ? 50 : 40;
        /// <summary>速度決定每回合出手順序：懸索騎最快、沉錨衛最慢。</summary>
        public static int TroopSpeed(TroopType t) => t == TroopType.Rider ? 3 : t == TroopType.Crossbow ? 2 : 1;

        /// <summary>克制：沉錨衛 → 懸索騎 → 鳴弦手 → 沉錨衛。</summary>
        public static bool Counters(TroopType attacker, TroopType target)
        {
            return (attacker == TroopType.Shield && target == TroopType.Rider)
                || (attacker == TroopType.Rider && target == TroopType.Crossbow)
                || (attacker == TroopType.Crossbow && target == TroopType.Shield);
        }

        public static int CounterPercent(TroopType attacker, TroopType target)
        {
            if (Counters(attacker, target)) return CounterBonusPercent;
            if (Counters(target, attacker)) return CounteredPercent;
            return 100;
        }
    }

    /// <summary>
    /// 確定性自動戰鬥。只用整數；亂數只來自 seed。伺服器結算後把 BattleResult（戰報）下發給客戶端播放。
    /// 規則（MVP）：
    ///  - 每回合依速度出手（速度相同時攻方先、再依隊伍順序）。
    ///  - 每支小隊優先攻擊自己克制的敵人，否則攻擊最前面還活著的敵人。
    ///  - 傷害 = 攻擊力 × 克制% × 浮動(90~110)% × 100 / (100 + 目標防禦)。
    ///  - 攻擊力 = 剩餘士兵 × 兵種攻擊 + 領軍攻擊 × 10；防禦 = 兵種防禦 + 領軍防禦 / 10。
    ///  - 傷害先由士兵承受（每名 = 兵種生命），士兵全滅後才傷到領軍者；領軍者倒下則小隊退場。
    ///  - 一方全滅即結束；打滿 MaxRounds 回合為平手。
    /// </summary>
    public static class BattleEngine
    {
        private sealed class Squad
        {
            public SquadInput In;
            public SquadResult Out;
            public long Troops;
            public long TroopHpLeft;   // 目前這名士兵還剩多少生命（部分受傷）
            public long LeaderHp;
            public int Index;
            public BattleSide Side;
            public bool Alive => LeaderHp > 0;
        }

        public static BattleResult Run(IList<SquadInput> attackers, IList<SquadInput> defenders, ulong seed)
        {
            Validate(attackers, nameof(attackers));
            Validate(defenders, nameof(defenders));

            var rng = new DeterministicRandom(seed);
            var result = new BattleResult { Seed = seed };
            var a = Build(attackers, BattleSide.Attacker, result.Attackers);
            var d = Build(defenders, BattleSide.Defender, result.Defenders);

            int round = 0;
            while (round < BattleConfig.MaxRounds && AnyAlive(a) && AnyAlive(d))
            {
                round++;
                bool firstOfRound = true;
                foreach (var actor in TurnOrder(a, d))
                {
                    if (!actor.Alive) continue;
                    var enemies = actor.Side == BattleSide.Attacker ? d : a;
                    var target = PickTarget(actor, enemies);
                    if (target == null) break;
                    if (firstOfRound) { actor.Out.FirstStrikes++; firstOfRound = false; }
                    result.Actions.Add(Attack(actor, target, round, rng));
                }
            }

            result.Rounds = round;
            bool aAlive = AnyAlive(a), dAlive = AnyAlive(d);
            result.Outcome = aAlive && !dAlive ? BattleOutcome.AttackerWins
                : dAlive && !aAlive ? BattleOutcome.DefenderWins
                : BattleOutcome.Draw;
            foreach (var s in a) Finish(s);
            foreach (var s in d) Finish(s);
            return result;
        }

        private static void Validate(IList<SquadInput> side, string name)
        {
            if (side == null || side.Count == 0 || side.Count > BattleConfig.MaxSquadsPerSide)
                throw new ArgumentException($"{name} 需要 1~{BattleConfig.MaxSquadsPerSide} 支小隊");
            foreach (var s in side)
            {
                if (s.Count < 0 || s.LeaderHp <= 0 || s.LeaderAttack < 0 || s.LeaderDefense < 0)
                    throw new ArgumentException($"{name} 有不合法的小隊數值");
                if (!Enum.IsDefined(typeof(TroopType), s.Troop))
                    throw new ArgumentException($"{name} 有未知兵種");
            }
        }

        private static List<Squad> Build(IList<SquadInput> inputs, BattleSide side, List<SquadResult> results)
        {
            var list = new List<Squad>();
            for (int i = 0; i < inputs.Count; i++)
            {
                var r = new SquadResult { TroopsStart = inputs[i].Count };
                results.Add(r);
                list.Add(new Squad
                {
                    In = inputs[i], Out = r, Index = i, Side = side,
                    Troops = inputs[i].Count,
                    TroopHpLeft = BattleConfig.TroopHp(inputs[i].Troop),
                    LeaderHp = inputs[i].LeaderHp,
                });
            }
            return list;
        }

        private static bool AnyAlive(List<Squad> side)
        {
            foreach (var s in side) if (s.Alive) return true;
            return false;
        }

        private static List<Squad> TurnOrder(List<Squad> a, List<Squad> d)
        {
            var all = new List<Squad>(a.Count + d.Count);
            all.AddRange(a);
            all.AddRange(d);
            // 穩定排序：速度高者先；同速攻方先；再依隊伍順序（List.Sort 不穩定，所以把順序寫進比較）
            all.Sort((x, y) =>
            {
                int c = (BattleConfig.TroopSpeed(y.In.Troop) + y.In.SpeedBonus).CompareTo(BattleConfig.TroopSpeed(x.In.Troop) + x.In.SpeedBonus);
                if (c != 0) return c;
                c = ((int)x.Side).CompareTo((int)y.Side);
                return c != 0 ? c : x.Index.CompareTo(y.Index);
            });
            return all;
        }

        private static Squad PickTarget(Squad actor, List<Squad> enemies)
        {
            Squad first = null;
            foreach (var e in enemies)
            {
                if (!e.Alive) continue;
                if (first == null) first = e;
                if (BattleConfig.Counters(actor.In.Troop, e.In.Troop)) return e;
            }
            return first;
        }

        private static BattleAction Attack(Squad actor, Squad target, int round, DeterministicRandom rng)
        {
            long power = (actor.Troops * BattleConfig.TroopAttack(actor.In.Troop) + actor.In.LeaderAttack * 10) * actor.In.AttackPercent / 100;
            int counter = BattleConfig.CounterPercent(actor.In.Troop, target.In.Troop);
            if (counter > 100) counter += actor.In.CounterBonus;
            else if (counter < 100) counter = System.Math.Min(100, counter + actor.In.CounterBonus);
            long roll = rng.Range(BattleConfig.RandomMinPercent, BattleConfig.RandomMaxPercent);
            long defense = BattleConfig.TroopDefense(target.In.Troop) + target.In.LeaderDefense / 10;
            long damage = power * counter / 100 * roll / 100 * 100 / (100 + defense) * target.In.DamageTakenPercent / 100;
            if (damage < 1) damage = 1;

            long before = target.Troops;
            long dealt = ApplyDamage(target, damage);
            actor.Out.DamageDealt += dealt;
            target.Out.DamageTaken += dealt;

            return new BattleAction
            {
                Round = round, Side = actor.Side, Actor = actor.Index, Target = target.Index,
                Damage = dealt, TroopsKilled = before - target.Troops, CounterPercent = counter,
            };
        }

        /// <summary>先扣士兵，再扣領軍者。回傳實際造成的傷害（不超過剩餘生命）。</summary>
        private static long ApplyDamage(Squad s, long damage)
        {
            long dealt = 0;
            long hpPer = BattleConfig.TroopHp(s.In.Troop);
            while (damage > 0 && s.Troops > 0)
            {
                long hit = Math.Min(damage, s.TroopHpLeft);
                s.TroopHpLeft -= hit;
                damage -= hit;
                dealt += hit;
                if (s.TroopHpLeft == 0)
                {
                    s.Troops--;
                    s.TroopHpLeft = hpPer;
                }
                // 大量傷害時直接整批扣兵，避免逐一迴圈（結果與逐一相同）
                if (damage >= hpPer && s.Troops > 0 && s.TroopHpLeft == hpPer)
                {
                    long whole = Math.Min(s.Troops, damage / hpPer);
                    s.Troops -= whole;
                    damage -= whole * hpPer;
                    dealt += whole * hpPer;
                }
            }
            if (damage > 0 && s.Troops == 0)
            {
                long hit = Math.Min(damage, s.LeaderHp);
                s.LeaderHp -= hit;
                dealt += hit;
            }
            return dealt;
        }

        private static void Finish(Squad s)
        {
            s.Out.TroopsLeft = s.Troops;
            s.Out.LeaderStanding = s.Alive;
        }
    }
}
