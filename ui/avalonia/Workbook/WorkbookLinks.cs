using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace CyanCalc.Controls;

/// <summary>Worksheet link UI. The host decides how an explicit external open is handled.</summary>
public sealed partial class WorkbookView
{
    private readonly Dictionary<(int Row, int Column), JsonElement> _visibleLinks = new();
    private Border? _linkTooltip;
    private Rect? _linkTooltipRect;
    private (Guid Session, int Sheet, int Row, int Column)? _hoveredLink;
    private Window? _linkDialog;
    private readonly DispatcherTimer _linkHideTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public Action<Uri>? OpenExternalLink { get; set; }
    // Native host callbacks; internal navigation follows the pinned cellLinks parser.
    internal Func<bool>? LinkCanEdit { get; set; }
    internal Func<string, bool>? InternalLinkNavigator { get; set; }

    internal void InstallLinkUi()
    {
        ActualThemeVariantChanged += (_, _) => HideLinkTooltip();
        _linkHideTimer.Tick += (_, _) => { _linkHideTimer.Stop(); HideLinkTooltip(); };
        Worksheet.AddHandler(InputElement.PointerPressedEvent, LinkPointerPressed, RoutingStrategies.Tunnel);
        Worksheet.PointerMoved += (_, e) => ShowLinkTooltipAt(e.GetPosition(Worksheet));
        Worksheet.PointerExited += (_, _) => ScheduleHideLinkTooltip();
    }

    internal void RefreshLinks()
    {
        var hovered = _hoveredLink;
        var previous = hovered is { } h && _visibleLinks.TryGetValue((h.Row, h.Column), out var old) ? old.GetRawText() : null;
        _visibleLinks.Clear();
        if (Session is not { } session || Snapshot is not { } snapshot) { HideLinkTooltip(); return; }
        foreach (var link in session.GetLinks(snapshot.View.Sheet))
            _visibleLinks[(link.GetProperty("row").GetInt32(), link.GetProperty("column").GetInt32())] = link;
        if (hovered is not { } current) return;
        if (State.Editing is not null || current.Session != session.Identity || current.Sheet != snapshot.View.Sheet ||
            !_visibleLinks.TryGetValue((current.Row, current.Column), out var updated) || updated.GetRawText() != previous ||
            Worksheet.Layout.CellRect(current.Row, current.Column) != _linkTooltipRect) HideLinkTooltip();
    }

    internal void ShowLinkDialog() => ShowLinkDialogAt(Snapshot?.View.Sheet ?? -1, Snapshot?.View.Row ?? 0, Snapshot?.View.Column ?? 0);

    internal async void ShowLinkDialogAt(int sheet, int row, int column)
    {
        if (Session is not { } session || Snapshot is not { } snapshot || sheet < 0 || row < 1 || column < 1) return;
        if (LinkCanEdit?.Invoke() != true) { Report(Text.Key("native.read_only")); return; }
        if (State.Editing is not null) { Report(Text.Key("native.finish_edit_link")); return; }
        if (_linkDialog is not null) return;
        var editable = session.GetCellLink(sheet, row, column);
        if (editable is null && session.GetLinks(sheet).Any(link =>
                link.GetProperty("row").GetInt32() == row && link.GetProperty("column").GetInt32() == column &&
                link.GetProperty("dynamic").GetBoolean()))
        { Report(Text.Key("native.hyperlink_formula")); return; }
        var initialContent = session.GetFormattedCellValue(sheet, row, column);
        var dialog = new LinkEditorWindow(snapshot.Worksheets.Select(x => x.Name).ToArray(), snapshot.Worksheets[sheet].Name, editable, initialContent, Text);
        NativeDialogTheme.Follow(dialog, this);
        _linkDialog = dialog;
        HideLinkTooltip();
        try
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is null) { Report(Text.Key("native.link_needs_window")); return; }
            var result = await dialog.ShowDialog<LinkEditorResult?>(owner);
            if (result is null || !ReferenceEquals(Session, session) ||
                !ReferenceEquals(TopLevel.GetTopLevel(this), owner) || State.Editing is not null) return;
            // Workbook.tsx onSaveLink/onDeleteLink use the dialog's cell even
            // after other writes or a canEdit transition. Only native lifetime
            // checks belong around the asynchronous Window result.
            if (result.Delete)
            {
                session.DeleteCellLink(sheet, row, column);
            }
            else if (result.Link is { } link)
            {
                var label = result.Label.Trim();
                if (label.Length == 0)
                    label = link.GetProperty("type").GetString() == "Internal" ? link.GetProperty("location").GetString() :
                        LinkEditorWindow.DisplayTarget(link.GetProperty("target").GetString() ?? "");
                session.SetCellLink(sheet, row, column, link, label);
            }
        }
        catch (Exception error) when (error is IronCalcException or InvalidOperationException or FormatException or ArgumentException) { Report(error.Message); }
        finally { _linkDialog = null; FocusCell(); }
    }

    internal void DeleteSelectedLink()
    {
        if (LinkCanEdit?.Invoke() != true || Session is not { } session || Snapshot is not { } snapshot || State.Editing is not null) return;
        var (sheet, row, column) = (snapshot.View.Sheet, snapshot.View.Row, snapshot.View.Column);
        if (session.GetCellLink(sheet, row, column) is not null) Run(() => session.DeleteCellLink(sheet, row, column));
        FocusCell();
    }

    internal Control[] LinkMenuItems()
    {
        var selected = Snapshot?.View;
        var editable = selected is not null && Session?.GetCellLink(selected.Sheet, selected.Row, selected.Column) is not null;
        var dynamicLink = selected is not null && !editable && Session?.GetLinks(selected.Sheet).Any(link =>
            link.GetProperty("row").GetInt32() == selected.Row && link.GetProperty("column").GetInt32() == selected.Column &&
            link.GetProperty("dynamic").GetBoolean()) == true;
        var insert = MenuItem(Text.Key(editable ? "link_dialog.title_edit" : "toolbar.add_link"), ShowLinkDialog);
        insert.IsEnabled = LinkCanEdit?.Invoke() == true && !dynamicLink;
        var remove = MenuItem(Text.Key("link_tooltip.break"), DeleteSelectedLink);
        remove.IsEnabled = LinkCanEdit?.Invoke() == true && editable;
        return [insert, remove];
    }

    private void LinkPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Snapshot is null || Session is null || State.Editing is not null ||
            !e.GetCurrentPoint(Worksheet).Properties.IsLeftButtonPressed ||
            (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) == 0) return;
        var hit = Worksheet.Layout.HitTest(e.GetPosition(Worksheet));
        if (!_visibleLinks.TryGetValue((hit.Row, hit.Column), out var link)) return;
        e.Handled = true;
        HideLinkTooltip();
        FollowLink(link);
    }

    private void FollowLink(JsonElement link)
    {
        var type = link.GetProperty("type").GetString();
        if (type == "Internal")
        {
            var location = link.GetProperty("location").GetString() ?? "";
            Run(() => InternalLinkNavigator?.Invoke(location));
            return;
        }
        if (type != "External" || !LinkEditorWindow.TryAbsoluteAllowedUri(link.GetProperty("target").GetString() ?? "", out var uri)) return;
        // Never call the host while rendering, refreshing, or hovering.
        OpenExternalLink?.Invoke(uri);
    }

    private void ShowLinkTooltipAt(Point point)
    {
        if (Snapshot is not { } snapshot || Session is not { } session) return;
        var hit = Worksheet.Layout.HitTest(point);
        if (!_visibleLinks.TryGetValue((hit.Row, hit.Column), out var link)) { ScheduleHideLinkTooltip(); return; }
        if (_hoveredLink == (session.Identity, snapshot.View.Sheet, hit.Row, hit.Column)) { _linkHideTimer.Stop(); return; }
        var rect = Worksheet.Layout.CellRect(hit.Row, hit.Column);
        if (rect is null) return;
        HideLinkTooltip();
        _hoveredLink = (session.Identity, snapshot.View.Sheet, hit.Row, hit.Column);
        _linkTooltipRect = rect;
        var palette = SheetPalette.For(ActualThemeVariant);
        var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new WorkbookIcon("Globe", 12) { Foreground = palette.Token("grey-400") });
        var label = link.GetProperty("type").GetString() == "Internal" ? link.GetProperty("location").GetString() ?? "" :
            LinkEditorWindow.TooltipTarget(link.GetProperty("target").GetString() ?? "");
        var target = Button(label, () => { HideLinkTooltip(); FollowLink(link); }, Text.Key("native.open_link"));
        target.Content = new TextBlock { Text = label, MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis };
        target.Height = 20; target.Padding = new Thickness(0); target.Foreground = palette.Token("common-white");
        target.Classes.Add("link-label"); row.Children.Add(target);
        var actions = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 2 };
        Button ActionButton(string icon, LocalizedText title, Action action)
        {
            var button = Button("", action, title); button.Content = new WorkbookIcon(icon, 12);
            button.Width = button.Height = 20; button.Padding = new Thickness(4); button.CornerRadius = new CornerRadius(2);
            button.Foreground = palette.Token("grey-400"); button.Classes.Add("link-action"); return button;
        }
        actions.Children.Add(ActionButton("Copy", Text.Key("link_tooltip.copy"), () => _ = CopyLinkAsync(link)));
        if (!link.GetProperty("dynamic").GetBoolean() && LinkCanEdit?.Invoke() == true)
        {
            actions.Children.Add(ActionButton("PencilLine", Text.Key("link_tooltip.edit"), () => ShowLinkDialogAt(snapshot.View.Sheet, hit.Row, hit.Column)));
            actions.Children.Add(ActionButton("Link2Off", Text.Key("link_tooltip.break"), () => { if (LinkCanEdit?.Invoke() == true && ReferenceEquals(Session, session)) Run(() => session.DeleteCellLink(snapshot.View.Sheet, hit.Row, hit.Column)); HideLinkTooltip(); }));
        }
        row.Children.Add(actions);
        var stack = new StackPanel { Spacing = 2 }; stack.Children.Add(row);
        if (link.TryGetProperty("tooltip", out var tip) && tip.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(tip.GetString()))
            stack.Children.Add(new TextBlock { Text = tip.GetString(), MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = palette.Token("grey-400"), Margin = new Thickness(0, 0, 4, 2) });
        var color = ((ISolidColorBrush)palette.Token("grey-900")).Color;
        _linkTooltip = new Border { Name = "LinkTooltip", Child = stack, Background = new SolidColorBrush(Color.FromArgb(240, color.R, color.G, color.B)),
            Padding = new Thickness(6, 2, 2, 2), CornerRadius = new CornerRadius(4), BoxShadow = BoxShadows.Parse("0 4 12 #26000000") };
        _linkTooltip.PointerEntered += (_, _) => _linkHideTimer.Stop();
        _linkTooltip.PointerExited += (_, _) => ScheduleHideLinkTooltip();
        _editorLayer.Children.Add(_linkTooltip);
        _linkTooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var top = rect.Value.Y - _linkTooltip.DesiredSize.Height + 1;
        if (top < SheetLayout.ColumnHeader) top = rect.Value.Bottom - 1;
        Canvas.SetLeft(_linkTooltip, Math.Max(SheetLayout.RowHeader + 2, rect.Value.X));
        Canvas.SetTop(_linkTooltip, top);
    }

    private async Task CopyLinkAsync(JsonElement link)
    {
        var address = link.GetProperty("type").GetString() == "Internal" ? link.GetProperty("location").GetString() : link.GetProperty("target").GetString();
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && address is not null) await clipboard.SetTextAsync(address);
    }

    private void ScheduleHideLinkTooltip() { if (!_linkHideTimer.IsEnabled) _linkHideTimer.Start(); }
    private void HideLinkTooltip()
    {
        _linkHideTimer.Stop(); _hoveredLink = null; _linkTooltipRect = null;
        if (_linkTooltip is not null) _editorLayer.Children.Remove(_linkTooltip);
        _linkTooltip = null;
    }
}
