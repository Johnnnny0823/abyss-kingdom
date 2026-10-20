-- 維度以自然鍵合併，代理鍵在重跑時不變；任務索引用 1～15 的任務編號。
INSERT INTO dw.dim_player(player_id, display_name, persona, first_seen_at, cohort_date)
SELECT player_id,
       (array_agg(COALESCE(data->>'displayName', player_id) ORDER BY at_ms, event_id))[1],
       CASE WHEN (array_agg(data->>'displayName' ORDER BY at_ms, event_id))[1] LIKE 'SIM-%'
            THEN split_part((array_agg(data->>'displayName' ORDER BY at_ms, event_id))[1], '-', 2)
            ELSE 'unknown' END,
       to_timestamp(min(at_ms) / 1000.0),
       (to_timestamp(min(at_ms) / 1000.0) AT TIME ZONE 'Asia/Taipei')::date
FROM staging.raw_events GROUP BY player_id
ON CONFLICT (player_id) DO UPDATE SET display_name = EXCLUDED.display_name,
    persona = EXCLUDED.persona, first_seen_at = EXCLUDED.first_seen_at, cohort_date = EXCLUDED.cohort_date;

INSERT INTO dw.dim_date(date_key, full_date, day_of_week, is_weekend)
SELECT to_char(day, 'YYYYMMDD')::int, day::date, extract(isodow FROM day)::smallint,
       extract(isodow FROM day) IN (6, 7)
FROM generate_series(
    (SELECT min(to_timestamp(at_ms / 1000.0) AT TIME ZONE 'Asia/Taipei')::date FROM staging.raw_events)::timestamp,
    (SELECT GREATEST(max(to_timestamp(at_ms / 1000.0) AT TIME ZONE 'Asia/Taipei')::date,
        (SELECT (observation_end_at AT TIME ZONE 'Asia/Taipei')::date - 1 FROM staging.dataset))
     FROM staging.raw_events)::timestamp,
    interval '1 day') AS day
ON CONFLICT (date_key) DO NOTHING;

INSERT INTO dw.dim_quest SELECT * FROM staging.quest_catalog
ON CONFLICT (quest_index) DO UPDATE SET quest_title = EXCLUDED.quest_title, goal = EXCLUDED.goal;

INSERT INTO dw.dim_event_kind(kind, category)
SELECT DISTINCT kind, CASE
    WHEN kind LIKE 'session.%' THEN 'session'
    WHEN kind IN ('first.camp', 'combat.result') THEN 'combat'
    WHEN kind = 'first.alliance' OR kind LIKE 'alliance.%' THEN 'social'
    WHEN kind IN ('feedback', 'client.error') THEN 'feedback'
    ELSE 'progress' END
FROM staging.raw_events ORDER BY kind
ON CONFLICT (kind) DO UPDATE SET category = EXCLUDED.category;
