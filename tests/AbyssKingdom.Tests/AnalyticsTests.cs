using AbyssKingdom.Analytics;
using AbyssKingdom.Playtesting;

namespace AbyssKingdom.Tests;

public static class AnalyticsTests
{
    [Test]
    public static void Jsonl_PreservesSnakeFieldsAndNestedDataAfterDocumentDisposal()
    {
        var row = JsonlEventSource.ParseLine("""{"id":"e1","player_id":"dev-sim","kind":"quest.claim","at_ms":1790784000000,"data":{"questNumber":1,"nested":{"value":2}}}""", 1);
        Assert.Equal("e1", row.EventId); Assert.Equal("dev-sim", row.PlayerId);
        Assert.Equal(2, row.Data.GetProperty("nested").GetProperty("value").GetInt32());
    }

    [Test]
    public static void Jsonl_RejectsMalformedMissingAndInvalidFieldsWithoutLeakingInput()
    {
        foreach (var line in new[] { "not json", "{}", """{"id":"","player_id":"p","kind":"k","at_ms":0,"data":{}}""",
            """{"id":"e","player_id":"p","kind":"k","at_ms":9223372036854775807,"data":{}}""",
            """{"id":"e","player_id":"p","kind":"k","at_ms":0,"data":[]}""" })
            Assert.Throws<FormatException>(() => JsonlEventSource.ParseLine(line, 4));
    }

    [Test]
    public static async Task Jsonl_ReadsBomAndBlankLines_AndRejectsEmptyInput()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "\n" + """{"id":"e","player_id":"p","kind":"session.start","at_ms":0,"data":{}}""" + "\n\n", new System.Text.UTF8Encoding(true));
            var rows = await new JsonlEventSource(path).ReadAsync(default); Assert.Equal(1, rows.Count);
            await File.WriteAllTextAsync(path, " \n");
            bool failed = false; try { await new JsonlEventSource(path).ReadAsync(default); } catch (InvalidDataException) { failed = true; }
            Assert.True(failed, "An empty extract must not silently publish an empty warehouse");
        }
        finally { File.Delete(path); }
    }

    [Test]
    public static void PopulationPlan_RepeatsForSameSeed_AndSpansCohortsAndReturnVisits()
    {
        var options = PopulationOptions.Parse(new[] { "--population", "300", "--days", "7", "--seed", "42" });
        var a = PopulationPlan.Create(options); var b = PopulationPlan.Create(options);
        Assert.True(a.Players.SequenceEqual(b.Players) && a.Visits.SequenceEqual(b.Visits), "Same seed yields exactly the same plan");
        Assert.Equal(7, a.Players.Select(p => p.StartDay).Distinct().Count());
        Assert.Equal(4, a.Players.Select(p => p.Persona).Distinct().Count());
        Assert.True(a.Visits.Length > 300, "Returning sessions exist");
        Assert.True(!a.Visits.SequenceEqual(PopulationPlan.Create(options with { Seed = 43 }).Visits), "Different seeds change the plan");
        foreach (var group in a.Visits.GroupBy(v => v.Player))
        {
            long end = 0;
            foreach (var v in group.OrderBy(v => v.StartMs))
            { Assert.True(v.StartMs >= end && v.EndMs > v.StartMs, "No overlapping or negative planned sessions"); end = v.EndMs; }
        }
        Assert.Throws<ArgumentException>(() => PopulationOptions.Parse(new[] { "--population", "0" }));
        Assert.Throws<ArgumentException>(() => PopulationOptions.Parse(new[] { "--days" }));
    }
}
