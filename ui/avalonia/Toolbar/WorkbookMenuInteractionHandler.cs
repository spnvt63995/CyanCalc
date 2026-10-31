using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace CyanCalc.Controls;

// The upstream menu uses buttons: Space activates a command on release.
// Delegate activation to Avalonia's Enter path to retain toggles, commands,
// StaysOpenOnClick and the owning menu's close/focus behavior.
internal sealed class WorkbookMenuInteractionHandler : DefaultMenuInteractionHandler, IMenuInteractionHandler
{
    private MenuItem? _spaceItem;

    internal WorkbookMenuInteractionHandler() : base(isContextMenu: true) { }

    void IMenuInteractionHandler.Attach(MenuBase menu)
    {
        base.Attach(menu);
        menu.KeyUp += KeyUp;
        menu.Closed += MenuClosed;
    }

    void IMenuInteractionHandler.Detach(MenuBase menu)
    {
        _spaceItem = null;
        menu.KeyUp -= KeyUp;
        menu.Closed -= MenuClosed;
        base.Detach(menu);
    }

    protected override void KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None &&
            e.Source is MenuItem { IsFocused: true, IsEffectivelyEnabled: true, HasSubMenu: false } item)
        {
            _spaceItem ??= item;
            e.Handled = true;
            return;
        }

        _spaceItem = null;
        base.KeyDown(sender, e);
    }

    private void KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        var item = _spaceItem;
        _spaceItem = null;
        if (item is null) return;
        e.Handled = true;
        if (e.KeyModifiers != KeyModifiers.None || !ReferenceEquals(e.Source, item) ||
            !item.IsFocused || !item.IsEffectivelyEnabled || item.HasSubMenu) return;

        base.KeyDown(sender, new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Source = item, Key = Key.Enter });
    }

    protected override void LostFocus(object? sender, RoutedEventArgs e)
    {
        _spaceItem = null;
        base.LostFocus(sender, e);
    }

    private void MenuClosed(object? sender, RoutedEventArgs e) => _spaceItem = null;
}
