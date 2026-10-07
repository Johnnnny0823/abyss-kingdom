using System.Text.Json;
using System.Text.Json.Serialization;

namespace AbyssKingdom.Server.Infrastructure;

/// <summary>可替換的時鐘：正式環境用系統時間，測試用手動時鐘快轉。</summary>
public interface IClock
{
    long NowMs { get; }
}

public sealed class SystemClock : IClock
{
    public long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public sealed class ManualClock : IClock
{
    private long _now;
    public ManualClock(long startMs) => _now = startMs;
    public long NowMs => Interlocked.Read(ref _now);
    public void Advance(long ms) => Interlocked.Add(ref _now, ms);
}

/// <summary>全專案共用的 JSON 設定：camelCase、enum 以文字表示。</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}
