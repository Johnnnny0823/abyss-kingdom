# Codex 派工：封測紀錄、錯誤回報、回饋按鈕

基準 commit：`8a21480`。對應 `docs/PLAYTEST_CHECKLIST.md` 的 **A4、B1、B2**。
背景：要讓 5～10 位朋友玩前 30～60 分鐘，目的是知道他們**在哪裡卡住、在哪裡離開**。目前伺服器沒有任何遊玩紀錄，客戶端閃退也看不到。

## 共通原則

- **不收個資**：只用伺服器內部的玩家 ID（`dev-…`／Firebase uid）與玩家自取的領主名；不記 IP、裝置序號、電腦名稱或檔案路徑。
- 紀錄寫入失敗**不能影響遊戲**（不擋登入、不擋任何玩家操作，失敗只寫 log）。
- 秘密只放環境變數；GM 端點沿用現有 `X-Admin-Token` 保護（`Admin/AdminEndpoints.cs`）。
- 新訊息類型照專案規則：常數加在 `shared/.../Protocol/Messages.cs`、伺服器在 `Net/WebSocketHandler.cs` 分派、README 協定表同步更新。
- 測試用自帶 `[Test]` 框架，不用 NuGet。

## A4：遊玩紀錄

1. **事件儲存**：新增事件表 `db/003_playtest_events.sql`（Supabase，RLS 開、不對外暴露，照 `001`／`002` 的寫法），以及 InMemory 版（測試與本機 Dev 用），介面照 `Persistence/PlayerStores.cs` 的模式。欄位建議：`id`、`player_id`、`kind`、`at_ms`、`data jsonb`（小型）。
2. **要記的事件**（伺服器端記錄，客戶端不能偽造）：
   - `session.start`／`session.end`（登入、斷線；含本次遊玩秒數）
   - `quest.claim`（第幾個任務）
   - `building.upgrade.start`（哪棟、目標等級；主堡要能看出升級時間點）
   - `first.*` 里程碑：第一次訓練、第一次出征、第一次採集、第一次打營地、第一次加入聯盟
3. **GM 匯出**：`GET /admin/playtest/summary` 回傳每位玩家一列：領主名、首次登入、最後上線、總遊玩時間、登入天數（有沒有第二天回來）、目前任務索引、主堡等級、最後一個事件。另有 `GET /admin/playtest/events?playerId=` 看單人時間軸。輸出 JSON 即可；若方便，加 `?format=csv`。
4. 測試：事件寫入與匯出的單元測試；真 WebSocket 測試走一遍登入 → 領任務 → 斷線，summary 數字正確；事件儲存故障時登入與操作照常成功。

## B1：錯誤回報

1. **客戶端例外**：註冊 `Application.logMessageReceived`，遇到 Exception／Error 時送新訊息 `client.error`（訊息 + 前幾行 stack，長度上限例如 2,000 字；同一錯誤 60 秒內只送一次，每次登入最多 20 筆）。伺服器存成事件 `client.error`。
2. **GM 查詢**：`GET /admin/playtest/errors` 依錯誤訊息分組，列出次數、影響幾位玩家、最近一次時間。
3. **自己找 log**：介面上提供「開啟紀錄資料夾」（打開 `Application.persistentDataPath` 所在資料夾，Player.log 在那附近），放在 B2 的回饋視窗裡即可。
4. 伺服器未處理的例外確認已寫入 log（Docker logs 看得到）；若沒有就補。

## B2：回饋按鈕

1. HUD 上加一個「回饋」按鈕（位置自選，不要擋到既有按鈕；專案規則：**每個圖示都要能點開看名稱與說明**，用文字按鈕或有說明的圖示）。
2. 點開視窗：一個文字框（上限 1,000 字）＋送出＋「開啟紀錄資料夾」（B1）。送出後顯示「已收到，謝謝」。
3. 新訊息 `feedback.send`：伺服器附上當下狀態快照（主堡等級、任務索引、本次與總遊玩時間、目前畫面是城內或大地圖），存成事件 `feedback`。每位玩家每分鐘最多 3 則。
4. `GET /admin/playtest/feedback` 依時間列出所有回饋與快照。
5. 測試：空字串與超長拒絕、頻率限制、快照內容正確。

## 驗收

- `dotnet run --project tests/AbyssKingdom.Tests` 全部 PASS；Unity 打包 0 errors。
- 本機 Dev（`Auth__Mode=Dev`、`Storage__Mode=InMemory`、5090 埠，**不要用使用者的訪客帳號**）：用專用 dev 帳號玩幾步、送一則回饋、觸發一次測試用錯誤，貼上三個 GM 端點的實際輸出與回饋視窗截圖。
- Supabase 部分：`003` SQL 寫好即可，**不要在正式資料庫執行**；在交接裡寫清楚使用者要執行的步驟。
- 交付說明寫在本文件末尾：改動檔案、測試數、端點輸出範例、截圖路徑。不 commit、不 push、不部署，交給 Claude 驗收。

## 2026-10-13 實作交付

- 完整測試 `artifacts/playtest-tests.log`：**276 passed、0 failed**。涵蓋記憶體事件副本、first.* 去重／重啟、回饋輸入／滑動限流／重連、錯誤去重與二十筆上限、文字遮蔽、真 WS 登入→升主堡→領任務→回饋→斷線／隔日登入、存檔故障下正常升級、四個 GM 的 token 保護、Supabase 假 HTTP 標頭／欄位／500 筆分頁。無新 NuGet。
- 新訊息 `client.error`、`feedback.send`、`feedback.received` 已同步共用協定、WebSocket 與 README；沒有接受客戶端任意遊戲事件的 API。主堡、任務與時間快照使用伺服器狀態，screen 僅是 city/world 的客戶端畫面上下文。
- 事件由伺服器記錄：session.start/end、quest.claim、building.upgrade.start、first.training/march/gather/camp/alliance、client.error、feedback。first.* 以穩定 ID 跨重啟去重，不覆寫初次時間；舊玩家不能重建部署前已遺失的行為歷史。
- PlaytestTelemetry 的 1,024 筆背景佇列在遊戲路徑只 TryWrite，不等資料庫、不在玩家 Gate 內做事件儲存 I/O。背景寫入最多五秒，失敗只 warning 並放棄；accepted 是排隊確認，不保證故障時仍保存回饋文字。
- 事件表無 IP、裝置序號、電腦名稱等欄位；Error／Exception 登入後自動上報，訊息 1,200 字＋stack 前五行／800 字，雙端同訊息六十秒去重、每次登入二十筆。共用工具遮蔽常見路徑、URL、IP、email 與帶標籤的 token／password，不上傳整份 Player.log。自由回饋也遮蔽常見私人內容，視窗提醒不要填個資。
- 原生硬崩潰／未送達的例外不能保證上報；「開啟紀錄資料夾」只在玩家點擊時開本機 persistentDataPath，不傳路徑。既有 WebSocket 未處理例外 LogError，ASP.NET 例外進 console／Docker logs。
- 回饋上限一千字、滑動六十秒每玩家三則，重新登入不重置限額；顯示收到確認或失敗／逾時，reqId 防止舊確認套到新回饋。HUD 使用文字按鈕，沒有不可點擊的新圖示。
- 摘要包含內部玩家 ID、領主名、首次登入／最後上線、已結束＋當前 session 秒數、**UTC 日曆登入天數**、零起算任務索引、主堡級別、最後事件、online。正常斷線／連線取代只結束一次；硬殺伺服器缺 end 的段落可能漏計，不虛構離線時間。回饋 totalPlaySecondsKnown 表示啟動歷史載入成功；失敗時僅有本程序已知部分。
- 實際 Dev 輸出：`artifacts/playtest-summary.json`、`playtest-events.json`、`playtest-errors.json`、`playtest-feedback.json`；專用帳號 `dev-PlaytestPreview`。已觀察主堡二級、任務索引一、一次回饋與一筆測試錯誤。
- `artifacts/playtest-build.log` 第 770 行 Success、第 785 行 Succeeded／0 errors。`playtest-player.log` 第 31 行 PLAYTEST_CHECK PASS，`playtest-ui/validation.json` 為 true。`feedback-draft.png` 已開啟確認中文內容可讀，`feedback.png` 已確認「已收到，謝謝」。診斷走真 Upgrade／Speedup／Quest／Feedback API，透過 UI 所綁定 Send 方法送出，不冒稱 Windows 手動點擊。截圖由實際 UI Toolkit targetTexture 與場景相機合成；UI RenderTexture 使用含 stencil 的 24-bit depth，避免多行文字框遮罩顯示成白塊。

### Supabase 啟用步驟（由使用者／Claude 操作）

1. 先在獨立 Dev Supabase 的 SQL Editor 人工執行 `db/003_playtest_events.sql`，確認表、索引、RLS 與 anon/authenticated 撤權；不新增公開 policy。程式不自動 migration，本輪未碰正式資料庫。
2. 沿用環境變數 `Storage__Mode=Supabase`、`Supabase__Url`、`Supabase__ServiceKey`、`Admin__Token`；正式 `Auth__Mode=Firebase`。不需新帳號、付費服務或新金鑰，秘密不可入檔。
3. 獨立測試帳號登入／操作／回饋，用 token 查四個 GM GET；重啟確認事件、first.* 與總秒數仍在。真 Dev DB 驗證通過後，由使用者決定正式執行 003／部署。fake HTTP PASS 不等於真 Supabase 已驗證。
4. 003 未建或 REST 故障時遊戲照常，但紀錄可能缺漏，朋友封測前需確認 GM 已有資料。正常關服會嘗試排空，佇列滿或寫入失敗的事件不重試。

### 改動檔案

- 新增 `server/AbyssKingdom.Server/Persistence/PlaytestStores.cs`、`Game/PlaytestTelemetry.cs`、`db/003_playtest_events.sql`。
- 修改 `GameServer.cs`、`Game/SessionManager.cs`、`Net/WebSocketHandler.cs`、`Admin/AdminEndpoints.cs`。
- 新增 `shared/AbyssKingdom.Shared/Protocol/PlaytestReports.cs` 與 meta；修改 `Protocol/Messages.cs`。
- 新增 `client/Assets/Scripts/Game/ClientErrorReporter.cs`、`UI/FeedbackPanel.cs`、`Diagnostics/PlaytestCheck.cs` 與各 meta；修改 `Game/GameClient.cs`、`UI/GameUI.cs`、`UI/DisplayNames.cs`、`Resources/UI/Game.uss`。
- 新增 `tests/AbyssKingdom.Tests/PlaytestTelemetryTests.cs`、`PlaytestIntegrationTests.cs`、`PlaytestStoreTests.cs`；既有 `ServerIntegrationTests.cs` 改 partial，沿用真 WS fixture。
- README、GDD、PLAYTEST_CHECKLIST 與本文件。既有大改檔備份在 `C:\Users\xnikz\.claude\backups\2026-10-13\playtest-telemetry\`。
- 不 commit、不 push、不部署、不寫 .git；本輪 Dev／InMemory 測試不使用使用者的訪客帳號。
- 最終驗證完成，本輪客戶端已退出，Dev 伺服器已停止。SQL 僅寫檔，真 Supabase 仍待使用者／Claude 驗證。


### 最終本機 GM 實際輸出

GET /admin/playtest/summary

```json
[
  {
    "playerId": "dev-PlaytestPreview",
    "displayName": "PlaytestPreview",
    "firstLoginMs": 1791825470231,
    "lastOnlineMs": 1791825485815,
    "totalPlaySeconds": 15,
    "loginDays": 1,
    "questIndex": 1,
    "keepLevel": 2,
    "lastEvent": "session.end",
    "online": false
  }
]
```

GET /admin/playtest/errors

```json
[
  {
    "message": "PLAYTEST_SAMPLE: harmless local error for reporting verification",
    "count": 1,
    "players": 1,
    "lastAtMs": 1791825483190
  }
]
```

GET /admin/playtest/feedback

```json
[
  {
    "id": "73ccd958861545dd945e16d13c720e17",
    "playerId": "dev-PlaytestPreview",
    "kind": "feedback",
    "atMs": 1791825483176,
    "data": {
      "displayName": "PlaytestPreview",
      "keepLevel": 2,
      "questIndex": 1,
      "sequence": 4,
      "text": "封測測試：升級前置清楚，希望採集目標更容易找到。",
      "screen": "city",
      "sessionPlaySeconds": 12,
      "totalPlaySeconds": 12,
      "totalPlaySecondsKnown": true
    }
  }
]
```
