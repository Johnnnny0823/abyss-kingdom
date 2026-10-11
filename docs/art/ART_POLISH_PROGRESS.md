# 美術優化進度（2026-10-11）

基準交接 commit：c224b4b。使用者已確認美術全部納入，先修開場、再建築與外牆、最後圖示；後來指定先做不需建模的部分，碰到 Blender 建模時停下，等切換 Astra。S6 保持在美術後面，先提案再實作。

## 本輪修改

- GameUI.StartIntro 隱藏獨立通知層，結束（正常播放與跳過共用）時恢復；既有登入通知不再疊在開場上。
- 新增 `client/Assets/Scripts/Diagnostics/IntroCheck.cs`，只有本機 loopback 伺服器、`-devlogin -intro -introcheck <資料夾>` 才啟動。不影響普通啟動。逐幀檢查通知層，結束後監看城內五秒，輸出 JSON 與截圖。`-introcheckskip` 會呼叫與跳過按鈕相同的 Finish 方法；不等於驗證原生滑鼠點擊。
- 三張原創圖示樣稿：通用加速、主將小經驗書、通用信物。使用內建 imagegen，未使用外部 API、金鑰或下載套件。原圖位於 `art/source/items/samples/`，規格化的 256 PNG 位於 `samples/ready/`。
- `tools/art/prepare_item_icon_samples.ps1` 將原圖 alpha 主體等比例置中，保留至少 20 像素透明留白，產出尺寸／透明度／SHA256 驗證。只是規格化處理，未使用程式畫替代美術。
- 使用者已確認暖金＋青綠畫風；其餘 13 張已完成，16 張正式圖示全部覆蓋至 `Resources/Art/Items`，沿用原檔名和 .meta。加速沙色為金／橘／藍／紫／紅；經驗書為綠／藍／紫；四印卷軸為紅崩／青綠巧／黃綠迅／土褐磐；三位主將信物以既有頭像作身份參考。
- 規格化工具已擴充到全部 16 張，alpha 像素檢查改用內嵌 C# 加速；僅使用現有 PowerShell 與 System.Drawing，沒有安裝依賴。
- 新增本機 Dev 專用 `ItemIconCheck.cs`：`-itemcheck <資料夾>` 逐件 Resources.Load 驗證 16 張貼圖為 256×256，開啟實際背包並保存截圖。普通啟動不執行此檢查。

## 已取得證據

- `artifacts/intro-baseline-connected/result.json`：修正前 FAIL，開場期間通知層可見；正常結束後城內穩定。
- `artifacts/intro-fixed-natural-final/result.json`：完整播放 PASS，通知隱藏、結束後城內穩定。
- `artifacts/intro-fixed-skip/result.json`：跳過 PASS。同帳號不能同時啟動兩個 Dev 客戶端，會互相斷線，驗證請循序執行。
- `artifacts/intro-final-visible/`：跳過後五秒城內截圖及 PASS。隱藏視窗產生的黑圖不能當視覺證據；使用會正常呈現畫面的遊戲程序驗證。
- `artifacts/intro-delivery-natural/`：最終版正常播放 PASS；`intro-playing.png` 是無通知疊加的深淵運鏡，`intro-after-five-seconds.png` 是結束後五秒的城內畫面。
- `artifacts/intro-delivery-build.log`：Unity 6000.4.8f1 打包 Succeeded，189 MB、0 errors。曾嘗試 ScreenCapture API，但現有專案未啟用對應模組；已移除該嘗試，最終版沿用 ReadPixels，沒有增加套件。
- `artifacts/intro-polish-tests.log`：`dotnet run --project tests/AbyssKingdom.Tests`，184 passed、0 failed、184 total。
- `art/source/items/samples/ready/validation.json`：16 張圖示 256×256、透明背景與留白驗證 PASS，實際替換 PNG 的 SHA256 均與紀錄相同。
- `artifacts/item-icons-build.log`：圖示替換後 Unity 打包 Succeeded、189 MB、0 errors。
- `artifacts/item-icons-game/result.json` 與 `item-icons-player.log`：最終客戶端成功載入 16/16 貼圖，failures=0；`item-icons-game/bag.png` 是實際背包截圖（新建 Dev 帳號只持有部分道具，其餘圖示另外以資源載入驗證）。

開場結束後偶發自動切到大地圖的問題，本輪正常播放與呼叫跳過流程都未重現；沒有宣稱已修復。所有 SetMode 呼叫中，`WorldModelCapture` 在 `-artcapture` 模式會主動切換視角；目前沒有證據證明這就是先前觀察的原因。

## 接手順序

1. 道具圖示 16 張已完成並替換；原圖、遊戲規格版與驗證資料都保留。
2. 使用者已切換模型並授權建模；煙囪／旗布分件與三級大地圖外牆已完成並接入。詳細清單與重跑方法見 `tools/blender/BUILDING_POLISH.md`，等使用者驗收美術。
3. 美術驗收完，再用 grill-me 確認聯盟範圍，先寫 `docs/design/S6_ALLIANCE.md`，使用者確認後才寫程式。

沒有改遊戲規則，沒有寫入 .git、commit 或 push。修改前備份：`C:\Users\xnikz\.claude\backups\2026-10-11\intro-polish\`。artifacts 是 Git 忽略的本機驗證資料，交接時需保留或重跑。

### 建模接續結果

- `building_polish_manifest.json`：21 棟更新建築與 3 級外牆，24 個 FBX 回讀 PASS。建築 1044～4668 三角形、外牆 552／936／1104；建築單一 1024 albedo、外牆單一 512 albedo。
- `FlagCloth_*` 共 22 片旗布，固定邊零權重、自由端波浪；旗桿與建築不動，立面徽幟維持固定。15 個 `SmokeSocket_*` 在 Keep／Sawmill／Farm／Barracks／Hospital 三級定位炊煙。
- `artifacts/building-polish-build-final.log`：Unity Succeeded、190 MB、0 errors。
- `artifacts/building-polish-game-final/result.json`：24 models、0 failures。`flag-motion-a/b.png` 單獨旗布相隔 0.31 秒，40538 個像素改變；固定邊位置不變。
- 實機截圖：同資料夾的 `city-live.png`、`world-city-live.png`；三級並排檢查圖為 `Keep-a.png`、`CityWall-a.png` 等，展示台透過正常模型載入／材質流程呈現。
- `artifacts/building-polish-tests.log`：184 passed、0 failed。沒有遊戲規則變更。
- 本次备份：`.claude/backups/2026-10-11/building-polish/`。Dev 伺服器已關閉，客戶端自動退出；沒有部署、commit 或 push。

本機 Dev 伺服器與自動檢查客戶端已關閉。最終建置位於 `client/Builds/Windows/`，未另製發佈 ZIP、未部署。

道具圖示覆蓋前的素材和文件備份另存於 `C:\Users\xnikz\.claude\backups\2026-10-11\item-icons\`。所有圖示是 AI 生成，Steam 上架需依既有流程申報；主將原始立繪與頭像沒有更動。

```powershell
# Run this after starting a local Dev/InMemory server on port 5090.
& 'client/Builds/Windows/AbyssKingdom.exe' -server ws://127.0.0.1:5090/ws -devlogin -intro -introcheck C:/Users/xnikz/abyss-kingdom/artifacts/intro-check
# Add -introcheckskip to invoke the same Finish callback as the skip button.
```
