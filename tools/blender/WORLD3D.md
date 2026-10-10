# 大地圖模型交接

日期：2026-10-11。使用者提供的已採用基準為 `c3bfffb`。

本批交付 57 個原創低多邊形模型。只新增模型、貼圖、產生工具、預覽和驗證資料；沒有修改 `client/Assets/Scripts`，沒有 commit 或 push。模型尚未接入 Unity 地圖散佈、資源田替換、怪物動畫或坑口配置，不能把 Blender 預覽當成遊戲內驗收。

## 輸出清單

| 資料夾（Resources/Models/ 下） | 名稱 | 數量 |
| --- | --- | --- |
| WorldProps | Pine、Broadleaf、DeadTree、AbyssTree，各 A/B | 8 |
| WorldProps | Bush、GrassTuft、Flowers、GlowMushroom，各 A/B | 8 |
| WorldProps | Rock、Boulder、RockSpire，各 A/B | 6 |
| WorldProps | BrokenPillar、RuinArch、OldStatue、Stump、FallenLog，各 A/B | 10 |
| World3D | Field_Wood、Field_Stone、Field_Food、Field_Crystal，各 1/2/3 | 12 |
| World3D | MonsterCamp_1/2/3、Monster_1/2/3 | 6 |
| Abyss | RimRock_A/B/C、RimRuin_A/B、AbyssCrystal_A/B | 7 |

每個名稱都有 `<Name>.fbx` 和 `<Name>_Albedo.png`。既有 World、City、City3D、Units3D 模型不變。沒有另存大型 .blend；可由腳本重建。

## 重建與驗證

沿用既有 `city3d_common.py` 的造型工具，僅使用本機 Blender 5.2.2 LTS 與內附 Python。沒有下載權重、安裝套件、外部服務或 API 費用。

在專案根目錄執行：

```powershell
# Rebuild a family or several families.
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python-exit-code 1 --python tools/blender/generate_world3d.py -- Pine Broadleaf

# Rebuild all 57 assets.
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python-exit-code 1 --python tools/blender/generate_world3d.py -- all

# Import exported FBX files into a fresh scene and validate them.
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python-exit-code 1 --python tools/blender/verify_world3d.py -- all
```

驗證器也接受同樣的 family 名稱。每次重建後要重跑驗證，才能更新 manifest 與檔案雜湊。

- `artifacts/world3d_previews/<Name>.png`：每個模型各自一張 640×640、仰角 52°、方位 -30° 預覽。
- `<Name>.json`：產生紀錄，含尺寸、面數、配色、製作秒數。
- `<Name>_verified.json`：FBX 回讀結果、關節與 SHA256。
- `tools/blender/worldprops_manifest.json`：32 個裝飾物，含分區與密度。
- `tools/blender/world3d_manifest.json`：全部 57 個模型的驗證資料。
- `artifacts/world3d_delivery_validation.json`：最終名稱完整性與雜湊核對。

`artifacts/` 由專案既有規則忽略；預覽與執行紀錄留在本機，提交時請注意，manifest 則在 tools/blender 下。部分 Blender 執行紀錄有使用者偏好快取目錄寫入被拒的警告；模型匯出與驗證以程序退出碼及 PASS 為準。

## 尺寸、材質與匯入

- 1 公尺 = 1 地圖格。模型本身採公尺，不預先乘上遊戲顯示縮放；接入時沿用地圖格的換算。
- 靜態模型底部中央為原點，最低點 Y=0；Y 向上、正面 -Z。樹約 2.15～2.71 格；其餘尺寸詳見 manifest。
- 資源田和營地水平包圍盒均為 2×2 格。資源田逐級增加樹木、石料、食用菇床、晶簇或設施；营地逐級增加帳篷、骨刺。
- WorldProps 每件最多 800 三角形；本批實際最多 528。其他靜態物件採 1,500 上限，本批最高為 Field_Wood_3 的 1,392。
- 每件一個材質、一張 512×512 PNG、一套 UV。以純色格配置貼圖，沒有烘入陰影、光照、金屬或高光。預覽的明暗來自 Blender 展示燈光，不在貼圖內。
- FBX 使用 `axis_forward='-Z'`、`axis_up='Y'`、`apply_unit_scale=True`、`apply_scale_options='FBX_SCALE_UNITS'`、`bake_space_transform=True`；貼圖嵌入並另存 PNG。
- 發光保留色為 B8FFF1、37BCAE、F6B647。FBX 本身是霧面材質；遊戲中需使用既有辨識保留色的材質流程，才能呈現發光。不要讓 Standard 材質重新加入高光。
- 坑口岩塊採 282422、302A28 暗褐灰，呼應既有 AbyssPit。晶簇底部有暗色母岩；放入坑壁時由整個物件旋轉、位移貼合。斷崖可由配置位置伸出坑口，模型本身沒有挖坑或地形修改功能。

## 分區與密度

外環以綠樹、花叢、草叢為主；中環以枯樹、乾草、岩塊為主；崖緣以尖石、契印樹、發光菇與稀疏遺跡為主。石像為無臉石碑與斷裂契印構成的原創造型。

`density_per_100_tiles` 是每 100 格面積中「整個 family 的合計數量」，A/B 共用此數值，不是兩個變體各放一次。例如 Pine 外環為 3，表示 A+B 合計約 3 棵，而非 6 棵。數值是美術建議，不是遊戲規則。分區名為 `outer`、`middle`、`rim`；資源田、營地和坑口件沒有隨機散佈密度。

接入時需避開主城、資源田、營地、隘口、通路和深淵空洞，搭配既有 chunk culling；尚未測量實際裝飾密度下的手機效能。GrassTuft_A 偏綠、B 偏乾，可依分區調整選取權重。

## 怪物分件

| 模型 | 高度 | 三角形 | 根物件下的直接子物件 |
| --- | --- | --- | --- |
| Monster_1 | 0.8 | 516 | Body、Head、LegFL、LegFR、LegBL、LegBR、Tail |
| Monster_2 | 1.2 | 496 | Body、Head、LegL、LegR、ArmL、ArmR |
| Monster_3 | 1.6 | 836 | Body、Head、LegFL、LegFR、LegBL、LegBR、Tail |

各部件原點位於腰、頸、腿根、肩膀、尾根，共用單一材質及貼圖，沒有骨架。每個關節的 Unity XYZ 座標列於 manifest 的 `parts`。外環怪物是苔甲短吻獸；中環是空心契印頭與長臂的雙足魔物；崖緣是四眼、角冠、殼背與長尾的四足魔物。

驗證器核對零件名稱、父子關係、面數、尺寸、落地、軸向變換、材質數、貼圖尺寸、UV 範圍與保留色；並旋轉零件確認關節位置不漂移。這不等於已驗證 Unity 的行走碰撞或擺動穿模，動畫幅度仍需接入後調整。

## 驗收範圍與接手工作

逐張檢查 Blender 預覽；修正了草葉重疊面、松樹頂端露出的樹幹、殘拱石塊間距及坑口鍊環方向與間距。模型均有獨立 FBX 回讀驗證。

本輪未重打 Unity、未連使用者帳號、未改遊戲規則，故沒有宣稱遊戲內效果或 164 項規則測試已重新通過。接手者需加入載入與散佈、套用發光材質、綁定怪物程式動畫，再於本機 Dev 客戶端驗收遠近視角與效能。

## 2026-10-11：剩餘舊模型換新（基準 ba92d85）

前述 57 件已由使用者採用並接入。本節新增五件，全部放在 `client/Assets/Resources/Models/World3D/`，不覆蓋 `Models/World/`。這五件尚未在 Unity 內驗收，沒有修改載入程式或進行 commit／push。

| 模型 | 底面 X×Z | 高度 | 三角形 | 外觀 |
| --- | --- | --- | --- | --- |
| Mountain_A | 3×3 | 3.70 | 96 | 雙峰、雪頂、暗色岩層 |
| Mountain_B | 3×3 | 3.85 | 96 | 高主峰與側峰、雪頂 |
| Mountain_C | 3×3 | 3.60 | 96 | 暗色峰頂與裸岩層 |
| PassTower | 1.35×1.35 | 2.50 | 492 | 八角塔、扶壁、垛口、正面青綠契印 |
| AbyssGate | 3×3 | 3.08 | 688 | 抬高井緣、七級下降石階、殘拱與深井契印 |

每件一個材質、一張 512 albedo，沒有烘入光照與高光。沿用保留色 B8FFF1、37BCAE、F6B647；雪色 DFE8E7、井底深色 20242B。原點位於底部中央，Y 朝上，正面 -Z，1 公尺 = 1 格。山脈遵循本輪指定的 1,200 面上限；塔與入口都低於 1,500 面。

### 重建與驗證

新增 `tools/blender/generate_world_landmarks.py`，匯入既有 `generate_world3d.py` 的材質、輸出與預覽流程。既有 57 件的生成腳本和模型未改。

```powershell
# Generate the five replacements and assembly previews.
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python-exit-code 1 --python tools/blender/generate_world_landmarks.py

# Rebuild only one asset; names are case-sensitive.
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python-exit-code 1 --python tools/blender/generate_world_landmarks.py -- AbyssGate

# Verify all five exported assets and merge records by name.
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python-exit-code 1 --python tools/blender/verify_world3d.py -- Mountain PassTower AbyssGate
```

驗證器原先把非空 footprint 固定視為 2 格，現改成讀取每件報告的 footprint（舊件仍為 2、新件為 3）。manifest 改為依名稱合併，避免新工作環境缺少既有 artifacts 時清掉先前紀錄。`world3d_manifest.json` 合計 62 件；本輪五件各自標記 `base_commit: ba92d85`，其餘紀錄保留。

### 配置方式與證據

- **山牆**：山脊沿模型本地 X 軸，左右端面採同一組七點輪廓。直線中心距離 3 格可精確相接。環狀配置請令本地 X 沿圓環切線，建議中心距離約 2.7 格、略為交疊；不要任意旋轉每座山，否則端面無法對齊。已另做相鄰轉角 10° 的彎曲配置預覽，其他半徑與地形高差仍需遊戲端調整。
- **石塔**：正面契印朝 -Z，兩座以各自 Y 軸轉向通道。配對預覽使用中心距離 2.6 格，通道淨寬約 1.25 格；遊戲內可自行加寬，不必非等比縮放。
- **深淵入口**：由正面高處階梯向後方暗井下降。井底高於模型地面，避免未挖洞的地形把洞口遮住。視覺上通往深處，沒有地下延伸網格、通行邏輯或地形挖洞功能。
- 五張單件預覽：`artifacts/world3d_previews/<Name>.png`；各有 `<Name>.json` 和 `<Name>_verified.json`。
- 組合預覽：同目錄的 `Mountain_Joined_52.png`、`Mountain_Curved_52.png`、`PassTower_Pair_52.png`。
- 山脈接縫驗證：`artifacts/world_landmarks_seams.json`，FBX 回讀檢查六個端面及兩處直線接縫，高度差 0。
- 最終交付核對：`artifacts/world_landmarks_delivery_validation.json`；模型回讀紀錄：`artifacts/world_landmarks_verify.log`。

本次既有文件、manifest、驗證器的修改前備份位於 `C:\Users\xnikz\.claude\backups\2026-10-11\world3d-remaining\`。預覽與 JSON 證據仍放在被 Git 忽略的 artifacts；交接給其他電腦時需另行帶走，或依上方指令重建。
