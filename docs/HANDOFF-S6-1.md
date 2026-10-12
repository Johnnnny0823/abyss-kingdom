# S6 聯盟實作交接（2026-10-12）

本次完成 S6-0 的立即存檔失敗回報修正，並接上 S6-1 的成員與階級第一個可操作版本。**尚未提交或推送。** 不要將 S6-2～S6-6 視為已完成。

## 已接通

- `shared/.../Social/AllianceRules.cs`：主堡門檻、建立費、人數與幹部上限、名稱正規化、階級權限、24 小時申請／離盟冷卻。
- `server/.../Persistence/AllianceStores.cs`：獨立盟籍快照；Memory 與 Supabase 實作，SQL 在 `db/002_alliances.sql`。本版只支援**一個**遊戲伺服器實例；正式 Supabase 部署前先套 SQL，並補資料庫環境整合測試。
- `server/.../Game/AllianceService.cs`：`SocialGate` 序列化盟籍變更；創盟先預留名稱，玩家城市以 `SocialReceipts` 扣款並即時存檔，再使聯盟生效。失敗或 ACK 遺失用同一 UUID 重試，不重扣。玩家可透過 `alliance.get` 取得待完成創盟 UUID。
- 會員操作：自由加入、申請／審核、離盟、踢人、任免幹部、轉讓盟主、單人解散、加入模式。入盟時檢查玩家主堡，不能靠客戶端傳等級。
- `WebSocketHandler`、`Messages.cs`、`GameClient`、`AlliancePanel`、`GameUI` 已接通聯盟協定和操作頁；大地圖名牌顯示 `[簡稱] 領主名`。
- 同盟城市在出兵時拒絕攻打；若出兵後才成為同盟，抵達前重新檢查並無損折返。已凍結在守方攻城紀錄的戰鬥仍沿用原結果。
- `SessionManager.ModifyCityAsync(saveNow:true)` 存檔失敗不再回成功或推播成功狀態。

## 已驗證

- `dotnet run --project tests/AbyssKingdom.Tests` 最後完整執行結果：**197 passed, 0 failed, 197 total**。
- 聯盟測試包含同時加入兩盟、30 人滿員、四名幹部、申請與冷卻邊界、越權審核、創盟扣款重送、城市存檔失敗重試，以及出兵／抵達同盟防線。
- Unity batchmode `BuildScript.BuildWindows` 最後在 `artifacts/s6-alliance-build-final.log` 顯示 `Build Finished, Result: Success.`，沒有 `error CS`。

## 下一步與限制

1. 用兩個本機 Dev 玩家截圖聯盟頁與名牌，確認大小、字體與按鈕可用。不要用正式帳號。本段已有兩玩家 WebSocket 測試，但尚未做視覺截圖。
2. 正式 Supabase 的 `db/002_alliances.sql` 尚未套用，也尚未跑真資料庫整合測試。未完成前不要正式部署 S6。
3. 創盟的跨玩家資料恢復以待完成 UUID 重試；重啟後需透過聯盟頁「重試完成創盟」。後續可加伺服器背景恢復，並驗證所有中斷點。
4. 本版尚未做聯盟聊天、幫助、援軍、盟旗與 PvE 集結；依 `docs/design/S6_ALLIANCE.md` 的 S6-2～S6-6 順序推進。援軍與集結是每人一位主將、三兵種可混編，派兵容量隨主堡等級成長。
5. 使用者另要求護盾中的大地圖主城有青綠防護罩，罩住主城與外牆；目前仍未實作，需依伺服器 `CityPin.Shielded` 驅動，並能點擊查看剩餘時間與條件。

遵守 `CLAUDE.md`：規則變更加測試，改既有檔超過 30 行先備份到 `C:\Users\xnikz\.claude\backups\日期\`。本次備份在 `2026-10-12/s6-implementation/`。不要 commit、push 或寫入 `.git`；由使用者或 Claude 提交。
