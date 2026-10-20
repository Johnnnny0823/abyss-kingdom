-- 唯讀快照：不只比筆數，也比所有欄位內容，確認同一來源重跑後資料不變。
SELECT 'dim_player' AS relation, count(*) AS rows, md5(string_agg(row_to_json(t)::text, '' ORDER BY player_key)) AS content_hash FROM dw.dim_player t
UNION ALL SELECT 'dim_date', count(*), md5(string_agg(row_to_json(t)::text, '' ORDER BY date_key)) FROM dw.dim_date t
UNION ALL SELECT 'dim_quest', count(*), md5(string_agg(row_to_json(t)::text, '' ORDER BY quest_index)) FROM dw.dim_quest t
UNION ALL SELECT 'dim_event_kind', count(*), md5(string_agg(row_to_json(t)::text, '' ORDER BY kind_key)) FROM dw.dim_event_kind t
UNION ALL SELECT 'fact_event', count(*), md5(string_agg(row_to_json(t)::text, '' ORDER BY event_id)) FROM dw.fact_event t
UNION ALL SELECT 'fact_session', count(*), md5(string_agg(row_to_json(t)::text, '' ORDER BY session_id)) FROM dw.fact_session t
UNION ALL SELECT 'fact_player_day', count(*), md5(string_agg(row_to_json(t)::text, '' ORDER BY player_key, date_key)) FROM dw.fact_player_day t
UNION ALL SELECT 'raw_events', count(*), md5(string_agg(row_to_json(t)::text, '' ORDER BY event_id)) FROM staging.raw_events t
ORDER BY relation;
