param([string]$Container = 'abyss-analytics-postgres-1')
$ErrorActionPreference = 'Stop'
if (-not $env:ANALYTICS_CONNECTION_STRING -or -not $env:ANALYTICS_USER) {
    throw 'Set ANALYTICS_CONNECTION_STRING and ANALYTICS_USER in the environment.'
}
$repo = Split-Path $PSScriptRoot
$folder = Join-Path $PSScriptRoot 'data/regression'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
# These are synthetic TEST FIXTURES, kept separate from the server-generated simulation export.
$rows = [Collections.Generic.List[object]]::new()
function Add-Event($id, $player, $kind, [DateTimeOffset]$at, $data) {
    $rows.Add([ordered]@{id=$id; player_id=$player; kind=$kind; at_ms=$at.ToUnixTimeMilliseconds(); data=$data})
}
$t = [DateTimeOffset]::Parse('2026-10-01T23:59:59.200+08:00')
Add-Event 'f1' 'fixture-p1' 'session.start' $t @{displayName='SIM-quest-9001'; sessionId='s1'; sequence=1}
Add-Event 'f2' 'fixture-p1' 'quest.claim' ($t.AddMilliseconds(100)) @{questNumber=1; sequence=2}
Add-Event 'f3' 'fixture-p1' 'session.end' ($t.AddMilliseconds(1200)) @{sessionId='s1'; sequence=3}
Add-Event 'f4' 'fixture-p1' 'session.start' ($t.AddMilliseconds(1200)) @{sessionId='s2'; sequence=4}
Add-Event 'f5' 'fixture-p1' 'quest.claim' ($t.AddMilliseconds(2200)) @{questNumber=2; sequence=5}
Add-Event 'f6' 'fixture-p1' 'building.upgrade.start' ($t.AddMilliseconds(12200)) @{building='Keep'; targetLevel=2; sequence=6}
$noon = [DateTimeOffset]::Parse('2026-10-01T12:00:00+08:00')
Add-Event 'f7' 'fixture-p2' 'session.start' $noon @{displayName='SIM-random-9002'; sessionId='s3'; sequence=7}
Add-Event 'f8' 'fixture-p2' 'session.end' ($noon.AddSeconds(300)) @{sessionId='s3'; sequence=8}
Add-Event 'f9' 'fixture-p3' 'session.start' ($noon.AddDays(1)) @{displayName='SIM-offline-9003'; sessionId='s4'; sequence=9}
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllLines((Join-Path $folder 'raw_events.jsonl'), @($rows | ForEach-Object {ConvertTo-Json $_ -Depth 8 -Compress}), $utf8)
[IO.File]::WriteAllText((Join-Path $folder 'simulation.json'), '{"isSimulated":true,"observationEndAt":"2026-10-02T16:00:00Z"}', $utf8)
$exists = docker exec $Container psql -U $env:ANALYTICS_USER -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='abyss_analytics_fixture'"
if ($LASTEXITCODE -ne 0) {throw 'Cannot inspect fixture database'}
if ($exists -ne '1') {
    docker exec $Container psql -U $env:ANALYTICS_USER -d postgres -v ON_ERROR_STOP=1 -c 'CREATE DATABASE abyss_analytics_fixture'
    if ($LASTEXITCODE -ne 0) {throw 'Cannot create fixture database'}
}
dotnet run --project (Join-Path $repo 'tools/PlaytestEtl') -- (Join-Path $folder 'raw_events.jsonl') --database=abyss_analytics_fixture
if ($LASTEXITCODE -ne 0) {throw 'Fixture ETL failed'}
$result = Get-Content (Join-Path $PSScriptRoot 'sql/91_fixture_checks.sql') -Raw |
    docker exec -i $Container psql -U $env:ANALYTICS_USER -d abyss_analytics_fixture -v ON_ERROR_STOP=1 -At -F '|'
if ($LASTEXITCODE -ne 0) {throw 'Fixture queries failed'}
$result | ForEach-Object {Write-Output $_}
if ($result -match '\|FAIL') {throw 'SQL semantic fixture failed'}
Add-Event 'invalid-gap' 'fixture-p1' 'quest.claim' ($t.AddMinutes(2)) @{questNumber=15; sequence=10}
$invalid = Join-Path $folder 'gap_events.jsonl'
[IO.File]::WriteAllText($invalid, (ConvertTo-Json $rows[$rows.Count - 1] -Depth 8 -Compress), $utf8)
$failureLog = Join-Path $folder 'expected-failure.log'
dotnet run --project (Join-Path $repo 'tools/PlaytestEtl') --no-build -- $invalid --database=abyss_analytics_fixture > $failureLog 2>&1
if ($LASTEXITCODE -eq 0) {throw 'A quest gap should fail quality checks'}
$remaining = docker exec $Container psql -U $env:ANALYTICS_USER -d abyss_analytics_fixture -tAc "SELECT count(*) FROM staging.raw_events WHERE event_id='invalid-gap'"
if ($LASTEXITCODE -ne 0 -or $remaining -ne '0') {throw 'Failed ETL did not roll back staging'}
Write-Output 'failed_quality_check_rolls_back|PASS'
Write-Output 'SQL boundary fixtures PASS (main warehouse unchanged).'
