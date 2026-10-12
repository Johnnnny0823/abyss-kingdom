# S6-2 聊天與幫助進度（2026-10-12）

本段接在 `docs/HANDOFF-S6-1.md` 之後。**尚未 commit／push，也未部署。** S6-2 主流程已接通，但下列驗收缺口未補完，不能宣稱整批完成；S6-3～S6-6 尚未實作。

## 已接通

- 聯盟聊天：`AllianceChat.cs` 在 `SocialGate` 下驗證當下盟籍、200 字純文字、每人 2 秒／20 則每分鐘、操作 UUID 去重；先儲存聯盟快照再 ACK／推播。聊天保留最近 100 則且最多七天，歷史按序號補發；隱藏訊息只回空文字。獨立的 `ChatRateWindows` 避免 100 則歷史被其他人擠掉後限流失效。
- 管理：盟主／幹部公告、檢舉、隱藏、禁言 1／24 小時；對幹部只限盟主，稽核保存在聯盟快照。`AlliancePanel` 顯示公告、在線數、聊天與管理按鈕；`GameClient` 在重連時帶最後聊天序號，手動刷新用完整快照。
- 幫助規則：`AllianceHelpProgress` 隨升級／治療隊列存在城主存檔；固定 queueId、原始時長、建隊時主堡等級決定的次數上限、已幫助者集合與已縮短量均持久化。公式依 `docs/design/S6_ALLIANCE.md` 第 5 節；同人重送、自己幫自己、舊 queueId、完成時間邊界皆由共用規則檢查。
- 幫助流程：城主請求後，聯盟快照列出隊列；盟友「幫助全部」逐項驗證當下盟籍和 epoch，透過 `SessionManager.ModifyCityAsync(saveNow:true)` 修改城主狀態。其他在線盟員會收到刷新通知。城主退盟／被踢時移除其未接納的請求。
- `WebSocketHandler`、`Messages.cs`、`GameClient`、`AlliancePanel`、`DisplayNames` 已接協定與中文錯誤說明。新增 `tests/AbyssKingdom.Tests/AllianceChatTests.cs`、`AllianceHelpRuleTests.cs` 與跨玩家 WebSocket 幫助整合測試。

## 證據

- 最後完整執行 `dotnet run --project tests/AbyssKingdom.Tests`：**207 passed, 0 failed, 207 total**。
- 最新 Unity batchmode 日誌：`artifacts/s6-visual-build.log`，第 816 行 `Build Finished, Result: Success.`；無 `error CS`。
- 兩個隔離的本機 Dev 玩家畫面：盟主 `artifacts/s6-ui-capture-visible/world-city.png`、成員 `artifacts/s6-ui-capture-helper/world-city.png`。兩次客戶端日誌皆有 `ART_CHECK COMPLETE models=38 failures=0`；聯盟名稱、公告、聊天、幫助數與雙方不同權限按鈕都可讀。擷取後只關閉本輪啟動的伺服器，5090 埠沒有殘留監聽。
- 截圖用 `-devname` 指定隔離玩家，不覆寫平常 PlayerPrefs 名稱；`-alliance` 直接開聯盟頁。`artifacts/s6_visual_seed.py` 只使用 localhost 和每次啟動時環境變數中的臨時 Admin token，無正式帳號／金鑰。**尚未驗證 Supabase 真實資料庫的快照大小／故障恢復。**

## 待補與風險

1. 手動取消／道具加速後會同步更新或撤下請求，聯盟頁顯示已幫／上限次數；自然完成後頁面按到期時間隱藏，但持久化快照中的舊請求要等下一次「幫助全部」或同隊列重新請求才清除。需補背景清理及完整失效原因。
2. 聊天操作收據保留七天，放在單列 JSON 快照；高流量聯盟可能讓快照變大。正式上線前應量測，必要時將聊天、收據與稽核拆成獨立資料表並保留單伺服器序列化語意。
3. 跨玩家幫助採城主存檔先成功、聯盟快照再更新；已新增「第二段失敗後重試不二次縮時且補回幫助計數」的故障注入測試，其他中斷點仍待覆蓋。`ModifyCityAsync(saveNow:true)` 失敗時記憶體變更仍可能等稍後重試保存；既有 S6-0 限制仍在。
4. S6-2 規格中的所有可點圖示說明、幫助細項不可用理由尚未做。聯盟頁目前是單一 ScrollView；1280×800 的盟主畫面下方名單需捲動，手機長聊天與按鈕排版仍需檢查。
5. 正式 Supabase 的 `db/002_alliances.sql` 尚未套用或跑獨立資料庫整合測試；勿部署 S6。

## 建議接續順序

先完成失效請求清理和 S6-2 故障注入／兩 Dev 玩家截圖；再依提案做 S6-3 混兵援軍、S6-4 盟旗、S6-5 PvE 集結、S6-6 整合與引導。使用者另外指定的青綠主城護盾罩仍待做，須由伺服器護盾狀態驅動。遵守繁體中文回報、不寫 `.git`、修改既有檔超過 30 行先備份到指定日期資料夾。
