# 行軍角色 3D 試作：Shield

日期：2026-10-10。建築採用基準：47c1277。此次只做 Shield；其餘五個角色等使用者確認後再做。

## 方法與交付

使用本機 Blender 5.2.2 LTS 及內附 Python，沿用 `city3d_common.py` 的幾何工具與純色貼圖方法。沒有外部服務、下載、套件安裝、API key 或服務費。單次生成與兩張渲染約 20 秒，確切時間見 `artifacts/units3d_previews/Shield.json`；這不包含腳本編寫、檢查與修正時間。

參考只使用 `client/Assets/Resources/Art/Troops/Shield.png`。製作單一磐盾衛，保留灰藍重甲、黃銅面罩、大塔盾、青綠缺角契印及短矛；約 3 頭身。可在遊戲中複製成小隊，沒有把參考圖上的三名士兵合成一個模型。

- `client/Assets/Resources/Models/Units3D/Shield.fbx`
- `client/Assets/Resources/Models/Units3D/Shield_Albedo.png`
- `artifacts/units3d_previews/Shield_52.png`：仰角 52°，方位 -25°。
- `artifacts/units3d_previews/Shield_front.png`：正面平視。
- `artifacts/units3d_previews/Shield.blend`：含分件、相機與預覽燈光的可編輯場景。
- `artifacts/units3d_previews/Shield.json`：生成尺寸、零件、關節及面數。
- `artifacts/units3d_previews/Shield_verified.json`：匯出後讀回驗證、原始 FBX 變換、貼圖色值與 SHA256。
- `tools/blender/shield_manifest.json`：可提交的驗證副本（artifacts 被 Git 忽略）。

## 尺度與分件

總計 **1,842 三角形**；6 個網格共用 1 個材質、1 張 1024×1024 albedo。
含盾與矛尺寸約 **X 0.835 × Y 1.006 × Z 0.439**；頭盔頂的小脊略高於 1。根物件 Shield 在兩腳中間地面 (0,0,0)，Y 向上、臉朝 -Z。

六個零件都是 Shield 的直接子物件，沒有骨架、蒙皮或動畫片段；相互不構成父子鏈。下表為 Unity / FBX 座標，L/R 指角色本人的左右（面向 -Z 時左側為 +X）。

| 零件 | 關節原點 (X,Y,Z) | 三角形 | 內容 |
| --- | --- | --- | --- |
| Body | (0,0.38,0) | 348 | 胸甲、腰帶、分叉裙甲與背布 |
| Head | (0,0.68,0) | 220 | 頭盔與面罩 |
| LegL | (0.105,0.37,0) | 204 | 左腿及靴 |
| LegR | (-0.105,0.37,0) | 204 | 右腿及靴 |
| ArmL | (0.225,0.61,0) | 510 | 左肩、左臂、大盾 |
| ArmR | (-0.225,0.61,0) | 356 | 右肩、右臂、短矛 |

FBX 的各零件局部旋轉為 0、縮放為 1。步行可繞局部 X 軸交錯擺腿，手臂小幅反向擺動；身體整體上下起伏請移動根物件或共同的外層容器。單獨轉 Body 不會帶動其他零件。盾與短矛離地較近，動畫振幅與上下起伏仍需 Unity 實機調整，避免穿地。此輪只驗證旋轉原點不漂移，未驗證完整步行碰撞或穿插。

## 材質

直接建立無光照的 sRGB 色塊 atlas，每個面採樣固定色格；沒有烘入陰影、高光、AO 或環境光。所有零件僅一組 UV，材質 metallic=0、specular=0、roughness=1。

盾上契印使用 **37BCAE** 與 **B8FFF1**，已檢查輸出 PNG 像素值完全一致；**F6B647** 保留在 atlas 中，但 Shield 沒有暖光部件。金邊使用 C5A361 / DDC18A，不當作發光色。

預覽的立體明暗來自 Blender 燈光，未模擬遊戲的呼吸發光。需由遊戲載入端為所有子網格套用對應霧面發光 shader；本輪不修改遊戲程式。

## 重跑

在專案根目錄執行；目前生成器只支援 Shield。

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python-exit-code 1 --python tools/blender/generate_units3d.py
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python-exit-code 1 --python tools/blender/verify_units3d.py
```

生成器會覆寫本次 Shield 產物，不處理其他角色。共用建築工具保持原樣。

## 驗證與限制

FBX 重新匯入驗證：6 個指定名字、共同根節點、關節座標、旋轉後原點固定、三角形上限、貼地、高度、單一共享材質及貼圖、單組 UV、霧面參數、原始 FBX 局部變換及 PNG 色值。結果必須顯示 `UNITS3D_VERIFIED` 且 status=PASS。

已人工檢視兩張最終預覽。優點是輪廓清楚、面數低、盾矛隨手臂移動；限制是色塊造型較原圖簡化，沒有原图的磨損、手繪紋理與細部曲面，肘膝不能彎曲。六個零件代表每個角色至少六個網格，單材質不保證只需一次 draw call，日後大量小隊仍需實機評估。

未啟動 Unity、未打包、未修改 `client/Assets/Scripts`、未改 2D 圖或建築模型。Unity 材質、縮小辨識度及走路效果交由使用者／Claude 接入驗證。未 commit / push。**停在 Shield 試做，等待確認後才處理 Crossbow、Rider、Hold、Vesper、Karst。**
