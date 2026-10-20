-- 每一列 violations 都應為零；ETL 有任一 FAIL 就回滾，不發布半套資料。
WITH checks AS (
    SELECT 'event_count_reconciliation' AS check_name,
        abs((SELECT count(*) FROM staging.raw_events) - (SELECT count(*) FROM dw.fact_event)) AS violations
    UNION ALL SELECT 'event_foreign_keys', count(*) FROM dw.fact_event e
      LEFT JOIN dw.dim_player p USING (player_key) LEFT JOIN dw.dim_date d USING (date_key)
      LEFT JOIN dw.dim_event_kind k USING (kind_key) LEFT JOIN dw.dim_quest q USING (quest_index)
      WHERE p.player_key IS NULL OR d.date_key IS NULL OR k.kind_key IS NULL OR (e.quest_index IS NOT NULL AND q.quest_index IS NULL)
    UNION ALL SELECT 'session_foreign_keys', count(*) FROM dw.fact_session s
      LEFT JOIN dw.dim_player p USING (player_key) LEFT JOIN dw.dim_date d USING (date_key)
      WHERE p.player_key IS NULL OR d.date_key IS NULL
    UNION ALL SELECT 'player_day_foreign_keys', count(*) FROM dw.fact_player_day f
      LEFT JOIN dw.dim_player p USING (player_key) LEFT JOIN dw.dim_date d USING (date_key)
      WHERE p.player_key IS NULL OR d.date_key IS NULL
    UNION ALL SELECT 'nonnegative_durations', count(*) FROM dw.fact_session WHERE play_seconds < 0 OR ended_at < started_at
    UNION ALL SELECT 'session_no_overlap', count(*) FROM (
        SELECT started_at, lag(ended_at) OVER (PARTITION BY player_key ORDER BY started_at, session_id) AS previous_end
        FROM dw.fact_session) s WHERE started_at < previous_end
    UNION ALL SELECT 'start_session_reconciliation', abs(
        (SELECT count(*) FROM staging.raw_events WHERE kind = 'session.start') - (SELECT count(*) FROM dw.fact_session))
    UNION ALL SELECT 'daily_seconds_reconciliation', abs(
        (SELECT COALESCE(sum(play_seconds), 0) FROM dw.fact_session) -
        (SELECT COALESCE(sum(active_seconds), 0) FROM dw.fact_player_day))
    UNION ALL SELECT 'quest_claim_reconciliation', abs(
        (SELECT count(*) FROM staging.raw_events WHERE kind = 'quest.claim') -
        (SELECT count(*) FROM dw.fact_event WHERE quest_index IS NOT NULL))
    UNION ALL SELECT 'quest_sequence_no_gaps', count(*) FROM (
        SELECT player_key, count(DISTINCT quest_index) AS claims, max(quest_index) AS highest
        FROM dw.fact_event WHERE quest_index IS NOT NULL GROUP BY player_key) q WHERE claims <> highest
    UNION ALL SELECT 'retention_bounds', count(*) FROM mart.retention WHERE
        d1_retention NOT BETWEEN 0 AND 1 OR d3_retention NOT BETWEEN 0 AND 1 OR d7_retention NOT BETWEEN 0 AND 1
)
SELECT check_name, violations, CASE WHEN violations = 0 THEN 'PASS' ELSE 'FAIL' END AS result FROM checks ORDER BY check_name;
