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

正式環境務必設定 `Auth__Mode=Firebase`，否則任何人都能用名字登入別人的帳號。`Auth__Mode`、`Storage__Mode` 填了不認得的值（例如拼錯）伺服器會拒絕啟動，不會默默退回 Dev 或記憶體存檔。

啟用 S6 聯盟前，Supabase 需先套用 `db/002_alliances.sql`。目前聯盟資料由單一遊戲伺服器序列化寫入；不要以多個遊戲伺服器實例同時提供聯盟寫入。

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
| → | `city.heal` | `{ troops: { "Shield": 30 } }` 治療醫館裡的傷兵（花訓練成本的一半與一半時間；同時一批）；取消／加速用 `city.cancel`／`city.speedup`，queue = `Healing` |
| → | `city.move` | `{ building, x, y }` 把建築移到城內格子（佔地左上角；不可重疊、不可超出 24×24） |
| → | `hero.levelUp` | `{ hero: "Hold", item: "HeroExpSmall", count: 1 }` |
| → | `gear.equip` / `gear.unequip` | `{ gear: <uid>, hero }` / `{ gear: <uid> }` |
| → | `expedition.battle` | `{ stage: 1, teams: [["Hold","Vesper"],["Karst"]] }`（每條岔路一隊） |
| → | `expedition.claimIdle` | —（領掛機收益） |
| → | `domain.useScroll` | `{ hero, item: "DomainScrollPower", count }`（索譜補修練值） |
| → | `domain.trial` | `{ hero, domain: "Power" }`（修練值滿、且對應星色升滿才能挑戰） |
| → | `hero.starUp` | `{ hero }`（消耗信物＋響晶升一顆星） |
| → | `quest.claim` | —（領取目前新手任務的獎勵；成功回 `city.state`，任務進度在 `city.questIndex`、`city.stats`；失敗 `quest_rejected`：`NotDone`／`NoQuest`） |
| → | `alliance.get` | —（列出聯盟及自己的盟籍、成員、可審核申請、創盟待完成操作） |
| → | `alliance.create` | `{ name, tag, operationId }`（`operationId` 是重試時不變的 UUID；主堡 Lv.3、根材／層岩各 5,000） |
| → | `alliance.join` / `alliance.apply` | `{ allianceId }`（主堡 Lv.2；自由加入或審核申請） |
| → | `alliance.review` | `{ playerId, accept }`（盟主／幹部審核申請） |
| → | `alliance.leave` / `alliance.dissolve` | —（離盟或僅剩盟主時解散） |
| → | `alliance.kick` / `alliance.transfer` | `{ playerId }`（踢人／轉讓盟主） |
| → | `alliance.officer` | `{ playerId, officer }`（任免幹部） |
| → | `alliance.joinMode` | `{ mode: "Open" \| "Approval" }` |
| → | `alliance.rally.create` | `{ hero, troops, x, y, seconds: 60\|180\|300, operationId }`（Lv.2／3 營地，最多五人自由混兵；至少兩人到齊） |
| → | `alliance.rally.join` / `alliance.rally.recall` | `{ id, hero, troops, operationId }` / `{ id }`（準備期可退出；出發後僅隊長可整隊召回；結算中不能召回） |
| → | `alliance.flag.place` / `alliance.flag.remove` | `{ slot: 1..3, x, y, operationId }` / `{ slot }`（盟主／幹部；付款重試 UUID 不變；撤旗不退款且槽位冷卻 24 小時） |
| ↔ | `world.territory` | — → `{ flags: [{ id, allianceId, tag, x, y, paid, completesAtMs }] }`（公開領地；生效以伺服器時間為準） |
| → | `alliance.chat.post` / `alliance.chat.history` | `{ operationId, text }` / `{ afterSequence }`（成員聊天，UUID 重試不變） |
| → | `alliance.chat.report` / `alliance.chat.hide` / `alliance.chat.mute` | `{ sequence }` / `{ sequence }` / `{ playerId, hours: 1 \| 24 }` |
| → | `alliance.announcement` | `{ text }`（盟主／幹部公告） |
| → | `alliance.help.request` / `alliance.help.all` | `{ queue: "Building" \| "Healing" }` / — |
| → | `alliance.reinforcement.send` | `{ playerId, hero, troops, operationId }`（一主將混兵，雙方主堡 Lv.3；在途預留唯一席位；UUID 重試不變） |
| → | `alliance.reinforcement.return` / `alliance.reinforcement.accept` | `{ id }`（原主人召回／收援者遣返） / `{ accept }`（只關閉新派遣） |
| ← | `alliance.chat` / `alliance.chat.messages` | `{ allianceId, messages, fullSnapshot, oldestSequence, latestSequence }` |
| ← | `alliance.help.result` / `alliance.help.changed` | `{ applied, expired }` / `{}`（收到變更後重讀聯盟資料） |
| ← | `alliance.military.changed` | `{}`（只推播援軍雙方；重讀 `alliance.get`，援軍狀態在 `reinforcements`） |
| → | `map.march` | `{ hero, troops: { "Rider": 50 }, x, y }`（從主城出征到地表格子 (x, y)；直線行進，跨分區時自動繞經關隘；目標是資源田就採集、怪物營地就開打、別人的主城就攻城（會解除自己的護盾）；狀態在 `city.surface.marches`） |
| → | `map.recall` | `{ march }`（召回行軍，路上或駐紮中都可以；回到主城後部隊歸營） |
| → | `world.tile` | `{ x, y }`（查詢某一格的即時狀態：資源田的存量、誰在採集） |
| → | `world.cities` | —（取得地表所有主城的公開資訊；自己的主城位置在 `city.surface.x / y`） |
| → | `world.camps` | —（取得目前被擊敗、還沒重生的怪物營地） |
| → | `ping` | — |
| ← | `login.ok` | `{ playerId, displayName, serverTimeMs }` |
| ← | `city.state` | `{ serverTimeMs, city }` |
| ← | `city.upgradeCompleted` | `{ building, level }`（伺服器主動推播，reqId = 0） |
| ← | `city.trainingCompleted` | `{ troop, count }`（伺服器主動推播，reqId = 0） |
| ← | `expedition.result` | `{ stage, win, reports: [戰報…], rewards }`，之後再送一次 `city.state` |
| ← | `expedition.idleClaimed` | `{ resources, items }` |
| ← | `domain.trialResult` | `{ hero, domain, success, report }`，之後再送一次 `city.state` |
| ← | `map.marchArrived` | `{ march, hero, tile, disbanded, gathering, occupied, gatherDone, carry, lootItems, battleWon, battleLost, campGone, wounded, dead, shielded, allied, targetName }`（tile = y × 800 + x；抵達、採集、打怪、攻城與同盟無戰鬥折返；伺服器主動推播，reqId = 0） |
| ← | `alliance.state` | `{ version, serverTimeMs, alliances, myAllianceId, myRole, members, applications, rejoinAfterMs, guideProgress, helpRequests, reinforcements, flags, rallies, pendingCreateOperationId, pendingCreateName, pendingCreateTag }`（審核資料只給有權者；操作 UUID 與派兵原配置只給本人；舊版本快照不覆蓋新版本） |
| ← | `world.tile` | `{ x, y, stock, max, occupant, mine, campBackAt }` |
| ← | `city.healingCompleted` | `{ troops }`（治療完成，伺服器主動推播） |
| ← | `city.attacked` | `{ attackerName, defenderWon, wounded, dead, plundered }`（你的主城被攻打，伺服器主動推播） |
| ← | `world.cities` | `{ cities: [{ x, y, name, power, mine, allied, shielded, keepLevel }] }`（`allied` 由伺服器依盟籍判定；自己／同盟淺藍，非同盟紅色，不公開帳號 ID） |
| ← | `world.camps` | `{ camps: [{ x, y, backAt }] }`（`backAt` = 重生時間，伺服器 Unix 毫秒） |
| ← | `error` | `{ code, detail }` |
