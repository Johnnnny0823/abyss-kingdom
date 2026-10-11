# 道具圖示規格（2026-10-11）

背包、加速視窗用的道具圖示。目前是 `tools/art/make_item_icons.py` 用程式畫的暫代版；要換成正式美術時，**用同樣檔名覆蓋** `client/Assets/Resources/Art/Items/` 裡的檔案就生效（讀取：`UI/ArtLibrary.cs` 的 `ItemIcon`，找不到圖會顯示道具名稱第一個字）。

## 規格

- 256 × 256 PNG，透明背景，主體置中、四邊留約 8% 空白。
- 不要畫格子邊框與品質底色（遊戲會依品質加上綠／藍／紫邊框）；不要放數量或文字（遊戲會疊上）。
- 畫風：和主將頭像一致的手繪 Q 版、粗描邊，配色用介面的暖金＋深淵青綠點綴。
- 給 AI 繪圖的提示詞不可寫作品名或畫家名（GDD 2.5）。

## 檔案清單

| 檔名 | 道具 | 畫面建議 |
| --- | --- | --- |
| `Speedup_General` | 通用加速（1 分～1 小時共用一張） | 沙漏，金色沙＋星形徽記 |
| `Speedup_Building` | 建築加速 | 沙漏，橘色沙＋鐵鎚徽記 |
| `Speedup_Research` | 科研加速 | 沙漏，藍色沙＋燒瓶徽記 |
| `Speedup_Magic` | 魔導加速 | 沙漏，紫色沙＋寶石徽記 |
| `Speedup_Training` | 訓練加速 | 沙漏，紅色沙＋交叉雙劍徽記 |
| `HeroExpSmall` / `Medium` / `Large` | 主將經驗書（小／中／大） | 綠／藍／紫封面的書，封面有深淵之眼 |
| `DomainScrollPower` / `Technique` / `Speed` / `Durability` | 印譜 · 崩／巧／迅／磐 | 卷軸，中央印章寫該字（紅／青綠／黃綠／土褐） |
| `HeroShardAny` | 通用信物 | 六角金色墜飾，中間是深淵契印 |
| `HeroShardHold` / `Vesper` / `Karst` | 各主將信物 | 同款墜飾，中間是該主將頭像 |

新增道具時：在這張表加一列、在 `make_item_icons.py` 加暫代圖（或直接放正式圖）。
