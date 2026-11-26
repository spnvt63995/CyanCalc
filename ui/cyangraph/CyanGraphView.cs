using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using CyanGraph.Data;

namespace CyanGraph;

public sealed partial class CyanGraphView : UserControl
{
    internal static readonly IBrush Ink = Brush.Parse("#182D36"), Muted = Brush.Parse("#536A75"),
        Line = Brush.Parse("#CAD7DB"), Teal = Brush.Parse("#08736C"), Paper = Brush.Parse("#FFFFFF");
    readonly QueryWorkspace workspace;
    readonly GraphDialogs dialogs = new();
    readonly TextBox editor = Editor("Cypher query", "Enter a Cypher query…");
    readonly TextBox parameters = Editor("Parameters (JSON)", "{}");
    readonly Button connect = new(), run = Primary("Run"), stop = new() { Content = "Stop" };
    readonly MenuItem disconnect = new() { Header = "Disconnect" }, runMenu = new() { Header = "Run Cypher" },
        stopMenu = new() { Header = "Stop query" }, separateMenu = new() { Header = "Run in separate window…" };
    readonly TextBlock endpoint = Caption("");
    readonly QueryResultsPane results;
    string lastAddress = "bolt://localhost:7687", lastDatabase = "neo4j", lastUser = "neo4j";
    bool closing;
    Task? shutdown;
    Task separateWork = Task.CompletedTask;

    public CyanGraphView(QueryWorkspace workspace, string questionFile)
    {
        this.workspace = workspace;
        library = new QuestionLibrary(questionFile);
        results = new QueryResultsPane(dialogs);
        Background = Brush.Parse("#F2F6F6");
        questions = new QuestionsPane(library);
        questions.OpenRequested += OpenQuestion;
        parameters.Text = "{}";
        parameters.FontSize = 14;
        connect.Click += async (_, _) => await EditConnection();
        run.Click += async (_, _) => await Execute();
        stop.Click += (_, _) => workspace.Stop();
        disconnect.Click += async (_, _) => await workspace.DisconnectAsync();
        runMenu.Click += async (_, _) => await Execute();
        separateMenu.Click += (_, _) =>
        {
            if (separateWork.IsCompleted && workspace.Connected != null)
                separateWork = ShowSeparateResult(editor.Text ?? "", parameters.Text ?? "");
        };
        stopMenu.Click += (_, _) => workspace.Stop();
        workspace.Changed += Refresh;
        var outer = new Grid { RowDefinitions = new("Auto,*") };
        Put(outer, Header(), 0, 0);
        var content = new Grid { Margin = new Thickness(24, 18, 24, 20), RowDefinitions = new("0.7*,12,1.55*") };
        Put(content, QueryPane(), 0, 0);
        Put(content, Card(results), 2, 0);
        var workArea = new Grid { ColumnDefinitions = new("Auto,*") };
        Put(workArea, content, 0, 1);
        Put(workArea, questions, 0, 0);
        workArea.SizeChanged += (_, _) =>
        {
            bool overlay = workArea.Bounds.Width < 1040;
            Grid.SetColumn(content, overlay ? 0 : 1);
            Grid.SetColumnSpan(content, overlay ? 2 : 1);
        };
        Put(outer, workArea, 1, 0);
        Content = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Light, Child = outer };
        AddHandler(KeyDownEvent, OnShortcut, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        editor.TextChanged += (_, _) => Refresh();
        parameters.TextChanged += (_, _) => Refresh();
        Refresh();
    }

    static TextBox Editor(string name, string placeholder)
    {
        var box = new TextBox
        {
            Watermark = placeholder, AcceptsReturn = true, AcceptsTab = false, FontFamily = new FontFamily("Menlo, monospace"),
            FontSize = 15, Padding = new Thickness(12), TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch, BorderBrush = Line, BorderThickness = new Thickness(1)
        };
        AutomationProperties.SetName(box, name);
        return box;
    }

    internal static Button Primary(string text) => new()
    {
        Content = text, Background = Teal, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold,
        HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(20, 7)
    };
    internal static TextBlock Caption(string text) => new() { Text = text, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
    internal static TextBlock Heading(string text) => new() { Text = text, Foreground = Ink, FontSize = 17, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    internal static void Put(Grid grid, Control control, int row, int col)
    {
        Grid.SetRow(control, row); Grid.SetColumn(control, col); grid.Children.Add(control);
    }
    static Border Card(Control content) => new()
    {
        Background = Paper, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = content
    };

    Control Header()
    {
        var connectionSettings = new MenuItem { Header = "Connection settings…" };
        connectionSettings.Click += async (_, _) => await EditConnection();
        var connections = new MenuItem { Header = "Connection", ItemsSource = new[] { connectionSettings, disconnect } };
        var query = new MenuItem { Header = "Query", ItemsSource = new[] { runMenu, separateMenu, stopMenu } };
        var menu = new Menu { VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, ItemsSource = new[] { connections, query, QuestionsMenu() } };
        var bar = new Grid { ColumnDefinitions = new("Auto,Auto,*,Auto,Auto"), Margin = new Thickness(22, 9), ColumnSpacing = 14 };
        Put(bar, new TextBlock { Text = "CyanGraph", Foreground = Teal, FontSize = 23, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        Put(bar, menu, 0, 1); Put(bar, endpoint, 0, 3); Put(bar, connect, 0, 4);
        return new Border { Background = Paper, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = bar };
    }

    Control QueryPane()
    {
        var pane = new Grid { RowDefinitions = new("Auto,*"), Margin = new Thickness(16, 12, 16, 16), RowSpacing = 10 };
        var header = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
        var heading = new StackPanel { Spacing = 4 };
        queryTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        historyProblem.IsVisible = false;
        historyProblem.TextTrimming = TextTrimming.CharacterEllipsis;
        heading.Children.Add(queryTitle); heading.Children.Add(historyProblem);
        Put(header, heading, 0, 0); Put(header, stop, 0, 1); Put(header, run, 0, 2);
        ToolTip.SetTip(run, "Run query (⌘ / Ctrl + Enter)");
        var inputs = new Grid { ColumnDefinitions = new("3*,*"), ColumnSpacing = 14, RowDefinitions = new("Auto,*"), RowSpacing = 5 };
        Put(inputs, Caption("Statement"), 0, 0); Put(inputs, Caption("Parameters (JSON)"), 0, 1);
        Put(inputs, editor, 1, 0); Put(inputs, parameters, 1, 1);
        Put(pane, header, 0, 0); Put(pane, inputs, 1, 0);
        return Card(pane);
    }

    async Task EditConnection()
    {
        var dialog = new ConnectionWindow(workspace, lastAddress, lastDatabase, lastUser);
        await dialogs.ShowAsync(dialog, this);
        lastAddress = dialog.Address; lastDatabase = dialog.Database; lastUser = dialog.Username;
    }
    Task Execute() => RunUserQuery(workspace, editor.Text ?? "", parameters.Text ?? "");

    async Task ShowSeparateResult(string statement, string parameterJson)
    {
        var secondary = workspace.CreateSeparateQuery();
        var view = new QueryResultsPane(dialogs);
        var dialog = new Window
        {
            Title = "Separate query result", Width = 920, Height = 510, MinWidth = 680, MinHeight = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Paper
        };
        var body = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(16), RowSpacing = 12 };
        var query = new TextBlock { Text = statement, FontFamily = new FontFamily("Menlo, monospace"),
            FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxLines = 4, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(query, statement);
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => dialog.Close();
        Put(body, query, 0, 0); Put(body, Card(view), 1, 0); Put(body, close, 2, 0);
        dialog.Content = body;
        secondary.Changed += RefreshResult;
        void RefreshResult() => view.Update(secondary);
        dialog.Opened += (_, _) => _ = RunUserQuery(secondary, statement, parameterJson);
        bool draining = false, drained = false;
        dialog.Closing += async (_, e) =>
        {
            if (drained) return;
            e.Cancel = true;
            if (draining) return;
            draining = true;
            dialog.IsEnabled = false;
            await secondary.DisconnectAsync();
            drained = true;
            dialog.Close();
        };
        try { await dialogs.ShowAsync(dialog, this); }
        finally
        {
            secondary.Changed -= RefreshResult;
            if (!drained) await secondary.DisconnectAsync();
        }
    }

    void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            e.Handled = true;
            _ = Execute();
        }
    }

    void Refresh()
    {
        var active = workspace.Connected;
        endpoint.Text = active is null ? "Not connected" : $"{active.Database}  ·  {active.Address.Authority}";
        connect.Content = active is null ? "Connect…" : "Change connection…";
        disconnect.IsEnabled = active != null;
        run.IsEnabled = runMenu.IsEnabled = separateMenu.IsEnabled = active != null && !string.IsNullOrWhiteSpace(editor.Text);
        stop.IsEnabled = stopMenu.IsEnabled = workspace.Phase == QueryPhase.Running;
        saveQuestion.IsEnabled = !string.IsNullOrWhiteSpace(editor.Text);
        queryTitle.Text = activeQuestion == null ? "Cypher query" : activeQuestion.Name +
            (editor.Text == activeQuestion.Statement ? "" : " · edited");
        results.Update(workspace, editor.Text != lastStatement || parameters.Text != lastParameters);
    }

    public Task CloseAsync() => shutdown ??= CloseCoreAsync();

    async Task CloseCoreAsync()
    {
        closing = true;
        IsEnabled = false;
        workspace.Stop();
        await dialogs.CloseAsync();
        await separateWork;
        await workspace.DisconnectAsync();
        await pendingHistory;
        workspace.Changed -= Refresh;
    }
}
