-- BI 只讀這些視圖。比例為 0～1，Power BI 將格式設為百分比；模擬資料不代表真實營運。
-- 漏斗「達成」採伺服器驗證後的 quest.claim，不推測已滿足條件但未領獎者。
CREATE OR REPLACE VIEW mart.quest_funnel AS
WITH reached AS (
    SELECT q.quest_index, q.quest_title, count(DISTINCT e.player_key) AS players_reached
    FROM dw.dim_quest q LEFT JOIN dw.fact_event e USING (quest_index)
    GROUP BY q.quest_index, q.quest_title
), previous AS (
    SELECT *, lag(players_reached, 1, (SELECT count(*) FROM dw.dim_player)) OVER (ORDER BY quest_index) AS previous_players
    FROM reached
)
SELECT *, round(players_reached::numeric / NULLIF(previous_players, 0), 4) AS step_retention,
       previous_players - players_reached AS players_lost,
       (SELECT is_simulated FROM staging.dataset) AS is_simulated
FROM previous;

-- 精確日曆日 D1／D3／D7；只有完整觀察到該日結束的人才進分母。
-- 不足觀察窗回傳 NULL，不把它誤算成 0% 留存。
CREATE OR REPLACE VIEW mart.retention AS
WITH horizons(day_number) AS (VALUES (1), (3), (7)), eligible AS (
    SELECT p.*, h.day_number,
        ((p.cohort_date + h.day_number + 1)::timestamp AT TIME ZONE 'Asia/Taipei') <= m.observation_end_at AS observed,
        EXISTS (SELECT 1 FROM dw.fact_player_day f JOIN dw.dim_date d USING (date_key)
                WHERE f.player_key = p.player_key AND d.full_date = p.cohort_date + h.day_number) AS returned
    FROM dw.dim_player p CROSS JOIN horizons h CROSS JOIN staging.dataset m
), rates AS (
    SELECT cohort_date, day_number, count(*) AS cohort_players,
           count(*) FILTER (WHERE observed) AS eligible_players,
           count(*) FILTER (WHERE observed AND returned) AS retained_players,
           round(count(*) FILTER (WHERE observed AND returned)::numeric /
                 NULLIF(count(*) FILTER (WHERE observed), 0), 4) AS rate
    FROM eligible GROUP BY cohort_date, day_number
)
SELECT cohort_date, max(cohort_players) AS cohort_players,
       max(eligible_players) FILTER (WHERE day_number = 1) AS d1_eligible_players,
       max(retained_players) FILTER (WHERE day_number = 1) AS d1_retained_players,
       max(rate) FILTER (WHERE day_number = 1) AS d1_retention,
       max(eligible_players) FILTER (WHERE day_number = 3) AS d3_eligible_players,
       max(retained_players) FILTER (WHERE day_number = 3) AS d3_retained_players,
       max(rate) FILTER (WHERE day_number = 3) AS d3_retention,
       max(eligible_players) FILTER (WHERE day_number = 7) AS d7_eligible_players,
       max(retained_players) FILTER (WHERE day_number = 7) AS d7_retained_players,
       max(rate) FILTER (WHERE day_number = 7) AS d7_retention,
       (SELECT is_simulated FROM staging.dataset) AS is_simulated
FROM rates GROUP BY cohort_date;

CREATE OR REPLACE VIEW mart.daily_activity AS
SELECT d.full_date, count(f.player_key) AS dau,
       round(COALESCE(avg(f.active_seconds), 0), 2) AS avg_play_seconds,
       (SELECT count(*) FROM dw.dim_player p WHERE p.cohort_date = d.full_date) AS new_players,
       COALESCE(sum(f.active_seconds), 0) AS total_active_seconds,
       (SELECT is_simulated FROM staging.dataset) AS is_simulated
FROM dw.dim_date d LEFT JOIN dw.fact_player_day f USING (date_key)
WHERE d.full_date::timestamp AT TIME ZONE 'Asia/Taipei' < (SELECT observation_end_at FROM staging.dataset)
GROUP BY d.full_date;

CREATE OR REPLACE VIEW mart.session_length_buckets AS
WITH buckets(bucket_order, bucket, lower_seconds, upper_seconds) AS (
    VALUES (1, '0-5', 0, 300), (2, '5-15', 300, 900), (3, '15-30', 900, 1800),
           (4, '30-60', 1800, 3600), (5, '60+', 3600, NULL)
)
SELECT b.bucket_order, b.bucket AS minutes, count(s.session_id) AS sessions,
       round(count(s.session_id)::numeric / NULLIF((SELECT count(*) FROM dw.fact_session), 0), 4) AS share,
       (SELECT is_simulated FROM staging.dataset) AS is_simulated
FROM buckets b LEFT JOIN dw.fact_session s ON s.play_seconds >= b.lower_seconds AND
    (b.upper_seconds IS NULL OR s.play_seconds < b.upper_seconds)
GROUP BY b.bucket_order, b.bucket;

CREATE OR REPLACE VIEW mart.persona_summary AS
WITH per_player AS (
    SELECT p.player_key, p.persona, COALESCE(sum(f.active_seconds), 0) AS play_seconds,
           COALESCE(max(f.max_quest_index), 0) >= (SELECT max(quest_index) FROM dw.dim_quest) AS completed
    FROM dw.dim_player p LEFT JOIN dw.fact_player_day f USING (player_key)
    GROUP BY p.player_key, p.persona
)
SELECT persona, count(*) AS players, round(avg(play_seconds), 2) AS avg_play_seconds,
       count(*) FILTER (WHERE completed) AS chapter_completed_players,
       round(count(*) FILTER (WHERE completed)::numeric / count(*), 4) AS chapter_completion_rate,
       (SELECT is_simulated FROM staging.dataset) AS is_simulated
FROM per_player GROUP BY persona;
