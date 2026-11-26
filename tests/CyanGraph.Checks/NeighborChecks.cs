using CyanGraph.Data;
using Neo4j.Driver;

static partial class Program
{
    static async Task Neighbors(QueryWorkspace workspace, bool graphExamples, bool selectionExamples)
    {
        async Task<GraphScene> Run(string cypher)
        {
            await workspace.RunAsync(cypher, "{}");
            return workspace.Scene ?? throw new InvalidOperationException(workspace.Status);
        }
        static GraphElementKey Node(string id) => new(GraphElementKind.Node, id);
        const string movieQuery = "MATCH (movie:Movie {title:'The Matrix'}) RETURN movie";
        var scene = await Run(movieQuery);
        var original = workspace.Result!;
        var originalJson = RawResult.Format(original);
        string movie = scene.Original.Nodes.Keys.Single();
        Check(scene.Visible.Nodes.Count == 1 && scene.Visible.Relationships.Count == 0 && original.Rows.Count == 1, "initial movie and original row");

        var expand = scene.ExpandAsync(movie);
        var repeat = scene.ExpandAsync(movie);
        await Task.WhenAll(expand, repeat);
        Check(scene.Failure == null && scene.ProgressFor(movie) is { Loaded: 8, HasMore: false }, "entire Matrix neighborhood fits in one page");
        Check(scene.Visible.Nodes.Count == 9 && scene.Visible.Relationships.Count == 8, "eight distinct neighbors");
        var complete = scene.Visible;
        await scene.ExpandAsync(movie);
        Check(ReferenceEquals(complete, scene.Visible), "completed and overlapping expansions cannot add duplicates");
        var credits = complete.Relationships.Values.ToArray();
        Check(credits.All(edge => edge.EndNodeElementId == movie) &&
            credits.Count(e => e.Type == "ACTED_IN") == 5 && credits.Count(e => e.Type == "DIRECTED") == 2 &&
            credits.Count(e => e.Type == "PRODUCED") == 1, "stored incoming directions, types and counts");
        var carrie = complete.Nodes.Values.Single(n => n.Entity!.Properties.TryGetValue("name", out var name) && name.Equals("Carrie-Anne Moss"));
        var role = credits.Single(e => e.StartNodeElementId == carrie.ElementId);
        var actor = Node(carrie.ElementId);
        var credit = new GraphElementKey(GraphElementKind.Relationship, role.ElementId);
        Check(scene.Original.Nodes.Count == 1 && !scene.Original.Contains(actor) &&
            complete.CellsFor(actor).Count == 0 && complete.CellsFor(Node(movie)).Single() == new ResultCell(0, 0),
            "expanded nodes do not acquire query cells");
        scene.Hide(actor);
        Check(scene.Visible.Nodes.Count == 8 && scene.Visible.Relationships.Count == 7 && !scene.Visible.Contains(credit) &&
            scene.LastHidden() == new HiddenElement(actor, 1, 1), "hide actor and incident edge, preview undo");

        var lookup = workspace.CreateSeparateQuery();
        await lookup.RunAsync("""
            MATCH (a:Person {name:'Carrie-Anne Moss'})-[r:ACTED_IN]->(m:Movie {title:'The Matrix'})
            RETURN a.name AS actor, m.title AS film, r.roles AS roles
            """, "{}");
        var verify = lookup.Result!.Rows.Single();
        Check(verify["actor"].As<string>() == "Carrie-Anne Moss" && verify["film"].As<string>() == "The Matrix" &&
            verify["roles"].As<List<string>>().SequenceEqual(["Trinity"]) && !scene.Visible.Contains(actor),
            "separate read returns Trinity while original graph is hidden");
        await lookup.DisconnectAsync();
        Check(ReferenceEquals(workspace.Result, original) && workspace.Connected != null && RawResult.Format(original) == originalJson,
            "closing independent result preserves original query and driver");
        scene.RestoreLast();
        Check(complete.Nodes.Keys.ToHashSet().SetEquals(scene.Visible.Nodes.Keys) &&
            complete.Relationships.Keys.ToHashSet().SetEquals(scene.Visible.Relationships.Keys) &&
            scene.Visible.Relationships[role.ElementId]["roles"].As<List<string>>().Single() == "Trinity", "undo restores exact identities and properties");
        scene.Hide(credit); scene.Hide(actor);
        Check(scene.LastHidden() == new HiddenElement(actor, 1, 0), "independently hidden edge stays hidden when restoring its endpoint");
        scene.RestoreLast();
        Check(scene.Visible.Contains(actor) && !scene.Visible.Contains(credit) && scene.LastHidden() == new HiddenElement(credit, 0, 1),
            "undo order and standalone edge preview");
        scene.RestoreLast();
        scene.Hide(Node(movie));
        Check(scene.Visible.Nodes.Count == 8 && scene.Visible.Relationships.Count == 0 &&
            original.Rows[0][0].As<INode>().ElementId == movie, "hiding a query node keeps the original cell");
        scene.RestoreLast();
        Check(scene.Visible.Relationships.Count == 8 && scene.LastHidden() == null, "all eight incident edges return");

        scene = await Run("MATCH (n:Person {name:'Tom Hanks'}) RETURN n");
        string tom = scene.Original.Nodes.Keys.Single();
        await scene.ExpandAsync(tom);
        Check(scene.ProgressFor(tom) is { Loaded: GraphConnection.NeighborPageSize, HasMore: true } &&
            scene.Visible.Relationships.Count == GraphConnection.NeighborPageSize, "first page is bounded and advertises more");
        var pageOne = scene.Visible.Relationships.Keys.ToHashSet();
        var firstNeighbor = Node(scene.Visible.Nodes.Keys.First(id => id != tom));
        scene.Hide(firstNeighbor);
        await scene.ExpandAsync(tom);
        Check(!scene.Visible.Contains(firstNeighbor) && scene.ProgressFor(tom) is { Loaded: 13, HasMore: false }, "next page preserves a prior hide");
        scene.RestoreLast();
        var all = await workspace.Connected!.QueryAsync("MATCH (:Person {name:'Tom Hanks'})-[r]-() RETURN DISTINCT r", new Dictionary<string, object>());
        var ids = all.Rows.Select(row => row[0].As<IRelationship>().ElementId).ToHashSet();
        Check(pageOne.IsProperSubsetOf(ids) && ids.SetEquals(scene.Visible.Relationships.Keys) &&
            scene.Visible.Nodes.Count == 13, "all pages equal independent database query, including parallel credits");
        if (graphExamples)
        {
            var example = await Run("MATCH (n:Example {name:'가다'}) RETURN n");
            var start = example.Original.Nodes.Keys.Single();
            await example.ExpandAsync(start);
            Check(example.Visible.Nodes.Count == 2 && example.Visible.Relationships.Count == 4 &&
                example.ProgressFor(start) is { Loaded: 4, HasMore: false }, "parallel, reverse and self edges loaded once");
            var loop = example.Visible.Relationships.Values.Single(e => e.StartNodeElementId == e.EndNodeElementId);
            Check(loop.StartNodeElementId == start && example.Visible.Relationships.Values.Count(e => e.Type == "RELATED") == 2,
                "parallel edge identity and self direction retained");
            example.Hide(Node(start));
            Check(example.LastHidden() == new HiddenElement(Node(start), 1, 4) && example.Visible.Relationships.Count == 0,
                "a loop contributes one hidden relationship");
            example.RestoreLast();
        }
        if (selectionExamples)
        {
            var lonely = await Run("MATCH (n:SelectionExample) RETURN n ORDER BY n.marker LIMIT 1");
            string isolated = lonely.Visible.Nodes.Keys.Single();
            await lonely.ExpandAsync(isolated);
            Check(lonely.ProgressFor(isolated) is { Loaded: 0, HasMore: false } && lonely.Visible.Nodes.Count == 1, "isolated node completes with zero neighbors");
            lonely.Hide(Node(isolated));
            Check(lonely.Visible.Nodes.Count == 0 && lonely.LastHidden() is { NodesToRestore: 1, RelationshipsToRestore: 0 },
                "last visible node can be restored from an empty canvas");
            lonely.RestoreLast();
        }
        var stale = await Run(movieQuery);
        var pending = stale.ExpandAsync(stale.Original.Nodes.Keys.Single());
        await Task.WhenAll(pending, workspace.RunAsync("RETURN 1 AS current", "{}"));
        Check(workspace.Scene!.Visible.Nodes.Count == 0 && workspace.Result!.Columns.Single() == "current" &&
            workspace.Scene.LastHidden() == null, "new query retires pending expansion and hidden history");
        await stale.ExpandAsync(movie);
        Check(stale.LoadingNode == null && workspace.Scene != stale, "retired graph cannot start a new read");
    }
}
