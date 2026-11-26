using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CyanGraph.Data;

namespace CyanGraph;

sealed class QuestionsPane : Border
{
    readonly QuestionLibrary library;
    readonly StackPanel entries = new() { Spacing = 4 };
    readonly TextBlock message = CyanGraphView.Caption("");
    readonly Button history = new() { Content = "History" }, saved = new() { Content = "Saved" };
    readonly ScrollViewer scroll = new();
    int revision;
    bool showHistory;
    public event Action<string, SavedQuestion?>? OpenRequested;

    public QuestionsPane(QuestionLibrary library)
    {
        this.library = library;
        Width = 252; HorizontalAlignment = HorizontalAlignment.Left;
        Background = CyanGraphView.Paper; BorderBrush = CyanGraphView.Line; BorderThickness = new Thickness(0, 0, 1, 0);
        IsVisible = false;
        var body = new Grid { Margin = new Thickness(14, 18), RowDefinitions = new("Auto,Auto,Auto,*"), RowSpacing = 10 };
        var title = new Grid { ColumnDefinitions = new("*,Auto") };
        CyanGraphView.Put(title, CyanGraphView.Heading("Questions"), 0, 0);
        var close = new Button { Content = "×", Padding = new Thickness(8, 3) };
        AutomationProperties.SetName(close, "Close questions panel");
        ToolTip.SetTip(close, "Close questions panel");
        close.Click += (_, _) => IsVisible = false;
        CyanGraphView.Put(title, close, 0, 1);
        var switcher = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        AutomationProperties.SetName(history, "Show run history");
        AutomationProperties.SetName(saved, "Show saved questions");
        history.Click += async (_, _) => await ShowAsync(true);
        saved.Click += async (_, _) => await ShowAsync(false);
        switcher.Children.Add(history); switcher.Children.Add(saved);
        message.TextWrapping = TextWrapping.Wrap;
        scroll.Content = entries;
        CyanGraphView.Put(body, title, 0, 0); CyanGraphView.Put(body, switcher, 1, 0);
        CyanGraphView.Put(body, message, 2, 0); CyanGraphView.Put(body, scroll, 3, 0);
        Child = body;
    }

    public async Task ShowAsync(bool recent)
    {
        showHistory = recent;
        IsVisible = true;
        history.Background = recent ? CyanGraphView.Teal : Brushes.Transparent;
        history.Foreground = recent ? Brushes.White : CyanGraphView.Ink;
        saved.Background = recent ? Brushes.Transparent : CyanGraphView.Teal;
        saved.Foreground = recent ? CyanGraphView.Ink : Brushes.White;
        scroll.Offset = default;
        await ReloadAsync();
    }

    public Task RefreshHistoryAsync() => IsVisible && showHistory ? ReloadAsync() : Task.CompletedTask;
    public Task RefreshSavedAsync() => IsVisible && !showHistory ? ReloadAsync() : Task.CompletedTask;

    async Task ReloadAsync()
    {
        int read = ++revision;
        entries.Children.Clear();
        message.IsVisible = true; message.Text = "Loading…";
        try
        {
            var contents = await library.ReadAsync();
            if (read != revision) return;
            if (showHistory)
            {
                for (var i = 0; i < contents.History.Length; i++)
                {
                    var entry = contents.History[i];
                    string when = entry.Time.ToLocalTime().ToString("g");
                    AddEntry(entry.Statement.Trim().ReplaceLineEndings(" "), $"{entry.Database} · {when}", entry.Statement,
                        $"Open history entry {i + 1}", null);
                }
            }
            else foreach (var question in contents.Questions)
                AddEntry(question.Name, question.Statement.Trim().ReplaceLineEndings(" "), question.Statement,
                    "Open saved question " + question.Name, question);
            message.Text = showHistory ? "Run a query to see it here." : "Save a question from the Questions menu.";
            message.IsVisible = entries.Children.Count == 0;
        }
        catch (Exception error)
        {
            if (read == revision) message.Text = "Could not read questions. " + error.Message;
        }
    }

    void AddEntry(string title, string detail, string statement, string accessibleName, SavedQuestion? question)
    {
        var text = new StackPanel { Spacing = 3 };
        text.Children.Add(new TextBlock { Text = title, Foreground = CyanGraphView.Ink, FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = detail, Foreground = CyanGraphView.Muted, FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis });
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8), Background = Brushes.Transparent };
        AutomationProperties.SetName(button, accessibleName);
        ToolTip.SetTip(button, statement);
        button.Click += (_, _) => OpenRequested?.Invoke(statement, question);
        entries.Children.Add(button);
    }
}
