using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CyanGraph.Data;

namespace CyanGraph;

public sealed class QueryWindow : Window
{
    readonly CyanGraphView view;
    bool closing, drained;

    public QueryWindow()
    {
        Title = "CyanGraph";
        Width = 1170; Height = 780; MinWidth = 740; MinHeight = 530;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RequestedThemeVariant = ThemeVariant.Light;
        Background = Brush.Parse("#F2F6F6");
        Content = view = new CyanGraphView(new QueryWorkspace(), Program.ProfileFile);
        Closing += OnClosing;
    }

    async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (drained) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        IsEnabled = false;
        await view.CloseAsync();
        drained = true;
        Close();
    }
}
