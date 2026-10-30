using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CyanCalc.Controls;

namespace CyanCalc.Desktop;

// Application host corresponding to app.ironcalc.com's App.tsx. WorkbookView
// stays embeddable; file dialogs and the local library belong to this example.
public sealed class WorkbookWindow : Window
{
    private readonly WorkbookLibrary _library;
    private readonly WorkbookShell _shell = new();
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(1) };
    private UserModel? _session;
    private Guid? _selected;
    private long _savedRevision = -1;
    private bool _busy, _initializing = true;
    private static readonly HttpClient TemplatesClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public WorkbookWindow() : this(WorkbookLibrary.DefaultPath) { }
    public WorkbookWindow(string libraryPath)
    {
        _library = new WorkbookLibrary(libraryPath);
        Width = 1240; Height = 790;
        Content = _shell; Title = "CyanCalc";
        WorkbookLinkHost.Bind(_shell.Workbook, this, _shell.ShowLocalizedMessage);
        _shell.Workbook.Error += (_, error) => _shell.ShowMessage(error);
        _shell.Workbook.LocalizedError += (_, error) => _shell.ShowLocalizedMessage(error);
        _shell.Workbook.ExportPngRequested += (_, _) => _ = RunAsync(ExportPngAsync);
        _shell.Workbook.DocumentChanged += (_, _) => RefreshTitle();
        _shell.Requested += (_, request) => _ = RunAsync(() => HandleAsync(request));
        ApplyPreferences();
        _autosave.Tick += (_, _) =>
        {
            if (_busy || _initializing) return;
            try { SaveCurrent(); }
            catch (Exception error) { _shell.ShowLocalizedMessage(_shell.Text.Key("native.save_failed", ("error", error.Message))); }
        };
        Opened += async (_, _) =>
        {
            await RunAsync(async () =>
            {
                if (_library.StartupSelection is { } id) SelectWorkbook(id);
                else
                {
                    await TemplatesAsync(welcome: true);
                    if (_session is null) NewWorkbook();
                }
            });
            _initializing = false; _autosave.Start(); _shell.Workbook.FocusCell();
        };
        Closing += (_, e) =>
        {
            if (_busy) { e.Cancel = true; return; }
            try { CommitAndSave(); }
            catch (Exception error) { e.Cancel = true; _shell.ShowLocalizedMessage(_shell.Text.Key("native.close_save_failed", ("error", error.Message))); }
        };
        Closed += (_, _) => { _autosave.Stop(); _shell.Workbook.Session = null; _session?.Dispose(); _library.Dispose(); };
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        try { await action(); }
        catch (Exception error) { _shell.ShowMessage(error.Message); }
        finally { _busy = false; }
    }
    private async Task HandleAsync(WorkbookShellRequest request)
    {
        switch (request.Action)
        {
            case "new": NewWorkbook(); break;
            case "select" when request.Workbook is { } id: SelectWorkbook(id); break;
            case "rename" when _session is not null:
                _session.SetName(request.Value ?? ""); SaveCurrent(); RefreshLibrary(); break;
            case "pin" when request.Workbook is { } id:
                _library.TogglePin(id); RefreshLibrary(); break;
            case "duplicate" when request.Workbook is { } id:
                CommitAndSave(); _library.Duplicate(id); RefreshLibrary(); break;
            case "delete" when (request.Workbook ?? _selected) is { } id: await DeleteAsync([id]); break;
            case "deleteMany": await DeleteAsync(request.Workbooks ?? []); break;
            case "templates": await TemplatesAsync(); break;
            case "import": await ImportAsync(); break;
            case "export": await ExportAsync(); break;
            case "language" when request.Value is { } language:
                CommitAndSave();
                _library.SetPreferences(_library.Settings with { Language = language });
                _session?.SetLanguage(_library.LanguageId); _session?.SetLocale(_library.Locale);
                ApplyPreferences(); SaveCurrent(); break;
            case "appearance" when request.Value is { } appearance:
                _library.SetPreferences(_library.Settings with { Appearance = appearance }); ApplyPreferences(); break;
            case "dismissNotice":
                _library.SetPreferences(_library.Settings with { NoticeDismissed = true }); ApplyPreferences(); break;
            case "link" when request.Value is { } link: await Launcher.LaunchUriAsync(new Uri(link)); break;
        }
    }
    private void ApplyPreferences()
    {
        var preferences = _library.Settings;
        _shell.SetPreferences(preferences.Language, preferences.Appearance, preferences.NoticeDismissed);
        RequestedThemeVariant = _shell.SelectedTheme;
    }
    private void SaveCurrent()
    {
        if (_session is null || _selected is not { } id || _savedRevision == _session.Revision) return;
        _library.Save(id, _session); _savedRevision = _session.Revision;
        _session.FlushSendQueue(); RefreshTitle();
    }
    private void CommitAndSave()
    {
        CommitEdit();
        SaveCurrent();
    }
    private void CommitEdit()
    {
        if (!_shell.Workbook.CommitEdit()) throw new InvalidOperationException(_shell.Text["native.finish_edit_switch"]);
    }
    private void ReplaceSession(UserModel model, Guid id)
    {
        var previous = _session;
        _session = model; _selected = id; _savedRevision = model.Revision;
        _shell.Workbook.Session = model; previous?.Dispose();
        RefreshLibrary(); _shell.ShowMessage(null); _shell.Workbook.FocusCell();
    }
    private void NewWorkbook()
    {
        CommitAndSave(); AddWorkbook(_library.NewModel(_shell.Text["default_workbook_name"]));
    }
    private void AddWorkbook(UserModel model)
    {
        try { CommitAndSave(); var entry = _library.Insert(model); ReplaceSession(model, entry.Id); }
        catch { if (!ReferenceEquals(model, _session)) model.Dispose(); throw; }
    }
    private void SelectWorkbook(Guid id)
    {
        CommitAndSave();
        if (id == _selected) return;
        var model = _library.Load(id);
        try { _library.Select(id); ReplaceSession(model, id); }
        catch { model.Dispose(); throw; }
    }
    private void RefreshTitle() => Title = $"{(_session?.Revision != _savedRevision ? "• " : "")}{_session?.GetName() ?? "CyanCalc"} — CyanCalc";
    private void RefreshLibrary()
    {
        _shell.SetLibrary(_library.Entries.Select(e => new WorkbookListEntry(e.Id, e.Name, e.CreatedAt, e.Pinned)), _selected, _session?.GetName() ?? "");
        RefreshTitle();
    }
    private async Task DeleteAsync(Guid[] ids)
    {
        var entries = _library.Entries.Where(e => ids.Contains(e.Id)).Select(e => new WorkbookListEntry(e.Id, e.Name, e.CreatedAt, e.Pinned)).ToArray();
        if (!await _shell.ConfirmDeleteAsync(this, entries)) return;
        CommitAndSave();
        foreach (var id in ids) _library.Delete(id);
        if (_selected is { } selected && ids.Contains(selected))
        {
            _shell.Workbook.Session = null; _session?.Dispose(); _session = null; _selected = null;
            if (_library.StartupSelection is { } next) SelectWorkbook(next); else NewWorkbook();
        }
        _shell.ClearChecked(); RefreshLibrary();
    }
    private async Task TemplatesAsync(bool welcome = false)
    {
        CommitAndSave();
        var template = await _shell.ChooseTemplateAsync(this, welcome);
        if (template is null) return;
        if (template == "blank") { NewWorkbook(); return; }
        if (template == "import") { await ImportAsync(); return; }
        if (!WorkbookShell.Templates.Any(t => t.Id == template)) return;
        _shell.ShowLocalizedMessage(_shell.Text.Key("native.loading_template"));
        // Same public examples as the original /models route. Inbound only.
        byte[] bytes;
        _shell.IsEnabled = false;
        try { bytes = await TemplatesClient.GetByteArrayAsync($"https://app.ironcalc.com/models/{template}.ic"); }
        finally { _shell.IsEnabled = true; }
        var model = UserModel.FromBytes(bytes, _library.LanguageId);
        model.SetName(_shell.Text["welcome_dialog.templates." + template]); AddWorkbook(model);
    }
    private async Task ImportAsync()
    {
        CommitAndSave();
        await _shell.ImportAsync(this, ImportFileAsync);
    }
    private async Task ImportFileAsync(Avalonia.Platform.Storage.IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        UserModel model;
        if (file.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            model = new UserModel();
            try { model.LoadXlsx(memory.ToArray(), file.Name, _library.Locale, WorkbookLibrary.Timezone, _library.LanguageId); }
            catch { model.Dispose(); throw; }
        }
        else model = UserModel.FromBytes(memory.ToArray(), _library.LanguageId);
        AddWorkbook(model);
    }
    private async Task ExportAsync()
    {
        CommitEdit(); if (_session is null) return;
        using var file = await StorageProvider.SaveFilePickerAsync(new()
        {
            Title = _shell.Text["file_bar.file_menu.download"],
            SuggestedFileName = WorkbookSaveNames.SuggestedStem(_session.GetName()), DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType(_shell.Text["native.xlsx_workbook"]) { Patterns = ["*.xlsx"] }]
        });
        if (file is null) return;
        await WriteAsync(file, _session.ToXlsx()); _shell.ShowLocalizedMessage(_shell.Text.Key("native.exported", ("name", file.Name)));
    }
    private async Task ExportPngAsync()
    {
        CommitEdit();
        using var bytes = new MemoryStream(); _shell.Workbook.ExportSelectionToPng(bytes);
        using var file = await StorageProvider.SaveFilePickerAsync(new() { Title = _shell.Text["toolbar.selected_png"], SuggestedFileName = "cyancalc.png", DefaultExtension = "png", FileTypeChoices = [new(_shell.Text["native.png_image"]) { Patterns = ["*.png"] }] });
        if (file is not null) await WriteAsync(file, bytes.ToArray());
    }
    private static async Task WriteAsync(IStorageFile file, byte[] bytes)
    {
        if (file.TryGetLocalPath() is { } path)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllBytesAsync(temporary, bytes); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        else { await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); await stream.WriteAsync(bytes); }
    }
}
