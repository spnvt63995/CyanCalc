using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CyanCalc.Tabular;
using CyanGraph.Data;
using INode = Neo4j.Driver.INode;
using IRelationship = Neo4j.Driver.IRelationship;

namespace CyanGraph;

sealed partial class QueryResultsPane : Grid
{
    enum View { Graph, Table, Together, Raw }
    static readonly IBrush ActiveViewBackground = Brush.Parse("#C8E8E5");
    readonly Grid surface = new() { ClipToBounds = true };
    readonly Grid tablePane = new() { RowDefinitions = new("*,Auto"), ClipToBounds = true };
    readonly Border divider = new() { Background = CyanGraphView.Line };
    readonly GraphPane graph;
    readonly ReadOnlyTable table = new();
    readonly TextBox raw = new()
    {
        IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Menlo, monospace"),
        FontSize = 13, Padding = new Thickness(14), TextWrapping = TextWrapping.NoWrap,
        VerticalContentAlignment = VerticalAlignment.Top, BorderThickness = new Thickness(0)
    };
    readonly TextBlock status = CyanGraphView.Caption(""), matches = CyanGraphView.Caption("");
    readonly TextBlock empty = new() { HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, Foreground = CyanGraphView.Muted };
    readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    readonly Dictionary<View, Button> buttons = [];
    readonly TextBox detail = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 84 };
    readonly StackPanel detailPanel = new() { Spacing = 4, Margin = new Thickness(10, 6), IsVisible = false };
    GraphResult? result;
    ResultGraph? projection;
    GraphElementKey? selected;
    View view = View.Table;
    View? preferred;
    bool splitAcross, rawReady;
    CancellationTokenSource? formatting;

    public QueryResultsPane(GraphDialogs dialogs)
    {
        graph = new GraphPane(dialogs);
        RowDefinitions = new("Auto,Auto,*");
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto"), ColumnSpacing = 14, Margin = new Thickness(16, 9) };
        CyanGraphView.Put(header, CyanGraphView.Heading("Results"), 0, 0);
        status.TextWrapping = TextWrapping.Wrap; status.MaxLines = 4; CyanGraphView.Put(header, status, 0, 1);
        matches.Foreground = CyanGraphView.Teal; CyanGraphView.Put(header, matches, 0, 2);
        AddView(View.Graph, "Graph"); AddView(View.Table, "Table"); AddView(View.Together, "Graph + Table"); AddView(View.Raw, "Raw");
        AddExportMenu();
        CyanGraphView.Put(header, tabs, 0, 3); CyanGraphView.Put(this, header, 0, 0);
        AutomationProperties.SetName(table, "Query result rows");
        AutomationProperties.SetName(detail, "Selected cell value");
        AutomationProperties.SetName(raw, "Raw result JSON");
        ScrollViewer.SetHorizontalScrollBarVisibility(raw, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(raw, ScrollBarVisibility.Auto);
        detailPanel.Children.Add(CyanGraphView.Caption("Selected value")); detailPanel.Children.Add(detail);
        CyanGraphView.Put(tablePane, table, 0, 0); CyanGraphView.Put(tablePane, detailPanel, 1, 0);
        surface.Children.Add(graph); surface.Children.Add(tablePane); surface.Children.Add(divider);
        surface.Children.Add(raw); surface.Children.Add(empty);
        CyanGraphView.Put(this, surface, 2, 0);
        table.SelectionChanged += (_, selection) => TablePicked(selection);
        graph.ElementPicked += GraphPicked;
        graph.VisibilityChanged += () =>
        {
            if (selected is not { } key) return;
            graph.SelectFromTable(key);
            Highlight(key);
        };
        surface.SizeChanged += (_, _) =>
        {
            if (view == View.Together && splitAcross != (surface.Bounds.Width >= 1020)) ArrangeViews();
        };
        ArrangeViews();
    }

    void AddView(View target, string name)
    {
        var button = new Button { Content = name };
        AutomationProperties.SetName(button, name + " view");
        button.Click += (_, _) => { preferred = target; SwitchTo(target); };
        buttons.Add(target, button); tabs.Children.Add(button);
    }

    public void Update(QueryWorkspace workspace, bool editorChanged = false)
    {
        status.Text = editorChanged && workspace.Result != null
            ? workspace.Status + " · editor changed since run" : workspace.Status;
        status.Foreground = workspace.Phase == QueryPhase.Failed ? Brush.Parse("#A2382B") : CyanGraphView.Muted;
        ToolTip.SetTip(status, status.Text);
        if (!ReferenceEquals(result, workspace.Result))
        {
            formatting?.Cancel(); formatting = null; rawReady = false; raw.Text = null;
            result = workspace.Result;
            UpdateExportScope();
            projection = workspace.Scene?.Original;
            selected = null; matches.IsVisible = false;
            table.Snapshot = result != null && projection != null ? Snapshot(result, projection) : null;
            graph.ShowScene(workspace.Scene);
            var hasGraph = projection is { Nodes.Count: > 0 };
            buttons[View.Graph].IsVisible = buttons[View.Together].IsVisible = hasGraph;
            var next = preferred ?? (hasGraph ? View.Graph : View.Table);
            SwitchTo(!hasGraph && next is View.Graph or View.Together ? View.Table : next);
        }
        tabs.IsVisible = result != null;
        empty.IsVisible = result == null;
        empty.Text = workspace.Phase switch
        {
            QueryPhase.Running => "Waiting for rows…",
            QueryPhase.Stopping => "Waiting for the query to stop…",
            QueryPhase.Failed => "Correct the error and run again.",
            QueryPhase.Stopped => "Run a query to fetch new results.",
            _ => workspace.Connected == null ? "Connect to a database to run Cypher." : "Run a query to see its rows."
        };
    }

    static string Caption(object? value, ResultGraph model) => value switch
    {
        INode n => GraphAppearance.Caption(model.Nodes[n.ElementId]),
        IRelationship r => r.Type,
        _ => GraphText.Format(value)
    };

    static TableSnapshot Snapshot(GraphResult data, ResultGraph model)
    {
        var columns = data.Columns.Select((name, col) =>
        {
            var length = data.Rows.Take(16).Select(row => Caption(row[col], model).Length).Prepend(name.Length).Max();
            return new TableColumn(col.ToString(), name, Width: Math.Clamp(28 + 7.5 * length, 104, 320));
        }).ToArray();
        return new TableSnapshot(Guid.NewGuid().ToString("N"), columns, data.Rows.Count, (row, col) =>
        {
            var value = data.Rows[(int)row][col];
            string text = GraphText.Format(value);
            string display = value is INode or IRelationship ? Caption(value, model) : text;
            var kind = value == null ? TableCellKind.Null : value is long or double ? TableCellKind.Number : TableCellKind.Text;
            return new TableCell(text, kind) { DisplayText = value == null ? null : GraphAppearance.Short(display, 512) };
        }, !data.HasMore);
    }

    void SwitchTo(View target)
    {
        view = target;
        foreach (var (mode, button) in buttons)
        {
            button.Background = mode == view ? ActiveViewBackground : Brushes.Transparent;
            button.Foreground = CyanGraphView.Ink;
            button.FontWeight = mode == view ? FontWeight.SemiBold : FontWeight.Normal;
        }
        ArrangeViews();
        if (result != null && view == View.Raw && !rawReady && formatting == null)
        {
            formatting = new CancellationTokenSource();
            _ = LoadRaw(result, formatting);
        }
        if (selected.HasValue)
            Dispatcher.UIThread.Post(() => { if (graph.IsVisible) graph.RevealSelection(); if (tablePane.IsVisible && table.Selection == null) RevealMatch(); }, DispatcherPriority.Loaded);
    }

    void ArrangeViews()
    {
        bool split = view == View.Together && result != null;
        splitAcross = split && surface.Bounds.Width >= 1020;
        surface.ColumnDefinitions = splitAcross ? new("0.87*,1,1.13*") : new("*");
        surface.RowDefinitions = split && !splitAcross ? new("1.4*,1,*") : new("*");
        SetColumn(graph, splitAcross ? 2 : 0); SetRow(tablePane, split && !splitAcross ? 2 : 0);
        SetColumn(divider, splitAcross ? 1 : 0); SetRow(divider, split && !splitAcross ? 1 : 0);
        divider.IsVisible = split;
        graph.IsVisible = result != null && view is View.Graph or View.Together;
        tablePane.IsVisible = result != null && view is View.Table or View.Together;
        raw.IsVisible = result != null && view == View.Raw;
    }

    void TablePicked(TableSelection? selection)
    {
        bool show = selection.HasValue && result != null;
        detailPanel.IsVisible = show;
        if (!show) { detail.Text = null; return; }
        var cell = selection!.Value.Active;
        var value = result!.Rows[(int)cell.Row][cell.Column];
        detail.Text = GraphText.Format(value);
        var key = GraphElementKey.FromValue(value);
        graph.SelectFromTable(key);
        Highlight(key);
    }

    void GraphPicked(GraphElementKey? key)
    {
        table.ClearSelection();
        Highlight(key);
        if (tablePane.IsVisible) RevealMatch();
    }

    void Highlight(GraphElementKey? key)
    {
        selected = key;
        var cells = key.HasValue ? projection!.CellsFor(key.Value) : Array.Empty<ResultCell>();
        table.HighlightCells(cells.Select(c => new TablePosition(c.Row, c.Column)));
        matches.Text = $"{cells.Count} matching {(cells.Count == 1 ? "cell" : "cells")}";
        if (key is { } value && !graph.IsOnCanvas(value)) matches.Text += " · hidden in graph";
        matches.IsVisible = key.HasValue;
    }

    void RevealMatch()
    {
        if (selected is not { } key || projection == null) return;
        var positions = projection.CellsFor(key);
        if (positions.Count == 0) return;
        var first = positions.MinBy(c => (c.Row, c.Column));
        table.ScrollIntoView(new(first.Row, first.Column));
    }

    async Task LoadRaw(GraphResult source, CancellationTokenSource request)
    {
        raw.Text = null; raw.Watermark = "Formatting result…";
        try
        {
            var json = await Task.Run(() => RawResult.Format(source, request.Token), request.Token);
            if (request.IsCancellationRequested || !ReferenceEquals(result, source)) return;
            raw.Text = json; raw.CaretIndex = 0; rawReady = true;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (!request.IsCancellationRequested) raw.Watermark = "Could not format this result. " + e.Message;
        }
        finally
        {
            if (ReferenceEquals(formatting, request)) formatting = null;
            request.Dispose();
        }
    }
}
