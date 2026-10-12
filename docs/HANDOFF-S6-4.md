# S6-4 盟旗與領地（2026-10-12，未提交）

已接上盟旗服務、共用規則、協定、聯盟頁與大地圖旗幟／領地色塊。最新完整測試 `artifacts/s6-flags-tests.log`：241 passed、0 failed。Unity 打包日誌 `artifacts/s6-flags-build.log` 待確認。S6 全部尚未完成，下一步是 PvE 集結；使用者明確要求做完所有剩餘內容，最後再統整。

- `AllianceTerritory.cs`：同區四向步行半徑 12，避開山脈／關隘；旗點保留所有主城候選佔地與固定物件；連接、精確完工、三槽固定成本與冷卻常數。
- `AllianceFlags.cs`：社交共用鎖序列化跨盟衝突；先預留覆蓋、再扣原付款人資源、最後確認 Paid；UUID／城市收據重試不二次付款；未完付款由軍事 ticker 復原。撤旗不退款，槽位 24 小時冷卻，不能拆斷連接或拆掉施工依赖。
- `WorldService.Territory.cs`：快取盟籍與 Paid 領地覆蓋及完工時間；加入／退出／踢人／解散和放撤旗更新快取，重啟載入。`GatherBonusBasisPoints` 只在开始採集時快照 +10%，倍率與實際每分鐘速率保存在 March；不改田存量／載重。
- `Battle.cs`／`Pvp.cs`／`SessionManager.AllianceDefense.cs`：城中心在有效領地時，本城兵、援軍與城防均 +5% 防禦（整數有理式，含兵種與主將）；默认分支保持原傷害算式。援軍戰鬥凍結記下領地倍率，重試不因撤旗變動，不改凍結原始輸入。
- 協定：`alliance.flag.place`、`.remove`、`world.territory`；私有 Flags 在 `alliance.state`，公開旗圖只放位置、簡稱與工期。README 已更新。
- `AlliancePanel.cs`：三槽、施工／冷卻倒數、付款待完成重試、座標選擇與二次費用／付款人確認。`SurfaceView.Territory.cs`：簡單旗桿與旗面標記，生效時合併網格領地色塊，建造中不顯示加成覆蓋。不需 Blender 新建模。
- `AllianceTerritoryTests`、`AllianceFlagServiceTests`、WebSocket `AllianceFlagProtocol_PlaceRetryPublicMapAndRemoval`：覆蓋／連接／保留區、槽費、權限、冷卻邊界、跨盟競爭、付款後目錄存檔斷點、拆橋與施工依赖、採集快照／不疊加、城防倍率、協定往返。
- 改既有檔前的備份：`C:\Users\xnikz\.claude\backups\2026-10-12\s6-flags\`。没有 commit／push／部署。

待補驗證：旗付款前城市存檔失敗、援軍凍結後撤旗的服務層測試、本機 Dev 旗／領地截圖（可以用隔離的 ManualClock 視覺測試 host 加速五分鐘工期；不應新增正式 GM 改時鐘端點）。先完成這些驗證與 Unity 打包，再完成 S6-5。

S6-5 必要架構：持久化集結與席位／容量預留；每玩家仍是原主人 March，準備期到發起城、發起城起共用路徑；五人自由混兵最多十五戰鬥小隊；單份營地獎勵依有效傷害與穩定最大餘數分配。普通打營地與集結必須共用持久化營地結算權，目前 `WorldService._campBack` 還是記憶體，不能直接照抄當作可復原集結。戰損、修練、Carry 與返城各用收據，玩家鎖逐一取，不同時拿多位玩家 Gate。
