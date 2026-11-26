using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyanGraph.Data;

public sealed record SavedQuestion(Guid Id, string Name, string Statement);
public sealed record QueryHistoryEntry(DateTimeOffset Time, string Database, string Statement);
public sealed record QuestionLibraryContents(int Version, SavedQuestion[] Questions, QueryHistoryEntry[] History);

/// <summary>Local Cypher definitions and recent submissions. Bindings and results live in the query window.</summary>
public sealed class QuestionLibrary(string path)
{
    public const int HistoryLimit = 100;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, AllowDuplicateProperties = false, RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    readonly string file = Path.GetFullPath(path);

    public async Task<QuestionLibraryContents> ReadAsync()
    {
        try
        {
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var data = await JsonSerializer.DeserializeAsync<QuestionLibraryContents>(input, Json).ConfigureAwait(false);
            if (data is not { Version: 1, Questions: not null, History: not null } || data.Questions.Any(q => q == null || q.Id == Guid.Empty ||
                    string.IsNullOrWhiteSpace(q.Name) || string.IsNullOrWhiteSpace(q.Statement)) ||
                data.Questions.DistinctBy(q => q.Id).Count() != data.Questions.Length ||
                data.Questions.DistinctBy(q => q.Name, StringComparer.OrdinalIgnoreCase).Count() != data.Questions.Length ||
                data.History.Any(h => h == null || string.IsNullOrWhiteSpace(h.Database) || string.IsNullOrWhiteSpace(h.Statement)))
                throw new InvalidDataException("The question library has invalid or unsupported contents.");
            return data;
        }
        catch (FileNotFoundException) { return new(1, [], []); }
        catch (DirectoryNotFoundException) { return new(1, [], []); }
    }

    public Task RecordAsync(QueryHistoryEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Statement);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Database);
        return UpdateAsync(data => data with { History = data.History.Prepend(entry)
            .OrderByDescending(h => h.Time).Take(HistoryLimit).ToArray() });
    }

    public async Task<SavedQuestion> SaveAsync(string name, string statement, SavedQuestion? original = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        name = name.Trim();
        if (name.Length == 0 || name.Any(char.IsControl)) throw new ArgumentException("Enter a question name on one line.");
        var saved = new SavedQuestion(original?.Id ?? Guid.NewGuid(), name, statement);
        await UpdateAsync(data =>
        {
            if (original != null && data.Questions.SingleOrDefault(q => q.Id == original.Id) != original)
                throw new InvalidOperationException("This question changed. Reopen it before saving your edits.");
            if (data.Questions.Any(q => q.Id != saved.Id && string.Equals(q.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A question with this name already exists. Choose another name.");
            return data with { Questions = data.Questions.Where(q => q.Id != saved.Id).Append(saved)
                .OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase).ToArray() };
        }).ConfigureAwait(false);
        return saved;
    }

    async Task UpdateAsync(Func<QuestionLibraryContents, QuestionLibraryContents> update)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        // Merge against the latest library while holding the same lock across app instances.
        using var lease = await AcquireWriteLockAsync().ConfigureAwait(false);
        var updated = update(await ReadAsync().ConfigureAwait(false));
        string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, PrivateOptions(FileMode.CreateNew)))
            {
                await JsonSerializer.SerializeAsync(output, updated, Json).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, file, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    async Task<FileStream> AcquireWriteLockAsync()
    {
        for (int retry = 0; ; retry++)
        {
            try { return new FileStream(file + ".lock", PrivateOptions(FileMode.OpenOrCreate)); }
            catch (IOException) when (retry < 40) { await Task.Delay(50).ConfigureAwait(false); }
        }
    }

    static FileStreamOptions PrivateOptions(FileMode mode)
    {
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return options;
    }
}
