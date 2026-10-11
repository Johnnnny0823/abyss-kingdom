# 建築生命感與大地圖外牆（2026-10-11）

使用者已切換模型並授權繼續建模。沿用已採用 City3D 建築造型，沒有外部 API、套件或付費服務。

## 產物與重跑

- `polish_city3d.py`：Keep、Sawmill、Farm、Warehouse、Barracks、Hospital、CrystalMine 三級，共 21 棟。Quarry、Site 沒有需要修改的旗幟／爐灶，維持原檔。
- Keep_1 只重用 `keep_1_trial.py` 的幾何建構區段，不執行舊版的檔案操作與匯出。其餘重用 `generate_city3d.py`。
- `city3d_common.finish_asset` 加入可選的烘焙後分件回呼；原有呼叫方式不變。
- `generate_city_walls.py`：World3D/CityWall_1～3，三級分別是矮石牆、四角守望塔、契印城門。內院淨寬 2.16 格，容納底面 2×2 的主堡。外牆不代表新的遊戲佔地或防禦加成。
- 建築沿用單一 1024 albedo；外牆單一 512 albedo。旗布與本體共用同一張貼圖、同一匯出材質。

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python-exit-code 1 --python tools/blender/polish_city3d.py
# A single family: append -- Hospital (all three tiers).
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python-exit-code 1 --python tools/blender/generate_city_walls.py
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python-exit-code 1 --python tools/blender/verify_building_polish.py
```

## 動態契約

- `BuildingBody` 是不動的建築網格；`FlagCloth_00` 等是獨立旗布，包含金邊、不含旗桿。
- 旗布 vertex color R 是從固定邊 0 到自由端 1 的權重。Building3D shader 只對旗布材質啟用 `_Cloth`，幅度依实例縮放；GPU 動畫不建立每幀 CPU 網格或配置記憶體。立面上的懸掛徽幟仍為固定裝飾。
- `SmokeSocket_00` 等 Empty 隨模型匯出，已套用與頂點一致的尺寸正規化。LivingModel 使用實際 Transform 世界位置生成炊煙，不再推估新模型座標。舊伐木場座標保留作舊模型備案。
- 爐灶：Keep、Sawmill、Farm、Barracks、Hospital 各三級，共 15 個煙口。Warehouse 不冒煙，礦場不增加不合理的屋頂爐灶。醫館沒有紅十字。
- 原有 shader 契印、暖光、草木風保留；旗布由零權重固定邊過渡到自由端波浪。旗布及本體各有 renderer，所以分件會增加少量 draw calls。
- SurfaceView 的城堡換外觀時同步產生對應三級外牆；城內擺放與伺服器規則均不變。

## 驗證與交接

- Blender 52 度預覽、生成紀錄和重新匯入驗證：`artifacts/building_polish/`。
- 可提交完整驗證清單：`building_polish_manifest.json`。24 個全部 PASS；重新匯入後檢查三角形、單材質、實際貼圖尺寸、底面／接地、旗布遮罩和煙口。本清單取代 `city3d_manifest.json` 中同名資產的歷史雜湊／網格數，後者仍保留作原批次紀錄。
- `BuildingPolishCheck.cs` 以 `-buildingpolishcheck <目錄>` 啟動，必須同時使用 loopback `-server` 和 `-devlogin`。檢查遊戲中的材質、旗布設定與煙口／粒子位置，截取實際城內、大地圖，以及三級模型並排的兩個時間點。普通啟動不執行。
- 完整規則測試：`artifacts/building-polish-tests.log`，184 passed，0 failed。
- 最終 Unity 打包：`artifacts/building-polish-build-final.log`，Succeeded，190 MB，0 errors。
- 最終實機：`artifacts/building-polish-game-final/result.json`，24 models、0 failures；`building-polish-player-final.log` 有逐件 PASS。`flag-motion-a.png`／`flag-motion-b.png` 是單獨旗布兩個時間點，排除煙霧、UI 等干擾，40538 個像素發生變化，GPU 動畫 PASS。
- 實際城內與一級大地圖主城：`city-live.png`、`world-city-live.png`。各建築與外牆的三級比較：`<Family>-a.png`／`-b.png`；三級比較用測試展示台、相同載入與材質流程，不冒充完整升級流程驗證。
- Windows 客戶端已更新；沒有發佈 ZIP 或部署。測試客戶端自動退出，本輪 Dev 伺服器已關閉。
- 修改前備份：`C:\Users\xnikz\.claude\backups\2026-10-11\building-polish\`，含整個 City3D 舊素材及改動程式／文件。
- 沒有 commit、push、正式部署，也沒有改 Models/City 舊灰盒。

## 本輪檔案清單（不含前一輪圖示／開場修改）

- 更新 `Resources/Models/City3D/` 上列 21 棟 FBX 與必要的 albedo；原 `.meta` 保留。CrystalMine 主要跟隨統一匯出階層，沒有新增爐灶或旗桿。
- 新增 `Resources/Models/World3D/CityWall_1～3.fbx`、`_Albedo.png` 及 Unity 產生的 `.meta`。
- 修改 `World/LivingModel.cs`、`World/SurfaceView.cs`、`Shaders/Building3D.shader`；Unity 材質 `Resources/Abyss/Building3D.mat` 自動增加 `_Cloth: 0`。
- 新增 `Diagnostics/BuildingPolishCheck.cs` 及 `.meta`。
- 修改 `tools/blender/city3d_common.py`；新增 `polish_city3d.py`、`generate_city_walls.py`、`verify_building_polish.py`、`building_polish_manifest.json`、本文件。
- 更新 `docs/art/MODEL_SPEC.md`、`ART_POLISH_PROGRESS.md`、`docs/HANDOFF-CODEX.md`。
- GPU 旗布是輕量視覺波浪，不是布料物理模擬；沒有修改任何遊戲規則。下一步等美術驗收，S6 仍必須先提案並經使用者確認。
