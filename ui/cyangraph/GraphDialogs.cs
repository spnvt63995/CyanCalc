using Avalonia.Controls;
using Avalonia.Styling;

namespace CyanGraph;

sealed class GraphDialogs
{
    readonly Dictionary<Window, Task> open = new();
    bool closing;

    public async Task ShowAsync(Window dialog, Control anchor)
    {
        if (closing || TopLevel.GetTopLevel(anchor) is not Window owner) return;
        dialog.RequestedThemeVariant = ThemeVariant.Light;
        var completion = dialog.ShowDialog(owner);
        open.Add(dialog, completion);
        try { await completion; }
        finally { open.Remove(dialog); }
    }

    public Task CloseAsync()
    {
        closing = true;
        // Close nested property dialogs before their separate result window.
        var windows = open.ToArray();
        foreach (var entry in windows.Reverse()) entry.Key.Close();
        return Task.WhenAll(windows.Select(entry => entry.Value));
    }
}
