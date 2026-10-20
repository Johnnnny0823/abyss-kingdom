using System.Text;
using System.Text.Json;
using AbyssKingdom.Client.UI;
using AbyssKingdom.Shared.City;
using Npgsql;
using NpgsqlTypes;

namespace AbyssKingdom.Analytics;

public sealed class EtlPipeline(NpgsqlConnection connection, string sqlDirectory)
{
    public async Task<IReadOnlyList<RawEvent>> ExtractAsync(IRawEventSource source, CancellationToken ct)
    {
        var rows = await source.ReadAsync(ct);
        Console.WriteLine($"Extract: rows={rows.Count}");
        return rows;
    }

    public async Task LoadStagingAsync(IReadOnlyList<RawEvent> rows, string rawPath, CancellationToken ct)
    {
        int inserted = 0;
        foreach (var batch in rows.Chunk(500))
        {
            const string sql = """
                INSERT INTO staging.raw_events(event_id, player_id, kind, at_ms, data)
                SELECT id, player_id, kind, at_ms, data FROM jsonb_to_recordset($1) AS
                    x(id text, player_id text, kind text, at_ms bigint, data jsonb)
                ON CONFLICT (event_id) DO NOTHING;
                """;
            await using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(batch));
            inserted += await cmd.ExecuteNonQueryAsync(ct);
        }
        var quests = QuestConfig.Chapter1.Select(q => new { quest_index = q.Id, quest_title = DisplayNames.QuestText(q), goal = q.Goal.ToString() });
        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO staging.quest_catalog SELECT * FROM jsonb_to_recordset($1) AS
                x(quest_index int, quest_title text, goal text)
            ON CONFLICT (quest_index) DO UPDATE SET quest_title = EXCLUDED.quest_title, goal = EXCLUDED.goal;
            """, connection))
        {
            cmd.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(quests));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        string manifestPath = Path.Combine(Path.GetDirectoryName(rawPath)!, "simulation.json");
        bool simulated = false;
        var cutoff = DateTimeOffset.FromUnixTimeMilliseconds(rows.Max(e => e.AtMs));
        if (File.Exists(manifestPath))
        {
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
            simulated = manifest.RootElement.GetProperty("isSimulated").GetBoolean();
            cutoff = manifest.RootElement.GetProperty("observationEndAt").GetDateTimeOffset();
            if (cutoff.ToUnixTimeMilliseconds() < rows.Max(e => e.AtMs)) throw new InvalidDataException("Observation cutoff precedes an event.");
        }
        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO staging.dataset VALUES (1, $1, $2, $3)
            ON CONFLICT (singleton) DO UPDATE SET is_simulated = staging.dataset.is_simulated AND EXCLUDED.is_simulated,
                observation_end_at = GREATEST(staging.dataset.observation_end_at, EXCLUDED.observation_end_at), source_label = EXCLUDED.source_label;
            """, connection))
        {
            cmd.Parameters.AddWithValue(simulated); cmd.Parameters.AddWithValue(cutoff.ToUniversalTime());
            cmd.Parameters.AddWithValue(Path.GetFileName(rawPath)); await cmd.ExecuteNonQueryAsync(ct);
        }
        Console.WriteLine($"Load staging: input={rows.Count}, inserted={inserted}, duplicates={rows.Count - inserted}, simulated={simulated}");
    }

    public async Task TransformAsync(CancellationToken ct)
    {
        foreach (var file in new[] { "03_transform_dims.sql", "04_transform_facts.sql", "05_marts.sql" })
        { await ExecuteFileAsync(file, ct); Console.WriteLine($"Transform: {file} completed"); }
        foreach (var table in new[] { "dim_player", "dim_date", "dim_quest", "dim_event_kind", "fact_event", "fact_session", "fact_player_day" })
        {
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM dw." + table, connection);
            Console.WriteLine($"Transform: dw.{table} rows={await cmd.ExecuteScalarAsync(ct)}");
        }
    }

    public async Task ExecuteFileAsync(string file, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(sqlDirectory, file), ct), connection) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CheckAsync(CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(sqlDirectory, "90_checks.sql"), ct), connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct); bool failed = false;
        while (await reader.ReadAsync(ct))
        {
            Console.WriteLine($"{reader.GetString(2)} {reader.GetString(0)} violations={reader.GetInt64(1)}");
            failed |= reader.GetInt64(1) != 0;
        }
        if (failed) throw new InvalidDataException("Data quality checks failed; rolling back.");
    }

    public async Task ExportMartsAsync(string output, CancellationToken ct)
    {
        Directory.CreateDirectory(output);
        var md = new StringBuilder("# Mart 前十列（本機實際查詢輸出）\n\n資料性質以各列 is_simulated 為準。本次為模擬資料。\n\n");
        var marts = new Dictionary<string, string> { ["quest_funnel"] = "quest_index", ["retention"] = "cohort_date",
            ["daily_activity"] = "full_date", ["session_length_buckets"] = "bucket_order", ["persona_summary"] = "persona" };
        foreach (var (view, order) in marts)
        {
            await using var cmd = new NpgsqlCommand($"SELECT * FROM mart.{view} ORDER BY {order} LIMIT 10", connection);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            md.Append("## mart.").Append(view).Append("\n\n");
            var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            md.Append('|').AppendJoin('|', names).Append("|\n|").AppendJoin('|', names.Select(_ => "---")).Append("|\n");
            while (await reader.ReadAsync(ct))
            {
                var values = Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "NULL" :
                    reader.GetValue(i) is DateTime d ? d.ToString("yyyy-MM-dd") :
                    reader.GetValue(i) is DateOnly day ? day.ToString("yyyy-MM-dd") : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)!).ToArray();
                md.Append('|').AppendJoin('|', values).Append("|\n");
            }
            md.AppendLine();
        }
        await File.WriteAllTextAsync(Path.Combine(output, "mart-samples.md"), md.ToString(), ct);
    }
}
