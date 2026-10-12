# S6-3 援軍基礎進度（2026-10-12）

**已實作援軍派遣、駐守、召回、遣返、城防與戰損歸屬，完整測試 230 passed、0 failed。Unity 打包與護盾場景驗收通過；未 commit／push。** 詳細設計見 `docs/design/S6_ALLIANCE.md` 第 6 節，聊天與幫助的進度見 `docs/HANDOFF-S6-2.md`。S6 全部尚未完成：盟旗、PvE 集結仍待做；使用者要求繼續，最後再統整所有內容。

- `shared/AbyssKingdom.Shared/Social/AllianceMilitaryRules.cs`：主堡 Lv.3 解鎖；個人援軍／集結派兵容量 `100 × 自己主堡等級`，收援方也以其主堡容量限制三兵種總和；集結總容量 `500 × 發起者主堡等級`。驗證只接權威 `CityState`，並檢查非負兵數、實兵存量、超額與溢位，不套普通行軍的主將統率上限。
- `shared/AbyssKingdom.Shared/Battle/Battle.cs`：`BattleScenario.Standard` 仍限每側五小隊；只有伺服器選 `GarrisonDefense` 時守方至多七小隊，`PveRally` 時攻方至多十五小隊。戰鬥公式與種子流程未變，援軍 PvP 現已啟用新模式，PvE 集結仍未啟用。
- `AllianceMilitaryRulesTests.cs`、`BattleTests.cs` 驗證混兵容量、主堡升級、偽造／負數／超大兵數、各模式只放寬指定方，以及原五小隊限制。基礎階段測試：**210 passed, 0 failed, 210 total**。
- Unity batchmode `artifacts/s6-visual-build.log` 顯示 `Build Finished, Result: Success.`；這份舊日誌只記錄基礎階段；最新驗證見下方接續實作。

## 2026-10-12 接續實作

- `MarchLogic.Reinforcement.cs`／`Marches.cs`：一主將混兵、扣實兵占行軍位、Dijkstra、保留盾、抵達不攻城；援軍可單独推進，離線復原不吞其他行軍事件；返城只歸還一次。
- `AllianceReinforcementRecord.cs`／`AllianceReinforcements.cs`：先存唯一席位預留，後存城市扣兵，最後存派遣狀態。UUID 與城市收據防止重試重複扣兵。接收開關、双方返還、離盟／盟籍 epoch 改變返還。
- `AllianceMilitaryTicker.cs`：每五秒復原未完成派遣／返城，包含離線玩家。`AllianceSynchronization` 共用社交與聯盟城防鎖，不會同時拿兩位玩家 Gate。
- `Garrison.cs`／`Pvp.cs`：本城三兵種＋援軍三兵種＋最後城防，最多七守方小隊；同兵種依擁有者分別記戰損，援軍傷兵回原主人醫館。原普通戰鬥限制維持。
- `SessionManager.AllianceDefense.cs`：先存戰鬥預留，再凍結援軍與雙方陣容／種子，再存守方結果，最後存援軍戰損。各段可重試，舊結果不能清掉新凍結；結算中召回先存意圖，完成後返還。
- 修正既有攻城收尾：攻方立即存檔失敗時，不再刪守方 SiegeLog。
- 協定：`alliance.reinforcement.send`、`.return`、`.accept`、`alliance.military.changed`；`alliance.state.reinforcements` 只給援軍雙方。普通 `map.recall` 援軍會轉交持久化返還流程。README 已補協定表，也補上前批聊天與幫助訊息。
- `AlliancePanel.cs`／`GameClient.cs`：成員派援、主將／三兵種選擇、容量合計、召回／遣返／接收開關、未完成派遣重試；離盟後仍顯示返城援軍。
- `SurfaceView.cs`：護盾薄圓改為半透明青綠罩。舊背景截圖 `artifacts/s6-shield-capture/world-city.png` 是黑圖，**不能當視覺證據**。`WorldModelCapture.cs` 新增 opt-in `-artcapture-offscreen`，由正式世界相機 render texture 擷取場景（不含 UI），待用新打包驗證。
- 新測試：`ReinforcementMarchTests.cs`、`AllianceReinforcementTests.cs`、`GarrisonBattleTests.cs`、真正 WebSocket 派遣至返城的 `ServerIntegrationTests.AllianceReinforcementProtocol_DispatchGarrisonAndNormalRecall`。含兩個派遣存檔斷點、守方／援軍主人戰損存檔斷點、重啟、固定種子、不重複傷兵、競爭席位、離盟、權限與護盾。
- 完整輸出：`artifacts/s6-reinforcement-tests.log`，**230 passed, 0 failed, 230 total**。最新 Unity 日誌 `artifacts/s6-reinforcement-build.log` 第 772 行 `Build Finished, Result: Success.`；客戶端日誌 `s6-reinforcement-player.log` 有 `ART_CHECK COMPLETE models=38 failures=0`。
- 護盾有效場景截圖：`artifacts/s6-reinforcement-scene/world-city.png`（2026-10-12 最後重拍），已目視確認青綠罩包住主城。使用正式 Dev 權威主城 snapshot、正式世界相機與資產，52 度；不含 UI。空場景原因是開場鏡頭仍控制 chunk culling，诊斷擷取已固定鏡頭、強制本次 Cull、套用正常主城 snapshot；正常遊玩剔除節流不變。
- 備份：`C:\Users\xnikz\.claude\backups\2026-10-12\s6-reinforcement\`。不 commit／push／部署，不寫 .git。

下一步先完成 Unity 與本機 Dev 視覺驗收，再讀提案第 7 節實作 S6-4 盟旗，然後做 S6-5 PvE 集結（五位玩家各一主將混兵、最多十五攻方小隊）。最後整理 S6 完整摘要。

待注意：介面收援容量目前使用 WorldService 主堡等級快取，已快取主堡工程的完成時間與目標等級，離線完工後能顯示新上限；實際派援仍由雙方權威 snapshot 驗證。援軍歷史與永久操作／戰損收據尚無清理策略，需評估 Supabase snapshot 大小。此輪 Dev 伺服器目前已關閉；下一輪開啟後記得關閉自己的程序。
