-- 深淵王國：玩家存檔資料表
-- 使用方式：Supabase 後台 → SQL Editor → 貼上整段 → Run
--
-- 設計：
--   * 每位玩家一列，城市狀態整包存成 jsonb（MVP 階段最簡單，之後再拆表）。
--   * 開啟 RLS 且不建立任何 policy → 用 anon key 的客戶端完全無法讀寫；
--     只有伺服器用 service_role key 可以存取（service_role 會略過 RLS）。

create table if not exists public.players (
    id             text primary key,          -- Firebase UID（或開發模式的 dev-名字）
    display_name   text not null default '',
    city           jsonb not null,
    created_at_ms  bigint not null,
    updated_at     timestamptz not null default now()
);

alter table public.players enable row level security;

-- 每次更新自動寫入 updated_at
create or replace function public.touch_updated_at() returns trigger
language plpgsql as $$
begin
    new.updated_at = now();
    return new;
end;
$$;

drop trigger if exists players_touch on public.players;
create trigger players_touch before update on public.players
for each row execute function public.touch_updated_at();

-- 權限：只有伺服器用的 service_role 可以存取；客戶端角色（anon、authenticated）一律拿不到。
-- （建立專案時若關閉了「Automatically expose new tables」，就必須靠這段授權伺服器存取。）
revoke all on public.players from anon, authenticated;
grant select, insert, update, delete on public.players to service_role;
