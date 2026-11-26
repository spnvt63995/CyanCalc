using System.Text.Json;
using CyanGraph.Data;
using Neo4j.Driver;

static partial class Program
{
    const string Coactors = """
        MATCH (a:Person {name: $actor})-[:ACTED_IN]->(m:Movie)<-[:ACTED_IN]-(b:Person)
        WHERE a <> b
        RETURN b.name AS actor, count(DISTINCT m) AS films
        ORDER BY films DESC, actor
        """;

    static async Task CoactorChecks(QueryWorkspace workspace)
    {
        var connection = workspace.Connected;
        await workspace.RunAsync(Coactors, "{}");
        Check(workspace.Phase == QueryPhase.Failed && workspace.Status.Contains("actor", StringComparison.OrdinalIgnoreCase) &&
            workspace.Result == null && workspace.Connected == connection, "missing actor can be corrected on the same database");
        await workspace.RunAsync(Coactors, """{"actor":""");
        Check(workspace.Phase == QueryPhase.Failed && workspace.Status.Contains("JSON") && workspace.Result == null, "malformed bindings do not publish a result");
        await workspace.RunAsync("RETURN 1 AS value LIMIT $number", """{"number":"wrong type"}""");
        Check(workspace.Phase == QueryPhase.Failed && workspace.Connected == connection, "driver rejects an invalid LIMIT type");
        await workspace.RunAsync("RETURN 1 AS value LIMIT $number", """{"number":1}""");
        Check(workspace.Result?.Rows.Single()[0].As<long>() == 1, "corrected typed parameter works");
        await workspace.RunAsync(Coactors, """{"actor":"Keanu Reeves"}""");
        var keanu = workspace.Result!;
        Check(keanu.Columns.SequenceEqual(["actor", "films"]) && keanu.Rows.Count == 14 &&
            keanu.Rows[0][0].As<string>() == "Carrie-Anne Moss" && keanu.Rows.Take(3).All(row => row[1].As<long>() == 3),
            "Keanu Reeves coactors with a bound value");
        const string tom = """{"actor":"Tom Hanks"}""";
        await workspace.ClearResultAsync();
        Check(workspace.Result == null && workspace.Scene == null && workspace.Phase == QueryPhase.Ready &&
            workspace.Connected == connection, "opening a question clears rows without changing its database");
        await workspace.RunAsync(Coactors, tom);
        var full = workspace.Result!.Rows.Select(row => (row[0].As<string>(), row[1].As<long>())).ToList();
        Check(full.Count == 34 && full[0] == ("Meg Ryan", 3) && full[^1] == ("Victor Garber", 1), "Tom Hanks coactors in rank order");
        await workspace.RunAsync(Coactors + "\nLIMIT 1", tom);
        Check(workspace.Result!.Rows.Single()[0].As<string>() == "Meg Ryan" && workspace.Result.Rows[0][1].As<long>() == 3,
            "limited reopened statement returns Meg Ryan alone");
        await workspace.RunAsync(Coactors, tom);
        Check(workspace.Result!.Rows.Select(row => (row[0].As<string>(), row[1].As<long>())).SequenceEqual(full),
            "removing the limit restores all 34 names and counts");
        var pending = workspace.RunAsync(Coactors, tom);
        await Task.WhenAll(pending, workspace.ClearResultAsync());
        Check(workspace.Result == null && workspace.Scene == null && workspace.Phase == QueryPhase.Ready,
            "a superseded execution cannot refill a reopened question");
    }

    static async Task LibraryChecks()
    {
        var folder = Directory.CreateTempSubdirectory("cyangraph-questions-");
        try
        {
            string file = Path.Combine(folder.FullName, "questions.json");
            var first = new QuestionLibrary(file);
            Check((await first.ReadAsync()).Questions.Length == 0 && !File.Exists(file), "empty library reads without creating a profile");
            var question = await first.SaveAsync("  함께 출연  ", Coactors);
            var second = new QuestionLibrary(file);
            Check((await second.ReadAsync()).Questions.Single() == question && question.Name == "함께 출연" &&
                question.Statement == Coactors, "named multiline Cypher round trips in another instance");
            var edit = await second.SaveAsync("Movie coactors", Coactors + "\nLIMIT 1", question);
            Check(edit.Id == question.Id && (await first.ReadAsync()).Questions.Single() == edit, "explicit save updates the same named question");
            try { await first.SaveAsync("other name", "RETURN 2", question); throw new Exception("Stale edit was saved."); }
            catch (InvalidOperationException) { checks++; }
            try { await first.SaveAsync("movie COACTORS", "RETURN 2"); throw new Exception("Duplicate name was saved."); }
            catch (InvalidOperationException) { checks++; }
            Check((await first.ReadAsync()).Questions.Single() == edit, "collision and stale edit preserve the saved statement");

            var start = DateTimeOffset.UtcNow;
            await Task.WhenAll(first.RecordAsync(new(start, "alpha", Coactors)), second.SaveAsync("Another", "RETURN 1"),
                second.RecordAsync(new(start.AddSeconds(1), "beta", "RETURN $value")));
            var contents = await new QuestionLibrary(file).ReadAsync();
            Check(contents.Questions.Length == 2 && contents.History.Length == 2 &&
                contents.History.Select(h => h.Database).SequenceEqual(["beta", "alpha"]) && contents.History[1].Statement == Coactors,
                "concurrent history and named saves merge, newest submission first");
            using (var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file)))
            {
                Check(doc.RootElement.GetProperty("questions")[0].EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(["id","name","statement"]) &&
                    doc.RootElement.GetProperty("history")[0].EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(["time","database","statement"]),
                    "storage contains only definitions and submission metadata");
            }
            var many = Enumerable.Range(0, QuestionLibrary.HistoryLimit).Select(n => new QueryHistoryEntry(start.AddDays(1).AddSeconds(n), "beta", $"RETURN {n}")).ToArray();
            var prepared = contents with { History = many };
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(prepared, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await first.RecordAsync(new(start.AddDays(2), "beta", "RETURN 'latest'"));
            var bounded = await first.ReadAsync();
            Check(bounded.History.Length == QuestionLibrary.HistoryLimit && bounded.History[0].Statement == "RETURN 'latest'" &&
                !bounded.History.Contains(many[0]) && bounded.Questions.Length == 2, "history cap retains recent runs and all named questions");
            const string unknown = """{"version":2,"questions":[],"history":[]}""";
            await File.WriteAllTextAsync(file, unknown);
            try { await first.SaveAsync("keep", "RETURN 1"); throw new Exception("Future format was overwritten."); }
            catch (InvalidDataException) { checks++; }
            Check(await File.ReadAllTextAsync(file) == unknown, "unsupported file survives a save attempt");
        }
        finally { folder.Delete(recursive: true); }
    }
}
