# Keep_1 單棟 3D 試作交接

> 2026-10-09 後續（Claude）：使用者決定採用 3D。新版模型改放 `client/Assets/Resources/Models/City3D/`（`Keep_1.fbx`、`Keep_1_Albedo.png`），`Models/City/Keep_1.fbx` 還原成舊灰盒，灰盒備份資料夾已刪除。下方「檔案清單」是試作當時的路徑。規格與接入方式見 `docs/art/MODEL_SPEC.md` 5b。

日期：2026-10-09。狀態：試作與 Blender／FBX 檢查完成，等待使用者確認，未批次製作、未接入 Unity、未 commit／push。

## 方法與費用

- 工具：本機 Blender 5.2.2 LTS，Python 背景執行。
- 參考：`client/Assets/Resources/Art/Buildings/Keep_1.png`；未使用 `_fx.png`。
- 方法：依圖人工判讀輪廓，以 Python 建立實體低多邊形網格，再將程序化色彩烘成單張 albedo。不是神經網路單圖重建，也不是把原畫直接投影到模型。
- 沒有使用外部生成服務、API key、帳號或下載任何模型權重／套件。外部服務新增費用為 0；不含既有 Codex 使用額度及電力。
- 最終一次生成、烘焙、兩張預覽共 39.8 秒；經三次生成修正細金邊接縫及色彩。腳本建立至完成驗證約 5 分鐘，不含前置閱讀及可行性判斷。

## 規格與證據

| 項目 | 結果 |
| --- | --- |
| 三角形 | 4,584，低於 5,000 |
| 網格／材質 | 1／1 |
| 貼圖 | Keep_1_Albedo.png，1024×1024，FBX 亦內嵌同一張圖 |
| FBX 尺寸 X/Y/Z | 1.000 / 1.291 / 0.9915 |
| 原點 | 底部中心 (0,0,0) |
| 軸向 | FBX Y 向上，正門／階梯朝 -Z |
| 材質 | 金屬度 0、高光 0、粗糙度 1；純色彩烘焙，不含光照與高光 |
| 預覽 | 正面，仰角 52°；另附方位偏轉 -32°、仰角 52° 的斜視圖 |
| 驗證 | 解析實際 FBX 頂點並重新匯入 Blender，檢查尺寸、朝向、底部、面數、材質與貼圖 |

證據在 `artifacts/keep_1_trial/validation.json`、`fbx_roundtrip_validation.json`；執行紀錄在 `artifacts/keep_1_trial_build.log`、`artifacts/keep_1_trial_verify.log`。

## 效果與限制

- 保留中央高塔、四角塔、藍瓦金邊、木門、雙側旗幟和門上青綠契印；為完整可旋轉幾何，不是平面立牌。
- 背面為依正面語彙補建，來源圖無法提供真實背面資訊。
- 現階段仍是風格化低多邊形試作，石材、瓦片、植被、磨損與原圖的手繪層次有明顯差距；尚未達到原畫的精緻度，不建議直接批次化後當作完成美術。
- 契印保留亮色與位置，但只有 albedo，不會真的照亮周邊，也沒有 bloom、旗幟動畫、碰撞體或 LOD。
- 正面 52° 預覽遵照此次要求；原圖本身是斜視構圖，因此另附斜視圖便於比較。
- 未執行 Unity 或修改遊戲載入方式。Gamma 色彩空間的最終亮度、Standard 材質映射、縮放與鏡頭效果由 Claude 接入時驗證。Blender 往返材質檢查不等同 Unity 實機檢查。
- 本次尺寸遵照新要求約 1×1；舊 MODEL_SPEC 的城內尺寸較大，本次未更動舊規格或遊戲程式。

## 檔案清單

修改：
- `client/Assets/Resources/Models/City/Keep_1.fbx`

新增：
- `client/Assets/Resources/Models/City/Keep_1_Albedo.png`
- `art/source/models-greybox-backup/Keep_1.fbx`
- `art/source/models-greybox-backup/Keep_1.fbx.meta`
- `tools/blender/keep_1_trial.py`
- `tools/blender/verify_keep_1_trial.py`
- `tools/blender/KEEP_1_TRIAL.md`
- `artifacts/keep_1_trial/Keep_1.blend`
- `artifacts/keep_1_trial/Keep_1_front_52.png`
- `artifacts/keep_1_trial/Keep_1_three_quarter_52.png`
- `artifacts/keep_1_trial/validation.json`
- `artifacts/keep_1_trial/fbx_roundtrip_validation.json`
- 上述兩份執行 log。

原本 `Keep_1.fbx.meta` 保持不變，以保留 Unity GUID。新貼圖的 Unity `.meta` 待專案匯入產生。`artifacts/` 被 Git 忽略，提交時若要保留預覽需由使用者決定收錄方式。

## 重跑（只會做 Keep_1）

在專案根目錄以 PowerShell 執行：

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python tools/blender/keep_1_trial.py
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python tools/blender/verify_keep_1_trial.py
```

重跑會更新本次試作輸出，已存在的灰盒備份不會覆蓋。等待確認後才決定是否處理其餘 24 張。
