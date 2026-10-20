-- 小型作品採完整重算事實表，避免晚到事件讓 session 與逐日累計失真。
-- 在 ETL 同一交易中執行；失敗全部回滾，維度代理鍵不重建。
TRUNCATE dw.fact_player_day, dw.fact_session, dw.fact_event;
INSERT INTO dw.fact_event
SELECT r.event_id, p.player_key, d.date_key, k.kind_key, to_timestamp(r.at_ms / 1000.0),
       CASE WHEN r.kind = 'quest.claim' THEN (r.data->>'questNumber')::int END,
       r.data->>'building', (r.data->>'targetLevel')::int
FROM staging.raw_events r
JOIN dw.dim_player p USING (player_id)
JOIN dw.dim_event_kind k USING (kind)
JOIN dw.dim_date d ON d.full_date = (to_timestamp(r.at_ms / 1000.0) AT TIME ZONE 'Asia/Taipei')::date;

-- 累計 session.start 的視窗函數將事件切成遊玩區段。
-- 同毫秒使用伺服器 sequence 排序；只認同 sessionId 的 end。
-- 缺 end 時以區段最後事件時間結束，end_inferred 明確標記低估可能。
WITH numbered AS (
    SELECT r.*, to_timestamp(at_ms / 1000.0) AS event_at,
           sum(CASE WHEN kind = 'session.start' THEN 1 ELSE 0 END) OVER (
               PARTITION BY player_id ORDER BY at_ms, COALESCE((data->>'sequence')::bigint, 0), event_id
               ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS session_group
    FROM staging.raw_events r
), starts AS (
    SELECT player_id, session_group, min(event_at) AS started_at,
           max(data->>'sessionId') AS session_id
    FROM numbered WHERE kind = 'session.start' GROUP BY player_id, session_group
), paired AS (
    SELECT s.*, min(n.event_at) FILTER (WHERE n.kind = 'session.end' AND n.data->>'sessionId' = s.session_id) AS explicit_end,
           max(n.event_at) AS last_event_at,
           count(*) FILTER (WHERE n.kind = 'quest.claim')::int AS quests_claimed
    FROM starts s JOIN numbered n USING (player_id, session_group)
    GROUP BY s.player_id, s.session_group, s.started_at, s.session_id
)
INSERT INTO dw.fact_session
SELECT a.session_id, p.player_key, d.date_key, a.started_at,
       COALESCE(a.explicit_end, a.last_event_at),
       floor(extract(epoch FROM COALESCE(a.explicit_end, a.last_event_at) - a.started_at))::int,
       a.quests_claimed, a.explicit_end IS NULL
FROM paired a JOIN dw.dim_player p USING (player_id)
JOIN dw.dim_date d ON d.full_date = (a.started_at AT TIME ZONE 'Asia/Taipei')::date;

-- 跨午夜的遊玩秒數切給各自的台灣日曆日，不把整段都塞在登入當天。
WITH session_days AS (
    SELECT s.player_key, d.date_key,
        (floor(extract(epoch FROM LEAST(s.ended_at, (d.full_date + 1)::timestamp AT TIME ZONE 'Asia/Taipei') - s.started_at)) -
         floor(extract(epoch FROM GREATEST(s.started_at, d.full_date::timestamp AT TIME ZONE 'Asia/Taipei') - s.started_at)))::int AS seconds
    FROM dw.fact_session s JOIN dw.dim_date d
      ON d.full_date >= (s.started_at AT TIME ZONE 'Asia/Taipei')::date
     AND d.full_date <= (s.ended_at AT TIME ZONE 'Asia/Taipei')::date
     AND (d.full_date::timestamp AT TIME ZONE 'Asia/Taipei' < s.ended_at OR s.started_at = s.ended_at)
), session_totals AS (
    SELECT player_key, date_key, sum(seconds)::int AS seconds, count(*)::int AS sessions
    FROM session_days GROUP BY player_key, date_key
), event_totals AS (
    SELECT e.player_key, e.date_key,
           count(*) FILTER (WHERE k.kind = 'quest.claim')::int AS quests,
           COALESCE(max(e.quest_index), 0) AS max_quest
    FROM dw.fact_event e JOIN dw.dim_event_kind k USING (kind_key)
    WHERE k.kind <> 'session.end' -- Closing exactly at midnight alone is not next-day activity.
    GROUP BY e.player_key, e.date_key
), keys AS (
    SELECT player_key, date_key FROM session_totals UNION SELECT player_key, date_key FROM event_totals
)
INSERT INTO dw.fact_player_day
SELECT a.player_key, a.date_key, COALESCE(s.seconds, 0), COALESCE(s.sessions, 0), COALESCE(e.quests, 0),
       max(COALESCE(e.max_quest, 0)) OVER (PARTITION BY a.player_key ORDER BY a.date_key ROWS UNBOUNDED PRECEDING)
FROM keys a LEFT JOIN session_totals s USING (player_key, date_key)
LEFT JOIN event_totals e USING (player_key, date_key);
