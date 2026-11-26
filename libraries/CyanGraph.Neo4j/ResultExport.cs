using System.Text;
using System.Text.Json;

namespace CyanGraph.Data;

public enum ResultFileFormat { Csv, Json }

/// <summary>Writes the fetched records without opening a database connection.</summary>
public static class ResultExport
{
    public static void Save(string path, GraphResult result, ResultFileFormat format, bool overwrite,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string temporary = Path.Combine(directory, ".cyangraph-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                switch (format)
                {
                    case ResultFileFormat.Csv: WriteCsv(file, result, token); break;
                    case ResultFileFormat.Json: JsonSerializer.Serialize(file, NamedRows(result, token), RawResult.Options); break;
                    default: throw new ArgumentOutOfRangeException(nameof(format));
                }
                file.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite);
        }
        finally { File.Delete(temporary); }
    }

    static IEnumerable<Dictionary<string, object?>> NamedRows(GraphResult result, CancellationToken token)
    {
        foreach (var record in result.Rows)
        {
            token.ThrowIfCancellationRequested();
            var row = new Dictionary<string, object?>(result.Columns.Count, StringComparer.Ordinal);
            for (int i = 0; i < result.Columns.Count; i++)
                row.Add(result.Columns[i], RawResult.Value(record[i], token, browserRecord: true));
            yield return row;
        }
    }

    static void WriteCsv(Stream file, GraphResult result, CancellationToken token)
    {
        using var csv = new StreamWriter(file, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\r\n" };
        csv.WriteLine(string.Join(',', result.Columns.Select(Quote)));
        foreach (var record in result.Rows)
        {
            token.ThrowIfCancellationRequested();
            for (int i = 0; i < result.Columns.Count; i++)
            {
                if (i > 0) csv.Write(',');
                if (record[i] is not { } value) { csv.Write(@"\N"); continue; }
                string text = value as string ?? GraphText.Format(value);
                // Reserve \N for NULL. Escape an initial backslash in a non-null field.
                csv.Write(Quote(text.StartsWith('\\') ? "\\" + text : text));
            }
            csv.WriteLine();
        }
    }

    static string Quote(string text) => text.Length == 0 || text.IndexOfAny([',', '"', '\r', '\n']) >= 0
        ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
}
