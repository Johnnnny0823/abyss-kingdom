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
