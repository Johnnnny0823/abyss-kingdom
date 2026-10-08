# 深淵王國 Abyss Kingdom

「深淵探勘 × 4X 策略」SLG（PC 先行，Steam＋自架下載；手遊之後）。一人 + AI（Vibe Coding）開發，零成本起步。
企劃書與技術架構請看 Claude Docs 上的《深淵王國》SLG 手遊企劃書。

## 目前進度：第 1–2 週里程碑

- [x] 共用 C# 規則庫（城建、資源產出、升級計時、連線協定），伺服器與 Unity 共用同一份程式碼
- [x] ASP.NET Core 遊戲伺服器：WebSocket 連線、開發模式登入、Firebase 登入驗證、玩家上線管理、每秒推進、定時存檔
- [x] 存檔：記憶體（本機開發）／Supabase（正式）
- [x] 22 個自動測試（規則單元測試 + 真實連線的端到端測試 + Supabase 存取測試）
- [x] Unity 客戶端：連線元件 + 開發用城建面板（按 Play 自動出現）
- [x] 部署：本機 Docker + Cloudflare Quick Tunnel（`wss://`），見 [docs/DEPLOY.md](docs/DEPLOY.md)；之後再搬雲端主機
- [ ] Unity 端接 Firebase 登入

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
| → | `domain.useScroll` | `{ hero, item: "DomainScrollPower", count }`（境書補修練值） |
| → | `domain.trial` | `{ hero, domain: "Power" }`（修練值滿、且對應星色升滿才能挑戰） |
| → | `hero.starUp` | `{ hero }`（消耗信物＋響晶升一顆星） |
| → | `ping` | — |
| ← | `login.ok` | `{ playerId, displayName, serverTimeMs }` |
| ← | `city.state` | `{ serverTimeMs, city }` |
| ← | `city.upgradeCompleted` | `{ building, level }`（伺服器主動推播，reqId = 0） |
| ← | `city.trainingCompleted` | `{ troop, count }`（伺服器主動推播，reqId = 0） |
| ← | `expedition.result` | `{ stage, win, reports: [戰報…], rewards }`，之後再送一次 `city.state` |
| ← | `expedition.idleClaimed` | `{ resources, items }` |
| ← | `domain.trialResult` | `{ hero, domain, success, report }`，之後再送一次 `city.state` |
| ← | `error` | `{ code, detail }` |
