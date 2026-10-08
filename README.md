# 深淵王國 Abyss Kingdom

「深淵探勘 × 4X 策略」SLG（PC 先行，Steam＋自架下載；手遊之後）。一人 + AI（Vibe Coding）開發，零成本起步。
企劃書與技術架構請看 Claude Docs 上的《深淵王國》SLG 手遊企劃書。

## 目前進度

城建（訓練、取消、加速、背包）、主將（品質、等級、裝備、戰力、升星、四股索、淵脈）、確定性戰鬥核心、遠征第一章已完成；伺服器部署在本機 Docker＋Cloudflare Tunnel，Windows 版可下載試玩。
**遊戲設定（世界觀、名詞、數值、路線）以 [docs/design/GDD.md](docs/design/GDD.md) 為準**；接手開發請先讀 [docs/HANDOFF.md](docs/HANDOFF.md)。

## 資料夾結構

```
shared/AbyssKingdom.Shared/   共用規則（純 C#，Unity 以本機套件引用）
  City/                       城建：數值設定、狀態、規則
  Protocol/                   客戶端與伺服器之間的訊息格式
server/AbyssKingdom.Server/   遊戲伺服器（模組化單體）
  Auth/                       登入驗證（Dev / Firebase）
  Game/                       上線玩家管理、每秒推進、存檔
  Net/                        WebSocket 收發與訊息分派
  Persistence/                存檔（Memory / Supabase）
tests/AbyssKingdom.Tests/     自動測試（不需任何套件）
client/                       Unity 6 專案（Scripts/Game：連線與狀態；Scripts/UI：UI Toolkit 介面、DisplayNames 顯示名稱表）
db/                           Supabase 資料表 SQL
docs/                         安裝、帳號、開發流程說明
```

## 快速開始（本機）

先照 [docs/SETUP.md](docs/SETUP.md) 安裝 .NET 8 SDK 與 Unity。

```bash
# 1. 跑測試（全部 PASS 才往下）
dotnet run --project tests/AbyssKingdom.Tests

# 2. 啟動伺服器（預設 http://127.0.0.1:5080，開發模式、記憶體存檔）
dotnet run --project server/AbyssKingdom.Server

# 3. 用 Unity Hub 開啟 client/ 資料夾，按 Play
#    面板出現後輸入名字 → 「連線並登入」→ 升級建築
```

## 設定（環境變數）

| 變數 | 說明 | 預設 |
| --- | --- | --- |
| `Auth__Mode` | `Dev`（名字即登入）或 `Firebase` | `Dev` |
| `Firebase__ProjectId` | Firebase 專案 ID（Auth 為 Firebase 時必填） | |
| `Storage__Mode` | `Memory` 或 `Supabase` | `Memory` |
| `Supabase__Url` | 例如 `https://xxxx.supabase.co` | |
| `Supabase__ServiceKey` | Supabase 的 service_role key，**只放在伺服器環境變數** | |

正式環境務必設定 `Auth__Mode=Firebase`，否則任何人都能用名字登入別人的帳號。

## 連線協定（MVP）

WebSocket `/ws`，每則訊息為 JSON：`{ "type": "...", "reqId": 1, "data": { ... } }`

| 方向 | type | data |
| --- | --- | --- |
| → | `login` | `{ token, displayName }` |
| → | `city.get` | — |
| → | `city.upgrade` | `{ building: "Keep" }` |
| → | `city.train` | `{ troop: "Shield", count: 50 }`（需兵營；單批上限 = 兵營等級 × 50） |
| → | `city.cancel` | `{ queue: "Building" \| "Training" }`（退 50%；訓練已完成的士兵照算） |
| → | `city.speedup` | `{ queue, item?, count? }`（不帶 item = 剩餘 5 分鐘內免費完成；帶 item = 使用加速道具） |
| → | `hero.levelUp` | `{ hero: "Hold", item: "HeroExpSmall", count: 1 }` |
| → | `gear.equip` / `gear.unequip` | `{ gear: <uid>, hero }` / `{ gear: <uid> }` |
| → | `expedition.battle` | `{ stage: 1, teams: [["Hold","Vesper"],["Karst"]] }`（每條岔路一隊） |
| → | `expedition.claimIdle` | —（領掛機收益） |
| → | `domain.useScroll` | `{ hero, item: "DomainScrollPower", count }`（索譜補修練值） |
| → | `domain.trial` | `{ hero, domain: "Power" }`（修練值滿、且對應星色升滿才能挑戰） |
| → | `hero.starUp` | `{ hero }`（消耗信物＋響晶升一顆星） |
| → | `map.march` | `{ hero, troops: { "Rider": 50 }, destination }`（從自己的垂降口出征；只能走已探索廊道，終點可為相鄰未知節點；地圖依種子生成，狀態在 `city.map`） |
| → | `map.recall` | `{ march }`（召回駐紮中的行軍，回到垂降口後部隊歸營） |
| → | `world.cities` | —（取得地表所有主城的公開資訊；自己的主城位置在 `city.surface.site`） |
| → | `ping` | — |
| ← | `login.ok` | `{ playerId, displayName, serverTimeMs }` |
| ← | `city.state` | `{ serverTimeMs, city }` |
| ← | `city.upgradeCompleted` | `{ building, level }`（伺服器主動推播，reqId = 0） |
| ← | `city.trainingCompleted` | `{ troop, count }`（伺服器主動推播，reqId = 0） |
| ← | `expedition.result` | `{ stage, win, reports: [戰報…], rewards }`，之後再送一次 `city.state` |
| ← | `expedition.idleClaimed` | `{ resources, items }` |
| ← | `domain.trialResult` | `{ hero, domain, success, report }`，之後再送一次 `city.state` |
| ← | `map.marchArrived` | `{ march, hero, node, discovered, disbanded }`（伺服器主動推播，reqId = 0） |
| ← | `world.cities` | `{ cities: [{ site, name, power, mine }] }` |
| ← | `error` | `{ code, detail }` |
