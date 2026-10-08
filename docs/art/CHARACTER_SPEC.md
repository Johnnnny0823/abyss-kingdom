# 角色美術規格（主將立繪、頭像、兵種圖）

遊戲已經改成**有圖就用圖**：PNG 放進指定資料夾、重新打包，主將頁就會顯示立繪，大地圖上的行軍部隊就會變成 2D 圖。沒有圖的角色，照舊顯示剪影或 3D 小兵，可以一張一張慢慢補。

---

## 1. 檔案規格

| 種類 | 放哪裡 | 檔名 | 尺寸（像素） | 內容 |
| --- | --- | --- | --- | --- |
| 主將立繪 | `client/Assets/Resources/Art/Heroes/` | `Hold_Portrait.png`、`Vesper_Portrait.png`、`Karst_Portrait.png` | 1024 × 1536（直式 2:3） | 全身、站姿、略朝右前方。顯示在主將頁中央。 |
| 主將頭像 | 同上 | `Hold_Icon.png`、`Vesper_Icon.png`、`Karst_Icon.png` | 256 × 256 | 頭與肩膀，臉置中。會被裁成圓形，四個角落不要放重要東西。顯示在主將列表，以及地圖上行軍隊伍的頭上。 |
| 兵種圖 | `client/Assets/Resources/Art/Troops/` | `Shield.png`（磐盾衛）、`Crossbow.png`（鳴弦手）、`Rider.png`（裂蹄騎） | 512 × 512 | 3～5 名士兵站在一起，或一名騎兵，四分之三側面。**腳底貼齊圖片下緣**：遊戲會把下緣放在地面上。這就是地圖上行軍時看到的 2D 部隊。 |

共通規則：

- **背景一定要透明**（PNG）。很多 AI 工具會給白底，要先去背，例如用 remove.bg、Photoshop，或工具內建的去背功能。
- 檔名要**完全一樣**（大小寫也一樣），英文是程式內部的名字。
- 同一組角色的畫風、光源方向（建議左上方來光）、線條粗細要一致，可以先做一張滿意的，再用同一套提示詞做其他張。

## 2. 畫風

- **手遊 4X 的 Q 版比例**（類似寒霜啟示錄、王國之歌這類遊戲）：頭身比約 1:3～1:4，輪廓清楚，縮小也認得出來。
- **世界觀氛圍**：古老地層、失落文明的遺跡、苔蘚、繩索與探險裝備、青綠色發光的「契印」（菱形缺角的符文）。
- **配色**：灰藍石板、黃銅與暖金、青綠契印，每位主將再加上自己的淵脈色（見下方）。
- **版權**（CLAUDE.md、GDD 2.5）：
  - 可以參考《來自深淵》的氛圍，但**不可以**模仿它的角色造型、服裝、道具（例如哨笛）、畫風。
  - **提示詞裡不要寫任何作品名或畫家名**，例如 "Made in Abyss style"、"in the style of …"，否則生成的圖容易太像原作，有侵權風險。
- **上架提醒**：Steam 上架時要申報是否使用 AI 生成的內容（商店頁會標示）。用 AI 做的圖請保留紀錄：用了哪個工具、生成日期，之後填表要用。

## 3. 角色設定

| 主將 | 稱號 | 淵脈（代表色） | 外型重點 |
| --- | --- | --- | --- |
| 霍德（Hold） | 鐵錘同盟・老盾衛隊長 | 浮層（土、赭黃） | 五十多歲、寬肩、灰白鬍子、表情沉穩。灰藍厚板甲配黃銅邊，背著刻有青綠菱形契印的大塔盾。「在裂口邊守了三十年的盾，說話不多，退後的次數更少。」 |
| 薇絲珀（Vesper） | 銀弦公會・遊擊射手 | 沉鐘（金屬、銀白） | 二十出頭的女性、輕裝、銀藍色連帽斗篷，手持弦上有青綠微光的弩，像在側耳聆聽，眼神專注。「聽得見弦上的回響，總是第一個發現往下的路。」 |
| 卡斯特（Karst） | 自由傭兵團・疾蹄騎手 | 浮根（木、綠） | 二十多歲的男性、笑容張揚、皮甲配綠色圍巾，騎著一頭**原創的**粗壯坐騎（蹄上有發光裂紋）。「他說馬蹄比任何道路都可靠，整片大陸對他來說都太小了。」 |

| 兵種 | 外型重點 |
| --- | --- |
| 磐盾衛（Shield） | 前排重步兵：灰藍盔甲、面罩頭盔、刻契印的大方盾、短矛。 |
| 鳴弦手（Crossbow） | 遠程射手：連帽輕甲、背箭筒，手持弦會發出青綠回響的弩。 |
| 裂蹄騎（Rider） | 機動騎兵：皮甲騎手，騎原創坐騎（粗壯、蹄有發光裂紋），手持長槍與小旗。 |

## 4. 可以直接貼給 AI 的提示詞（英文效果通常較好）

共通前綴（每張都加）：

```
chibi-proportioned mobile strategy game character art, clean painterly style, bold readable silhouette,
soft warm key light from upper left, ancient layered stone ruins theme, teal glowing broken-diamond rune
motif, slate blue and brass color palette, full body, transparent background, no text, no logo
```

各張的主體描述（接在前綴後面）：

- **霍德立繪**：`veteran shield captain in his fifties, broad shoulders, grey beard, calm stern expression, heavy slate-blue plate armor with brass trim, huge tower shield engraved with a teal glowing diamond rune, ochre earth-toned accents, standing pose facing slightly right`
- **薇絲珀立繪**：`young female skirmisher archer in her early twenties, light armor, silver-blue hooded cloak, holding a crossbow whose string glows faint teal, head tilted as if listening, focused eyes, silver and white accents, standing pose facing slightly right`
- **卡斯特立繪**：`cheerful young male mercenary rider in his twenties, confident grin, leather armor, green scarf, riding a sturdy original fantasy mount with glowing cracked hooves, forest green accents, dynamic pose facing slightly right`
- **頭像**：用同一位角色的描述，最後加上 `head and shoulders portrait, centered face, circular framing friendly`
- **磐盾衛**：`a small squad of three heavy infantry soldiers, slate-blue armor, visored helmets, large square shields with teal rune, short spears, three-quarter view, feet at the bottom edge`
- **鳴弦手**：`a small squad of three hooded crossbow soldiers, light armor, quivers, crossbow strings glowing teal, three-quarter view, feet at the bottom edge`
- **裂蹄騎**：`a single cavalry soldier in leather armor riding a sturdy original fantasy mount with glowing cracked hooves, lance with a small pennant, three-quarter view, hooves at the bottom edge`

> 不同 AI 工具的語法不同：Midjourney 可以在最後加 `--ar 2:3`（立繪）或 `--ar 1:1`（頭像、兵種）。

## 5. 放進遊戲

1. 去背、裁成規定尺寸，用規定的檔名存成 PNG。
2. 放到對應資料夾。
3. 跟 AI 助手說一聲，它會重新打包並截圖檢查：立繪在框內的位置、地圖上的大小、頭像有沒有被裁到。

## 6. 程式這邊的對應（給 AI 助手看）

- 讀取：`client/Assets/Scripts/UI/ArtLibrary.cs`（`HeroPortrait`、`HeroIcon`、`Troop`），找不到回傳 null。
- 主將頁：`HeroPage.cs`。有立繪就當背景顯示（等比例縮放、完整顯示），並隱藏剪影；列表的主將卡片有頭像就加在名字前面。
- 大地圖行軍：`SurfaceView.ApplyTroopArt`。用人數最多的兵種的圖，高 0.85 世界單位、永遠面向鏡頭；主將頭像掛在上方，高 0.32。有 2D 圖時隱藏 3D 小兵，但保留表示出征（金）或返回（藍）的旗子。
- **2D 部隊動畫**（`World/MarchAnimator.cs`，程式動畫，不需要逐格圖）：行軍時隨步伐彈跳、左右輕晃、落地微壓扁，往畫面左邊走時翻面；駐紮時慢慢呼吸。腳下影子的顏色表示出征（暖金）或返回（藍）。之後要真正擺手邁腿的走路動畫，需要逐格走路圖（一個循環 6～8 格）或把角色拆成零件做骨架動畫。
- 2026-10-09 已用暫時的方塊測試圖驗證過主將頁與地圖兩處，驗證後已刪除。
- **目前的圖（第一版）**：使用者用 ChatGPT 生成（2026-10-09；Steam 申報 AI 內容時要用到這筆紀錄）。原始檔在 `art/source/`：`characters_sheet_v1.webp` 是九張擠在一張的大圖，`hold_portrait_v1.webp` 是霍德的單張高解析立繪。用 `tools/art/slice_sheet.py` 切成規格檔案：裁切、清掉邊緣半透明雜點、去掉被切進來的隔壁角色、縮放，頭像裁成圓形。之後換新圖時，裁切範圍要重新調。
