using AbyssKingdom.Analytics;
using Npgsql;

try
{
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root != null && !File.Exists(Path.Combine(root.FullName, "CLAUDE.md"))) root = root.Parent;
    if (root == null) throw new InvalidOperationException("Repository root not found.");
    string analytics = Path.Combine(root.FullName, "analytics");
    string raw = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(analytics, "data", "raw_events.jsonl");
    var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ANALYTICS_CONNECTION_STRING") ??
        throw new InvalidOperationException("Set ANALYTICS_CONNECTION_STRING in the environment."));
    var databaseOverride = args.FirstOrDefault(a => a.StartsWith("--database="));
    if (databaseOverride != null) settings.Database = databaseOverride.Substring(11);
    if (settings.Host is not ("127.0.0.1" or "localhost" or "::1") || settings.Database?.StartsWith("abyss_analytics", StringComparison.Ordinal) != true)
        throw new InvalidOperationException("ETL only accepts a loopback abyss_analytics database; production is forbidden.");
    await using var source = NpgsqlDataSource.Create(settings.ConnectionString);
    await using var connection = await source.OpenConnectionAsync();
    var pipeline = new EtlPipeline(connection, Path.Combine(analytics, "sql"));
    var rows = await pipeline.ExtractAsync(new JsonlEventSource(raw), default);
    await using (var transaction = await connection.BeginTransactionAsync())
    {
        await pipeline.ExecuteFileAsync("01_schemas.sql", default);
        await pipeline.ExecuteFileAsync("02_tables.sql", default);
        await pipeline.LoadStagingAsync(rows, raw, default);
        await pipeline.TransformAsync(default);
        await pipeline.CheckAsync(default);
        await transaction.CommitAsync();
    }
    await pipeline.ExportMartsAsync(Path.GetDirectoryName(raw)!, default);
    Console.WriteLine("ETL PASS (committed); mart samples exported.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ETL FAIL: {ex.GetType().Name}: {ex.Message}");
    return 1;
}
