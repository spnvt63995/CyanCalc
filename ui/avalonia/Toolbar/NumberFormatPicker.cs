using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    private Window? _toolbarDialog;

    // FormatPicker.tsx uses a modal Prompt with arbitrary format text, Save and Cancel.
    private void ShowNumberFormat()
    {
        if (!CanEdit || Session is not { } model || !CommitEdit()) return;
        var area = CellArea.FromView(model.GetSelectedView());
        ShowInputDialog(Text.Key("num_fmt.title"), model.Cell(area.Sheet, area.Row, area.Column).ExtendedStyle.Style.NumFmt ?? "General",
            text => model.SetStyle(CellArea.FromView(model.GetSelectedView()), "num_fmt", text), Text.Key("num_fmt.save"));
    }

    private void ShowInputDialog(LocalizedText title, string initial, Action<string> apply, LocalizedText? confirmLabel = null, bool pixels = false)
    {
        if (!CanEdit || Session is not { } model || !CommitEdit()) return;
        CloseToolbarPopups(); _toolbarDialog?.Close();
        var window = new Window();
        NativeDialogTheme.Follow(window, this);
        title.Bind(window, Window.TitleProperty);
        _toolbarDialog = window;
        var panel = WorkbookDialog.Body();
        Control field;
        Func<string> value;
        if (pixels)
        {
            var numeric = new NumericUpDown { Name = "AxisSize", Value = decimal.Parse(initial, System.Globalization.CultureInfo.InvariantCulture), Increment = 1,
                NumberFormat = System.Globalization.CultureInfo.InvariantCulture.NumberFormat,
                ParsingNumberStyle = System.Globalization.NumberStyles.Float, HorizontalAlignment = HorizontalAlignment.Stretch };
            var row = new Grid { ColumnDefinitions = new("*,Auto") };
            row.Children.Add(numeric);
            var unit = new TextBlock { Text = "px", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(unit, 1); row.Children.Add(unit); panel.Children.Add(row);
            field = numeric; value = () => numeric.Text ?? "";
        }
        else
        {
            var text = new TextBox { Text = initial }; WorkbookDialog.Input(text, title);
            panel.Children.Add(text); field = text; value = () => text.Text ?? "";
        }
        void Apply() => Run(() =>
        {
            if (!window.IsVisible || !CanEdit || !ReferenceEquals(Session, model)) return;
            apply(value()); window.Close();
        });
        var submit = WorkbookDialog.Button(confirmLabel ?? Text.Key("common.apply"), Apply, "primary");
        var buttons = WorkbookDialog.Footer(WorkbookDialog.Button(Text.Key("common.cancel"), window.Close), submit);
        WorkbookDialog.Configure(window, title, Text.Key("link_dialog.close"), panel, buttons);
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { window.Close(); e.Handled = true; }
            else if (e.Key == Key.Enter && (field.IsKeyboardFocusWithin || e.Source == field)) { submit.Focus(); e.Handled = true; }
            else if (e.Key == Key.Enter && (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0) { Apply(); e.Handled = true; }
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_toolbarDialog, window)) _toolbarDialog = null;
            FocusCell();
        };
        if (TopLevel.GetTopLevel(this) is Window owner) _ = window.ShowDialog(owner); else window.Show();
        Dispatcher.UIThread.Post(() => { if (window.IsVisible) { field.Focus(); if (field is TextBox text) text.SelectAll(); } }, DispatcherPriority.Input);
    }
}
