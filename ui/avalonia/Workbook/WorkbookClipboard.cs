using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    // The web uses application/json. macOS names the same native format by UTI.
    private static readonly DataFormat<string> WorkbookClipboardFormat = DataFormat.CreateStringPlatformFormat(
        OperatingSystem.IsMacOS() ? "public.json" : "application/json");
    private readonly string _clipboardId = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
    private long _latestClipboardWrite;
    private JsonElement? _paintStyles;

    public async Task CopyAsync(bool cut = false)
    {
        try
        {
            if (cut && !CanEdit) return;
            if (!CommitEdit() || Session is null || Snapshot is null || TopLevel.GetTopLevel(this) is not { Clipboard: { } clipboard } owner) return;
            var session = Session;
            var area = CellArea.FromView(Snapshot.View);
            var data = session.CopyToClipboard();
            // Keep Workbook.tsx's payload: clipboardId is metadata, not a gate on cut/paste.
            var payload = JsonSerializer.Serialize(new
            {
                type = cut ? "cut" : "copy", area = data.GetProperty("range"),
                sheetData = ClipboardJson(data.GetProperty("data")), sheet = session.GetSelectedSheet(), clipboardId = _clipboardId
            });
            var transfer = new DataTransfer();
            var csv = data.GetProperty("csv").GetString()!;
            var item = DataTransferItem.CreateText(cut ? csv : csv.Trim());
            item.Set(WorkbookClipboardFormat, payload);
            transfer.Add(item);
            var write = ++_latestClipboardWrite;
            await clipboard.SetDataAsync(transfer);
            // Only the latest OS write updates this view's marker/focus. The data
            // actually published by the OS always retains its original copy/cut type.
            if (write != _latestClipboardWrite || !ReferenceEquals(session, Session)
                || !ReferenceEquals(owner, TopLevel.GetTopLevel(this)) || cut && !CanEdit) return;
            if (cut) State.CutRange = area;
            Worksheet.InvalidateVisual();
            if (State.Editing is null) FocusCell();
        }
        catch (Exception exception) { Report(Text.Key("native.copy_failed", ("error", exception.Message))); }
    }

    public async Task PasteAsync()
    {
        try
        {
            if (!CanEdit) return;
            if (!CommitEdit() || Session is null || Snapshot is null || TopLevel.GetTopLevel(this) is not { Clipboard: { } clipboard } owner) return;
            // A native clipboard read must not write into a replacement document,
            // detached view, read-only view, or a newly started TextBox/IME draft.
            var session = Session;
            using var transfer = await clipboard.TryGetDataAsync();
            if (!CanPaste() || transfer is null) return;
            var raw = await transfer.TryGetValueAsync(WorkbookClipboardFormat);
            if (!CanPaste()) return;
            if (!string.IsNullOrEmpty(raw))
            {
                ClearMarker();
                using var document = JsonDocument.Parse(raw);
                var source = document.RootElement;
                // Match onPaste: use the stored sheet/area and type verbatim. The
                // upstream engine owns merge geometry, overlap checks and Undo.
                session.PasteFromClipboard(source.GetProperty("sheet").GetInt32(),
                    source.GetProperty("area").Deserialize<int[]>()!, source.GetProperty("sheetData"),
                    source.GetProperty("type").GetString() == "cut");
            }
            else
            {
                var text = await transfer.TryGetTextAsync();
                // Even an empty response must not steal a new draft's focus.
                if (!CanPaste()) return;
                ClearMarker();
                // Like the web's paste event, use the selection at dispatch time.
                if (!string.IsNullOrEmpty(text)) session.PasteCsvText(CellArea.FromView(session.GetSelectedView()), text);
            }
            FocusCell();

            bool CanPaste() => CanEdit && ReferenceEquals(session, Session)
                && ReferenceEquals(owner, TopLevel.GetTopLevel(this)) && State.Editing is null;
            void ClearMarker()
            {
                State.CutRange = null;
                Worksheet.InvalidateVisual();
            }
        }
        catch (Exception exception) { ShowPasteError(exception.Message); }
    }

    private static JsonNode ClipboardJson(JsonElement value)
    {
        // WASM exposes absent optional fields as undefined, so JSON.stringify in
        // Workbook.tsx omits them. The native JSON ABI represents those as null.
        var node = JsonNode.Parse(value.GetRawText())!;
        OmitAbsentFields(node);
        return node;

        static void OmitAbsentFields(JsonNode? current)
        {
            if (current is JsonObject obj)
                foreach (var pair in obj.ToArray())
                {
                    if (pair.Value is null) obj.Remove(pair.Key);
                    else OmitAbsentFields(pair.Value);
                }
            else if (current is JsonArray array)
                foreach (var child in array) OmitAbsentFields(child);
        }
    }

    private void StartFormatPainter()
    {
        if (!CanEdit) return;
        Run(() =>
        {
            if (!CommitEdit()) return;
            if (Session is null || Snapshot is null) return;
            var area = CellArea.FromView(Snapshot.View);
            _paintStyles = Session.Query("copy_styles", new { area });
            State.IsFormatPainter = true;
            Worksheet.InvalidateVisual();
        });
    }
    private void ApplyFormatPainter()
    {
        if (!State.IsFormatPainter || _paintStyles is not { } styles) return;
        State.IsFormatPainter = false;
        if (!CanEdit) return;
        Session?.Execute("paste_styles", new { styles });
    }
}
