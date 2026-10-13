# Codex 派工：AI 試玩修正第一批

基準 commit：`1906c59`。問題來源：`docs/AI_PLAYTEST_REPORT.md`。
使用者 2026-10-13 決定：**第一戰要穩贏（A）＋醫館提早到打營地之前（C）**；其他項目照報告建議修。

## 1. 第一戰穩贏（報告 🟡2，使用者選 A）

- 目標：照任務走的新手（任務 #3 練出的 **50 名磐盾衛**＋開局主將，主將等級照任務路線當時的實際狀態）打**最近的 Lv.1 營地**，要**穩定獲勝**，而且不要損失慘重。
- 驗收標準：用伺服器實際產生種子的方式跑至少 200 個種子，**勝率 100%**，平均陣亡 ≤ 10 人（傷兵另計）。若 100% 做不到就回報最低可達值，不要硬改戰鬥核心。
- 做法優先順序：先調 **Lv.1 營地本身的數值**（兵力或屬性），不動兵種、主將、戰鬥公式；Lv.2 以上營地不變。改完確認既有難度曲線測試（遠征、營地、斷索試煉）仍通過，數值變更寫進 GDD 對應段落。
- 加測試：上述 200 種子的勝率／陣亡測試。

## 2. 醫館移到打營地之前（使用者選 C）

- 第一章任務 #8（擊敗營地）和 #9（建醫館）**對調**：先建醫館，再打營地（`shared/.../City/Quests.cs`，GDD 4.12 與 4.11b 之後的任務列表同步）。
- 檢查任務獎勵在新順序下仍合理（醫館任務的獎勵夠不夠打營地前用），只回報，不要大改獎勵。
- 存檔相容：任務進度以索引儲存。目前沒有真實玩家，接受對調造成的小誤差，但在交付說明寫清楚影響（索引 7／8 的舊存檔會看到什麼）。
- 打營地**輸了**之後的導航：任務「前往」若偵測到兵營可用兵力不足（例如低於任務 #3 的 50）或有傷兵，改成先帶去醫館治療或兵營補兵，並顯示一句提示；兵力恢復後才再帶去營地。

## 3. 成本圖示可點開說明（報告 🟠1）

- 建築卡、訓練、治療等所有**成本列**的資源圖示，點擊都要開出和頂部資源列相同的說明視窗（名稱、由來、用途、取得方式）。共用同一個處理方法，不要複製一份。
- 順手檢查其他用到 `ResIcon` 的地方是否都能點；專案規則：**每個圖示都要能點開看名稱與說明**。

## 4. 任務「前往」要把鏡頭移過去（報告 🟡3）

- 任務前往城內建築時，沿用既有的建築聚焦方法把鏡頭移到該建築，資訊卡不能被 HUD 擋住。大地圖目標已有聚焦的維持原樣。

## 5. 未建醫館不顯示治療入口（報告 🟡4）

- 醫館未建造（0 級）時，卡片不顯示「治療」，改顯示建造；傷兵 HUD 的提示改成「先建造醫館」並可點過去選取醫館空地。

## 6. 小瑕疵（報告 ⚪5、⚪6）

- `map_rejected` 等錯誤碼補齊中文對照（`DisplayNames.cs`），掃一遍所有伺服器可能回的錯誤碼，避免再出現英文代碼。
- 城內／大地圖的建築名牌加暗色描邊或底，淺色地面與霧上看得清楚。

## 驗收

- `dotnet run --project tests/AbyssKingdom.Tests` 全部 PASS；Unity 打包 0 errors。
- 重跑 `tools/AiPlaytest` 的「照任務走」角色：**第一章要能全部完成**，回報完成時間；四個角色的摘要和上一輪對照。
- 截圖：成本圖示說明、任務前往聚焦、未建醫館卡、補兵導航提示、名牌描邊。
- 只用本機 Dev／InMemory 與 `dev-` 測試帳號。不 commit、不 push、不部署；交付說明寫在本文件末尾，交給 Claude 驗收。


## 交付紀錄（2026-10-13，Codex，待 Claude 驗收與提交）

本輪六項修正均已實作並完成本機驗收。沒有 commit、push、部署或寫入 `.git`；只用 Dev／InMemory 與獨立 `dev-aiplay-*` 帳號。修改前備份：`C:/Users/xnikz/.claude/backups/2026-10-13/playtest-fixes-1/`。

### 實作內容

- **第一戰**：只改 Lv.1 營地，每隊 20 兵；領軍攻擊 49、防禦 35、生命 610（原第 2 關領軍數值各減半）。保留種類、輪替、獎勵、重生時間；Lv.2／3、遠征、斷索試煉、兵種、主將及戰鬥公式不變。
- **任務順序**：#8 建醫館，#9 擊敗營地，獎勵跟隨原任務保留。GDD 4.11b／4.12 與 S4 數值說明已同步。任務文案依 Goal 組成，沒有舊 ID 對應錯置。
- **恢復導航**：營地「前往」依序檢查在外行軍、傷兵／治療中、城內可用總兵力不足 50。先等返城、先建／治療醫館、再去兵營補兵；恢復後仍選最近同區 Lv.1 營地。這是導航提示，不新增伺服器最低 50 兵限制。
- **成本圖示**：所有 ResIcon 共用 UiKit 回呼，呼叫原 OpenResourceInfo；建築、訓練、治療成本與頂部 HUD 使用同一視窗。阻止 HUD 圖示事件重複冒泡；資源視窗重開時取代上一個，避免堆疊。其餘 ResIcon 使用處（說明視窗標頭）亦可點。
- **聚焦與醫館空地**：城內建築任務沿用 FocusBuilding；0 級醫館卡沒有治療入口，保留建造。傷兵 HUD 顯示「先建造醫館」，按下後聚焦醫館空地。
- **小瑕疵**：補齊所有 shared Result／Code enum 與 AllianceResult 的中文錯誤說明，並涵蓋協定 error code 的中文兜底，未知細節不向玩家顯示原始英文。城內全部建築與大地圖主城名稱使用暗色底板，保留淺藍／紅的同盟辨識。
- **驗收工具**：AiPlaytest 新增 `--fixes-1`，輸出到獨立資料夾，完成時間保留半分鐘；Unity 診斷新增真實玩家意圖的修正回歸，不使用 GM 加兵、資源或修改城市。

### 獎勵與舊存檔

- 醫館建造成本：根材／層岩／菌糧各 200；完成 #8 仍各給 1,000。本輪嚴格角色在 #8 領獎後有根材 13,495、菌糧 12,275，足夠出征與恢復。
- 即使 50 磐盾衛全倒下、35 傷兵／15 陣亡，治療 35＋補回 15 的成本也只有根材／層岩各 325、菌糧 650，低於醫館任務的三種各 1,000。沒有調整任何任務獎勵。
- 舊索引 **7** 現在看到建醫館（原來看擊敗營地）；索引 **8** 現在看到擊敗營地（原來看建醫館）。已達條件可領獎；曾跨過其中一項的存檔可能跳過或重領對調目標的獎勵。索引 0～6、9 以上不變，按本次核准不做遷移。

### 測試與實測

- `dotnet run --project tests/AbyssKingdom.Tests`：**283 passed，0 failed**（原 279，新增 4 個）。證據：`artifacts/playtest-fixes-1-tests.log`。
- 200 個種子使用與 WorldService.NextSeed 相同的 `RandomNumberGenerator.GetBytes(8)`＋BitConverter 方式；開局 Lv.1 霍德、50 磐盾衛、不穿裝備，涵蓋三種營地輪替。最後一輪 **200／200 勝**；總陣亡 967、平均 **4.835**、單場最多 7；總傷兵 1,963、平均 9.815。這是本次抽樣驗證，不代表窮舉所有 64 位種子。
- 高階營地數值不變的回歸測試全過；遠征與斷索試煉測試全過。兩個舊「少兵必敗」測試改用未調整的 Lv.2，繼續完整驗證敗戰、傷兵、治療推播，沒有移除斷言。
- Unity batchmode Windows 打包：**Succeeded，190 MB，0 errors**。證據：`artifacts/playtest-fixes-1-build.log`。
- 真實 Unity 客戶端回歸：**38 項全部通過、50 張 PNG**，紀錄在 `artifacts/playtest-fixes-1-ui-final/fixes-checks.jsonl`，完整退出紀錄在 `artifacts/playtest-fixes-1-ui-final.log`。
- 客戶端由實際任務「前往」按鈕觸發聚焦（先把鏡頭移遠）；成本測試逐一發送 ClickEvent，驗證資源名稱正確、只開一個視窗。傷兵 HUD 也使用實際 Button 的 Clickable 回呼。
- 敗戰回歸合法建兵營、訓練 10 人，再派 5 人打 Lv.2：確實敗戰，3 傷兵／2 陣亡；等返城、建醫館、治療、補兵至 ≥50，驗證恢復後可再去營地。針對營地導航分支呼叫同一 GoToQuest 處理函式；沒有偽造此測試帳號已推進到第九任務。
- 第一趟 `playtest-fixes-1-ui/` 的 5 人小隊打贏了弱化後 Lv.1、但產生傷兵，**不作為敗戰證據**；正式驗收以 `playtest-fixes-1-ui-final/` 為準。
- Socket 四角色的 GM errors 為空陣列；最終 UI 主機 errors 只有兩個測試角色各一次刻意注入的 `AI_PLAYTEST_SAMPLE: harmless local error`，供診斷管線驗證，不是非預期錯誤。UnknownMarch 的錯誤吐司也是刻意送不存在的行軍來驗證中文對照。

### 四角色與上一輪比較

| 角色 | 上輪第一章 | 本輪第一章 | 本輪結束狀態 |
| --- | --- | --- | --- |
| 照任務走（60 分） | 60 分仍卡舊 #8，任務索引 7 | **17 分**，索引 15 | 主堡 4；三座生產＋晶礦 3；倉庫／兵營 3、醫館 2；可用兵 42、傷兵 5 |
| 勤奮（60 分） | 21.5 分 | **21.5 分** | 八棟均 5 級；可用兵 347、傷兵 0 |
| 隨意操作（60 分） | 21.5 分 | **19.5 分** | 主堡與四座生產、醫館 5；倉庫／兵營 4；可用兵 300、傷兵 0 |
| 離線 15 分→8 小時→15 分 | 21.5 活躍分 | **21.5 活躍分** | 八棟均 4 級；可用兵 347、傷兵 0 |

上輪勤奮／離線採用修正隊列策略後的 `ai-playtest-queues/`，另外兩個採 `ai-playtest/`；旧摘要把時間截成整數，對照時間由首次索引 15 的 timeline 恢復。離線時間不列入活躍時間；出生位置與戰鬥種子隨機，分鐘差異不當作固定效能基準。
本輪資料：`artifacts/ai-playtest-fixes-1/`（四組 summary／timeline／events、GM 摘要、`comparison.json`）。421 個城市快照檢查全過：無負資源／部隊／傷兵、建築等級 0～10、任務索引 0～15；`state-check.txt`。嚴格角色第一次營地帶 50 兵，返城剩 42、傷 5、陣亡 3，沒有再給兵或先培養主將。

### 截圖索引（相對 artifacts/playtest-fixes-1-ui-final/）

- 成本說明：`cost-building-0.png`、`cost-training-0.png`、`cost-healing-0.png`；其餘資源的 1／2 同前綴。
- 任務實際前往：`quest-focus.png`；從遠處回到主堡，建築及資訊卡未被 HUD 蓋住。
- 0 級醫館：`hospital-zero-card.png`；傷兵後的空地與 HUD：`recover-build-hospital.png`。
- 治療與補兵：`recover-heal.png`、`recover-train.png`；恢復後營地：`recover-ready-camp.png`。
- 名牌暗底：`city-nameplates.png`、`world-nameplates.png`。
- 中文錯誤：`error-toast.png`。

### 改動檔案

- shared：`City/Quests.cs`、`Map/Camps.cs`。
- client：`UI/GameUI.cs`、`UI/UiKit.cs`、`UI/DisplayNames.cs`、`World/WorldScene.cs`、`World/CityView.cs`、`World/SurfaceView.cs`、`Diagnostics/AiPlaytestPolicy.cs`、`Diagnostics/AiPlaytestCheck.cs`（均在 client/Assets/Scripts/）。
- tests：`PlaytestFixesTests.cs`（新增）、`CampTests.cs`、`ServerIntegrationTests.cs`、`AbyssKingdom.Tests.csproj`（連結純 C# DisplayNames 做錯誤碼測試）。
- 工具：`tools/AiPlaytest/Program.cs`。
- 文件：`docs/design/GDD.md`、本文件。

### 重跑

1. 設定只用於本機、至少 24 字元的隨機 `Admin__Token` 環境變數；不要放到檔案。
2. `dotnet run --project tests/AbyssKingdom.Tests`。
3. `dotnet run --project tools/AiPlaytest -- --fixes-1`，約八分鐘實際時間完成四角色；新檔案會覆蓋同輪資料，先留存需比較的證據。
4. Unity batchmode 按既有 BuildScript.BuildWindows 打包。
5. `dotnet run --project tools/AiPlaytest -- --host` 啟動 loopback 5090 的 Dev／InMemory、10 倍時間主機。
6. 啟動 Windows 客戶端，帶 `-devlogin -devname aiplay-fixes1-new -server ws://127.0.0.1:5090/ws -screen-width 1280 -screen-height 800 -windowed -aiplaytest <新的絕對輸出路徑> -aiplaytest-smoke -aiplaytest-fixes-1 -logFile <絕對紀錄路徑>`。只接受 loopback 與 devlogin；不要改用正式帳號。
7. 客戶端自行退出後停止本機主機；交由 Claude 驗收、提交。
