using System.Text.Json;
using System.Text.Json.Serialization;

namespace AbyssKingdom.Analytics;

public sealed record RawEvent(
    [property: JsonPropertyName("id")] string EventId,
    [property: JsonPropertyName("player_id")] string PlayerId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("at_ms")] long AtMs,
    [property: JsonPropertyName("data")] JsonElement Data);

// A future GM HTTP source can implement this without changing the load/transform steps.
public interface IRawEventSource
{
    Task<IReadOnlyList<RawEvent>> ReadAsync(CancellationToken ct);
}

public sealed class JsonlEventSource(string path) : IRawEventSource
{
    public static RawEvent ParseLine(string line, int lineNumber)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            string Required(string key)
            {
                string? value = root.GetProperty(key).GetString();
                if (string.IsNullOrWhiteSpace(value)) throw new FormatException($"Missing {key}");
                return value;
            }
            var id = Required("id"); var player = Required("player_id"); var kind = Required("kind");
            long at = root.GetProperty("at_ms").GetInt64();
            _ = DateTimeOffset.FromUnixTimeMilliseconds(at);
            var data = root.GetProperty("data");
            if (data.ValueKind != JsonValueKind.Object) throw new FormatException("data must be an object");
            return new(id, player, kind, at, data.Clone());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentOutOfRangeException)
        { throw new FormatException($"Invalid raw event at line {lineNumber}: {ex.GetType().Name}", ex); }
    }

    public async Task<IReadOnlyList<RawEvent>> ReadAsync(CancellationToken ct)
    {
        var result = new List<RawEvent>(); int lineNumber = 0;
        using var reader = new StreamReader(path);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            lineNumber++;
            if (!string.IsNullOrWhiteSpace(line)) result.Add(ParseLine(line, lineNumber));
        }
        if (result.Count == 0) throw new InvalidDataException("Input contains no events.");
        return result;
    }
}
