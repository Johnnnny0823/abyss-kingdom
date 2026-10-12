# S6-5 PvE 集結與營地持久化（2026-10-12，未提交）

> 本文件是過程快照；收尾後完整狀態請讀 `S6_DELIVERY.md`，完整測試已更新為 268 全過，下方 252 與待補測試敘述已被取代。

使用者要求做完全部剩餘 S6，最後整理內容。盟旗已實作，見 HANDOFF-S6-4；本段集結伺服器、共用規則、協定與介面已接上，正在完成更多復原／協定／視覺驗證，**尚不要宣稱全部 S6 完成**。

- `RallyBattle.cs`：五位不同玩家，各一至三小隊，最多十五小隊，仍只有五位主將。有效傷害加權、單份資源／道具獎勵池、最大餘數與 ordinal ownerId 決勝、BigInteger 整數防溢位；每人結果／戰損／主將修練戰報索引分開。
- `AllianceRallyRecord.cs`／`AllianceRallies.cs`：持久化 Forming／Launching／Traveling／Settling／Returning／Finished；先預留席位與容量，後扣自己實兵占主將與行軍位，城市收據避免重試扣兵。倒數 60／180／300 秒，抵達不晚於 departureMs。至少兩人與發起者到齊才發出共同路徑／最慢速度，各城市行軍引用 rallyId。新派遣按雙方現時主堡容量驗證，既有隊伍不追溯刪兵。
- 退出／取消：準備期個人退出，發起者可取消；出發後只發起者能整隊撤回；凍結結算不接受取消。準備期離盟回軍，出發後離盟仍完成凍結的戰損／獎勵。斷線與重啟逐玩家復原，不同時持有多位 Gate。
- `CampSettlementRecord.cs`／`SessionManager.Camps.cs`：普通單打與集結共用 social gate、持久化營地結算權、輸入／種子／結果／逐人 AppliedOwners、營地重生 deadline。先保存結果與營地擊敗，再依城市收據套用戰損／修練／Carry，回城才入庫；任何存檔斷點可重試。
- `IControlledCamps`：正式 WorldService 的普通打營地也轉成伺服器協調結算，SimpleFieldRegistry 共用單機規則不變。CampPending 行軍不能召回；SessionManager 事件分派與 stuck recovery 已包含營地，軍事 ticker 先復原營地，再推進集結。
- 協定 `alliance.rally.create`、`.join`、`.recall` 與 `alliance.state.rallies`；普通 map.recall 集結轉交集結服務。操作 UUID 跨不同集結重用會被拒絕；自己的未完成派遣有原 UUID 重試。私有操作 ID／部隊原始配置只送本人。
- `AlliancePanel` 已有發起、加入、主將／混兵、倒數、狀態、隊員、人數／容量、有效傷害、分配與整隊召回。前端只送意圖。`SurfaceView` 正在完成每位參與者都只畫一支共同隊伍、隊長造型與人數徽記；返程各自顯示本人。
- 已過 `RallyBattleTests`（十五小隊、單份池、不複製主將、消失營地、最大餘數與大整數）；`AllianceRallyTests` 原五測試已過（多人搶席位、組隊出發返城、取消、UUID 反覆／跨集結防重用、出發存檔與第二位玩家戰損存檔斷點）。另三項離盟／遲到／營地競爭測試正在跑，結果須查。
- 最新完整測試 `artifacts/s6-final-tests.log` 為 252 passed、0 failed（後續變更需再跑）。最新 Unity `artifacts/s6-final-build.log` 尚待確認最後一次打包。旗版打包 `s6-flags-build.log` 第 804 行成功。
- 備份 `C:\Users\xnikz\.claude\backups\2026-10-12\s6-rally\`；不 commit／push／部署。Dev 伺服器目前未開。

剩餘驗證與收尾：先修必要錯誤，完整測試全過／Unity 成功；新增真 WS 集結流程、單打與集結搶營地／重啟、一人未到齊及取消邊界、五人混兵與五人單兵種平衡量測。補本機 Dev 旗／領地與集結介面截圖（可用隔離 ManualClock host，別加正式改時鐘 GM 端點）。更新 GDD、S6 全功能摘要、檔案與證據交接。持久化營地與收據歷史目前沒有清理，單 JSON 目錄大小應列限制，不能宣稱已驗證正式 Supabase。
