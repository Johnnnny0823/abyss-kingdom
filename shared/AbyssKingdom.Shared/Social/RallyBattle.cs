using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Expedition;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;

namespace AbyssKingdom.Shared.Social
{
    public sealed class CampParticipantInput
    {
        public string OwnerId { get; set; }
        public long MarchId { get; set; }
        public HeroId Hero { get; set; }
        public List<SquadInput> Squads { get; set; } = new List<SquadInput>();
    }
    public sealed class CampParticipantOutcome
    {
        public string OwnerId { get; set; }
        public long MarchId { get; set; }
        public long Contribution { get; set; }
        public Dictionary<TroopType, long> Losses { get; set; } = new Dictionary<TroopType, long>();
        public Dictionary<TroopType, long> Survivors { get; set; } = new Dictionary<TroopType, long>();
        public Dictionary<ResourceType, long> Resources { get; set; } = new Dictionary<ResourceType, long>();
        public Dictionary<ItemType, long> Items { get; set; } = new Dictionary<ItemType, long>();
        public BattleResult Report { get; set; }
    }
    public sealed class CampSettlementOutcome
    {
        public bool Won { get; set; }
        public bool CampGone { get; set; }
        public List<CampParticipantOutcome> Participants { get; set; } = new List<CampParticipantOutcome>();
    }
    public static class RallyBattle
    {
        public static bool ValidCountdown(int seconds) => seconds == 60 || seconds == 180 || seconds == 300;
        /// <summary>Exact largest-remainder allocation; stable owner ids break ties. No floating point or overflow.</summary>
        public static Dictionary<string, long> Split(long amount, IReadOnlyDictionary<string, long> weights)
        {
            if (amount < 0 || weights.Count == 0 || weights.Values.Any(w => w < 0)) throw new ArgumentException("Invalid reward weights.");
            var ids = weights.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
            BigInteger sum = BigInteger.Zero; foreach (long w in weights.Values) sum += w;
            if (sum == 0) throw new ArgumentException("At least one positive weight is required.");
            var result = new Dictionary<string, long>(); var remainders = new List<(string id, BigInteger remainder)>();
            long assigned = 0;
            foreach (var id in ids)
            {
                BigInteger product = (BigInteger)amount * weights[id];
                long share = (long)(product / sum); result[id] = share; assigned += share;
                remainders.Add((id, product % sum));
            }
            foreach (var item in remainders.OrderByDescending(r => r.remainder).ThenBy(r => r.id, StringComparer.Ordinal).Take((int)(amount - assigned)))
                result[item.id]++;
            return result;
        }
        public static CampSettlementOutcome Resolve(IReadOnlyList<CampParticipantInput> participants,
            int level, int tile, ulong seed, bool rally, bool campGone = false)
        {
            if (participants.Count == 0 || participants.Count > 5 || participants.Select(p => p.OwnerId).Distinct().Count() != participants.Count)
                throw new ArgumentException("A camp battle has one to five different owners.");
            var output = new CampSettlementOutcome { CampGone = campGone };
            var inputs = new List<SquadInput>();
            foreach (var p in participants)
            {
                if (p.Squads.Count == 0 || p.Squads.Count > 3 || p.Squads.Select(s => s.Troop).Distinct().Count() != p.Squads.Count)
                    throw new ArgumentException("Invalid participant squads.");
                inputs.AddRange(p.Squads);
            }
            var enemies = CampConfig.Lineup(level, tile).Select(ExpeditionLogic.EnemySquadInput).ToList();
            var report = campGone ? null : BattleEngine.Run(inputs, enemies, seed, rally ? BattleScenario.PveRally : BattleScenario.Standard);
            output.Won = report?.Outcome == BattleOutcome.AttackerWins;
            int offset = 0;
            foreach (var p in participants)
            {
                var o = new CampParticipantOutcome { OwnerId = p.OwnerId, MarchId = p.MarchId };
                if (report != null)
                {
                    o.Report = new BattleResult { Outcome = report.Outcome, Rounds = report.Rounds, Seed = report.Seed,
                        Attackers = report.Attackers.Skip(offset).Take(p.Squads.Count).ToList() };
                    foreach (var action in report.Actions.Where(a => a.Side == BattleSide.Attacker && a.Actor >= offset && a.Actor < offset + p.Squads.Count))
                        o.Report.Actions.Add(new BattleAction { Round = action.Round, Side = action.Side, Actor = action.Actor - offset,
                            Target = action.Target, Damage = action.Damage, TroopsKilled = action.TroopsKilled,
                            CounterPercent = action.CounterPercent, ElementPercent = action.ElementPercent });
                }
                for (int i = 0; i < p.Squads.Count; i++)
                {
                    var squad = p.Squads[i]; long left = report == null ? squad.Count : report.Attackers[offset + i].TroopsLeft;
                    o.Survivors[squad.Troop] = left; o.Losses[squad.Troop] = squad.Count - left;
                    if (report != null) o.Contribution += report.Attackers[offset + i].DamageDealt;
                }
                offset += p.Squads.Count; output.Participants.Add(o);
            }
            if (output.Won)
            {
                var weights = output.Participants.ToDictionary(p => p.OwnerId, p => p.Contribution);
                if (weights.Values.All(w => w == 0)) weights = participants.ToDictionary(p => p.OwnerId, p => p.Squads.Sum(s => s.Count));
                foreach (var reward in CampConfig.ResourceLoot(level))
                    foreach (var share in Split(reward.Value, weights)) output.Participants.First(p => p.OwnerId == share.Key).Resources[reward.Key] = share.Value;
                foreach (var reward in CampConfig.ItemLoot(level, tile))
                    foreach (var share in Split(reward.Value, weights)) output.Participants.First(p => p.OwnerId == share.Key).Items[reward.Key] = share.Value;
            }
            return output;
        }
    }
}
