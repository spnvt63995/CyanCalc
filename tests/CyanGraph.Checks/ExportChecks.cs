using System.Text.Json;
using CyanGraph.Data;
using Microsoft.VisualBasic.FileIO;

static partial class Program
{
    const string Chai = """
        MATCH (supplier:Supplier)-[supply:SUPPLIES]->(product:Product)-[grouping:PART_OF]->(category:Category)
        WHERE product.productName = 'Chai'
        RETURN supplier, supply, product, grouping, category,
               supplier.companyName AS supplierName, product.productName AS productName, category.categoryName AS categoryName
        """;

    // Use independent parsers to reopen the completed files, including multiline CSV fields.
    static (List<string[]> Csv, JsonElement Json) ExportAndRead(string folder, GraphResult result)
    {
        string csv = Path.Combine(folder, "data.csv"), json = Path.Combine(folder, "data.json");
        ResultExport.Save(csv, result, ResultFileFormat.Csv, overwrite: true);
        ResultExport.Save(json, result, ResultFileFormat.Json, overwrite: true);
        using var reader = new TextFieldParser(csv) { TrimWhiteSpace = false };
        reader.SetDelimiters(",");
        var lines = new List<string[]>();
        while (!reader.EndOfData) lines.Add(reader.ReadFields()!);
        using var document = JsonDocument.Parse(File.ReadAllBytes(json));
        return (lines, document.RootElement.Clone());
    }

    static void CheckEmptyExport()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var empty = new GraphResult(["one", "쉼표, \"열\""], [], false);
            var files = ExportAndRead(folder.FullName, empty);
            Check(files.Csv.Single().SequenceEqual(empty.Columns) && files.Json.GetArrayLength() == 0, "empty output and quoted headers");
            string path = Path.Combine(folder.FullName, "data.json");
            var original = File.ReadAllBytes(path);
            try { ResultExport.Save(path, empty, ResultFileFormat.Csv, overwrite: false); throw new Exception("Unexpected overwrite"); }
            catch (IOException) { checks++; }
            try { ResultExport.Save(path, empty, (ResultFileFormat)99, overwrite: true); throw new Exception("Unexpected format"); }
            catch (ArgumentOutOfRangeException) { checks++; }
            try { ResultExport.Save(path, empty, ResultFileFormat.Json, true, new CancellationToken(true)); throw new Exception("Unexpected save"); }
            catch (OperationCanceledException) { checks++; }
            Check(File.ReadAllBytes(path).SequenceEqual(original) && folder.GetFiles().Length == 2, "failed or canceled export preserves the previous file and removes staging");
        }
        finally { folder.Delete(true); }
    }

    static void CheckMovieExport(GraphResult result)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var (csv, json) = ExportAndRead(folder.FullName, result);
            var row = json[0];
            Check(csv[1][1].Contains("Trinity") && row.GetProperty("role").GetProperty("properties").GetProperty("roles")[0].GetString() == "Trinity",
                "relationship properties survive both formats");
            var edge = row.GetProperty("path").GetProperty("relationships")[0];
            Check(edge.GetProperty("type").GetString() == "ACTED_IN" &&
                edge.GetProperty("startNodeElementId").GetString() == row.GetProperty("actor").GetProperty("elementId").GetString() &&
                edge.GetProperty("endNodeElementId").GetString() == row.GetProperty("film").GetProperty("elementId").GetString(),
                "nested path retains directed relationship IDs");
        }
        finally { folder.Delete(true); }
    }

    static async Task Northwind(string uri, string db, string user, string password)
    {
        var workspace = new QueryWorkspace();
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            await workspace.ConnectAsync(uri, db, user, password);
            async Task<GraphResult> Query(string text, string parameters = "{}")
            {
                await workspace.RunAsync(text, parameters);
                return workspace.Result ?? throw new InvalidOperationException(workspace.Status);
            }
            var result = await Query(Chai);
            var (csv, json) = ExportAndRead(folder.FullName, result);
            var row = json[0];
            string[] columns = ["supplier", "supply", "product", "grouping", "category", "supplierName", "productName", "categoryName"];
            Check(csv.Count == 2 && json.GetArrayLength() == 1 && csv[0].SequenceEqual(columns) &&
                row.EnumerateObject().Select(p => p.Name).SequenceEqual(columns), "Northwind file row count and all eight ordered columns");
            string[] names = ["Exotic Liquids", "Chai", "Beverages"];
            Check(csv[1].Skip(5).SequenceEqual(names) && columns.Skip(5).Select(c => row.GetProperty(c).GetString()).SequenceEqual(names),
                "three Northwind scalar names in both files");
            Check(csv[1][1] == "[:SUPPLIES]" && csv[1][3] == "[:PART_OF]", "CSV directed relationship types");
            foreach (var (column, type, start, end) in new[] { ("supply", "SUPPLIES", "supplier", "product"), ("grouping", "PART_OF", "product", "category") })
            {
                var edge = row.GetProperty(column);
                Check(edge.GetProperty("type").GetString() == type &&
                    edge.GetProperty("startNodeElementId").GetString() == row.GetProperty(start).GetProperty("elementId").GetString() &&
                    edge.GetProperty("endNodeElementId").GetString() == row.GetProperty(end).GetProperty("elementId").GetString(),
                    "JSON direction " + type);
            }
            const string value = "한글, \"인용\"\r\n둘째 줄";
            (csv, json) = ExportAndRead(folder.FullName, await Query("RETURN $v AS text, null AS missing, '' AS empty, $literal AS marker, true AS flag, 9223372036854775807 AS number",
                JsonSerializer.Serialize(new { v = value, literal = @"\N" })));
            Check(csv[1].SequenceEqual([value, @"\N", "", @"\\N", "true", "9223372036854775807"]), "Korean, comma, quotes, CRLF, null, empty and literal marker in CSV");
            Check(json[0].GetProperty("missing").ValueKind == JsonValueKind.Null && json[0].GetProperty("text").GetString() == value &&
                json[0].GetProperty("marker").GetString() == @"\N" && json[0].GetProperty("flag").GetBoolean() &&
                json[0].GetProperty("number").GetInt64() == long.MaxValue, "JSON native scalars and literal marker");
            var limited = await Query("UNWIND range(1,205) AS n RETURN n ORDER BY n");
            (csv, json) = ExportAndRead(folder.FullName, limited);
            Check(limited.HasMore && csv.Count == 201 && json.GetArrayLength() == 200 &&
                csv.Skip(1).Select(r => int.Parse(r[0])).SequenceEqual(Enumerable.Range(1, 200)) &&
                json.EnumerateArray().Select(r => r.GetProperty("n").GetInt32()).SequenceEqual(Enumerable.Range(1, 200)),
                "exports contain exactly the fetched 200 rows in order");
        }
        finally { await workspace.DisconnectAsync(); folder.Delete(true); }
    }
}
