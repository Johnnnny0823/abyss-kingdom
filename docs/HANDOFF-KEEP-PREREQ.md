# Codex 派工：主堡升級前置條件

基準 commit：`7bf959e`。設定以 `docs/design/GDD.md` 4.1「主堡升級前置」為準（2026-10-12 使用者同意）。

## 目標

主堡每升一級，指定的建築必須先達到要求等級，否則不能開始升級主堡。玩家要看得到「還缺哪棟、幾級」，並且能一鍵跳到那棟建築。

## 對照表（主堡目標等級 → 需要）

| 主堡升到 | 需要 |
|---|---|
| 2 | 無 |
| 3 | 兵營 1、倉庫 1 |
| 4 | 掘根場 3、菌圃 3 |
| 5 | 倉庫 4、兵營 4 |
| 6 | 層岩坑 5、醫館 5 |
| 7 | 響晶礦脈 6、兵營 6 |
| 8 | 倉庫 7、菌圃 7 |
| 9 | 醫館 8、掘根場 8 |
| 10 | 兵營 9、倉庫 9 |

大使館、民居、研究所尚未實作，**這次不要加**；資料結構要能日後直接在表裡加一格。

## 要做的事

1. **共用規則**（`shared/AbyssKingdom.Shared/City/`，純 C#、netstandard2.1、C# 9、只用整數）
   - 對照表放 `CityConfig`（例如 `KeepPrerequisites(int targetLevel)` 回傳 `(BuildingType, int)` 清單）。
   - `CityLogic.CanUpgrade`：建築是主堡時檢查前置，不足回傳新結果碼。新碼**附加在 `UpgradeResult` 最後**（`KeepPrerequisiteMissing = 6`），不可改既有數值或名稱（會壞存檔與協定）。
   - 只在開始升級時檢查；已經比較高等級的舊存檔不需要遷移。
2. **測試**（`tests/AbyssKingdom.Tests`，自帶 `[Test]` 框架，不用 NuGet）
   - 每一級：前置剛好達到 → Ok；差一級 → `KeepPrerequisiteMissing`，且不扣資源、不排隊列。
   - Lv.2 沒有前置；表中每一列的建築等級都 < 主堡目標等級（不會和「其他建築不可超過主堡」互相卡死）。
   - 從新帳號照表升到主堡 10 的可行性測試（每一步都能合法完成）。
   - 伺服器整合測試：客戶端送 `city.upgrade Keep` 而前置不足時，回傳對應錯誤，狀態不變。
3. **客戶端**
   - `DisplayNames.cs` 兩處錯誤訊息對照（字串版與 enum 版）都加上新碼。
   - 主堡資訊卡／升級按鈕：列出每項前置與目前等級，未達成的標紅；點該項會選取那棟建築並把鏡頭移過去。專案規則：**每個圖示都要能點開看名稱與說明**。
4. **新手節奏**
   - 檢查第一章任務（`shared/.../City/Quests.cs`）順序在新規則下仍可完成，尤其「主堡 3」之前已蓋兵營和倉庫、「主堡 4」之前四座生產建築 3 級。
   - 把前置加進 `tools/NewbieSim` 後重跑，回報前 30 分鐘的操作次數和各建築等級，跟 GDD 4.1 記錄的節奏（前 5 分鐘約 40 次操作、30 分鐘時各建築約 3 級）比較。明顯變慢就回報，不要自行改數值。
5. **文件**：GDD 4.1 把「尚未實作」改成 ✅；README 協定表若新增錯誤碼一併更新。

## 驗收

- `dotnet run --project tests/AbyssKingdom.Tests` 全部 PASS。
- Unity 打包 0 errors。
- 本機 Dev 伺服器（`Auth__Mode=Dev`、`Storage__Mode=InMemory`，5090 埠）截一張主堡資訊卡顯示前置未達成的畫面。**不要用使用者的訪客帳號。**
- 交付說明寫在本文件末尾：改動檔案、測試數、NewbieSim 前後比較、截圖路徑。不要 commit、push 或部署，交給 Claude 驗收。

## 2026-10-12 實作交付

- 前置表位於 `CityConfig.KeepPrerequisites(targetLevel)`，回傳唯讀清單；目前只含既有八種建築，日後可在表中增加要求。`CanUpgrade` 在資源檢查前檢查主堡前置，新結果碼附加為 `KeepPrerequisiteMissing = 6`，既有值未變；完成舊隊列不追溯檢查。
- 主堡資訊卡列出每棟要求與目前等級，未達成標紅；文字按鈕點擊會選取並移動鏡頭到該棟建築（含未建造空地），選取卡顯示名称與說明。不是另加不可點擊的裝飾圖示。兩種錯誤對照皆已更新。
- 第一章順序檢核通過，未改任務與數值：兵營任務 #2、倉庫 #7 先於主堡三級 #12；四生產三級 #13 先於主堡四級 #15。
- 完整測試 **272 passed、0 failed、272 total**，`artifacts/keep-prereq-tests.log`。新增三個規則／成長／任務測試與一個真 WebSocket 前置不足測試。既有三個取消／加速測試原設定主堡五級卻沒層岩坑、醫館五級；補足合法前置後通過，未更改其取消／加速斷言。
- NewbieSim 實作前後同一貪婪策略的六段操作數均為 **40／4／12／7／9／3**，三十分鐘共 **75 次**；各段免費完成 **17／0／5／2／1／0**。三十分鐘八棟建築皆三級，24:54 開始主堡四級工程。資源、收入與隊列空等一致，沒有明顯變慢；未調整數值。模擬未含任務獎勵，屬固定策略比較。
- 比較報告：`artifacts/keep-prereq-sim-before.md`、`keep-prereq-sim-after.md`；工具新增前置不足觀測七十一秒，該數字不代表空等，這些時間仍可升其他建築。
- UI／打包驗證：`artifacts/keep-prereq-build.log` 第 772 行 Success、第 787 行 Succeeded／0 errors；`keep-prereq-player.log` 第 30 行 `KEEP_PREREQ_CHECK PASS rows=2 blocked=True jump=True noOverlap=True`。`keep-prereq-ui/validation.json` 為 PASS；`keep-prereq-ui/keep-prerequisites.png` 已開啟確認兵營達成、倉庫未達成標紅、按鈕阻擋且清單與頁尾不重疊。截圖由實際場景相機與 UI Toolkit targetTexture 合成，克服隱藏視窗黑色 swap-chain；不是繪製的 UI 樣稿。跳轉測試呼叫實際按鈕所綁定的處理方法，不冒稱 Windows 手動點擊測試。
- 改動：`shared/.../City/CityConfig.cs`、`CityLogic.cs`、`Types.cs`；`client/Assets/Scripts/UI/GameUI.cs`、`DisplayNames.cs`；新增 `Diagnostics/KeepPrerequisiteCheck.cs` 與 `.meta`；`tests/.../KeepPrerequisiteTests.cs`、`QueueTests.cs`、`ServerIntegrationTests.cs`；`tools/NewbieSim/Program.cs`；`docs/design/GDD.md`、README、本交接。
- 超過三十行的既有檔改動前備份在 `C:\Users\xnikz\.claude\backups\2026-10-12\keep-prereq\`。只使用專用 `dev-KeepPrereqPreview` 與本機 loopback Dev／InMemory；客戶端已退出、本輪 Dev server 已停止；不 commit、不 push、不部署。
