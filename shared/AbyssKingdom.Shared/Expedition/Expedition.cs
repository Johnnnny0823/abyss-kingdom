using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;

namespace AbyssKingdom.Shared.Expedition
{
    /// <summary>魔物（顯示名稱在客戶端 DisplayNames.cs）。第一章「餘燼緣野」的小怪與守層者。</summary>
    public enum MonsterId
    {
        MossChild = 1,           // 苔面童（沉錨衛型：前排）
        LanternWisp = 2,         // 燈魅（鳴弦手型：遠程）
        BellRat = 3,             // 骨鈴鼠（懸索騎型：快攻）
        HundredEyeUmbrella = 4,  // 百眼紙傘（第一章守層者）
    }

    public enum ExpeditionCode
    {
        Ok = 0,
        ChapterComplete = 1,
        StageLocked = 2,
        WrongTeamCount = 3,
        EmptyTeam = 4,
        TeamTooLarge = 5,
        DuplicateHero = 6,
        HeroNotOwned = 7,
        NothingToClaim = 8,
    }

    public sealed class EnemySquad
    {
        public MonsterId Monster;
        public TroopType Troop;
        public long Count, Attack, Defense, Hp;
    }

    public sealed class StageDef
    {
        public int Number;
        /// <summary>1 條或 2 條岔路；每條是一組敵方陣容。全部打贏才算通關。</summary>
        public List<List<EnemySquad>> Branches = new List<List<EnemySquad>>();
        public long ResourceReward;
        public Dictionary<ItemType, long> ItemRewards = new Dictionary<ItemType, long>();
        public GearId? GearReward;
        public bool IsBoss;
    }

    /// <summary>玩家的遠征進度（存在城市存檔裡）。</summary>
    public class ExpeditionState
    {
        /// <summary>已通關的最高關（0 = 還沒通關任何一關）。</summary>
        public int Cleared { get; set; }
        /// <summary>掛機收益從何時開始累積（0 = 尚未開始）。</summary>
        public long IdleSinceMs { get; set; }
    }

    public sealed class ExpeditionRewards
    {
        public Dictionary<ResourceType, long> Resources { get; set; } = new Dictionary<ResourceType, long>();
        public Dictionary<ItemType, long> Items { get; set; } = new Dictionary<ItemType, long>();
        public GearId? Gear { get; set; }
    }

    public sealed class ExpeditionResult
    {
        public int Stage { get; set; }
        public bool Win { get; set; }
        /// <summary>每條岔路一份戰報（順序同隊伍）。</summary>
        public List<BattleResult> Reports { get; set; } = new List<BattleResult>();
        public ExpeditionRewards Rewards { get; set; }
    }

    public static class ExpeditionConfig
    {
        public const long MaxIdleMs = 12 * 3_600_000L;

        // 必須宣告在 Chapter1 之前：靜態欄位依宣告順序初始化
        static readonly MonsterId[] Rotation = { MonsterId.MossChild, MonsterId.LanternWisp, MonsterId.BellRat };

        /// <summary>第一章：餘燼緣野。數值為測試用，之後統一調平衡（見 ExpeditionTests 的難度曲線測試）。</summary>
        public static readonly StageDef[] Chapter1 = BuildChapter1();

        static StageDef[] BuildChapter1()
        {
            var stages = new StageDef[10];
            for (int s = 1; s <= 10; s++)
            {
                var st = new StageDef { Number = s, ResourceReward = 300L * s };
                st.ItemRewards[ItemType.HeroExpSmall] = 5 + s;
                st.ItemRewards[ItemType.HeroExpMedium] = 1 + s / 2;
                if (s == 5 || s == 10) st.ItemRewards[ItemType.HeroExpLarge] = 1;
                if (s == 5) st.ItemRewards[ItemType.SpeedupGeneral30m] = 2;

                if (s == 6)
                {
                    // 岔路：兩條路各兩隊，需要兩支隊伍
                    st.Branches.Add(Lineup(s, 2, 0, 75));
                    st.Branches.Add(Lineup(s, 2, 1, 75));
                }
                else if (s == 10)
                {
                    st.IsBoss = true;
                    var boss = Squad(MonsterId.HundredEyeUmbrella, s, 160);
                    boss.Hp *= 3;
                    var line = new List<EnemySquad> { Squad(MonsterId.MossChild, s, 100), boss, Squad(MonsterId.BellRat, s, 100) };
                    st.Branches.Add(line);
                }
                else
                {
                    st.Branches.Add(Lineup(s, s <= 1 ? 1 : s <= 3 ? 2 : s <= 8 ? 3 : 4, s, 100));
                }

                if (s == 4) st.GearReward = GearId.BoneBellSpear;
                if (s == 7) st.GearReward = GearId.BoneBellRelic;
                if (s == 10) st.GearReward = GearId.HundredEyeRib;
                stages[s - 1] = st;
            }
            return stages;
        }

        static List<EnemySquad> Lineup(int stage, int squads, int offset, long scalePercent)
        {
            var list = new List<EnemySquad>();
            for (int i = 0; i < squads; i++) list.Add(Squad(Rotation[(i + offset) % Rotation.Length], stage, scalePercent));
            return list;
        }

        /// <summary>第 s 關的魔物小隊；scalePercent = 強度百分比（100 = 標準）。</summary>
        static EnemySquad Squad(MonsterId m, int s, long scalePercent)
        {
            TroopType troop = m == MonsterId.MossChild ? TroopType.Shield : m == MonsterId.BellRat ? TroopType.Rider : TroopType.Crossbow;
            return new EnemySquad
            {
                Monster = m,
                Troop = troop,
                Count = (60 + 45L * s) * scalePercent / 100,
                Attack = (50 + 24L * s) * scalePercent / 100,
                Defense = (40 + 15L * s) * scalePercent / 100,
                Hp = (700 + 260L * s) * scalePercent / 100,
            };
        }

        public static StageDef Stage(int number) =>
            number >= 1 && number <= Chapter1.Length ? Chapter1[number - 1] : null;

        /// <summary>每小時掛機收益：每種資源 = 已通關數 × 60；小經驗書 = 已通關數 ÷ 2。</summary>
        public static long IdleResourcePerHour(int cleared) => cleared * 60L;
        public static long IdleExpBooksPerHour(int cleared) => cleared / 2;
    }

    public static class ExpeditionLogic
    {
        /// <summary>主將帶「統率」數量的專精兵種出征。遠征不消耗兵營部隊、不會死兵。</summary>
        public static SquadInput HeroSquad(CityState city, HeroId id)
        {
            HeroConfig.TryGet(id, out var def);
            var s = PowerLogic.TotalStats(city, id);
            return new SquadInput
            {
                Leader = id.ToString(),
                LeaderAttack = s.Attack, LeaderDefense = s.Defense, LeaderHp = s.Hp,
                Troop = def.Specialty,
                Count = s.Leadership,
            };
        }

        public static SquadInput EnemySquadInput(EnemySquad e) => new SquadInput
        {
            Leader = e.Monster.ToString(),
            LeaderAttack = e.Attack, LeaderDefense = e.Defense, LeaderHp = e.Hp,
            Troop = e.Troop, Count = e.Count,
        };

        /// <summary>
        /// 挑戰下一關。teams 的數量必須等於岔路數；同一位主將只能出現一次。
        /// 全部岔路都贏才通關、發獎勵。seed 由伺服器決定（每條岔路用 seed+i）。
        /// </summary>
        public static ExpeditionCode Fight(CityState city, int stage, IList<IList<HeroId>> teams, ulong seed, long nowMs, out ExpeditionResult result)
        {
            result = null;
            var exp = city.Expedition;
            if (exp.Cleared >= ExpeditionConfig.Chapter1.Length) return ExpeditionCode.ChapterComplete;
            if (stage != exp.Cleared + 1) return ExpeditionCode.StageLocked;
            var def = ExpeditionConfig.Stage(stage);
            if (teams == null || teams.Count != def.Branches.Count) return ExpeditionCode.WrongTeamCount;

            var used = new HashSet<HeroId>();
            foreach (var team in teams)
            {
                if (team == null || team.Count == 0) return ExpeditionCode.EmptyTeam;
                if (team.Count > BattleConfig.MaxSquadsPerSide) return ExpeditionCode.TeamTooLarge;
                foreach (var h in team)
                {
                    if (!city.Heroes.ContainsKey(h)) return ExpeditionCode.HeroNotOwned;
                    if (!used.Add(h)) return ExpeditionCode.DuplicateHero;
                }
            }

            result = new ExpeditionResult { Stage = stage, Win = true };
            for (int i = 0; i < teams.Count; i++)
            {
                var ours = new List<SquadInput>();
                foreach (var h in teams[i]) ours.Add(HeroSquad(city, h));
                var theirs = new List<SquadInput>();
                foreach (var e in def.Branches[i]) theirs.Add(EnemySquadInput(e));
                var report = BattleEngine.Run(ours, theirs, seed + (ulong)i);
                result.Reports.Add(report);
                if (report.Outcome != BattleOutcome.AttackerWins) result.Win = false;
            }

            if (result.Win)
            {
                exp.Cleared = stage;
                if (exp.IdleSinceMs == 0) exp.IdleSinceMs = nowMs;
                result.Rewards = GrantStageRewards(city, def);
            }
            return ExpeditionCode.Ok;
        }

        static ExpeditionRewards GrantStageRewards(CityState city, StageDef def)
        {
            var r = new ExpeditionRewards();
            foreach (var res in CityConfig.AllResources)
            {
                city.Resources[res] = city.GetResource(res) + def.ResourceReward;
                r.Resources[res] = def.ResourceReward;
            }
            foreach (var kv in def.ItemRewards)
            {
                city.Items[kv.Key] = city.GetItemCount(kv.Key) + kv.Value;
                r.Items[kv.Key] = kv.Value;
            }
            if (def.GearReward != null)
            {
                GearLogic.Grant(city, def.GearReward.Value);
                r.Gear = def.GearReward;
            }
            return r;
        }

        /// <summary>目前可領的掛機收益（不修改狀態），最多累積 12 小時。</summary>
        public static ExpeditionRewards PendingIdle(CityState city, long nowMs)
        {
            var exp = city.Expedition;
            var r = new ExpeditionRewards();
            if (exp.Cleared <= 0 || exp.IdleSinceMs <= 0) return r;
            long elapsed = Math.Min(ExpeditionConfig.MaxIdleMs, Math.Max(0, nowMs - exp.IdleSinceMs));
            long res = ExpeditionConfig.IdleResourcePerHour(exp.Cleared) * elapsed / CityConfig.MsPerHour;
            long books = ExpeditionConfig.IdleExpBooksPerHour(exp.Cleared) * elapsed / CityConfig.MsPerHour;
            if (res > 0) foreach (var t in CityConfig.AllResources) r.Resources[t] = res;
            if (books > 0) r.Items[ItemType.HeroExpSmall] = books;
            return r;
        }

        public static ExpeditionCode ClaimIdle(CityState city, long nowMs, out ExpeditionRewards rewards)
        {
            rewards = PendingIdle(city, nowMs);
            if (rewards.Resources.Count == 0 && rewards.Items.Count == 0) return ExpeditionCode.NothingToClaim;
            foreach (var kv in rewards.Resources) city.Resources[kv.Key] = city.GetResource(kv.Key) + kv.Value;
            foreach (var kv in rewards.Items) city.Items[kv.Key] = city.GetItemCount(kv.Key) + kv.Value;
            city.Expedition.IdleSinceMs = nowMs;
            return ExpeditionCode.Ok;
        }
    }
}
