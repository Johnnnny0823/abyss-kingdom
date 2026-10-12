# S6 聯盟交付與驗證（2026-10-12）

本輪完成本機 S6 範圍，尚未提交或部署。使用者與 Claude 負責最後驗收與提交；不寫入 `.git`。本文件取代已刪除的 HANDOFF-S6-1～5 過程紀錄，正式玩法細則見 `design/S6_ALLIANCE.md`。

## 已實作功能

1. **盟籍與權限**：創盟、搜尋／加入、申請與審核、退盟與踢人、三階權限、幹部、盟主轉讓、解散、公告。創盟主堡三級、根材／層岩各五千；加入主堡二級；上限四十人（2026-10-12 由三十人調高）、四位幹部，離盟冷卻二十四小時。同盟不能互相攻城。
2. **聊天與幫助**：聯盟聊天、歷史與重連補查、輸入限制；建築升級與治療隊列的幫助請求、同一人一次、重試防重複、失效請求清理。可被幫助次數為 `min(10, 2 + 主堡等級)`；每次縮短原工期 1%（至少一秒、最多六十秒），合計不超過原工期 20%。
3. **援軍城防**：一支援軍駐守盟友，每人一位主將，可混合全部三種兵。雙方主堡三級解鎖，派遣上限 `100 × min(派出方主堡等級, 接收方主堡等級)`，佔用實際部隊、主將及行軍位；召回、傷亡歸屬、返城、攻城凍結快照及斷線／重啟復原。最多七支防守戰鬥小隊；不重複主將加成。
4. **盟旗領地**：三個槽位，各根材／層岩一萬、二萬、三萬，五分鐘建成；同區四向可通行半徑十二格，領地連接、跨盟不得重疊。撤旗不退款、不拆斷連接、槽位冷卻二十四小時；不阻擋定居／行軍。領地採集速度 +10%、城防防禦 +5%，皆快照且不疊加；旗幟預覽、建造／冷卻及費用確認介面。
5. **PvE 集結**：最多五人，每人一位主將、最多三兵種，總派兵量依主堡；可選六十／一百八十／三百秒倒數。至少兩人與發起者到齊才能出發，最慢速度、共同路線，一支可辨識的世界隊伍。準備期個人退出，發起者取消；出發後由發起者整隊召回。凍結結算不受退盟影響。每人實際有效傷害分配單份獎勵池，整數最大餘數分配，各自戰損、修練及攜帶獎勵，返城才入庫。
6. **介面與新手引導**：聯盟頁涵蓋以上操作；加入／幫助／集結三項檢核，不額外發放經濟獎勵。主城護盾罩涵蓋外牆。自己與同盟主城名稱、底圈、全圖點為淺藍，非同盟紅色，自己點較大；同盟卡隱藏攻城並指向援軍。盟籍由伺服器判定，不解析名稱。
7. **管理與維運**：受 Admin token 保護的唯讀聯盟診斷與有理由／稽核紀錄的復原重試端點；未新增正式環境改時間端點。版本與伺服器時間防止舊快照覆蓋新狀態；私有操作 UUID／派兵配置只傳本人。

## 一致性與復原

- 長作業序列與短 metadata 存取鎖分開；短鎖內不呼叫玩家修改。玩家修改仍取得各自 `PlayerSession.Gate`，不同時持有兩位玩家 Gate。
- 創盟、旗幟、援軍、集結派遣與戰損均用持久化意圖／城市收據，存檔失敗可重試，不重複扣款、扣兵或發獎。
- 普通單打與集結共用持久化營地結算權，保存原始输入／種子／結果與各玩家套用回執；營地重生時間也持久化。全員套用後才清除大型戰報／輸入，保留最小完成紀錄。
- 軍事 ticker 依修訂版及下一個期限跳過閒置讀取，錯誤退避；復原離線攻城者的援軍結算，避免永遠凍結。

## 驗證

- `dotnet run --project tests/AbyssKingdom.Tests`：**268 passed、0 failed、268 total**，輸出 `artifacts/s6-final-tests.log`。包含其他作者同期新增的城內區域測試。
- 新增真實 WebSocket 盟籍辨識測試：同盟／異盟／無盟、同簡稱不能偽裝同盟、退盟更新、公開主城不含帳號 ID。既有 WebSocket 流程驗證創盟、權限、幫助、旗幟、集結與 GM 稽核。
- 故障注入測試涵蓋扣款、派遣、逐玩家戰損存檔前後；另含營地競爭、離盟、召回期限、盾與同盟變化、離線復原、鎖等待期間仍可讀 metadata。
- 最新 Unity 打包 `artifacts/s6-final-build.log` 第 772 行：`Build Finished, Result: Success`；第 787 行：`StandaloneWindows64: Succeeded, 190 MB, 0 errors`。
- 本輪實際場景截圖：`artifacts/s6-alliance-final/world-alliance-colors.png`（本人／盟友淺藍，異盟／無盟紅色）、`world-territory.png`、`world-rally.png`。已逐張開啟檢查。`artifacts/s6-alliance-player.log` 第 30～33 行確認權威盟籍與 RGB `91D7FF`／`DC5A50`，第 72 行 `ART_CHECK COMPLETE models=38 failures=0`。
- 場景 capture 是實際本機 Dev 場景相機、沒有 UI overlay；不可當作手動 UI 操作驗收。隔離工具見 `tools/S6VisualHost/README.md`。

## 五人混兵量測

固定五人各三百兵、初級主將、seed 42；僅作擴充戰鬥核心回歸，不代表全面平衡結論。二級營地單兵種五小隊：兩回合勝、損失二百一十；混兵十五小隊：兩回合勝、損失一百。三級營地單兵種：八回合胜、損失八百八十三；混兵：五回合勝、損失八百一十。仍只有五位主將，沒有將一位主將複製進三個小隊；本輪不改兵種數值。

## 改動檔案範圍

- Shared：`Social/AllianceRules.cs`、`AllianceMilitaryRules.cs`、`AllianceTerritory.cs`、`RallyBattle.cs`；`Map/Garrison.cs`、`MarchLogic.Reinforcement.cs`、`MarchLogic.Rally.cs`、`Marches.cs`、`Camps.cs`、`Gather.cs`、`Pvp.cs`；`Battle/Battle.cs`、`City/CityState.cs`、`CityLogic.cs`、`CityLogic.Healing.cs`、`CityLogic.AllianceHelp.cs`、`Protocol/Messages.cs`。
- Server：`Game/Alliance*.cs`、`SessionManager.cs`、`SessionManager.Alliance*.cs`、`SessionManager.Camps.cs`、`WorldService.cs`、`WorldService.Territory.cs`；`Persistence/Alliance*.cs`、`CampSettlementRecord.cs`；`Net/WebSocketHandler.cs`、`Admin/AdminEndpoints.cs`、`GameServer.cs`、`db/002_alliances.sql`。
- Client：`Game/GameClient.cs`、`UI/AlliancePanel.cs`、`ContinentPage.cs`、`DisplayNames.cs`、`GameUI.cs`、`WorldOverview.cs`；`World/SurfaceView.cs`、`SurfaceView.Territory.cs`、`WorldModelCapture.cs`，`Resources/UI/Game.uss` 與新腳本 meta。
- Tests：`Alliance*Tests.cs`、`RallyBattleTests.cs`、`BattleTests.cs`、`ServerIntegrationTests.cs`；隔離視覺工具 `tools/S6VisualHost/`。
- Docs：本文件、`design/S6_ALLIANCE.md`、`design/GDD.md`、`HANDOFF-CODEX.md`、README。
- Claude 同期加入 `CityFrontier`／`CityZones`／區域點擊資訊等內容，不是本輪 S6 新增；目前 `GameUI` 的 `FrontierClicked`／`OpenFrontierInfo` 和 `DisplayNames.CityZones` 仍保留。未整檔回寫或撤銷 Claude 的改動，仍請 Claude 最後核對。

## 正式上線前限制

- 只驗證本機記憶體與假 HTTP／故障儲存，**尚未驗證正式 Supabase／PostgreSQL**。請在獨立 Dev DB 建立 `002_alliances.sql` 並驗證重啟／存檔，不用使用者帳號；目前不可視為已部署。
- 聯盟目錄是單一 JSON 快照，設計限單伺服器實例；沒有分散式資料庫鎖。歷史收據／完成操作仍會成長，大型戰報已在完成後清除，仍需後續留存／歸檔政策。
- 尚未包含全服 BOSS、集結攻城、摧毀盟旗、公庫、聯盟科技或跨服。未重新啟用作廢的深淵大陸與回響代價。
- 自動 build 不等於 UI 的所有手動操作均驗收；使用者／Claude 可依聯盟頁操作再檢查排版與易用性。

備份在 `C:\Users\xnikz\.claude\backups\2026-10-12\s6-*\`。本輪 capture 客戶端已自行退出，Dev host 已停止；不提交、不推送、不部署。
