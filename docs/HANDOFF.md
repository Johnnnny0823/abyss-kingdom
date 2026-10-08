# 交接文件：深淵王國 Abyss Kingdom

> 給接手的 AI（Claude Code）與開發者本人。先讀這份，再讀 `CLAUDE.md`（開發規則）與 `README.md`（怎麼跑）。
> 最後更新：2026-10-08

## 1. 專案是什麼

4X / SLG 策略遊戲（**2026-10-08 起 PC 先行**：Windows 下載版，Steam 上架＋自架網站提供載點；手遊版之後用同一份 Unity 專案推出），參考《王國之歌》（Top Heroes）、《寒霜啟示錄》（Whiteout Survival）、《萬國覺醒》（Rise of Kingdoms）。
差異化：把平面大地圖改成「**垂直分層的深淵**」——玩家在巨大裂口邊建城，派探窟隊往下探索；越深資源越稀有，但從深層返回要付出「**回響**」代價。

設計三支柱：
1. **向下探索**：每一層深淵是一張獨立地圖。
2. **上升代價**：從深層撤回要支付回響成本，行軍與補給成為策略決策。
3. **聯盟共存**：聯盟一起打通樓層、共建升降井，賽季末比誰先抵達深淵之底。

世界觀（原創草案）：三百年前大陸中央塌陷出垂直裂口「永暗之喉」，玩家是新任「端座領主」。陣營：銀繩公會、鐵錘同盟、暗潮教團、自由探窟團。英雄以「繩級」分品質（綠→藍→紫→金→黑）。

**版權紅線**：只借用《來自深淵》的「垂直深淵探勘」概念。不得使用其名詞（如奈落、白笛）、角色、造型、劇情或畫風；美術與文案（含 AI 生圖）都必須原創。

完整企劃書（含競品研究、系統清單、商業化、技術架構圖、路線圖）在 claude.ai 的 Claude Docs：《深淵王國》SLG 手遊企劃書與技術架構。

## 2. 開發方式（已定案）

- **一人 + AI 全程開發**（Vibe Coding），不組團隊。
- **零成本起步**：只用免費方案；玩家變多、開始有收入後才付費升級。
- 先做出能多人連線的「單層深淵」原型，再逐步擴充。

## 3. 技術決策（已定案，勿隨意更換）

| 項目 | 選擇 | 理由 / 備註 |
| --- | --- | --- |
| 客戶端 | Unity 6（本機 6000.4.8f1，Personal 免費） | **PC（Windows）先行**，Steam＋自架下載；Mac 版需 Mac 才能簽章公證，暫不做；手遊之後。不做 WebGL（不支援 `ClientWebSocket`） |
| 伺服器 | ASP.NET Core 8（C#）+ WebSocket | 與 Unity 同語言，規則程式碼共用 |
| 共用規則 | `shared/AbyssKingdom.Shared`（netstandard2.1、C# 9） | Unity 以本機 UPM 套件引用；不得引用 NuGet 或 UnityEngine |
| 架構 | 模組化單體、伺服器權威 | 之後人多再拆服務 |
| 主資料庫 | Supabase（託管 PostgreSQL） | 伺服器用 service_role key 走 REST（PostgREST）；客戶端不可直連 |
| 登入 / 推播 / 當機 | Firebase Auth（匿名 + Google）。**PC 版走 Firebase Auth REST API**（Firebase Unity SDK 桌面版僅供開發，不可上線）；上 Steam 後加 Steam 登入。FCM、Crashlytics、Remote Config 留給手遊版 | **不用 Firestore**（按讀寫計費、不適合 SLG） |
| 快取 | Redis（之後才加，同主機 Docker） | 目前狀態都在伺服器記憶體 |
| 主機 | Oracle Cloud Always Free（Ampere A1，ARM64） | 註冊被拒（防詐騙自動拒絕）；**目前改用本機 Docker + Cloudflare Quick Tunnel**，見 docs/DEPLOY.md |
| CI | GitHub Actions（`.github/workflows/ci.yml`） | 跑建置與測試 |
| 測試 | 自製迷你測試框架（無 NuGet） | 雲端開發環境當時無法連 NuGet，故維持零套件 |

## 4. 目前完成的內容（第 1–2 週里程碑）

```
shared/AbyssKingdom.Shared/
  City/Types.cs        資源 4 種、建築 7 種（主堡 + 伐木場/採石場/農田/晶礦場/倉庫/兵營）、升級結果碼
  City/CityConfig.cs   數值：上限 10 級、成本 ×1.5/級、時間 ×1.4/級、產量 100/級/小時、倉庫容量
  City/CityState.cs    玩家城市狀態（時間用 Unix 毫秒，資源用 long）
  City/CityLogic.cs    Advance（惰性結算 + 餘數防流失 + 升級完成時分段產量）、CanUpgrade、StartUpgrade
  Protocol/Messages.cs 訊息類型與資料結構
  package.json / .asmdef  讓 Unity 當本機套件引用
server/AbyssKingdom.Server/
  GameServer.cs        組裝（設定決定 Auth / Storage 模式），/ws、/healthz
  Auth/                DevAuthVerifier（"dev:名字"）、FirebaseAuthVerifier（手寫 RS256 JWT 驗證，無套件）
  Game/SessionManager  上線管理、每秒 Tick 推進並推播升級完成、30 秒存 dirty、下線/關機存檔、重複登入踢舊連線
  Net/WebSocketHandler JSON 信封 {type, reqId, data}，分派 login / city.get / city.upgrade / ping
  Persistence/         InMemoryPlayerStore、SupabasePlayerStore（REST upsert，snake_case 欄位）
  Dockerfile           多階段建置，ARM64 可用
tests/AbyssKingdom.Tests/  22 個測試：規則單元測試、真實 WebSocket 端到端、Supabase 假 HTTP 測試
client/                Unity 專案：Packages/manifest.json（引用 shared）、GameConnection（WebSocket + Newtonsoft）、
                       CityDebugPanel（IMGUI 開發面板，按 Play 自動出現）
db/001_players.sql     players 表（jsonb 存城市）、開 RLS、只授權 service_role
docs/                  SETUP.md（環境安裝）、ACCOUNTS.md（帳號註冊）、本文件
```

設定（`appsettings.json`，可用環境變數覆寫）：`Auth__Mode`（Dev/Firebase）、`Firebase__ProjectId`=`abyss-kingdom`、`Storage__Mode`（Memory/Supabase）、`Supabase__Url`、`Supabase__ServiceKey`（只放環境變數）。

## 5. 帳號與外部服務狀態

| 服務 | 狀態 |
| --- | --- |
| GitHub | repo：`https://github.com/Johnnnny0823/abyss-kingdom`（Private），已設為 git remote；由使用者用 GitHub Desktop 上傳 |
| Supabase | 專案已建立。Security：Data API 開、Automatically expose new tables 關、automatic RLS 開。`db/001_players.sql` 已執行（2026-10-08）；Project URL = https://bqgpiibomfktlrasuupb.supabase.co。本機開發機已在 Windows 使用者環境變數設好 `Supabase__Url`、`Supabase__ServiceKey` |
| Firebase | 專案 ID **`abyss-kingdom`**（注意：`project-abyss-kingdom` 是公開顯示名稱，不是 ID）。已啟用匿名與 Google 登入；**匿名帳號自動清理刻意不開**（會刪掉 30 天以上的訪客帳號 → 玩家 UID 改變、進度遺失），待做「訪客綁定 Google」後再評估 |
| Oracle Cloud | 2026-10-08 再次嘗試：填完資料後被拒（「系統在建立您的帳戶時發生錯誤…嘗試建立多個帳戶」，防詐騙自動拒絕），客服機器人無法處理。建議隔一兩週、用實體信用卡與一致的英文帳單地址再試。**目前改用本機 Docker + Cloudflare Quick Tunnel**（docs/DEPLOY.md）。 |

秘密（資料庫密碼、service_role key、Firebase 服務帳號）**從未也不應**出現在聊天或程式碼中。

## 6. 尚未驗證的部分（接手後優先確認）

1. ~~Unity 客戶端從未實際編譯過~~ → 2026-10-08 已在 Windows 用 Unity 6000.4.8f1 編譯通過，Play 登入、升級、完成推播皆正常，編輯器內中文顯示正常。**手機上的 IMGUI 中文字型仍未驗證**。
2. ~~SupabasePlayerStore 只用假 HTTP 測過~~ → 2026-10-08 已對真實 Supabase 驗證：建表、登入寫入、30 秒定時存檔、重啟伺服器後讀回狀態皆正常。
3. **FirebaseAuthVerifier 未用真實 ID Token 測過**，等客戶端接上 Firebase REST 訪客登入後驗證。
4. ~~Android 預設禁止明文連線~~ → 已透過 Cloudflare Quick Tunnel 提供 `wss://`，2026-10-08 實測 wss 登入成功。

## 7. 接下來的工作（依優先順序）

1. ~~**本機跑通**~~（2026-10-08 完成）：`dotnet run --project tests/AbyssKingdom.Tests`（22 passed）→ 啟動伺服器 → Unity 開 `client/` 按 Play 登入升級；修掉 Unity 編譯問題。
2. ~~**接真實 Supabase**~~（2026-10-08 完成）：執行 SQL、用環境變數設定 URL 與 key，驗證讀寫。
3. ~~**部署**~~（2026-10-08 完成，過渡方案）：本機 `docker compose up -d --build` 跑伺服器 + Cloudflare Quick Tunnel，見 docs/DEPLOY.md。缺點：電腦要開著、網址每次重啟會變、仍是 Dev 登入。找到雲端主機後沿用同一份 compose。
4. **PC 版訪客登入**：客戶端呼叫 Firebase Auth REST API（`accounts:signUp` 匿名註冊、refresh token 存本機換新 ID Token），伺服器改 `Auth__Mode=Firebase`。之後再加「綁定 Google」（桌面 OAuth loopback）。
   - 已完成：Windows 打包（`client/Assets/Editor/BuildScript.cs`，選單 Abyss Kingdom → Build Windows）。
5. **第 3–4 週**：城建完善（多建築隊列？兵營訓練部隊、資源 UI）。
6. **第 5–7 週**：第 1 層大地圖（約 200×200 格）、AOI 九宮格視野、行軍（伺服器只存起點/終點/出發時間/速度，客戶端插值）、採集與野怪。
7. **第 8–9 週**：英雄 5 名、3 兵種克制、確定性戰鬥（定點數、伺服器結算、下發戰報）。
8. **第 10–11 週**：聯盟（建立/加入、聊天）、攻城。
9. **第 12 週**：10–20 位朋友封測（發 Windows zip）、錄 Demo。
10. **封測之後**：Steam 上架（Steam Direct 每款 100 美元；Steam 登入；遊戲內購須走 Steam 錢包、抽成 30%）、Windows 程式碼簽章、手遊版。

MVP 刻意不做：抽卡、陣營羈絆、多層深淵與回響、升降井、集結、偵察、內購（保留設計）。

## 8. 已定案的遊戲設計細節（實作時遵守）

- **行軍呈現**：地圖上每支行軍只用 1–5 個小兵模型代表（依兵力分級），依鏡頭距離切換（近：英雄+小兵；中：軍旗；遠：光點或「× N 支」聚合）。點擊才查詢詳細兵力。自己與盟友顯示精確數字，敵方只顯示「約 5,000」，偵察後才精確。伺服器平時只推摘要（防外掛偷看）。
- **PvP 低懲罰**：新手護盾、戰敗傷兵進醫院而非死亡。
- **商業化（之後）**：首儲、月卡、賽季通行證、英雄招募（保底）、成長禮包、外觀；訂單必須在同一筆資料庫交易內「驗收據 → 寫訂單 → 發貨」，以訂單號冪等。
- **經濟**：基礎資源可掠奪、稀有資源不可掠奪；回響（抵響劑、醫療）作為長期消耗池。

## 9. 給 Claude Code 的開場提示（可直接貼上）

```
這是《深淵王國》SLG 專案（PC 先行，手遊之後）。請先讀 docs/HANDOFF.md、CLAUDE.md、README.md，
然後執行 dotnet run --project tests/AbyssKingdom.Tests 確認 22 個測試全部通過。
接著依 HANDOFF.md 第 7 節的優先順序，從第 1 項開始協助我。
每次只做一個小功能、規則改動都要加測試、秘密一律用環境變數。
```
