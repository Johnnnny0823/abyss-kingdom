# AI 試玩報告（2026-10-13）

## 摘要

只用loopback Dev／InMemory、dev-aiplay-*帳號及玩家協定；GM只有GET，沒有給資源／改等級。未修正本報告中的遊戲問題。AI可以找故障、卡住與不清楚的地方，**不能代替真人判斷好不好玩**；使用者A5與朋友封測仍需進行。

| 角色 | 範圍 | 第一章完成 | 正式採用的結果 |
|---|---|---|---|
| 照任務走 | 60分 | 未完成，停#8 | 主堡2、掘根場/菌圃2、層岩坑/晶礦/倉庫/兵營1、醫館0；兵力0、傷兵35 |
| 勤勞 | 60分 | 21.5分 | 八棟5級；採集期間仍持續建築的修正版，127次操作 |
| 隨便點 | 60分 | 21.5分 | 主堡/層岩坑/菌圃/倉庫/醫館5；掘根場/晶礦/兵營4；正確拒絕三種錯誤 |
| 離線回來 | 15分→8小時離線→15分 | 21.5活躍分鐘 | 八棟4級；104次操作，GM計1800秒活躍、不計8小時離線 |

真客戶端嚴格路線同樣停#8；另外透過既有介面合法補兵／培養的路線完成#1～15。兩條路線分開，不將補兵結果冒稱嚴格任務路線成功。

## 方法與限制

- 新增獨立tools/AiPlaytest，保留NewbieSim。工具ManualClock只准Dev/InMemory；正式GameServer維持SystemClock，沒有新增正式改時鐘設定／端點，已加測試。
- 協定每30秒快轉並等待真server tick；UI主機10倍時間。UI呼叫既有導航／升級／遠征／回饋處理器，其他操作送公開玩家意圖，不聲稱Windows實體滑鼠／鍵盤自動化。
- 每種是一次完整樣本，Battle種子由真server產生，不代表全面平衡。初版勤勞漏做採集等待期間的建築，已修正**試玩腳本**並補測／補跑，不能將原本idleTurns當遊戲必須空等的比例。
- 初批開場遮罩照片已排除。初版背包圖示檢查因選取會重建格子，舊元素失效，該組結果亦排除，補查實際新格子；不能誤列成遊戲bug。

## 問題（依嚴重度排序）

### 🔴 卡關／壞檔

未觀察伺服器未處理例外或壞檔。第8任務屬引導路線停滯，玩家可離開任務指示去補兵，並非遊戲完全不可繼續。

### 🟠 1. 建築成本圖示沒有說明

重現：選醫館／任意建築卡，點成本列根材、層岩、菌糧圖示，不出現名稱／用途／取得方式。HUD資源列有說明，成本列沒有，違反既定圖示規則。證據：ai-playtest-ui-helper/cost-icon-click.png與icon-audit.jsonl（cost.modalOpened=false）；UiKit.cs:39、GameUI.cs:965～970只有建立圖示，沒有成本列回呼。可能原因：只有HUD外層綁OpenResourceInfo。未修。

### 🟡 2. 任務#8輸掉第一戰後沒有恢復導航

重現：按#1～7，訓練50名磐盾衛、採集、建倉庫，霍德帶50兵打最近Lv.1營地。本次15陣亡、35傷兵，醫館未建；重按前往仍只去營地，沒有補兵／醫館／裝備的路線。證據：ai-playtest/quest-timeline.json，4.5分主城(623,643)→營地(650,620)，Shield50；quest-summary.json；ai-playtest-ui/notices.jsonl與final-city.json，真UI同樣重現。GameUI.cs:599～612只選營地，醫館任務在CampsWon之後。建議另派工處理輸後補兵／醫館捷徑，不自行改戰鬥數值。

### 🟡 3. 任務前往不聚焦所選建築

重現：先把城內鏡頭移到醫館／右側，再按主堡任務前往；資訊卡變主堡，鏡頭還在原處，主堡可能被HUD遮住。證據：ai-playtest-ui/quest-1.png；GameUI.cs:575～587只改_selBuilding，沒有FocusOn。診斷先看其他建築等同玩家先移動視角。建議沿用既有FocusBuilding，未修。

### 🟡 4. 未建醫館仍顯示治療入口

重現：醫館卡「未建造」卻有黃色治療按鈕，傷兵HUD再引導打開醫館頁，沒有直接建造路線。證據：building-Hospital.png、hospital.png、helper/quest-9.png。不把收容容量100當成數值錯誤，只記未建造與可治療狀態提示不一致。建議直接提示先建醫館並提供導航。

### ⚪ 5. 不存在行軍的錯誤未翻譯

重現：故意送map.recall、march=9223372036854775807；server正確拒絕，UI卻顯示map_rejected：UnknownMarch。證據：ai-playtest-ui/error-toast.png與notices.jsonl。這是錯誤意圖注入，不聲稱一般操作必然觸發；過期行軍／重連是否會撞到屬推測。建議補DisplayNames對照。

### ⚪ 6. 世界建築文字對比／遮擋

白字在淺色地面／霧上不易讀，移動鏡頭後有標籤被HUD遮住；證據building-Quarry.png、building-Sawmill.png。主要面板沒發現文字互疊；主將頁下半部是捲動區，不誤列永久裁切。建議暗底／描邊或避讓HUD。

## UI覆蓋

- 已逐張檢查城內、地圖、主將、遠征、兵營、背包、醫館、聯盟、出征；八張建築卡、四張資源說明、嚴格路線#1～8、補兵路線#9～15。沒有偽造未到達任務。
- 資源說明含名稱／用途／取得方式；八個現有背包圖示已逐一實際選取，各自有正確名稱與說明，見ai-playtest-icon-audit/icon-audit.jsonl及item-0～7.png，已逐張檢查。成本圖示點擊仍沒有說明，已列問題。出征空頁的「這裡不能去」是未選地點的診斷情境，不當正常出征故障。
- B1/B2：真UI發送回饋有收到確認，AI_PLAYTEST_SAMPLE是一筆刻意測試錯誤，helper保存GM實際查詢。未上線、未連正式帳號。

## 節奏

- 嚴格路線前5分鐘21次協定操作，後續因首戰失敗停滯，不是缺資源；最久為未完成#8。
- 修正版勤勞前5分鐘48次、隨機41次，與GDD約40次同量級但口徑不同：這裡計領獎／故意拒絕，未計UI導航、選兵點擊。
- 修正版勤勞採集任務#6仍由2.5到11.5分，隨機由3到13.5分；帶更多兵增加載重與採滿等待，不一定更快完成新手任務。勤勞每5分鐘操作數為48/0/24/7/23/4/6/4/2/4/4/1；5～10分在已嘗試可用建築後仍零操作，為這個策略與資源配置下的空窗，不代表所有真人只能等待。修正版84個半分鐘步沒有新操作，共42分鐘；不等於玩家完全沒有其他玩法可選。
- 任務獎勵與新手經驗書使本輪比不計任務獎勵的NewbieSim更快，不把兩者直接當相同平衡基準。

## 下一步（最多五項，讓使用者選；未實作）

1. 第一戰兵力提示與輸後補兵／醫館恢復路線。
2. 共用資源圖示點擊說明，補成本列。
3. 任務前往聚焦建築，整理未建醫館的建造捷徑。
4. 補中文錯誤與世界名牌對比。
5. 使用者A5與朋友封測，看等待與戰損是否真看得懂，不以AI判斷好不好玩。

## 證據與驗證

- 四角色JSON與可讀Markdown時間軸／GM：artifacts/ai-playtest/；修正版補跑：artifacts/ai-playtest-queues/。GM的random主堡6是稍後全服時間經過8小時，原60分時主堡5、排隊升6，屬不同時點，不是資料錯誤。
- UI：artifacts/ai-playtest-ui/及ai-playtest-ui-helper/，另有log、actions/notices.jsonl與final-city.json。UI任務loop60分不含前置畫面稽核，GM總秒數含全部在線時間，會略多於3600。
- 完整測試279 passed、0 failed（artifacts/ai-playtest-tests.log）；Unity打包成功／0 errors（ai-playtest-build.log第694／709行）。修正版協定補跑、兩種60分鐘UI路線與圖示補查均已完成。所有時間軸快照未發現負資源、負兵力／傷兵或建築等級越界，state-check.json為PASS。
- 本次改動只在tools/AiPlaytest、新Diagnostics/AiPlaytestPolicy.cs／AiPlaytestCheck.cs及meta、測試引用／AiPlaytestTests.cs與本報告，未改既有遊戲bug、數值；無commit／push／部署。工作目錄仍保留上一輪尚未提交的封測紀錄功能，不將它誤算成本輪修改或回滾。
- 最後所有本輪Dev主機及客戶端已停止，沒有連正式資料庫。
- 圖示覆蓋只宣稱新帳號當時持有的八種道具與已顯示的資源成本／HUD；未持有或未實作模組的其他圖示不使用GM造資料，不宣稱全部未來圖示均已驗證。

## 離線前有隊列的補充場景

另用合法city.upgrade在活躍15分開始菌圃2→3，工期235秒，然後離線8小時。登入後菌圃3、upgrade=null、四資源皆20,000（倉庫2級容量），護盾截止值1792478424693保持不變，傷兵0→0；沒有憑空增減。本輪沒有帶未完成治療隊列離線，不能將此結果冒稱完整驗證離線治療邊界。

證據：artifacts/ai-playtest-offline/offline-timeline.json的before offline／offline 8 hours兩個快照與Markdown；GM總在線秒數仍1800。該額外樣本完成第一章的時間與兵損可能受不同Battle種子影響，以主表的修正版樣本作統一比較。

## 補跑與GM對照

- 嚴格／隨機採第一輪ai-playtest；勤勞／離線採ai-playtest-queues。保留第一輪資料不覆寫，避免把原腳本限制說成遊戲限制。
- 對應GM玩家列：quest總3600秒、questIndex7、Keep2；random總3600秒、questIndex15；勤勞修正版總3600秒、questIndex15、Keep5；離線修正版總1800秒、questIndex15、Keep4。每個角色events.json的session、quest.claim與building.upgrade.start可對照各自時間軸。
- UI helper的gm-feedback.json實際有主堡5、任務索引15與當時畫面快照；gm-errors.json為AI_PLAYTEST_SAMPLE一筆、一位玩家。client log的COMPLETE questIndex=15，嚴格UI為7。收到回饋畫面已逐張核對。
- 勤勞在25.5分的最近快照八棟已4級，30分沒有新的建築操作；60分八棟5級，較GDD的NewbieSim「30分約3級」快，原因包含任務獎勵、立即領取與密集操作，不據此直接調數值。
