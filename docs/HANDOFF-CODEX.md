# 交接給 Codex（2026-10-11，最新 commit `c224b4b`）

使用者這週的 Claude 額度快用完，**美術優化與後續功能（聯盟等）交給你**。只寫對話裡才有、其他文件沒有的東西。開工前依序讀：`CLAUDE.md` → `docs/HANDOFF.md` → `docs/design/GDD.md`（**先讀 2.1.0、5.4、5.8、6 開發路線**，其他章節用到再查）→ 做美術時再讀 `docs/art/MODEL_SPEC.md`、`CHARACTER_SPEC.md`、`ICON_SPEC.md`。專案有 `.codegraph/` 索引，找程式碼先用 codegraph，不要整包讀檔（上次唯讀審查就用掉 91% 額度）。

## 1. 目前狀態

- **測試 184 個全過**（`dotnet run --project tests/AbyssKingdom.Tests`）。地表 S1～S5（地圖、行軍、採集、怪物營地、傷兵醫館、PvP 攻城、新手護盾）已部署。
- **10-09 之後新增**：
  - 新手任務線 15 個任務（`shared/.../City/Quests.cs`、`UI/QuestPanel.cs`，協定 `quest.claim`）。
  - 新手開場「墜入深淵」運鏡（`UI/IntroSequence.cs`，啟動參數 `-intro` 強制播放）。配音在 `Resources/Audio/Intro/line1~4.mp3`，是 Azure「雲哲」降聲調 -15%；目前經 edge-tts 產生，上架前要用正式 Azure 金鑰跑 `tools/tts/gen_intro_voice.ps1` 重產。
  - 背包改成圖示格子（`UI/ItemSlot.cs`、`ArtLibrary.ItemIcon`），16 張圖示是 `tools/art/make_item_icons.py` 程式畫的暫代版。測試參數 `-bag` 直接開背包。
  - 你上次做的 3D 建築、角色、世界物件、山脈關隘都已接進遊戲（見 git log）。
- **賽季轉向（2026-10-11 使用者定案，GDD 2.1.0）**：每季都在地表、一季一個神話主題；深淵不再是可進入的大世界，改成「深淵潮」活動（系列野怪 → 全服累積進度 → 巨型 BOSS）；深淵入口之後做小副本，先不做；賽季重置保留主城與主將。**GDD 5.3 深淵大陸、回響代價都已作廢，不要再照那些做。**

## 2. 交給你的工作（照順序；每項開工前先問使用者範圍，用 `grill-me`）

1. **美術優化**（你擅長的 Blender 流程）：
   - 城內 3D 建築加煙囪、旗幟等細節；大地圖主城加外城牆（之前提過、使用者還沒指定）。
   - 道具圖示：如果你能產生點陣圖，照 `docs/art/ICON_SPEC.md` 做正式版，同檔名覆蓋；不能就跳過，使用者會自己用 ChatGPT 生。
   - 已知小問題：開場播放時「歡迎回來」通知會疊在畫面上；有一次觀察到開場結束停在主城約 2 秒後畫面切到大地圖，原因未查。
2. **S6 聯盟與盟旗領地**：**先寫設計提案 `docs/design/S6_ALLIANCE.md` 給使用者確認，確認後才寫程式。** 提案建議涵蓋：
   - 建立／加入／退出／踢人、階級權限、成員列表、聯盟簡稱顯示在大地圖主城名牌。
   - 聯盟聊天、聯盟幫助（成員互按幫忙縮短隊列）。
   - 盟旗（GDD 5.8：領地由建築撐出來、數量有限、領地內採集與防守加成）。
   - 集結（GDD 5.4：倒數內最多 5 隊、共用 5v5 戰鬥核心），這是之後全服 BOSS 的基礎。
   - 資料存放：玩家資料目前是 `Persistence/PlayerStores.cs` 的 `IPlayerStore`（Supabase／InMemory），聯盟需要自己的儲存與鎖（同一玩家的修改仍要持有 `PlayerSession.Gate`，不要同時拿兩位玩家的鎖，參考 PvP 兩段結算）。
   - 拆成小步驟，每步都有測試、可單獨驗收。
3. 之後依序：**科技研究所**（科技樹，`ItemCategory.Research` 加速道具已預留）→ **主將招募** → **深淵潮活動＋全服 BOSS** → **賽季框架**。每項同樣先提案再做。

**權限**：這次可以修改所有程式（含 `client/Assets/Scripts`），但規則照 `CLAUDE.md`：共用規則放 `shared/`、整數與確定性、規則改動必加測試、新訊息同步 `Messages.cs`／`WebSocketHandler.cs`／README 協定表、玩家看到的名稱只寫在 `DisplayNames.cs` 並更新 GDD 名詞表、不改 enum 名稱。
## 3. 工作方式與踩過的坑

- 回覆用繁體中文，程式碼和 commit 用英文。宣稱完成前要有證據（截圖或測試輸出）。改既有檔超過 30 行，先備份到 `C:\Users\xnikz\.claude\backups\日期\`。
- **規則改動一律加測試**：`dotnet run --project tests/AbyssKingdom.Tests`。跑測試前要先關掉本機 Dev 伺服器，並殺掉殘留的 `AbyssKingdom.Server` 行程，否則 dll 被鎖住、編譯失敗。
- **本機 Dev 伺服器**：設定 `Auth__Mode=Dev`、`Storage__Mode=InMemory`、`Admin__Token=<自訂>`，然後 `dotnet run --project server/AbyssKingdom.Server --urls http://127.0.0.1:5090`。
  - GM 工具 `POST /admin/grant/dev-<名字>`，要帶標頭 `X-Admin-Token`，可以給資源、道具、部隊、**建築等級**（`{"buildings":{"Keep":9}}`）。
  - **不要用使用者自己的帳號測試。**
- **打包**（Unity 必須先關）：`Unity.exe -batchmode -quit -nographics -projectPath client -executeMethod BuildScript.BuildWindows -logFile <log>`，然後在 log 裡找 `error CS` 和 `Build Finished`。
- **截圖**：啟動遊戲時加 `-server ws://127.0.0.1:5090/ws -devlogin`，至少等 20～25 秒再截。
  - 測試參數：`-continent`、`-heroes`、`-hospital`、`-building <Type>`、`-tile x y`、`-marchtest x y`、`-gathertest`、`-camptest`、`-movetest`。
  - 遊戲內美術檢查：`-artcapture <資料夾>`，結果在 log 裡搜尋 `ART_CHECK`。
  - 滑鼠模擬會被 Windows 擋，驗證 UI 流程請用上面的測試參數。
- **Unity 專案**：Unity 6000.4.8f1，內建算繪管線。粒子系統模組是新開啟的（`Packages/manifest.json`）。半透明和自訂 shader 的材質，要在 `Editor/AbyssMaterials.cs` 產生成材質檔，打包時才會被包含進去。
- **部署**：
  1. 把 User 層級環境變數 `Supabase__Url`／`Supabase__ServiceKey`／`Admin__Token` 載入 `$env:`。
  2. `docker compose up -d --build server`，確認 log 出現 `Auth mode: Firebase`。
  3. 重新壓縮 `client/Builds/AbyssKingdom-Windows.zip`，排除 `*DoNotShip*`。
- **Codex 自己的限制**（上次的經驗）：Codex 沙箱不能寫入 `.git`，自動審查也會擋下 push。**做完請停下，交給使用者或 Claude 提交**；額度快用完前，先把進度寫下來。另外記得關掉自己開的 Dev 伺服器。
- **PowerShell 坑**：同一條指令裡如果同時有 `Remove-Item` 和 `"C:\Program Files"` 路徑，會被安全機制擋，要拆成兩條。大段修補請寫成 .py 檔再執行。
- **AI 繪圖的上架紀錄**：建築、主將、士兵圖都是使用者用 ChatGPT 生成的。Steam 上架要申報 AI 生成內容。

## 4. 建議使用的 skills

### 2026-10-11 Codex 本輪進度（尚未提交）

- 使用者授權美術四項全做；最新指示為先做不需建模的工作，需 Blender 建模時停下等切換 Astra。
- 已修正開場通知層未隱藏；新增 opt-in 的本機 Dev 開場回歸檢查。正常播放與跳過後五秒留在城內的檢查皆 PASS。偶發自動出城尚未重現，不算已修復。
- 使用者已確認圖示畫風；16 張正式道具圖示已完成並覆蓋 `Resources/Art/Items`。原圖與 256×256 透明規格版位於 `art/source/items/samples/` 及 `ready/`；尺寸、留白和檔案雜湊均驗證通過。覆蓋前備份在 `.claude/backups/2026-10-11/item-icons/`。
- 詳細檔案、證據與下一步見 `docs/art/ART_POLISH_PROGRESS.md`。接著需 Astra 建模：建築煙囪與旗幟細節、三級主城外牆；美術驗收完才開始 S6 提案。
- 沒有修改遊戲規則、沒有 commit／push。後續提交由使用者或 Claude 處理。
- 最終 Unity 打包成功，0 errors；完整測試 184 passed、0 failed。實際開場／結束後截圖在 `artifacts/intro-delivery-natural/`。本輪開的 Dev 伺服器與客戶端均已關閉。
- 圖示替換後再打包成功，0 errors；16/16 客戶端貼圖載入 PASS，背包截圖在 `artifacts/item-icons-game/bag.png`。本輪沒有開始 Blender 建模，等使用者切換 Astra 後再做建築細節與三級外牆。

### 建模接續（以上「等待切換」狀態已完成）

- 使用者已表示「切換了」，完成 21 棟建築細節／分件重建與 3 級大地圖外牆，並接入遊戲。沒有改遊戲規則。
- 旗布 GPU 飄動、15 個煙口定位炊煙；三級外牆按主堡外觀同步顯示。原本立面徽幟保持固定。
- 重跑腳本、修改清單、限制與備份見 `tools/blender/BUILDING_POLISH.md`；最新資產驗證清單為 `building_polish_manifest.json`，24 個 FBX 全部 PASS。
- 最終打包 `artifacts/building-polish-build-final.log`：0 errors；`building-polish-tests.log`：184 passed、0 failed。
- `artifacts/building-polish-game-final/`：實際城內與大地圖截圖、各三級比較圖、旗布兩時刻截圖；runtime 24/24 PASS，單獨旗布動態 40538 changed pixels PASS。
- 本輪 Dev 伺服器已關閉、客戶端已退出；沒有 commit／push／部署。等使用者美術驗收，再進行 S6 grill 與設計提案，確認提案後才寫程式。建模工作已完成，後續可由 Sol 接手。

`grill-me`（開工前跟使用者對齊範圍）、`tdd`（改規則時）、`diagnosing-bugs`、`matt-code-review`、`ponytail`（使用者要最簡解時）、`codegraph`（專案已建好 `.codegraph/` 索引）。TypeSafe 插件已安裝，但只能用在非遊戲規則的 AI 判斷，例如審查命名、檢查美術規格。

## 5. 玩法參考（2026-10-11 Claude 補充）

使用者提供的生存經營型 SLG 攻略已整理在 `docs/design/REF_SURVIVAL_SLG.md`。**S6 聯盟提案請一併涵蓋**：聯盟幫助、派援軍駐守盟友城（大使館類功能）、集結容量（指揮部類功能）。
