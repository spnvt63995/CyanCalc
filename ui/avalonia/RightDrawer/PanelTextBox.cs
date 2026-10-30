using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace CyanCalc.Controls;

/// <summary>Panel text input with single-line clipboard normalization.</summary>
internal sealed class PanelTextBox : TextBox
{
    private int _pasteVersion;

    protected override Type StyleKeyOverride => typeof(TextBox);

    public PanelTextBox() => PastingFromClipboard += PasteSingleLine;

    private async void PasteSingleLine(object? sender, RoutedEventArgs e)
    {
        if (e.Handled || AcceptsReturn) return;
        e.Handled = true;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null || IsReadOnly || !IsEffectivelyEnabled || !IsEffectivelyVisible) return;

        var version = ++_pasteVersion;
        string? text;
        try { text = await clipboard.TryGetTextAsync(); }
        catch (TimeoutException) { return; }

        // A delayed clipboard response must not edit a closed/replaced panel or
        // overwrite text/selection changed while the clipboard was being read.
        if (version != _pasteVersion || IsReadOnly || !IsEffectivelyEnabled ||
            !IsEffectivelyVisible || string.IsNullOrEmpty(text)) return;

        // SelectedText keeps TextBox's selection replacement, length limit and
        // undo/redo behavior. Multiline fields use the ordinary TextBox paste.
        SelectedText = text.ReplaceLineEndings(" ");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == CaretIndexProperty ||
            change.Property == SelectionStartProperty || change.Property == SelectionEndProperty ||
            change.Property == IsReadOnlyProperty || change.Property == AcceptsReturnProperty ||
            change.Property == IsFocusedProperty || change.Property == IsVisibleProperty ||
            change.Property == IsEffectivelyEnabledProperty)
            _pasteVersion++;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _pasteVersion++;
        base.OnDetachedFromVisualTree(e);
    }
}
