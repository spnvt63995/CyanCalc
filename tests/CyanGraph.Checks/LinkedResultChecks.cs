using System.Text.Json;
using CyanGraph.Data;
using Neo4j.Driver;

static partial class Program
{
    static async Task LinkedResults(QueryWorkspace workspace, bool sameNameFixture)
    {
        async Task<GraphResult> Run(string statement)
        {
            await workspace.RunAsync(statement, "{}");
            return workspace.Result ?? throw new InvalidOperationException(workspace.Status);
        }
        static GraphElementKey Key(object value) => GraphElementKey.FromValue(value)!.Value;
        static bool Matches(ResultGraph graph, GraphElementKey key, params ResultCell[] cells) =>
            graph.CellsFor(key).ToHashSet().SetEquals(cells);

        var cast = await Run("""
            MATCH (actor:Person)-[credit:ACTED_IN]->(film:Movie {title:'The Matrix'})
            RETURN actor, credit, credit.roles AS roles, film ORDER BY actor.name
            """);
        var graph = ResultGraph.From(cast);
        var first = cast.Rows[0];
        Check(cast.Columns.SequenceEqual(["actor","credit","roles","film"]) && cast.Rows.Count == 5, "cast column and row order");
        Check(graph.Nodes.Count == 6 && graph.Relationships.Count == 5, "six cast nodes and five credits");
        Check(first[0].As<INode>()["name"].As<string>() == "Carrie-Anne Moss" && Matches(graph,Key(first[0]),new ResultCell(0,0)), "actor cell");
        Check(Matches(graph,Key(first[1]),new ResultCell(0,1)) && GraphElementKey.FromValue(first[2]) == null, "credit and scalar roles remain distinct");
        Check(Matches(graph,Key(first[3]),Enumerable.Range(0,5).Select(r => new ResultCell(r,3)).ToArray()), "one movie in five film cells");

        static JsonElement Raw(GraphResult result)
        {
            using var doc = JsonDocument.Parse(RawResult.Format(result));
            return doc.RootElement.Clone();
        }
        var json = Raw(cast);
        var rows = json.GetProperty("rows");
        Check(json.GetProperty("columns").EnumerateArray().Select(c => c.GetString()).SequenceEqual(cast.Columns) &&
            rows.GetArrayLength() == 5 && !json.GetProperty("hasMore").GetBoolean(), "raw schema, rows and completeness");
        Check(rows[0][0].GetProperty("elementId").GetString() == Key(first[0]).Id &&
            rows[0][0].GetProperty("properties").GetProperty("born").GetInt64()==1967, "raw node identity and properties");
        var credit = rows[0][1];
        Check(credit.GetProperty("relationshipType").GetString()=="ACTED_IN" &&
            credit.GetProperty("startNodeElementId").GetString()==Key(first[0]).Id &&
            credit.GetProperty("endNodeElementId").GetString()==Key(first[3]).Id && rows[0][2][0].GetString()=="Trinity", "raw relationship endpoints and scalar list");

        var nested = await Run("""
            MATCH p=(f:Movie {title:'The Matrix'})<-[r:ACTED_IN]-(a:Person {name:'Carrie-Anne Moss'})
            UNWIND [1,2] AS i RETURN a, {items:[[p,a,r]]} AS nested, CASE WHEN i=2 THEN r END AS extra ORDER BY i
            """);
        var nestedGraph = ResultGraph.From(nested);
        Check(Matches(nestedGraph,Key(first[0]), new(0,0),new(0,1),new(1,0),new(1,1)), "noncontiguous cells, repeated within-cell IDs counted once");
        Check(Matches(nestedGraph,Key(first[1]),new(0,1),new(1,1),new(1,2)) &&
            Matches(nestedGraph,Key(first[3]),new(0,1),new(1,1)), "endpoint references do not become node cells");
        var path = Raw(nested).GetProperty("rows")[0][1].GetProperty("items")[0][0];
        Check(path.GetProperty("nodes")[0].GetProperty("elementId").GetString()==Key(first[3]).Id &&
            path.GetProperty("relationships")[0].GetProperty("startNodeElementId").GetString()==Key(first[0]).Id, "raw traversed path and stored direction");

        var names = await Run("MATCH (a:Person)-[:ACTED_IN]->(:Movie {title:'The Matrix'}) RETURN a.name AS name ORDER BY name");
        var nameRows = Raw(names).GetProperty("rows");
        Check(ResultGraph.From(names).Nodes.Count == 0 && nameRows[0][0].GetString()=="Carrie-Anne Moss" &&
            nameRows[4][0].GetString()=="Laurence Fishburne", "scalar-only rows preserve values and order");
        var empty = Raw(await Run("UNWIND [] AS x RETURN x"));
        Check(empty.GetProperty("rows").GetArrayLength()==0 && empty.GetProperty("columns")[0].GetString()=="x", "empty raw results retain column names");
        var limited = Raw(await Run("UNWIND range(1,201) AS n RETURN n"));
        Check(limited.GetProperty("rows").GetArrayLength()==200 && limited.GetProperty("hasMore").GetBoolean(), "raw reports preview limit");

        var special = await workspace.Connected!.QueryAsync("""
            RETURN 9223372036854775807 AS integer, null AS missing, {data:[true,'가다']} AS map,
                   date('1999-03-31') AS day, point({x:1,y:2}) AS loc, $bytes AS bytes, $nan AS nan
            """, new Dictionary<string,object> { ["bytes"]=new byte[]{0,255}, ["nan"]=double.NaN });
        var values = Raw(special).GetProperty("rows")[0];
        Check(values[0].GetInt64()==long.MaxValue && values[1].ValueKind==JsonValueKind.Null &&
            values[2].GetProperty("data")[1].GetString()=="가다", "raw exact integer, null and nested Unicode");
        Check(values[3].GetProperty("value").GetString()=="1999-03-31" &&
            values[4].GetProperty("coordinates").GetArrayLength()==2 && values[5].GetProperty("base64").GetString()=="AP8=" &&
            values[6].GetProperty("value").GetString()=="NaN", "raw temporal, spatial, binary and nonfinite values");
        if (!sameNameFixture) return;
        var twins = await Run("MATCH (n:SelectionExample) RETURN n, n.name AS name, n.marker AS marker ORDER BY n.marker");
        var twinGraph = ResultGraph.From(twins);
        var left = Key(twins.Rows[0][0]); var right = Key(twins.Rows[1][0]);
        Check(left != right && twins.Rows[0][1].Equals(twins.Rows[1][1]) && !twins.Rows[0][2].Equals(twins.Rows[1][2]), "two nodes have equal names and distinct identities");
        Check(Matches(twinGraph,left,new ResultCell(0,0)) && Matches(twinGraph,right,new ResultCell(1,0)), "names and their scalar copies do not link");
        var later = await Run("MATCH (n:SelectionExample) RETURN n ORDER BY n.marker DESC LIMIT 1");
        var laterGraph = ResultGraph.From(later);
        Check(laterGraph.CellsFor(left).Count == 0 && Matches(laterGraph,right,new ResultCell(0,0)) &&
            Matches(twinGraph,right,new ResultCell(1,0)), "each query has its own cell index");
    }
}
