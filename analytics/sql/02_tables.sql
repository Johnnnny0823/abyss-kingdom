-- 原始事件保留 JSON，不把遊戲規則混入載入。自然鍵去重，代理鍵供 BI 關聯。
CREATE TABLE IF NOT EXISTS staging.raw_events (
    event_id text PRIMARY KEY, player_id text NOT NULL, kind text NOT NULL,
    at_ms bigint NOT NULL, data jsonb NOT NULL, loaded_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS raw_player_time ON staging.raw_events(player_id, at_ms);
CREATE INDEX IF NOT EXISTS raw_kind_time ON staging.raw_events(kind, at_ms);
-- 補充資料：任務種子來自 shared，觀察截止時間來自模擬 manifest，不偽造成遊戲事件。
CREATE TABLE IF NOT EXISTS staging.quest_catalog (
    quest_index int PRIMARY KEY, quest_title text NOT NULL, goal text NOT NULL
);
CREATE TABLE IF NOT EXISTS staging.dataset (
    singleton int PRIMARY KEY CHECK (singleton = 1), is_simulated boolean NOT NULL,
    observation_end_at timestamptz NOT NULL, source_label text NOT NULL
);
CREATE TABLE IF NOT EXISTS dw.dim_player (
    player_key serial PRIMARY KEY, player_id text UNIQUE NOT NULL,
    display_name text NOT NULL, persona text NOT NULL,
    first_seen_at timestamptz NOT NULL, cohort_date date NOT NULL
);
CREATE INDEX IF NOT EXISTS player_cohort ON dw.dim_player(cohort_date);
CREATE INDEX IF NOT EXISTS player_persona ON dw.dim_player(persona);
CREATE TABLE IF NOT EXISTS dw.dim_date (
    date_key int PRIMARY KEY, full_date date UNIQUE NOT NULL,
    day_of_week smallint NOT NULL, is_weekend boolean NOT NULL
);
CREATE TABLE IF NOT EXISTS dw.dim_quest (
    quest_index int PRIMARY KEY, quest_title text NOT NULL, goal text NOT NULL
);
CREATE TABLE IF NOT EXISTS dw.dim_event_kind (
    kind_key serial PRIMARY KEY, kind text UNIQUE NOT NULL, category text NOT NULL
);
CREATE INDEX IF NOT EXISTS event_category ON dw.dim_event_kind(category);
CREATE TABLE IF NOT EXISTS dw.fact_event (
    event_id text PRIMARY KEY, player_key int NOT NULL REFERENCES dw.dim_player,
    date_key int NOT NULL REFERENCES dw.dim_date, kind_key int NOT NULL REFERENCES dw.dim_event_kind,
    occurred_at timestamptz NOT NULL, quest_index int NULL REFERENCES dw.dim_quest,
    building text NULL, target_level int NULL
);
CREATE INDEX IF NOT EXISTS event_player ON dw.fact_event(player_key);
CREATE INDEX IF NOT EXISTS event_date ON dw.fact_event(date_key);
CREATE INDEX IF NOT EXISTS event_kind ON dw.fact_event(kind_key);
CREATE INDEX IF NOT EXISTS event_quest ON dw.fact_event(quest_index);
CREATE INDEX IF NOT EXISTS event_time ON dw.fact_event(occurred_at);
CREATE INDEX IF NOT EXISTS event_building ON dw.fact_event(building, target_level);
CREATE TABLE IF NOT EXISTS dw.fact_session (
    session_id text PRIMARY KEY, player_key int NOT NULL REFERENCES dw.dim_player,
    date_key int NOT NULL REFERENCES dw.dim_date, started_at timestamptz NOT NULL,
    ended_at timestamptz NOT NULL, play_seconds int NOT NULL CHECK (play_seconds >= 0),
    quests_claimed int NOT NULL CHECK (quests_claimed >= 0),
    end_inferred boolean NOT NULL, CHECK (ended_at >= started_at)
);
CREATE INDEX IF NOT EXISTS session_player_time ON dw.fact_session(player_key, started_at);
CREATE INDEX IF NOT EXISTS session_date ON dw.fact_session(date_key);
CREATE INDEX IF NOT EXISTS session_ended ON dw.fact_session(ended_at);
CREATE TABLE IF NOT EXISTS dw.fact_player_day (
    player_key int REFERENCES dw.dim_player, date_key int REFERENCES dw.dim_date,
    active_seconds int NOT NULL CHECK (active_seconds >= 0), sessions int NOT NULL CHECK (sessions >= 0),
    quests_claimed int NOT NULL CHECK (quests_claimed >= 0), max_quest_index int NOT NULL,
    PRIMARY KEY (player_key, date_key)
);
CREATE INDEX IF NOT EXISTS player_day_date ON dw.fact_player_day(date_key);
