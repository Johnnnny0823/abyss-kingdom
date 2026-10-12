using System.Text;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Map;

// 新手旅程模擬：用共用規則（和伺服器同一套數值）模擬一位很勤勞的新玩家前 30 分鐘。
// 玩家行為（貪婪、每秒都在看畫面）：
//   1. 建築隊列空了就升級「等級最低、買得起」的建築（兵營優先蓋起來）。
//   2. 剩 5 分鐘內就按免費完成。
//   3. 訓練隊列空了就訓練磐盾衛：先保留下一個升級的資源，用剩下的訓練（最多一批的上限、總兵力 300）。
//   4. 兵營有 50 名以上就派一支 50 人的部隊去最近的 Lv.1 資源田採集（來回各 1 分鐘），三種基礎資源輪流採；
//      兵力超過 100、150 時第二、第三支也出發。
// 報告每 5 分鐘一段：操作次數、其中免費完成幾次、建築隊列因為缺資源而空等的秒數、收入來源（城內生產 vs 採集）。

const long T0 = 1_700_000_000_000;
const int Minutes = 30;
const int Bucket = 5;
const long TripTravelMs = 60_000;   // 單程（外環到最近的資源田大約 1 分鐘）
const int MarchSize = 50;

var city = CityState.CreateNew(T0);
var order = new[] { BuildingType.Barracks, BuildingType.Sawmill, BuildingType.Quarry, BuildingType.Farm, BuildingType.Warehouse,
    BuildingType.Hospital, BuildingType.CrystalMine, BuildingType.Keep };
var gatherCycle = new[] { ResourceType.Wood, ResourceType.Stone, ResourceType.Food };
var trips = new List<(long backAt, ResourceType res, long amount)>();
int nextRes = 0;

int buckets = Minutes / Bucket;
var actions = new int[buckets];
var freeTaps = new int[buckets];
var starved = new int[buckets];
var produced = new long[buckets];
var gathered = new long[buckets];
var log = new List<string>();
int keepBlockedSeconds = 0;

long ResourceSum() => CityConfig.AllResources.Sum(r => city.GetResource(r));

for (long s = 0; s < Minutes * 60; s++)
{
    long now = T0 + s * 1000;
    int b = (int)(s / 60 / Bucket);
    long before = ResourceSum();
    CityLogic.Advance(city, now, out var trained, out _);
    produced[b] += ResourceSum() - before;

    // 採集回城
    for (int i = trips.Count - 1; i >= 0; i--)
    {
        if (trips[i].backAt > now) continue;
        city.Resources[trips[i].res] = city.GetResource(trips[i].res) + trips[i].amount;
        city.Troops[TroopType.Shield] = city.GetTroops(TroopType.Shield) + MarchSize;
        gathered[b] += trips[i].amount;
        trips.RemoveAt(i);
    }

    // 1. 建築
    if (city.Upgrade == null)
    {
        if (CityLogic.CanUpgrade(city, BuildingType.Keep) == UpgradeResult.KeepPrerequisiteMissing) keepBlockedSeconds++;
        bool started = false;
        foreach (var t in order.OrderBy(t => t == BuildingType.Barracks && city.GetLevel(t) == 0 ? -1 : city.GetLevel(t)))
        {
            if (CityLogic.StartUpgrade(city, t, now) != UpgradeResult.Ok) continue;
            actions[b]++;
            log.Add($"{s / 60:00}:{s % 60:00} 升級 {t} → Lv.{city.GetLevel(t) + 1}（工期 {CityConfig.UpgradeDurationMs(t, city.GetLevel(t) + 1) / 1000} 秒）");
            started = true;
            break;
        }
        if (!started) starved[b]++;
    }
    // 2. 免費完成
    if (city.Upgrade != null && city.Upgrade.FinishAtMs - now <= CityConfig.FreeSpeedupMs && city.Upgrade.FinishAtMs > now)
        if (CityLogic.SpeedupFree(city, QueueType.Building, now) == SpeedupResult.Ok) { actions[b]++; freeTaps[b]++; }
    if (city.Training != null && city.Training.FinishAtMs - now <= CityConfig.FreeSpeedupMs && city.Training.FinishAtMs > now)
        if (CityLogic.SpeedupFree(city, QueueType.Training, now) == SpeedupResult.Ok) { actions[b]++; freeTaps[b]++; }
    CityLogic.Advance(city, now);

    // 3. 訓練
    if (city.Training == null && city.GetLevel(BuildingType.Barracks) > 0 && city.GetTroops(TroopType.Shield) + trips.Count * MarchSize < 300)
    {
        long batch = CityConfig.MaxTrainBatch(city.GetLevel(BuildingType.Barracks));
        long afford = CityConfig.AllResources.Where(r => CityConfig.TroopCost(TroopType.Shield, r) > 0)
            .Min(r => city.GetResource(r) / CityConfig.TroopCost(TroopType.Shield, r));
        // 先保留下一個升級要用的資源，剩下的才拿去訓練（真正的玩家不會把建築的錢全拿去練兵）
        var next = order.OrderBy(t => city.GetLevel(t)).First();
        int lvl = city.GetLevel(next) + 1;
        afford = CityConfig.AllResources.Where(r => CityConfig.TroopCost(TroopType.Shield, r) > 0)
            .Min(r => Math.Max(0, city.GetResource(r) - CityConfig.UpgradeCost(next, lvl, r)) / CityConfig.TroopCost(TroopType.Shield, r));
        long n = Math.Min(batch, afford);
        if (n >= 10 && CityLogic.StartTraining(city, TroopType.Shield, n, now) == TrainResult.Ok) actions[b]++;
    }

    // 4. 採集
    int wantMarches = Math.Min(3, (int)((city.GetTroops(TroopType.Shield) + trips.Count * MarchSize) / MarchSize));
    if (trips.Count < wantMarches && city.GetTroops(TroopType.Shield) >= MarchSize)
    {
        var res = gatherCycle[nextRes++ % gatherCycle.Length];
        long load = GatherConfig.Load(new Dictionary<TroopType, long> { [TroopType.Shield] = MarchSize });
        long cap = Math.Min(load, GatherConfig.FieldStock(1));
        long gatherMs = cap * 60_000 / GatherConfig.RatePerMinute(1);
        city.Troops[TroopType.Shield] = city.GetTroops(TroopType.Shield) - MarchSize;
        trips.Add((now + TripTravelMs * 2 + gatherMs, res, cap));
        actions[b]++;
    }
}

var sb = new StringBuilder();
sb.AppendLine("# 新手旅程模擬（前 30 分鐘）");
sb.AppendLine();
sb.AppendLine($"產生時間：{DateTime.Now:yyyy-MM-dd HH:mm}。數值來自 shared（和伺服器同一套），玩家行為見 tools/NewbieSim/Program.cs 開頭。");
sb.AppendLine();
sb.AppendLine("| 時段 | 操作次數 | 其中免費完成 | 建築隊列缺資源空等（秒） | 城內生產 | 採集收入 |");
sb.AppendLine("|---|---|---|---|---|---|");
for (int i = 0; i < buckets; i++)
    sb.AppendLine($"| {i * Bucket:00}–{(i + 1) * Bucket:00} 分 | {actions[i]} | {freeTaps[i]} | {starved[i]} | {produced[i]:N0} | {gathered[i]:N0} |");
sb.AppendLine();
sb.AppendLine("30 分鐘後的建築等級：" + string.Join("、", CityConfig.AllBuildings.Select(t => $"{t} {city.GetLevel(t)}")));
sb.AppendLine($"總操作：{actions.Sum()}；主堡前置未達成觀測秒數：{keepBlockedSeconds}（不等於建築隊列空等，仍可升級其他建築）。");
sb.AppendLine("下一級主堡前置：" + string.Join("、", CityConfig.KeepPrerequisites(city.GetLevel(BuildingType.Keep) + 1)
    .Select(p => $"{p.building} {p.level}（目前 {city.GetLevel(p.building)}）")));
sb.AppendLine();
sb.AppendLine($"兵力：磐盾衛 {city.GetTroops(TroopType.Shield) + trips.Count * MarchSize:N0}（含在外採集的）；資源：" +
    string.Join("、", CityConfig.AllResources.Select(r => $"{r} {city.GetResource(r):N0}")));
sb.AppendLine();
sb.AppendLine("## 升級紀錄");
sb.AppendLine();
foreach (var l in log) sb.AppendLine("- " + l);

var outPath = Path.GetFullPath(Path.Combine("artifacts", "newbie-sim.md")); // 在專案根目錄執行
Directory.CreateDirectory(Path.GetDirectoryName(outPath));
File.WriteAllText(outPath, sb.ToString());
Console.WriteLine(sb.ToString().Split("## 升級紀錄")[0]);
Console.WriteLine("report: " + outPath);
