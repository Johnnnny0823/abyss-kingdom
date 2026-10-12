# 主堡多角度參考圖：GPT／Nano Banana 提示詞包

目的：把主堡（`Keep`）做成**同一棟建築的多角度圖＋結構圖（至少 10 張）**，給 Blender 建模（`City3D/Keep_*`，見 `MODEL_SPEC.md` 5b）當參考，讓 3D 模型各面都對得上 2D 圖。

## 0. 參考圖用哪張

**用我們自己的 `client/Assets/Resources/Art/Buildings/Keep_3.png` 當主參考圖**（Lv.7～10 外觀，最完整），要做低等級再換 `Keep_1.png`／`Keep_2.png`。

- 不要直接餵網路上別款遊戲的城堡圖：GDD 規定美術全部原創、不沿用他作素材，而且 AI 會照抄造型。
- 想加入「喜歡的城堡」的感覺，可以用**真實歷史建築的名字當風格詞**（例如 `inspired by Carcassonne ramparts`、`Hohenzollern castle towers`），或用 CC0／公有領域照片（Wikimedia Commons 標 Public Domain / CC0 的）當第二張參考，只取比例和結構，不取別人的畫作。

## 1. 用法（兩個工具都一樣）

1. 開新對話，**上傳 `Keep_3.png`**，先貼「共通前綴」。
2. 之後**一次一張**貼下面的各角度提示詞（不要一次要 10 張，會變成縮圖拼貼、細節亂掉）。
3. 每張出來先檢查「一致性檢查表」（第 4 節），不對就回一句：`Keep everything identical to the reference, only fix: <哪裡不對>`。
4. 存檔命名 `Keep3_ref_01_front.png` … 放 `docs/art/ref/keep/`（參考圖不進 Unity 的 Resources）。

- **GPT（ChatGPT 圖片）**：一致性比較好，適合角度圖。
- **Nano Banana（Gemini 2.5 Flash Image）**：改圖快，適合「同一張圖轉角度／拿掉某部分」；如果角度轉不過去，把上一張產出的圖也一起上傳再要下一個角度。

## 2. 共通前綴（每張都先貼這段，或第一次貼過後說「same rules as before」）

```
Use the attached image as the exact design reference. This is the same building: a fortified fantasy keep
for a mobile strategy game. Keep EVERYTHING identical: blue slate roofs with gold trim and gold spires,
grey stone walls with moss and vines, the tall central tower with a teal glowing diamond rune, the arched
wooden main gate with a second teal diamond rune above it, the front staircase, the outer corner towers with
blue pennant flags, blue hanging banners with gold diamond, the arched colonnade on the right wing, the
stepped stone base with gold arrow buttresses. Same proportions, same number of towers, same colors.
Clean painterly stylized game art, chunky proportions, soft warm light from upper left,
plain light grey background, no text, no logo, no people, single building centered.
```

## 3. 各張提示詞（12 張：8 張角度＋4 張結構）

### 角度圖

| # | 檔名 | 提示詞（接在前綴後） |
|---|---|---|
| 01 | `front` | `Orthographic front elevation, camera level with the ground, looking straight at the main gate and staircase. No perspective distortion.` |
| 02 | `back` | `Orthographic back elevation, the opposite side of the main gate. Invent the rear wall consistently: a small postern door, windows, the colonnade wing seen from behind. Same towers and roofs.` |
| 03 | `left` | `Orthographic left side elevation (the side to the left of the main gate). Show the corner towers and the central tower profile.` |
| 04 | `right` | `Orthographic right side elevation (the colonnade side). Show the arched colonnade fully.` |
| 05 | `top` | `Pure top-down plan view from directly above, roofs visible, so the footprint and tower positions are clear. Square footprint.` |
| 06 | `iso_fl` | `Isometric 3/4 view from the front-left, 30 degrees above, same as a mobile strategy game camera.` |
| 07 | `iso_br` | `Isometric 3/4 view from the back-right, 30 degrees above. This is the hidden side of the reference; design it consistently.` |
| 08 | `low_hero` | `Low-angle hero shot from near the bottom of the front staircase looking up at the central tower, dramatic but same design.` |

### 結構圖（建模最有用）

| # | 檔名 | 提示詞（接在前綴後） |
|---|---|---|
| 09 | `turnaround` | `Character-sheet style turnaround: front, right, back, left orthographic views of the same building in one row, same scale, aligned on one ground line, thin grey separator lines.` |
| 10 | `exploded` | `Exploded axonometric diagram: separate the building into stacked layers floating above each other — stepped stone base, outer curtain walls and corner towers, central keep body, roofs and spires, flags. Thin dashed guide lines between layers. Clean technical illustration, still in the same painterly colors.` |
| 11 | `blockout` | `Simple 3D blockout version of the same building: only basic shapes (boxes, cylinders, pyramids), flat grey clay material, no textures, no decorations, isometric front-left view. Shows the main volumes for 3D modeling.` |
| 12 | `section` | `Architectural cross-section cut through the middle from front to back: show wall thickness, floor levels of the central tower, the gate passage, the courtyard behind the gate. Blueprint style with white lines on dark blue, no text labels.` |

> 想要更多張：把 01～08 換成 `Keep_1.png`／`Keep_2.png` 再跑一輪，就是三個外觀各一套。

## 4. 一致性檢查表（每張都看）

- [ ] 塔的數量和位置跟參考圖一樣（先在參考圖上數好：中央主塔、外圍各角塔、正門兩側小塔）
- [ ] 正門上方與主塔各一個**青綠菱形契印**（發光色 `37BCAE`／`B8FFF1`），沒有變成別的符號
- [ ] 屋頂藍＋金邊、旗幟是藍色燕尾旗、垂幡是藍底金菱形
- [ ] 右側（正門的右手邊）是拱廊，左側是城垛牆
- [ ] 沒有文字、Logo、紅十字、人物
- [ ] 背景單色，方便去背

## 5. 交給 3D 建模時

把 01～05、09～11 交給建模（Codex 或 Blender 腳本），附上這份檔案。規格照 `MODEL_SPEC.md` 5b：FBX、底面 1×1、正面朝 -Z、4,000～5,000 三角形、1 材質、1 張 1024 albedo，發光部位用保留色。
