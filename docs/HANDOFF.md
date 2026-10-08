# 交接文件：深淵王國 Abyss Kingdom

> 給接手的 AI（Claude Code）與開發者本人。閱讀順序：本文件 → `CLAUDE.md`（開發規則）→ **`docs/design/GDD.md`（設定集：世界觀、名詞、規則、數值的唯一正式版本）** → `README.md`（怎麼跑、連線協定）。
> 最後更新：2026-10-08

## 1. 專案一句話

PC 先行（Steam＋自架下載、手遊之後）的 4X / SLG：玩家在深淵「永暗之喉」周圍的大陸建城、養成主將、互相爭奪；每個賽季結束後開放深淵下一層的大陸。完整設定見 `docs/design/GDD.md`。

## 2. 開發方式（已定案）

- 一人＋AI 全程開發，不組團隊；零成本起步，玩家與收入出現後才付費升級。
- 每次只做一個小功能；規則改動一定要加測試；秘密一律放環境變數。
- **設定以 GDD 為準**：玩家看到的名稱只寫在 `client/Assets/Scripts/UI/DisplayNames.cs`，程式內部用中性英文 enum（改名不影響存檔）。

## 3. 技術決策（已定案，勿隨意更換）

| 項目 | 選擇 | 備註 |
| --- | --- | --- |
| 客戶端 | Unity 6（本機 6000.4.8f1，Personal 已啟用） | PC（Windows）先行；介面用 UI Toolkit，全部由程式建立；不做 WebGL |
| 伺服器 | ASP.NET Core 8（C#）＋ WebSocket | 伺服器權威；模組化單體 |
| 共用規則 | `shared/AbyssKingdom.Shared`（netstandard2.1、C# 9） | 伺服器與 Unity 共用；只用整數與傳入時間，不用浮點、亂數由伺服器給種子 |
| 資料庫 | Supabase（PostgreSQL，伺服器用 service_role 走 REST） | 城市整包 jsonb；客戶端不可直連 |
| 登入 | Firebase Auth：PC 版走 **REST API** 匿名登入（Unity SDK 桌面版不可上線） | 之後加綁定 Google、Steam 登入；手遊版再用 Firebase SDK、FCM、Crashlytics |
| 主機 | **本機 Docker＋Cloudflare Quick Tunnel**（`docs/DEPLOY.md`） | Oracle 註冊被拒；找到雲端主機後沿用同一份 compose |
| 測試 | 自製迷你框架（無 NuGet），`dotnet run --project tests/AbyssKingdom.Tests` | 目前 154 個測試 |
| CI | GitHub Actions（`.github/workflows/ci.yml`） | |

## 4. 程式結構（2026-10-08）

```
shared/AbyssKingdom.Shared/
  City/        城建（CityConfig/CityState/CityLogic）、部隊訓練、隊列取消與加速（CityLogic.Queues）、道具（Items）
  Heroes/      主將（Heroes）、裝備與戰力（Gear）、四股索與斷索試煉（Domains）、升星（Stars）
  Battle/      確定性戰鬥核心（Battle）、淵脈克制與隊伍加成（Elements）
  Expedition/  遠征第一章、掛機收益
  Map/         地表大陸（Surface：800×800 格、同心分區、山脈與關隘、資源田／怪物營地、主城候選位置）、行軍（Marches）、Legacy（舊深淵存檔遷移）
  Protocol/    訊息類型與資料結構
server/AbyssKingdom.Server/
  Auth/ Game/（含 WorldService：地表全服主城索引）/ Net/ Persistence/ Admin/（GM 工具）  GameServer.cs 組裝
tests/AbyssKingdom.Tests/   規則單元測試、難度曲線測試、真實 WebSocket 端到端、Supabase 假 HTTP
client/
  Assets/Scripts/Net/     GameConnection（WebSocket）、FirebaseGuestAuth（REST 訪客登入）
  Assets/Scripts/Game/    GameClient（狀態與玩家意圖）
  Assets/Resources/Abyss/ 3D 材質（打包時由 Editor/AbyssMaterials.cs 產生）
  Assets/Scripts/UI/      GameUI（主畫面 HUD、回城按鈕、彈出面板）、HeroPage、ExpeditionPage、ContinentPage（大地圖資訊卡）、MarchPanel（出征面板與行軍列表）、UiKit、Icons（向量圖示）、DisplayNames（所有顯示文字）
  Assets/Scripts/World/   WorldScene（3D 鏡頭與輸入）、CityView（城內建築擺放）、SurfaceView（大地圖）
  Assets/Resources/UI/    Game.uss（樣式）、AbyssTheme.tss
  Assets/Editor/BuildScript.cs   一鍵打包（選單 Abyss Kingdom → Build Windows）
db/001_players.sql   docs/（HANDOFF、DEPLOY、SETUP、ACCOUNTS、design/GDD.md）
docker-compose.yml   伺服器＋Cloudflare Quick Tunnel
```

## 5. 帳號與外部服務狀態

| 服務 | 狀態 |
| --- | --- |
| GitHub | `https://github.com/Johnnnny0823/abyss-kingdom`（Private）。本機 Git Credential Manager 已存登入，可直接 `git push` |
| Supabase | Project URL = https://bqgpiibomfktlrasuupb.supabase.co；`db/001_players.sql` 已執行。Data API 開、Automatically expose new tables 關、RLS 開 |
| Firebase | 專案 ID **`abyss-kingdom`**；已啟用匿名與 Google 登入；已註冊網頁應用程式，**網路 API 金鑰**（公開識別碼）寫在 `FirebaseGuestAuth.cs`。匿名帳號自動清理**刻意不開**（會讓訪客進度遺失），待做「綁定 Google」後再評估 |
| Cloudflare | Quick Tunnel（免帳號）；**網址每次重啟 tunnel 會變**，查法見 DEPLOY.md |
| Oracle Cloud | 註冊被防詐騙自動拒絕；建議隔一兩週、用實體信用卡與一致的英文帳單地址再試 |

**本機開發機的 Windows 使用者環境變數**：`Supabase__Url`、`Supabase__ServiceKey`、`Admin__Token`（GM 工具）。秘密從未也不應出現在聊天或程式碼中。

## 6. 已驗證／待驗證

已實機驗證（2026-10-08）：Unity 編譯與打包、Supabase 讀寫與重啟後讀回、Firebase 真實 ID Token（真 token 通過、`dev:` 與竄改 token 被拒）、外網 `wss://` 登入、打包版訪客登入與重開後進度延續、各頁介面（以 `-devlogin` 測試帳號截圖）。

待驗證／已知限制：
1. 使用者尚未親手測過：加速道具視窗、取消退還、四股索試煉、升星（伺服器測試皆通過，介面已截圖）。
2. 訪客帳號只存在單台電腦（refresh token 存 PlayerPrefs），重灌即遺失 → 需要「綁定 Google」。
3. 數值都是測試用；難度曲線由測試守住（遠征、斷索試煉），改數值時注意。
4. 戰鬥只有文字戰報，尚無動畫。

## 7. 接下來的工作

依 **GDD 第 6 節**的開發路線。**目前專心做地表**（GDD 5a：S1 大陸地圖與主城 ✅ → S2 地表行軍 ✅ → S3 採集 ✅ → S4 怪物營地／傷兵／醫院 ✅ → **S5 PvP 攻城** → S6 聯盟盟旗）；賽季與深淵各層延後（深淵每層都會是和地表一樣的大陸）。

## 8. 開發小抄（給 AI）

- 測試：`dotnet run --project tests/AbyssKingdom.Tests`（可加名稱片段當過濾，例如 `-- Probe`）。
- 編譯客戶端（Unity 必須先關）：`Unity.exe -batchmode -quit -nographics -projectPath client -logFile <log>`；打包加 `-executeMethod BuildScript.BuildWindows`。
- 更新線上伺服器：先把 Windows 使用者環境變數讀進 `$env:`，再 `docker compose up -d --build server`（tunnel 網址不變）。
- 打包版測試參數：`-server <url>`、`-autologin`（訪客）、`-devlogin`（開發模式）、`-continent`（開大地圖）、`-heroes`／`-expedition`（直接開面板）、`-building <Keep|Barracks…>`、`-tile <x> <y>`（大地圖選取並移過去）、`-movetest <建築> <x> <y>`（自動走一次移動建築流程）、`-marchtest <x> <y>`（用出征面板自動配兵出征）、`-gathertest`（派部隊去最近的資源田採集）、`-camptest`（對最近的 Lv.1 營地派兵）、`-hospital`（直接開醫館）、`-autofight`（自動出征）。截圖前在本機 5090 埠開 Dev 伺服器（可設 `Admin__Token` 用 GM 工具準備資料），**不要用使用者的訪客帳號做測試**。
- 給朋友的 zip：`client/Builds/AbyssKingdom-Windows.zip`（排除 `*DoNotShip*` 資料夾）。

## 9. 給 Claude Code 的開場提示（可直接貼上）

```
這是《深淵王國》SLG 專案（PC 先行，手遊之後）。請先讀 docs/HANDOFF.md、CLAUDE.md、
docs/design/GDD.md、README.md，然後執行 dotnet run --project tests/AbyssKingdom.Tests
確認全部測試通過。接著依 GDD 第 6 節的開發路線，從下一個未完成項目開始協助我。
每次只做一個小功能、規則改動都要加測試、秘密一律用環境變數、玩家看到的名稱以 GDD 為準。
```
