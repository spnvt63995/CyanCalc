using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CyanGraph.Data;

namespace CyanGraph;

public sealed partial class CyanGraphView
{
    readonly QuestionLibrary library;
    readonly QuestionsPane questions;
    readonly MenuItem saveQuestion = new() { Header = "Save question…" };
    readonly TextBlock queryTitle = Heading("Cypher query"), historyProblem = Caption("");
    SavedQuestion? activeQuestion;
    Task pendingHistory = Task.CompletedTask;
    string? lastStatement, lastParameters;

    MenuItem QuestionsMenu()
    {
        var recent = new MenuItem { Header = "Run history" };
        var saved = new MenuItem { Header = "Saved questions" };
        recent.Click += async (_, _) => await questions.ShowAsync(true);
        saved.Click += async (_, _) => await questions.ShowAsync(false);
        saveQuestion.Click += async (_, _) => await SaveQuestionAsync();
        return new MenuItem { Header = "Questions", ItemsSource = new[] { recent, saved, saveQuestion } };
    }

    Task RunUserQuery(QueryWorkspace target, string statement, string parameterJson)
    {
        if (closing || target.Connected is not { } db || string.IsNullOrWhiteSpace(statement)) return Task.CompletedTask;
        if (target == workspace) { lastStatement = statement; lastParameters = parameterJson; }
        var running = target.RunAsync(statement, parameterJson);
        pendingHistory = RecordAsync(pendingHistory, new QueryHistoryEntry(DateTimeOffset.UtcNow, db.Database, statement));
        return running;
    }

    async Task RecordAsync(Task previous, QueryHistoryEntry entry)
    {
        await previous;
        try
        {
            await library.RecordAsync(entry);
            historyProblem.IsVisible = false;
            await questions.RefreshHistoryAsync();
        }
        catch (Exception error)
        {
            historyProblem.Text = "Could not save run history. " + error.Message;
            ToolTip.SetTip(historyProblem, historyProblem.Text);
            historyProblem.IsVisible = true;
        }
    }

    void OpenQuestion(string statement, SavedQuestion? question)
    {
        activeQuestion = question;
        _ = workspace.ClearResultAsync();
        editor.Text = statement;
        editor.CaretIndex = 0;
        if (Bounds.Width < 1040) questions.IsVisible = false;
        editor.Focus();
        Refresh();
    }

    async Task SaveQuestionAsync()
    {
        if (editor.Text is not { } statement || string.IsNullOrWhiteSpace(statement)) return;
        var original = activeQuestion;
        var dialog = new Window { Title = "Save question", Width = 440, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Paper };
        var body = new StackPanel { Margin = new Thickness(22), Spacing = 10 };
        var name = new TextBox { Text = original?.Name, Watermark = "Question name" };
        AutomationProperties.SetName(name, "Question name");
        var feedback = Caption("");
        feedback.IsVisible = false; feedback.TextWrapping = TextWrapping.Wrap;
        var save = Primary(original == null ? "Save" : "Save changes");
        var cancel = new Button { Content = "Cancel" };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        actions.Children.Add(cancel); actions.Children.Add(save);
        body.Children.Add(Heading("Save question")); body.Children.Add(Caption("Name"));
        body.Children.Add(name); body.Children.Add(feedback); body.Children.Add(actions);
        dialog.Content = body;
        bool saving = false;
        async Task Submit()
        {
            if (saving || string.IsNullOrWhiteSpace(name.Text)) return;
            saving = true; name.IsEnabled = save.IsEnabled = cancel.IsEnabled = false;
            try
            {
                activeQuestion = await library.SaveAsync(name.Text, statement, original);
            }
            catch (Exception error)
            {
                feedback.Text = error.Message; feedback.IsVisible = true;
                saving = false; name.IsEnabled = save.IsEnabled = cancel.IsEnabled = true;
                if (closing) dialog.Close();
                return;
            }
            saving = false; dialog.Close();
            Refresh();
            await questions.RefreshSavedAsync();
        }
        save.IsEnabled = !string.IsNullOrWhiteSpace(name.Text);
        name.TextChanged += (_, _) => save.IsEnabled = !saving && !string.IsNullOrWhiteSpace(name.Text);
        save.Click += async (_, _) => await Submit();
        cancel.Click += (_, _) => dialog.Close();
        dialog.Closing += (_, e) => { if (saving) e.Cancel = true; };
        dialog.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await Submit(); }
            else if (e.Key == Key.Escape && !saving) { e.Handled = true; dialog.Close(); }
        };
        dialog.Opened += (_, _) => { name.Focus(); name.SelectAll(); };
        await dialogs.ShowAsync(dialog, this);
    }
}
