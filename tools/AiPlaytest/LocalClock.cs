using AbyssKingdom.Server.Infrastructure;

namespace AbyssKingdom.Playtesting;
public static class LocalClock
{
    public static ManualClock Create(string authMode, string storageMode, long startMs)
    {
        if (authMode != "Dev" || storageMode != "InMemory")
            throw new InvalidOperationException("AI playtest clock is only available on isolated Dev/InMemory hosts.");
        return new ManualClock(startMs);
    }
}
