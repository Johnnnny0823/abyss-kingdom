using AbyssKingdom.Playtesting;
using AbyssKingdom.Server;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace AbyssKingdom.Tests;
public static class AiPlaytestTests
{
    [Test]
    public static async Task AcceleratedClock_RejectsProduction_AndDefaultServerStillUsesSystemClock()
    {
        Assert.Throws<InvalidOperationException>(() => LocalClock.Create("Firebase", "InMemory", 0));
        Assert.Throws<InvalidOperationException>(() => LocalClock.Create("Dev", "Supabase", 0));
        var clock = LocalClock.Create("Dev", "InMemory", 123); clock.Advance(8 * 3_600_000); Assert.Equal(28_800_123L, clock.NowMs);
        await using var app = GameServer.Build(new[] { "--Auth:Mode=Firebase", "--Storage:Mode=Memory", "--Admin:Token=" });
        Assert.True(app.Services.GetRequiredService<IClock>() is SystemClock, "Production does not know the tool's clock");
    }
    [Test]
    public static void Policies_UsePlayerOnlyIntentsAndRespectPrerequisites()
    {
        var city = CityState.CreateNew(123); city.Buildings[BuildingType.Keep] = 2; city.QuestIndex = 11;
        for (int turn = 0; turn < 50; turn++)
        {
            var intent = AiPlaytestPolicy.Next(city, "random", turn, 123);
            Assert.True(intent == null || AiPlaytestPolicy.Allowed.Contains(intent.Type), "No GM or private game service calls");
        }
        var prerequisite = AiPlaytestPolicy.Next(city, "quest", 1, 123);
        Assert.Equal(MessageTypes.CityUpgrade, prerequisite.Type);
        Assert.Equal(BuildingType.Barracks, ((UpgradeRequest)prerequisite.Data).Building);
    }
    [Test]
    public static void Diligent_DoesNotStopBuildingJustBecauseGatheringMarchIsBusy()
    {
        var city = CityState.CreateNew(123); city.QuestIndex = 5; city.Buildings[BuildingType.Keep] = 2;
        city.Surface.Marches.Add(new AbyssKingdom.Shared.Map.March());
        var intent = AiPlaytestPolicy.Next(city, "diligent", 1, 123);
        Assert.True(intent != null && intent.Type == MessageTypes.CityUpgrade, "Keep the build queue productive while a march is out");
    }
}
