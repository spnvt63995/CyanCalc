using System.Text.Json;

namespace CyanCalc.Desktop;

// Native counterpart of app.ironcalc.com's storage.ts. The reusable control
// knows nothing about this directory or how the host persists workbooks.
internal sealed class WorkbookLibrary : IDisposable
{
    internal sealed record Entry(Guid Id, string Name, DateTimeOffset CreatedAt, bool Pinned, Guid Blob);
    internal sealed record Preferences(Guid? Selected = null, string Language = "en-US", string Appearance = "system", bool NoticeDismissed = false);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _root;
    private readonly FileStream _lock;
    private readonly Dictionary<Guid, Entry> _entries = [];
    internal IReadOnlyCollection<Entry> Entries => _entries.Values;
    internal Preferences Settings { get; private set; }

    internal WorkbookLibrary(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
        _lock = new FileStream(Path.Combine(_root, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            foreach (var path in Directory.EnumerateFiles(_root, "*.workbook.json"))
            {
                var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path), Json)
                    ?? throw new InvalidDataException("Invalid workbook metadata: " + path);
                if (Path.GetFileName(path) != entry.Id.ToString("N") + ".workbook.json" || entry.Blob == Guid.Empty)
                    throw new InvalidDataException("Invalid workbook identity: " + path);
                _entries.Add(entry.Id, entry);
            }
            Settings = ReadPreferences(_root);
        }
        catch { _lock.Dispose(); throw; }
    }

    internal static string DefaultPath => Environment.GetEnvironmentVariable("CYANCALC_LIBRARY_PATH")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyanCalc", "Workbooks");
    internal static Preferences ReadPreferences(string root)
    {
        var path = Path.Combine(root, "preferences.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException("Invalid library preferences")
            : new Preferences(Language: DefaultLanguage());
    }
    internal Guid? StartupSelection => Settings.Selected is { } id && _entries.ContainsKey(id)
        ? id : _entries.Values.OrderByDescending(e => e.CreatedAt).Select(e => (Guid?)e.Id).FirstOrDefault();
    internal string LanguageId => Settings.Language.Split('-')[0];
    internal string Locale => Settings.Language switch { "en-US" => "en", "en-GB" => "en-GB", _ => LanguageId };
    internal static string Timezone => TimeZoneInfo.Local.Id;
    internal UserModel Load(Guid id) => UserModel.FromBytes(File.ReadAllBytes(BlobPath(_entries[id].Blob)), LanguageId);

    internal UserModel NewModel(string baseName)
    {
        // This is the upstream naming fallback, not a limit on document count.
        var names = _entries.Values.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        var name = Enumerable.Range(1, 49).Select(i => baseName + i).FirstOrDefault(n => !names.Contains(n)) ?? baseName + "-Infinity";
        try { return new UserModel(name, Locale, Timezone, LanguageId); }
        catch (IronCalcException) { return new UserModel(name, Locale, "UTC", LanguageId); }
    }

    internal Entry Insert(UserModel model, bool select = true)
    {
        var entry = Write(new Entry(Guid.NewGuid(), model.GetName(), DateTimeOffset.UtcNow, false, Guid.Empty), model.ToBytes());
        if (select) Select(entry.Id);
        return entry;
    }
    internal void Save(Guid id, UserModel model) => Write(_entries[id] with { Name = model.GetName() }, model.ToBytes());
    internal void Select(Guid id)
    {
        if (!_entries.ContainsKey(id)) throw new KeyNotFoundException("Workbook no longer exists");
        SetPreferences(Settings with { Selected = id });
    }
    internal void SetPreferences(Preferences settings)
    {
        WriteJson(Path.Combine(_root, "preferences.json"), settings);
        Settings = settings;
    }
    internal void TogglePin(Guid id)
    {
        var entry = _entries[id] with { Pinned = !_entries[id].Pinned };
        WriteJson(EntryPath(id), entry);
        _entries[id] = entry;
    }
    internal Entry Duplicate(Guid id)
    {
        using var copy = Load(id);
        var name = _entries[id].Name;
        var names = _entries.Values.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        var counter = 1;
        while (names.Contains($"{name} ({counter})")) counter++;
        copy.SetName($"{name} ({counter})");
        return Insert(copy, select: false);
    }
    internal void Delete(Guid id)
    {
        if (!_entries.TryGetValue(id, out var entry)) return;
        File.Delete(EntryPath(id));
        _entries.Remove(id);
        // An orphaned blob is harmless if cleanup is interrupted. Never remove
        // bytes before the metadata commit, including when a write fails.
        TryDelete(BlobPath(entry.Blob));
    }
    private Entry Write(Entry entry, byte[] bytes)
    {
        var next = entry with { Blob = Guid.NewGuid() };
        var blob = BlobPath(next.Blob);
        File.WriteAllBytes(blob, bytes);
        try { WriteJson(EntryPath(next.Id), next); }
        catch { TryDelete(blob); throw; }
        _entries[next.Id] = next;
        if (entry.Blob != Guid.Empty) TryDelete(BlobPath(entry.Blob));
        return next;
    }
    private string EntryPath(Guid id) => Path.Combine(_root, id.ToString("N") + ".workbook.json");
    private string BlobPath(Guid id) => Path.Combine(_root, id.ToString("N") + ".ic");
    private static void WriteJson<T>(string path, T value)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
            File.Move(temporary, path, true);
        }
        finally { TryDelete(temporary); }
    }
    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private static string DefaultLanguage() => System.Globalization.CultureInfo.CurrentUICulture.Name switch
    {
        "en-GB" => "en-GB",
        var name when name.StartsWith("es") => "es-ES",
        var name when name.StartsWith("fr") => "fr-FR",
        var name when name.StartsWith("de") => "de-DE",
        var name when name.StartsWith("it") => "it-IT",
        _ => "en-US"
    };
    public void Dispose() => _lock.Dispose();
}
