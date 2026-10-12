using AbyssKingdom.Server;
using AbyssKingdom.Server.Game;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Social;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Isolated local visual fixture. No production storage/account or clock-changing endpoint.
var clock = new ManualClock(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
var app = GameServer.Build(new[] { "--Auth:Mode=Dev", "--Storage:Mode=Memory", "--Admin:Token=",
    "--Logging:LogLevel:Default=Warning" }, services => services.Replace(ServiceDescriptor.Singleton<IClock>(clock)),
    "http://127.0.0.1:5090");
var players = app.Services.GetRequiredService<IPlayerStore>();
var store = app.Services.GetRequiredService<IAllianceStore>();
var alliance = app.Services.GetRequiredService<AllianceService>();
var sites = SurfaceLogic.Sites().Where(s => s.z == SurfaceZone.Outer && s.x > 700 && Math.Abs(s.y - 400) < 20).Take(4).ToArray();
string[] ids = { "dev-S6PreviewLeader", "dev-S6PreviewMember", "dev-S6PreviewOther", "dev-S6PreviewNeutral" };
string[] names = { "盟主預覽", "盟友預覽", "異盟預覽", "無盟預覽" };
for (int i = 0; i < ids.Length; i++)
{
    var city = CityState.CreateNew(clock.NowMs);
    city.Buildings[BuildingType.Keep] = 3; city.Buildings[BuildingType.Hospital] = 10;
    city.Surface.X = sites[i].x; city.Surface.Y = sites[i].y;
    city.Resources[ResourceType.Wood] = 100_000; city.Resources[ResourceType.Stone] = 100_000;
    foreach (var t in CityConfig.AllTroops) city.Troops[t] = 1_000;
    await players.SaveAsync(new PlayerRecord { Id = ids[i], DisplayName = names[i], City = city }, CancellationToken.None);
}
await store.SaveAsync(new AllianceDirectory { Alliances = new() { new AllianceRecord
{
    Id = "local-s6-preview", Name = "契印盟", Tag = "SIG", Announcement = "共同遠征，向深處同行。",
    Members = ids.Take(2).Select((id, i) => new AllianceMemberRecord { PlayerId = id, DisplayName = names[i], Epoch = 1,
        Role = i == 0 ? AllianceRole.Leader : AllianceRole.Member }).ToList()
}, new AllianceRecord { Id = "local-other-preview", Name = "遠方盟", Tag = "RED", Members = new()
    { new AllianceMemberRecord { PlayerId = ids[2], DisplayName = names[2], Epoch = 1, Role = AllianceRole.Leader } } }
} }, CancellationToken.None);
var leader = (await players.LoadAsync(ids[0], CancellationToken.None))!.City;
var home = SurfaceLogic.CityCenter(leader.Surface);
var point = AllianceTerritory.Coverage(home.x, home.y, 10).Select(SurfaceLogic.FromTile)
    .Where(p => AllianceTerritory.AllowedPoint(p.x, p.y) && SurfaceLogic.Dist2(p.x, p.y, home.x, home.y) >= 16)
    .OrderBy(p => SurfaceLogic.Dist2(p.x, p.y, home.x, home.y)).First();
var placed = await alliance.PlaceFlagAsync(ids[0], 1, point.x, point.y, Guid.NewGuid().ToString(), CancellationToken.None);
if (placed != FlagResult.Ok) throw new InvalidOperationException("Flag fixture failed: " + placed);
clock.Advance(AllianceTerritory.BuildMs);
var camp = SurfaceLogic.Features().Where(f => f.Type == FeatureType.MonsterCamp && f.Level == 2)
    .OrderBy(f => SurfaceLogic.Dist2(f.X, f.Y, home.x, home.y)).First();
var troops = CityConfig.AllTroops.ToDictionary(t => t, _ => 100L);
var created = await alliance.CreateRallyAsync(ids[0], HeroId.Hold, troops, camp.X, camp.Y, 60, Guid.NewGuid().ToString(), CancellationToken.None);
var joined = await alliance.JoinRallyAsync(ids[1], created.Id!, HeroId.Hold, troops, Guid.NewGuid().ToString(), CancellationToken.None);
if (created.Result != AllianceResult.Ok || joined.Result != AllianceResult.Ok) throw new InvalidOperationException("Rally fixture failed.");
clock.Advance(60_000); await alliance.AdvanceRalliesAsync(CancellationToken.None);
var rally = (await alliance.SnapshotAsync(CancellationToken.None)).Rallies.Single();
clock.Advance((rally.Times[^1] - rally.DepartAtMs) / 3);
Console.WriteLine($"S6_VISUAL_READY flag=({point.x},{point.y}) rally={rally.Id} state={rally.State} troops=600");
await app.RunAsync();
