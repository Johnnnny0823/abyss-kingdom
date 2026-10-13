-- Run manually in a development Supabase project before enabling playtest telemetry.
create table if not exists public.playtest_events (
    id text primary key,
    player_id text not null,
    kind text not null,
    at_ms bigint not null,
    data jsonb not null default '{}'::jsonb,
    constraint playtest_data_size check (octet_length(data::text) <= 16384)
);
create index if not exists playtest_events_player_time on public.playtest_events(player_id, at_ms, id);
create index if not exists playtest_events_kind_time on public.playtest_events(kind, at_ms);
alter table public.playtest_events enable row level security;
revoke all on public.playtest_events from anon, authenticated;
-- No public policies. Only the server's service_role may access this table.
