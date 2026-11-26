using System.Diagnostics;
using CyanGraph.Data;
using Neo4j.Driver;

static partial class Program
{
    static int checks;
    static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        checks++;
    }

    static async Task Main(string[] args)
    {
        var parameters = QueryParameters.Parse("""{"max":9223372036854775807,"opts":{"flags":[true,null,"가다"]}}""");
        Check(parameters["max"] is long value && value == long.MaxValue, "Int64 precision");
        foreach (var bad in new[] { "[]", """{"x":1,"x":2}""", """{"x":9223372036854775808}""", """{"x":1e400}""" })
        {
            try { QueryParameters.Parse(bad); throw new InvalidOperationException("Invalid parameter accepted."); }
            catch (FormatException) { checks++; }
        }
        await LibraryChecks();
        CheckEmptyExport();
        if (args is ["--movies" or "--graph-examples" or "--selection-examples" or "--northwind"])
        {
            string Env(string name) => Environment.GetEnvironmentVariable("CYANGRAPH_TEST_" + name)
                ?? throw new ArgumentException("Set CYANGRAPH_TEST_" + name + " for the read-only fixture.");
            if (args[0] == "--northwind") await Northwind(Env("URI"), Env("DATABASE"), Env("USER"), Env("PASSWORD"));
            else await Movies(Env("URI"), Env("DATABASE"), Env("USER"), Env("PASSWORD"), args[0]);
        }
        else if (args.Length != 0) throw new ArgumentException("Usage: CyanGraph.Checks [--movies | --graph-examples | --selection-examples | --northwind]");
        Console.WriteLine($"CYANGRAPH_CHECKS_PASS {checks} assertions ({(args.Length == 0 ? "parameters, library and export" : args[0][2..])})");
    }

    static async Task Movies(string uri, string db, string user, string password, string suite)
    {
        var w = new QueryWorkspace();
        try
        {
            await w.ConnectAsync("not a bolt URI", db, user, password);
            Check(w.Connected == null && w.Phase == QueryPhase.Failed, "invalid URI");
            await w.ConnectAsync(uri, db, user, "incorrect-" + Guid.NewGuid());
            Check(w.Connected == null && w.Phase == QueryPhase.Failed && !w.Status.Contains(password), "incorrect credentials");
            await w.ConnectAsync(uri, "no-such-cyangraph-db", user, password);
            Check(w.Connected == null && w.Phase == QueryPhase.Failed, "database verified before Connected");
            await w.ConnectAsync(uri, db, user, password);
            Check(w.Connected?.Database == db && w.Phase == QueryPhase.Ready, "retry same workspace");

            const string query = """
                MATCH path=(actor:Person {name:$actor})-[role:ACTED_IN]->(film:Movie {title:$film})
                RETURN actor, role, film, path, $payload AS payload, null AS missing, date('1999-03-31') AS date
                """;
            await w.RunAsync(query, """{"actor":"Carrie-Anne Moss","film":"The Matrix","payload":{"data":[true,null,9223372036854775807,"가다"]}}""");
            var result = w.Result ?? throw new Exception(w.Status);
            Check(result.Columns.SequenceEqual(["actor","role","film","path","payload","missing","date"]), "column order");
            Check(result.Rows.Count == 1 && !result.HasMore, "one actual row");
            var row = result.Rows[0];
            var actor = row["actor"].As<INode>(); var role = row["role"].As<IRelationship>(); var film = row["film"].As<INode>();
            Check(actor["born"].As<long>() == 1967 && film["released"].As<long>() == 1999, "Movies node properties");
            Check(role.Type == "ACTED_IN" && role["roles"].As<List<string>>().SequenceEqual(["Trinity"]), "relationship property");
            Check(role.StartNodeElementId == actor.ElementId && role.EndNodeElementId == film.ElementId, "directed endpoints");
            var path = row["path"].As<IPath>();
            Check(path.Start.ElementId == actor.ElementId && path.End.ElementId == film.ElementId && path.Relationships[0].ElementId == role.ElementId, "path identity");
            var data = row["payload"].As<Dictionary<string,object>>()["data"].As<List<object>>();
            Check(data[0] is true && data[1] == null && (long)data[2] == long.MaxValue && (string)data[3] == "가다", "nested typed parameters");
            Check(row["missing"] == null && row["date"] is LocalDate, "null and temporal value");
            CheckMovieExport(result);

            var graph = ResultGraph.From(result);
            Check(graph.Nodes.Count == 2 && graph.Relationships.Count == 1, "entity columns and repeated path deduplicate");
            Check(graph.Nodes[actor.ElementId].Entity!["born"].As<int>() == 1967 &&
                graph.Nodes[film.ElementId].Entity!["released"].As<int>() == 1999, "graph node properties");
            Check(graph.Relationships[role.ElementId]["roles"].As<List<string>>().Single() == "Trinity", "graph edge properties");

            async Task<ResultGraph> Project(string cypher)
            {
                await w.RunAsync(cypher, "{}");
                return ResultGraph.From(w.Result ?? throw new InvalidOperationException(w.Status));
            }
            graph = await Project("""
                MATCH p=(film:Movie {title:'The Matrix'})<-[r:ACTED_IN]-(actor:Person {name:'Carrie-Anne Moss'})
                UNWIND [1,2] AS row RETURN {nested:[[p, r]]} AS data
                """);
            Check(graph.Nodes.Count == 2 && graph.Relationships.Count == 1, "graph nested in repeated maps/lists");
            Check(graph.Relationships[role.ElementId].StartNodeElementId == actor.ElementId &&
                graph.Relationships[role.ElementId].EndNodeElementId == film.ElementId, "reverse path keeps stored direction");
            graph = await Project("""
                MATCH (:Person {name:'Tom Hanks'})-[r]->(:Movie {title:'That Thing You Do'}) RETURN r
                """);
            Check(graph.Nodes.Count == 2 && graph.Nodes.Values.All(n => n.Entity == null), "relationship-only endpoint IDs");
            Check(graph.Relationships.Count == 2 && graph.Relationships.Values.Select(r => r.Type).ToHashSet()
                .SetEquals(["ACTED_IN", "DIRECTED"]), "distinct parallel Movies relationships");
            graph = await Project("""
                MATCH (a:Person {name:'Carrie-Anne Moss'})-[r:ACTED_IN]->(b:Movie {title:'The Matrix'}) RETURN r, b, a
                """);
            Check(graph.Nodes.Count == 2 && graph.Nodes.Values.All(n => n.Entity != null), "node columns fill earlier endpoints");
            graph = await Project("RETURN 1, null, {name:'not a node', data:[1,2]} AS metadata");
            Check(graph.Nodes.Count == 0 && graph.Relationships.Count == 0, "scalar and map result has no graph");
            if (suite != "--movies")
            {
                graph = await Project("MATCH p=(a:Example)-[r]->(b:Example) RETURN p, a, r, b");
                Check(graph.Nodes.Count == 2 && graph.Relationships.Count == 4, "parallel, opposite and loop IDs");
                var start = graph.Nodes.Values.Single(n => n.Entity!["name"].As<string>() == "가다");
                var end = graph.Nodes.Values.Single(n => n.ElementId != start.ElementId);
                Check(start.Entity!["description"].As<string>().EndsWith("끝까지 들려주었다.") &&
                    start.Entity["examples"].As<List<string>>().Last() == "강을 따라 천천히 가다", "full Korean value and list");
                var parallel = graph.Relationships.Values.Where(r => r.Type == "RELATED").ToArray();
                Check(parallel.Length == 2 && parallel.All(r => r.StartNodeElementId == start.ElementId &&
                    r.EndNodeElementId == end.ElementId) && parallel.Select(r => r["order"].As<int>()).ToHashSet().SetEquals([1,2]),
                    "same-type parallel edges keep separate properties");
                var reverse = graph.Relationships.Values.Single(r => r.Type == "RETURNS_TO");
                var loop = graph.Relationships.Values.Single(r => r.Type == "REPEATS");
                Check(reverse.StartNodeElementId == end.ElementId && reverse.EndNodeElementId == start.ElementId &&
                    loop.StartNodeElementId == start.ElementId && loop.EndNodeElementId == start.ElementId, "opposite edge and loop direction");
            }

            await LinkedResults(w, suite == "--selection-examples");
            await CoactorChecks(w);
            await Neighbors(w, suite != "--movies", suite == "--selection-examples");
            await w.RunAsync("UNWIND [] AS n RETURN n", "{}");
            Check(w.Result is { Rows.Count: 0, HasMore: false } && w.Result.Columns.Single() == "n", "empty with schema");
            foreach (var n in new[] { GraphConnection.RowLimit, GraphConnection.RowLimit + 1 })
            {
                await w.RunAsync("UNWIND range(1,$n) AS n RETURN n", "{\"n\":" + n + "}");
                Check(w.Result?.Rows.Count == GraphConnection.RowLimit && w.Result.HasMore == (n > GraphConnection.RowLimit), "one record lookahead");
            }
            await w.RunAsync("MATCH (", "{}");
            Check(w.Connected != null && w.Phase == QueryPhase.Failed && w.Result == null, "syntax error retains connection");
            await w.RunAsync("RETURN $n AS n", "{bad");
            Check(w.Phase == QueryPhase.Failed && w.Result == null, "malformed parameters recoverable");
            await w.RunAsync("CREATE (:CyanGraphReadOnlyProbe) RETURN 1", "{}");
            Check(w.Phase == QueryPhase.Failed && w.Result == null && w.Connected != null, "read-only fixture rejects writes");

            await using var monitor = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));
            // Identify this test's statement so unrelated queries are never counted.
            var marker = "cg_check_" + Guid.NewGuid().ToString("N");
            string slow = $"/* {marker} */ UNWIND range(1,6000) AS x UNWIND range(1,6000) AS y RETURN sum(sin(x + y)) AS total";
            async Task<int> Active()
            {
                await using var s = monitor.AsyncSession(c => c.WithDatabase("system"));
                var cursor = await s.RunAsync("SHOW TRANSACTIONS YIELD currentQuery, metaData WHERE currentQuery CONTAINS $marker AND metaData.cyanGraphRequest IS NOT NULL RETURN count(*) AS n", new { marker });
                return (await cursor.SingleAsync())[0].As<int>();
            }
            async Task Started()
            {
                for (var i = 0; i < 80; i++)
                {
                    if (await Active() == 1) return;
                    await Task.Delay(25);
                }
                throw new Exception("Slow query was not observed on the server.");
            }

            var first = w.RunAsync(slow, "{}"); await Started();
            var timer = Stopwatch.StartNew(); w.Stop();
            Check(w.Phase is QueryPhase.Stopping or QueryPhase.Stopped, "Stop feedback");
            await first.WaitAsync(TimeSpan.FromSeconds(8));
            Check(w.Phase == QueryPhase.Stopped && await Active() == 0 && w.Result == null && timer.Elapsed < TimeSpan.FromSeconds(8), "server stopped before timeout");

            var old = w.RunAsync(slow, "{}"); await Started();
            var replacement = w.RunAsync("RETURN 42 AS answer", "{}"); await Task.WhenAll(old, replacement);
            Check(w.Result?.Rows.Single()["answer"].As<int>() == 42 && await Active() == 0, "superseded query cannot replace new result");

            old = w.RunAsync(slow, "{}"); await Started();
            await w.ConnectAsync(uri, "system", user, password); await old;
            Check(w.Connected?.Database == "system" && w.Result == null && await Active() == 0, "database switch discards late result");
        }
        finally { await w.DisconnectAsync(); }
        Check(w.Connected == null && w.Result == null && w.Phase == QueryPhase.Disconnected, "disconnect clears session");
    }
}
