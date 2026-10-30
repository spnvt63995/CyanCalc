using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    private Window? _alertDialog;

    internal void ShowPasteError(string message) => ShowWorkbookAlert(Text.Key("error_dialog.error_paste"),
        message.Contains("merged cell", StringComparison.Ordinal) ? Text.Key("error_dialog.error_paste_merged") : Text.Compose(_ => message));

    // Workbook.tsx uses Alert for these errors, independently of host status banners.
    internal void ShowWorkbookAlert(LocalizedText title, LocalizedText message)
    {
        CloseToolbarPopups(); _alertDialog?.Close();
        var dialog = new Window(); NativeDialogTheme.Follow(dialog, this);
        _alertDialog = dialog;
        var content = WorkbookDialog.Body(); content.Children.Add(WorkbookDialog.Paragraph(message));
        var ok = WorkbookDialog.Button(Text.Compose(_ => "OK"), dialog.Close, "primary");
        ok.IsDefault = true;
        WorkbookDialog.Configure(dialog, title, Text.Key("link_dialog.close"), content, WorkbookDialog.Footer(ok));
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape || e.Key == Key.Enter && (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0) { dialog.Close(); e.Handled = true; } };
        dialog.Closed += (_, _) => { if (ReferenceEquals(_alertDialog, dialog)) _alertDialog = null; FocusCell(); };
        dialog.Opened += (_, _) => Dispatcher.UIThread.Post(() => { if (dialog.IsVisible) ok.Focus(); });
        if (TopLevel.GetTopLevel(this) is Window owner) _ = dialog.ShowDialog(owner); else dialog.Show();
    }
}
