-- One authoritative alliance snapshot for the current single-server deployment.
-- Apply before enabling S6 endpoints with Storage:Mode=Supabase.
create table if not exists public.alliance_directory (
    id integer primary key check (id = 1),
    state jsonb not null
);

alter table public.alliance_directory enable row level security;
revoke all on public.alliance_directory from anon, authenticated;
-- Supabase project has "Automatically expose new tables" off, so the server role needs an explicit grant.
grant select, insert, update, delete on public.alliance_directory to service_role;
