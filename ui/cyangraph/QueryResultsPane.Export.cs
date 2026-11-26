using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CyanGraph.Data;

namespace CyanGraph;

sealed partial class QueryResultsPane
{
    readonly Button export = new() { Content = "Export ▾" };
    readonly MenuItem exportScope = new() { IsEnabled = false };
    readonly TextBlock exportMessage = CyanGraphView.Caption("");

    void AddExportMenu()
    {
        AutomationProperties.SetName(export, "Export results");
        var menu = new MenuFlyout();
        menu.Items.Add(exportScope);
        foreach (var format in new[] { ResultFileFormat.Csv, ResultFileFormat.Json })
        {
            var item = new MenuItem { Header = format.ToString().ToUpperInvariant() + "…" };
            item.Click += async (_, _) => await ExportAsync(format);
            menu.Items.Add(item);
        }
        export.Flyout = menu;
        tabs.Children.Add(export);
        exportMessage.IsVisible = false;
        exportMessage.Margin = new Thickness(16, 0, 16, 8);
        exportMessage.TextWrapping = TextWrapping.Wrap;
        CyanGraphView.Put(this, exportMessage, 1, 0);
    }

    static string ExportScope(GraphResult source)
    {
        int rows = source.Rows.Count, columns = source.Columns.Count;
        string range = source.HasMore ? $"First {rows} rows only" : $"{rows} {(rows == 1 ? "row" : "rows")}";
        return $"{range} · {columns} {(columns == 1 ? "column" : "columns")}";
    }

    void UpdateExportScope()
    {
        exportMessage.IsVisible = false;
        if (result == null) return;
        exportScope.Header = ExportScope(result);
        ToolTip.SetTip(export, ExportScope(result) + "\n" + string.Join(", ", result.Columns));
    }

    async Task ExportAsync(ResultFileFormat format)
    {
        if (!export.IsEnabled || result is not { } source || TopLevel.GetTopLevel(this) is not { } owner) return;
        export.IsEnabled = false;
        exportMessage.IsVisible = false;
        string extension = format.ToString().ToLowerInvariant();
        string title = format.ToString().ToUpperInvariant();
        string name = source.HasMore ? $"cyangraph-first-{source.Rows.Count}-rows" : "cyangraph-result";
        string message;
        bool failed = false;
        try
        {
            using var choice = await owner.StorageProvider.SaveFilePickerAsync(new()
            {
                Title = $"Export {title}", SuggestedFileName = name + "." + extension,
                FileTypeChoices = [new FilePickerFileType(title) { Patterns = ["*." + extension] }],
                DefaultExtension = extension, ShowOverwritePrompt = true
            });
            if (choice == null) message = "Export canceled.";
            else
            {
                string path = choice.TryGetLocalPath() ?? throw new IOException("Choose a local file.");
                bool overwrite = File.Exists(path);
                await Task.Run(() => ResultExport.Save(path, source, format, overwrite));
                message = $"Saved {Path.GetFileName(path)} · {ExportScope(source)}";
            }
        }
        catch (Exception error)
        {
            message = "Export failed · " + error.Message;
            failed = true;
        }
        finally { export.IsEnabled = true; }
        if (!ReferenceEquals(result, source)) return;
        exportMessage.Text = message;
        exportMessage.Foreground = failed ? Brush.Parse("#A2382B") : CyanGraphView.Muted;
        exportMessage.IsVisible = true;
    }
}
