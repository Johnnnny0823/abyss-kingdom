# Codex 派工：封測數據分析（ETL → 資料倉儲 → SQL 分析）第一批

基準 commit：`c9ab9dc`。**期限：2026-10-26**（使用者 10/30 要交實習表單，這是履歷作品）。

## 背景與目標

使用者要應徵的實習需要：SQL、資料倉儲（DW）、ETL、BI。遊戲已有封測事件紀錄（`server/.../Game/PlaytestTelemetry.cs`、`db/003_playtest_events.sql`、GM `/admin/playtest/*`）。這批要把它做成一條**完整、可重跑、看得懂的數據管線**：

```
模擬玩家（AiPlaytest）→ 原始事件 → ETL（C#）→ PostgreSQL 星狀資料倉儲 → SQL 分析視圖 → Power BI（使用者自己做）
```

**這是給面試官看的作品**：程式要清楚好讀、每一步都能解釋；寧可簡單正確，不要複雜炫技。SQL 用標準寫法（PostgreSQL），避免冷門語法。

## 1. 模擬玩家資料（`tools/AiPlaytest` 擴充）

- 新增 `--population <N> --days <D>`（預設 300 人、7 天），用既有的隔離 Dev 伺服器與手動時鐘，讓 N 位玩家以**真實協定**遊玩。
- 玩家要有**不同的流失行為**才有分析價值：每位玩家依角色（照任務走／勤勞／隨便點／離線回來）加上隨機參數決定每次遊玩多久、在第幾個任務附近可能放棄、隔天回來的機率。參數與隨機種子寫在輸出的 `simulation.json`，**固定種子可重現**。
- 第一天分散在一週內不同時間開始（才有不同的同期群）。
- 輸出：伺服器實際寫進遊玩紀錄的事件（不是工具自己編的），匯出成 `analytics/data/raw_events.jsonl`（一行一筆，欄位同 `playtest_events`）。資料檔不進 git，加入 `.gitignore`；只提交產生方式。
- README 與報告一律標明「模擬資料」。

## 2. 分析資料庫

- `analytics/docker-compose.yml`：一個 PostgreSQL 16 容器（只綁 `127.0.0.1`），帳密從環境變數讀，不寫進檔案。
- 三個 schema：`staging`（原始資料）、`dw`（星狀模型）、`mart`（給 BI 的分析視圖）。

### 資料表（請照這個結構，SQL 練習題會用到）

```
staging.raw_events(event_id text PK, player_id text, kind text, at_ms bigint, data jsonb, loaded_at timestamptz)

dw.dim_player(player_key serial PK, player_id text UNIQUE, display_name text, persona text, first_seen_at timestamptz, cohort_date date)
dw.dim_date(date_key int PK /* yyyymmdd */, full_date date, day_of_week smallint, is_weekend boolean)
dw.dim_quest(quest_index int PK, quest_title text, goal text)        -- 第一章 15 個任務，資料來自 shared Quests
dw.dim_event_kind(kind_key serial PK, kind text UNIQUE, category text) -- category: session / progress / combat / social / feedback

dw.fact_event(event_id text PK, player_key int FK, date_key int FK, kind_key int FK, occurred_at timestamptz,
              quest_index int NULL FK, building text NULL, target_level int NULL)
dw.fact_session(session_id text PK, player_key int FK, date_key int FK, started_at timestamptz, ended_at timestamptz,
                play_seconds int, quests_claimed int)
dw.fact_player_day(player_key int, date_key int, active_seconds int, sessions int, quests_claimed int, max_quest_index int,
                   PRIMARY KEY (player_key, date_key))
```

- 時間一律轉成 `timestamptz`（UTC）；`dim_date` 用台灣時間（`Asia/Taipei`）切日。
- 每個外鍵、常用篩選欄位建索引。

## 3. ETL（C#，`tools/PlaytestEtl`）

- 可以用 NuGet（`Npgsql`）；**測試專案仍不用 NuGet**。
- 三步，各自是獨立方法、log 印出每步筆數：
  1. **Extract**：讀 `raw_events.jsonl`（之後也要能改成呼叫 GM `/admin/playtest/events`，先留介面）。
  2. **Load staging**：批次寫入 `staging.raw_events`，重跑不重複（`ON CONFLICT DO NOTHING`）。
  3. **Transform**：執行 `analytics/sql/` 裡的 SQL，把 staging 轉成維度與事實表。**轉換邏輯寫在 SQL 檔，不寫在 C#**，面試時可以直接打開 SQL 解釋。
- **可重跑（idempotent）**：同一份資料跑兩次，結果筆數相同。
- `fact_session` 用**視窗函數**從 `session.start`／`session.end` 配對；沒有 end 的 session 以最後一筆事件時間收尾並標記。

## 4. SQL 檔（`analytics/sql/`，編號照執行順序）

- `01_schemas.sql`、`02_tables.sql`、`03_transform_dims.sql`、`04_transform_facts.sql`、`05_marts.sql`。
- `05_marts.sql` 的分析視圖（給 Power BI）：
  - `mart.quest_funnel`：每個任務有幾位玩家達成、相對前一個任務的留存率（找出流失最多的任務）。
  - `mart.retention`：依同期群（cohort_date）算 D1、D3、D7 留存。
  - `mart.daily_activity`：每日活躍人數（DAU）、平均遊玩時間、新玩家數。
  - `mart.session_length_buckets`：遊玩時長分布（0–5、5–15、15–30、30–60、60+ 分鐘）。
  - `mart.persona_summary`：各角色的平均遊玩時間、第一章完成率（標明是模擬角色）。
- 每個 SQL 檔開頭用中文註解說明這一步在做什麼、為什麼這樣設計（面試會被問）。

## 5. 驗證

- 加資料品質檢查 `analytics/sql/90_checks.sql`：事實表外鍵都對得到維度、沒有負的遊玩秒數、session 不重疊、筆數對帳（staging 事件數 = fact_event 筆數）。ETL 最後執行並輸出 PASS／FAIL。
- 一般測試照常全部 PASS（`dotnet run --project tests/AbyssKingdom.Tests`）；新增的 C# 邏輯（例如 JSONL 解析）用既有迷你框架加測試。
- 全流程從零跑一次：`docker compose up` → 產生模擬資料 → ETL → 檢查 PASS，把指令寫成 `analytics/README.md`，並附上每個 mart 視圖前 10 列的實際輸出。

## 不要做

- 不要做 Power BI 檔案（使用者自己做，面試要能講）；但在 `analytics/README.md` 留一段「Power BI 連線步驟」：用 PostgreSQL 連接器連到 localhost、匯入 `mart` 視圖。
- 不要連正式 Supabase；不要改遊戲規則與數值。
- 不 commit、不 push；交付說明寫在本文件末尾，交給 Claude 驗收。


## 交付紀錄（2026-10-20，Codex；待 Claude 驗收／提交）

第一批已完成，早於 2026-10-26 期限。全程未 commit、push、部署或連正式 Supabase；只用隔離 Dev／InMemory、模擬帳號及 loopback PostgreSQL。接手時實際 HEAD 為 `10bcdf6`（派工文件原列基準 `c9ab9dc`）。

### 已交付

- `tools/AiPlaytest/PopulationSimulation.cs`：新增 `--simulate`、`--population N --days D --seed S`，預設 300 人／首日後追蹤七日；七天分批入組，角色回訪、時長與任務附近的永久流失皆有不同參數。完整計畫、種子與參數存入不進 git 的 `analytics/data/simulation.json`。
- 仍透過真實 WebSocket 意圖遊玩；工具只接管隔離主機時鐘與伺服器 tick 排程，不直接修改城市。從實際 InMemoryPlaytestStore 匯出 snake_case `raw_events.jsonl`，不是自行編造主資料集事件。
- WorldService 新增可注入的選址 Random／戰鬥種子委派，僅供工具固定種子。正式預設保持原選址亂數與 RandomNumberGenerator；沒有改兵種、主將、任務、經濟、戰鬥公式或數值。
- `analytics/docker-compose.yml`：PostgreSQL 16，僅綁 `127.0.0.1:54329`，帳密完全取環境變數。獨立 compose project／volume，不使用遊戲資料庫。
- `tools/PlaytestEtl/`：Npgsql 10.0.3（packages.lock.json 已產生）；獨立 Extract、LoadStaging、Transform 方法，印每步筆數。JSONL 來源介面可供日後 GM HTTP 實作。轉換全部放 SQL；載入／轉換／品質檢查同一交易，失敗整批回滾。
- SQL `01`～`05`：三個 schema、指定星狀表、外鍵與索引、五個 mart。任務目錄來自 shared Quests 與現有 DisplayNames，不另手寫十五個任務內容。
- `90_checks.sql`：十一項資料品質；`91_fixture_checks.sql`＋`verify.ps1`：獨立人工 fixture 資料庫的八項語意邊界，再驗證一項失敗交易回滾，共九項；`92_snapshot.sql`：八表筆數與完整內容雜湊。
- `analytics/README.md`：從零指令、來源／指標定義、DW 粒度、重跑限制、Power BI 連線步驟與五個 mart 前十列的真實查詢輸出。沒有建立 pbix，報表留給使用者製作。

### 實際驗證證據

| 項目 | 結果 | 證據 |
| --- | --- | --- |
| 一般測試 | 287 passed，0 failed（新增 4 個 C# 測試） | artifacts/analytics-tests.log |
| ETL 編譯 | 0 warnings，0 errors | artifacts/analytics-etl-build.log |
| 完整人口模擬 | 300 人、1,757 次遊玩、18,681 筆實際事件；30,988 次意圖，836 次預期拒絕 | artifacts/analytics-simulation.log、analytics/data/simulation.json |
| 固定種子重現 | 8 人與 300 人各重跑一次；業務欄位／時間差異均 0 | artifacts/analytics-reproduction.txt、analytics-reproduction300.txt |
| 兩次 ETL | 第二次 inserted=0；八張表筆數與整列內容雜湊相同 | artifacts/analytics-etl-first.log、analytics-etl-repeat.log、analytics-before.txt、analytics-after.txt |
| 品質 | 十一項全部 PASS，raw_events 與 fact_event 均 18,681 | 兩份 ETL log |
| SQL 語意／回滾 | 九項 PASS；故意跳任務的載入 FAIL，檢查 staging 沒殘留該筆 | artifacts/analytics-sql-tests.log、analytics/data/regression/expected-failure.log |
| 連線隔離 | 遠端主機在建立連線前即被拒絕；Docker PortBindings 僅 127.0.0.1 | artifacts/analytics-remote-guard.log、docker inspect 的唯讀檢查 |

主倉儲列數：dim_player 300、dim_date 15、dim_quest 15、dim_event_kind 8、fact_event 18,681、fact_session 1,757、fact_player_day 1,459。主資料集 end_inferred=0。日期維度多一天供午夜結束事件對應；mart.daily_activity 僅列觀察窗十四天，單獨 session.end 不算次日活躍。

語意邊界包含：毫秒跨午夜秒數對帳、缺少 end 標記、同毫秒 session 不串接任務、恰好五分鐘分到第二桶、D1 完整觀察分母、未觀察 D7 為 NULL、晚入組 D1 尚未成熟、零秒未結束 session。

### 本次模擬資料的分析示範

- 第一章領完：187／300（62.33%）。四種角色各 75 人，完成數 diligent 54、offline 55、quest 52、random 26。
- 最多流失人數在任務 #6 採集：275 →237，少 38 人，相鄰步驟留存 86.18%。
- 全體 D1 當日回訪 174／300＝58%；D7 157／300≈52.33%。這些是人工行為參數造成的模擬輸出，不可當作真實營運數據或因果證據。
- 真實前十列在 README 末尾，以及不進 git 的 analytics/data/mart-samples.md。

### 重要定義與設計取捨

- `days=7` 包含 D0 及其後 D1～D7；首日分散一週，因此預設觀察十四個日曆日。觀察截止時間由 manifest 明確提供，沒有夠長觀察期的 cohort 不填假 0% 留存。
- 固定種子重現遊玩計畫、事件時間與業務彙總；伺服器產生的 event/session GUID 不會相同。ETL 可重跑性指同一份匯出檔重載；重新模擬後的新 GUID 不視為同一批原始事件。
- quest_index 維度為 1～15 任務編號；fact_event 只從 quest.claim 的 questNumber 填入，避免把城市的 completed-count 誤當當前任務。漏斗統計已領獎者，不推測達標但未領取者。
- session 用視窗函數分段、同 sessionId 配對，缺 end 用區段最後事件收尾並標記 end_inferred。跨午夜使用累計秒數差分，不把離線時段當遊玩。
- 作品規模採全量重算事實表，維度 Type 1 upsert。兩萬筆容易說明與驗收；之後才按受影響玩家／日期增量重算。
- raw JSONL 來源沒有 manifest 時以最後事件當保守觀察截止；部分歷史若缺任務領獎，品質跳號檢查會失敗，需先補齊來源。
- 主模擬沒有產生聯盟或回饋互動，因此對應類別未必有資料；沒有偽造社交／回饋事件來填表。

### 檔案清單

既有修改：`.gitignore`、`tools/AiPlaytest/Program.cs`、`tools/AiPlaytest/SocketPlayer.cs`、`server/AbyssKingdom.Server/Game/WorldService.cs`、`tests/AbyssKingdom.Tests/AbyssKingdom.Tests.csproj`、本文件。
新增：`tools/AiPlaytest/PopulationSimulation.cs`；`tools/PlaytestEtl/{PlaytestEtl.csproj,packages.lock.json,Program.cs,RawEventSource.cs,EtlPipeline.cs}`；`tests/AbyssKingdom.Tests/AnalyticsTests.cs`；`analytics/{README.md,docker-compose.yml,verify.ps1}`；`analytics/sql/{01_schemas,02_tables,03_transform_dims,04_transform_facts,05_marts,90_checks,91_fixture_checks,92_snapshot}.sql`。
原有 `analytics/exercises/SQL_PRACTICE.md` 沒有更改。`analytics/data/` 已驗證 git check-ignore 命中；沒有將原始資料、manifest、密碼或資料庫加進版本控制。

### 本機環境與交接

- 修改前備份在 `C:/Users/xnikz/.claude/backups/2026-10-20/analytics-1/`。
- Docker 原先因零位元 AF_UNIX 殘留端點啟動失敗。只將僅含該 socket 的執行目錄改名保存為 `C:/Users/xnikz/AppData/Local/docker-secrets-engine.stale-20261020-analytics1/`；沒有重設 Docker 或刪除磁碟／容器／憑證。強制結束啟動程序的請求被自動審核拒絕，未執行；使用者手動退出重開後恢復正常。
- 驗收執行版本：Docker Engine 28.5.1、PostgreSQL 16.15、Npgsql 10.0.3。伺服器模擬工作已結束；本批 PostgreSQL 容器保留運行，供使用者接 Power BI。另有獨立 abyss_analytics_fixture 測試資料庫，不混入主資料集。
- 本機 DB 名稱 abyss_analytics、使用者 analytics_owner、連接埠 54329。隨機密碼僅在容器環境與執行程序環境；重新開終端可用 README 的本機指令載入，不要貼回聊天／報告。
- 下一步由 Claude 檢查改動、按 README 重跑與提交；使用者自行製作標明「模擬資料」的 Power BI 報表。

補充全新資料庫驗收：另建 `abyss_analytics_acceptance_20261020`，首次載入 inserted=18,681，第二次 inserted=0，八表筆數及完整內容雜湊一致。證據 `artifacts/analytics-clean-first.log`、`analytics-clean-repeat.log`、`analytics-clean-before.txt`、`analytics-clean-after.txt`。這個驗收庫也保留供檢查，與 Power BI 用的主庫分開。已驗證匯出資料無任何 tracked 檔案，模擬伺服器 5090 已停止。
