-- 僅供 verify.ps1 的獨立 fixture 資料庫：驗證一般資料品質檢查之外的語意邊界。
SELECT 'fractional_midnight_split' AS check_name,
       CASE WHEN (SELECT sum(active_seconds) FROM dw.fact_player_day f JOIN dw.dim_player p USING (player_key)
                  WHERE p.player_id = 'fixture-p1') = 12 THEN 'PASS' ELSE 'FAIL' END AS result
UNION ALL SELECT 'missing_end_is_marked', CASE WHEN
    (SELECT end_inferred AND play_seconds = 11 FROM dw.fact_session WHERE session_id = 's2') THEN 'PASS' ELSE 'FAIL' END
UNION ALL SELECT 'same_timestamp_sessions_do_not_steal_claims', CASE WHEN
    (SELECT quests_claimed = 1 FROM dw.fact_session WHERE session_id = 's1') AND
    (SELECT quests_claimed = 1 FROM dw.fact_session WHERE session_id = 's2') THEN 'PASS' ELSE 'FAIL' END
UNION ALL SELECT 'five_minutes_belongs_to_next_bucket', CASE WHEN
    (SELECT sessions FROM mart.session_length_buckets WHERE minutes = '5-15') = 1 THEN 'PASS' ELSE 'FAIL' END
UNION ALL SELECT 'd1_uses_observed_denominator', CASE WHEN
    (SELECT d1_eligible_players = 2 AND d1_retained_players = 1 AND d1_retention = 0.5
     FROM mart.retention WHERE cohort_date = DATE '2026-10-01') THEN 'PASS' ELSE 'FAIL' END
UNION ALL SELECT 'unobserved_d7_is_null', CASE WHEN
    (SELECT bool_and(d7_eligible_players = 0 AND d7_retention IS NULL) FROM mart.retention) THEN 'PASS' ELSE 'FAIL' END
UNION ALL SELECT 'later_cohort_d1_is_not_yet_observed', CASE WHEN
    (SELECT d1_eligible_players = 0 AND d1_retention IS NULL FROM mart.retention WHERE cohort_date = DATE '2026-10-02')
    THEN 'PASS' ELSE 'FAIL' END
UNION ALL SELECT 'zero_duration_missing_end', CASE WHEN
    (SELECT end_inferred AND play_seconds = 0 FROM dw.fact_session WHERE session_id = 's4') THEN 'PASS' ELSE 'FAIL' END;
