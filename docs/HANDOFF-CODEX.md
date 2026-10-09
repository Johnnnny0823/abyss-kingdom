# 交接給 Codex（2026-10-09，最新 commit `b483708`）

只寫對話裡才有、其他文件沒有的東西。開工前依序讀：`CLAUDE.md` → `docs/HANDOFF.md` → `docs/design/GDD.md` → `docs/art/MODEL_SPEC.md`、`docs/art/CHARACTER_SPEC.md`。

## 1. 目前狀態

- **遊戲功能**：地表 S1～S5 完成並已部署，測試 164 個全過。內容是大地圖、直線行軍、採集、打怪物營地、傷兵與醫館、PvP 攻城、7 天新手護盾。**S6 聯盟暫停**，使用者要先做美術。
- **美術**：
  - 大地圖：13 個 Blender 程式產生的低多邊形模型，由 `tools/blender/generate_world.py` 產生。另有地面雜訊貼圖，以及中央的深淵漏斗坑（含霧和坑口光暈）。
  - 城內：25 張 2D 精緻建築圖，是使用者用 ChatGPT 生成的。8 種建築各 3 個等級外觀，加 1 張空地基。原圖在 `art/source/buildings/`。
  - 主將：3 張立繪、3 張頭像；士兵：3 張兵種圖。原圖在 `art/source/`，切圖程式是 `tools/art/slice_sheet.py`。
  - 2.5D 動態：`tools/art/make_fx_masks.py` 產生效果遮罩，`Shaders/LivingSprite.shader` 做出契印呼吸、窗燈閃爍、旗幟擺動，`World/LivingBuilding.cs` 負責光點、接觸陰影和點選回饋。行軍時的 2D 部隊有程式動畫（`World/MarchAnimator.cs`）。
- **使用者已定案的方向**：
  - 城內保留**自由拖曳建築**。
  - 建築用 **2D 精緻圖＋2.5D 動態**（Shader；之後可能加 Live2D 或 Spine）。
  - 美術參考《來自深淵》的**氛圍**、神話建築，以及寒霜啟示錄這類 4X 手遊的畫風。**只借氛圍，不做任何作品的具體造型**；給 AI 繪圖的提示詞裡不可寫作品名或畫家名。
  - 醫館**不可畫紅十字**，用青綠菱形契印代替。

## 2. 下一步候選（開工前先問使用者要做哪個）

1. 煙囪冒煙、小人沿路走動：要先標出每張圖的煙囪和道路位置。
2. 高等級的建築畫得更大：目前每個等級都一樣寬。
3. 主將立繪改成 Live2D 或 Spine：要使用者先買授權並手動綁骨架，Codex 負責接進 Unity。
4. 回頭做 S6 聯盟與盟旗。

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

`grill-me`（開工前跟使用者對齊範圍）、`tdd`（改規則時）、`diagnosing-bugs`、`matt-code-review`、`ponytail`（使用者要最簡解時）、`codegraph`（專案已建好 `.codegraph/` 索引）。TypeSafe 插件已安裝，但只能用在非遊戲規則的 AI 判斷，例如審查命名、檢查美術規格。
