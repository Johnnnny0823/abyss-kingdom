# City3D 批次模型交接

基準 commit：`bf51933`。此次只新增其餘 24 個模型，已採用的 Keep_1 不變。

## 輸出

- 模型與貼圖：`client/Assets/Resources/Models/City3D/<名稱>.fbx`、`<名稱>_Albedo.png`。
- 每棟獨立預覽：`artifacts/city3d_previews/<名稱>.png`，仰角 52°、方位 -32°。
- 每棟生成紀錄：`artifacts/city3d_previews/<名稱>.json`。
- 每棟 FBX 往返驗證：`artifacts/city3d_previews/<名稱>_verified.json`，含檔案 SHA256。
- 彙總：`artifacts/city3d_previews/validation_summary.json`；最終可提交的清單另存 `tools/blender/city3d_manifest.json`。
- 最終執行紀錄：`artifacts/city3d_final_<種類>.log`、`artifacts/city3d_final_<種類>_verify.log`。

## 使用方式

只用既有 Blender 5.2.2 LTS 與內附 Python；未下載模型、未安裝套件、未使用外部 AI API。參考圖逐張取自 `Resources/Art/Buildings/`，沒有使用特效遮罩。

```powershell
# Rebuild one family, including all its tiers.
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python-exit-code 1 --python tools/blender/generate_city3d.py -- Farm

# Rebuild all 24 new assets; Keep_1 is deliberately excluded.
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python-exit-code 1 --python tools/blender/generate_city3d.py

# Validate exported FBX files, not just the modeling scene.
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python-exit-code 1 --python tools/blender/verify_city3d.py -- all
```

可指定 Keep、Sawmill、Quarry、Farm、CrystalMine、Warehouse、Barracks、Hospital、Site；Keep 只製作 2／3 級。脚本會覆寫指定種類的新模型，不會寫入 `Models/City/`。歷史 `keep_1_trial.py` 仍含舊試作路徑，本批次請使用新腳本。

## 升級差異

| 種類 | 視覺進程 |
| --- | --- |
| Keep | 保留既有 1 級；2 級增加廊道及旗飾；3 級雙層屋冠、額外望塔與契印 |
| Sawmill | 紅瓦木構＋鋸台；2 級水車；3 級吊架與屋頂小塔 |
| Quarry | 赭色採石階地＋礦車；2 級工作棚與階梯；3 級起重塔及切石設備 |
| Farm | 綠瓦屋與金色菌床；2 級增加菌床與水桶；3 級八角溫室及灌溉水道 |
| CrystalMine | 青綠晶簇、礦口及礦車；2 級吊架與工作棚；3 級旗飾及契晶石柱 |
| Warehouse | 紅瓦倉庫、木箱與桶；2 級通風塔及吊架；3 級較大主體及雙側裝卸棚 |
| Barracks | 訓練場、紅旗、武器架；2 級增添棚與旗；3 級守衛塔、紅頂及更多假人 |
| Hospital | 白牆青綠頂與藥草；2 級療養棚；3 級圓頂、柱廊及水盆，沒有紅十字 |
| Site | 矮石基、空地、零散石板、木樁及繩圍，入口保留空缺 |

所有模型底面為 1×1，所以升級的「變大」以主體高度、量體與附屬建築數量呈現，不擴張遊戲佔地。各種類高度設定見 `generate_city3d.py` 的 HEIGHTS。

## 材質與限制

- 沿用 Keep_1 的低多邊形程序建模與純色彩烘焙方法；共用工具在 `city3d_common.py`。
- 每棟一個網格、一個材質、一張 1024×1024 albedo；材質金屬度 0、高光 0、粗糙度 1。
- 小金邊與契印使用同一張 atlas 的保留色格，避免細 UV 島烘焙成黑線。水平石面改用 XY 投影，避免整面採到磚縫色。
- 貼圖不含光照、高光或陰影；預覽中的立體陰影來自 Blender 預覽燈光。
- 原圖不可見的背面採同樣建築語彙補建；細緻手繪磨損、透明玻璃、真正發光及動態效果未重建。Farm_3 的溫室使用不透明色塊呈現，維持單一霧面材質。
- 外觀依原圖做辨識特徵與升級差異，不是逐像素重建。精緻度仍是已採用試作的低多邊形層級。
- 預覽為每棟自動取景，不能靠圖片中的像素高度直接比較模型尺寸；以驗證 JSON 的 Y 高度為準。

## 交付邊界

- 未修改 `client/Assets/Scripts`、`Models/City/`、Keep_1 或原始 2D 圖。
- 未啟動 Unity、未打包客戶端、未操作使用者帳號。FBX 往返檢查不等於 Unity 實機驗證，請由 Claude 檢查實際材質、尺度與遮擋。
- 新增貼圖及模型的 Unity `.meta` 待匯入時產生。
- `artifacts/` 被 Git 忽略；若要提交預覽，請自行決定收錄位置。
- 不 commit、不 push。
