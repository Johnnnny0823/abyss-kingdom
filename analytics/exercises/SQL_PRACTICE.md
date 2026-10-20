# SQL 練習：用封測數據學 SQL

這些題目全部用本專案的分析資料庫（`analytics/`，PostgreSQL）。每天 30 分鐘、做 3～4 題。
**先自己寫，跑過、對結果有把握再看解答**（`SQL_ANSWERS.md`）。面試時要能說出「為什麼這樣寫」。

資料表速查（完整說明見 `docs/HANDOFF-ANALYTICS-1.md`）：

| 表 | 一列代表什麼 |
|---|---|
| `dw.dim_player` | 一位玩家（`player_key`、`player_id`、`display_name`、`persona`、`first_seen_at`、`cohort_date`） |
| `dw.dim_date` | 一天（`date_key` 例如 20261021、`full_date`、`day_of_week`、`is_weekend`） |
| `dw.dim_quest` | 第一章的一個任務（`quest_index`、`quest_title`、`goal`） |
| `dw.dim_event_kind` | 一種事件（`kind_key`、`kind`、`category`） |
| `dw.fact_event` | 發生過的一件事（誰、哪天、什麼事件、什麼時間、任務／建築） |
| `dw.fact_session` | 一次遊玩（開始、結束、秒數、這次領了幾個任務） |
| `dw.fact_player_day` | 某位玩家某一天的摘要（活躍秒數、遊玩次數、領任務數、最高任務） |

---

## 第 1 天：SELECT、WHERE、ORDER BY、LIMIT

1. 列出所有玩家的 `display_name` 和 `first_seen_at`，依第一次上線時間由早到晚排序。
2. 找出 `persona` 是「勤勞」（看資料裡實際的值）的玩家，只列前 10 位。
3. 列出 `fact_session` 中遊玩超過 30 分鐘（1,800 秒）的紀錄，依秒數由大到小。
4. 哪些事件種類（`dim_event_kind`）的 `category` 是 `progress`？

## 第 2 天：彙總函數、GROUP BY、HAVING

5. 一共有幾位玩家？平均每位玩家遊玩幾次（`fact_session`）？
6. 每種事件 `kind` 各發生幾次？由多到少排序。（提示：需要 `JOIN` 到 `dim_event_kind`，可以先跳過 JOIN、只用 `kind_key` 分組）
7. 每位玩家的總遊玩秒數，只列出總時數超過 1 小時的玩家。（`GROUP BY` + `HAVING`）
8. `WHERE` 和 `HAVING` 差在哪？用第 7 題說明。（寫在註解裡）

## 第 3 天：JOIN

9. 列出每次遊玩的「玩家名稱、日期、遊玩分鐘數」。（`fact_session` JOIN `dim_player` JOIN `dim_date`）
10. 每個任務（`dim_quest.quest_title`）被領取了幾次？沒有人領過的任務也要列出來、次數顯示 0。（提示：哪一種 JOIN？）
11. 找出「從來沒有領過任何任務」的玩家。（提示：`LEFT JOIN … WHERE … IS NULL`，或 `NOT EXISTS`）
12. `INNER JOIN` 和 `LEFT JOIN` 的差別是什麼？第 10、11 題為什麼要用 `LEFT JOIN`？

## 第 4 天：子查詢、CTE（WITH）

13. 遊玩總時數高於「所有玩家平均」的玩家有哪些？（子查詢）
14. 用 `WITH` 改寫第 13 題，比較哪個好讀。
15. 每位玩家「最高達到的任務」是第幾個？再統計：停在每個任務的玩家各有幾位（這就是流失點）。

## 第 5 天：視窗函數（面試加分題）

16. 每位玩家的每次遊玩，依時間編號（第 1 次、第 2 次…）。（`ROW_NUMBER() OVER (PARTITION BY … ORDER BY …)`）
17. 每次遊玩和「同一位玩家上一次遊玩」相隔幾小時？（`LAG`）
18. 每日活躍人數（DAU）的 3 日移動平均。（`AVG(...) OVER (ORDER BY … ROWS BETWEEN 2 PRECEDING AND CURRENT ROW)`）
19. 依總遊玩時間替玩家排名，同分同名次。（`RANK` 和 `DENSE_RANK` 差在哪？）

## 第 6 天：留存與漏斗（把前面全部用上）

20. **次日留存（D1）**：第一天上線的玩家中，隔天也有上線的比例，依同期群（`cohort_date`）分組。
21. **任務漏斗**：每個任務達成的人數，以及相對上一個任務的留存率；哪一個任務流失最多？
22. 對照你寫的 20、21 題和 `analytics/sql/05_marts.sql` 裡的 `mart.retention`、`mart.quest_funnel`，有哪裡寫法不同？哪個比較好？

## 第 7 天：資料庫觀念（面試口答）

23. 主鍵（PK）、外鍵（FK）、索引各是做什麼的？`fact_event` 上為什麼要對 `player_key` 建索引？
24. 什麼是正規化？遊戲伺服器把整座城存成一筆 `jsonb`，是不是違反正規化？為什麼還是這樣設計？分析時又怎麼處理？
25. 資料倉儲為什麼用「事實表＋維度表」的星狀結構，而不是直接查原始事件？
26. ETL 的 E、T、L 各是什麼？這個專案的 ETL 為什麼要「可以重跑」（idempotent）？用了什麼語法做到？
27. 交易（Transaction）是什麼？ACID 四個字母各代表什麼？
