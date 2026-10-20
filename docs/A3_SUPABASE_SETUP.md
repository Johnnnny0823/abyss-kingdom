# A3：在正式 Supabase 建立聯盟與遊玩紀錄的資料表

對應 `docs/PLAYTEST_CHECKLIST.md` 的 A3（也涵蓋 A4 要的 `003`）。大約 10 分鐘。

## 這一步在做什麼

正式伺服器把資料存在 Supabase。目前那裡只有「玩家存檔」一張表（`001`）。聯盟和封測遊玩紀錄需要另外兩張表，沒建的話：

- 聯盟：建立、加入聯盟會存不起來，伺服器重開就不見。
- 遊玩紀錄：朋友玩到哪裡、回饋、錯誤回報都收不到。

兩份 SQL 都是「**不存在才建立**」（`create table if not exists`），重複執行也不會出錯或刪資料，放心跑。

| 檔案 | 建立的表 | 內容 |
|---|---|---|
| `db/002_alliances.sql` | `alliance_directory` | 全部聯盟資料存成一筆 JSON |
| `db/003_playtest_events.sql` | `playtest_events` ＋ 2 個索引 | 每一筆遊玩事件（登入、領任務、回饋、錯誤…） |

兩張表都開啟 **Row Level Security** 並收回 `anon`／`authenticated` 的權限：**只有伺服器（service_role 金鑰）能讀寫**，遊戲客戶端或其他人拿不到。

## 步驟

### 1. 登入並恢復專案（如果被暫停了）

1. 打開 https://supabase.com/dashboard ，登入。
2. 點專案 **Johnnnny0823's Project**（ID 開頭 `bqgpiibomfktlrasuupb`）。
3. 如果畫面顯示 **Project is paused**，按 **Restore project**，等幾分鐘直到狀態變成綠色的 **Active**。

### 2. 執行 `002_alliances.sql`

1. 左側選單點 **SQL Editor**。
2. 點 **New query**（或 `+`）開一個空白查詢。
3. 在 VS Code／記事本打開專案裡的 `db/002_alliances.sql`，**全選複製**，貼到 SQL Editor。
4. 按右下角 **Run**（或 `Ctrl + Enter`）。
5. 下方出現 **Success. No rows returned** 就成功了。

### 3. 執行 `003_playtest_events.sql`

同上：再開一個 **New query**，貼上 `db/003_playtest_events.sql` 的全部內容，按 **Run**，看到 **Success**。

### 4. 檢查表有沒有建好

在 SQL Editor 開新查詢，貼上這段，按 **Run**：

```sql
select tablename, rowsecurity
from pg_tables
where schemaname = 'public'
  and tablename in ('players', 'alliance_directory', 'playtest_events')
order by tablename;
```

應該看到三列，而且 `rowsecurity` 都是 `true`：

| tablename | rowsecurity |
|---|---|
| alliance_directory | true |
| players | true |
| playtest_events | true |

少了哪一張就回到對應的步驟重跑；`rowsecurity` 是 `false` 的話把畫面截圖給 Claude。

再檢查伺服器帳號（`service_role`）有沒有讀寫權限（2026-10-21 補：這個專案關閉了「Automatically expose new tables」，每張新表都要明確授權）：

```sql
select table_name, string_agg(privilege_type, ', ' order by privilege_type) as privileges
from information_schema.role_table_grants
where grantee = 'service_role'
  and table_schema = 'public'
  and table_name in ('players', 'alliance_directory', 'playtest_events')
group by table_name
order by table_name;
```

三張表都要出現，而且 `privileges` 至少包含 `DELETE, INSERT, SELECT, UPDATE`。少了的話執行：

```sql
grant select, insert, update, delete on public.alliance_directory to service_role;
grant select, insert, update, delete on public.playtest_events to service_role;
```

> SQL 練習：`information_schema` 是 SQL 標準的系統目錄；`group by` 把同一張表的多筆權限合成一列，`string_agg` 把它們串成一個字串。

> 這段查詢本身也是很好的 SQL 練習：`pg_tables` 是 PostgreSQL 內建的「系統目錄」，記錄所有資料表；`where … in (…)` 篩選、`order by` 排序。

### 5. 跟 Claude 說「A3 跑完了」

接下來由 Claude 驗證「伺服器真的寫得進去、重開後資料還在」：

- 在你電腦上用正式 Supabase 開一台**本機**伺服器，用專用測試帳號 `dev-a3check` 建一個測試聯盟、產生幾筆遊玩紀錄。
- 重開伺服器，確認聯盟和紀錄都還在。
- 驗證完把測試資料刪掉（**會先問過你再刪**）。

## 常見問題

- **找不到 SQL Editor**：在左側選單，圖示像 `>_`。
- **出現 `permission denied`**：確認你是用專案擁有者的帳號登入，而且是在 SQL Editor 執行（不是在 Table Editor）。
- **出現 `relation already exists`**：不會發生（SQL 有 `if not exists`）；如果真的出現，代表表已經存在，可以直接做第 4 步檢查。
- **不要**在這裡執行 `drop`、`delete` 開頭的指令；這份說明不需要。
