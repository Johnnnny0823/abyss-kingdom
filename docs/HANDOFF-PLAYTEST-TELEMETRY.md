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
