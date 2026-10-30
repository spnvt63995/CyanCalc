using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace CyanCalc.Controls;

// AppKit overlay timing shared by the worksheet and drawer scrollbars.
internal sealed class OverlayScrollBarVisibility : IDisposable
{
    private readonly ScrollBar _bar;
    private readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly SplineEasing _easing = new() { X1 = .42, Y1 = 0, X2 = .58, Y2 = 1 };
    private bool _active, _overlay, _pressed;
    private int _animation;

    internal OverlayScrollBarVisibility(ScrollBar bar)
    {
        _bar = bar;
        bar.PointerEntered += Entered; bar.PointerExited += Exited;
        bar.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        bar.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel, handledEventsToo: true);
        bar.PointerCaptureLost += CaptureLost;
        _idle.Tick += (_, _) => Fade();
    }

    internal void Reset(bool active, bool overlay)
    {
        CancelFade(); _active = active; _overlay = overlay; _pressed = false;
        _bar.Classes.Set("overlay", overlay); _bar.Classes.Remove("expanded");
        _bar.Opacity = overlay ? 0 : 1; _bar.IsHitTestVisible = !overlay;
    }

    internal void Show()
    {
        if (!_active || !_overlay || !_bar.IsVisible) return;
        CancelFade(); _bar.Opacity = 1; _bar.IsHitTestVisible = true;
        if (!_pressed && !_bar.IsPointerOver) _idle.Start();
    }

    private void CancelFade() { _idle.Stop(); _animation++; }
    private void Entered(object? sender, PointerEventArgs e)
    {
        if (!_overlay || !_bar.IsHitTestVisible) return;
        Show(); _bar.Classes.Add("expanded");
    }
    private void Exited(object? sender, PointerEventArgs e)
    {
        if (_active && !_pressed && _overlay && _bar.Opacity > 0) { CancelFade(); _idle.Start(); }
    }
    private void Pressed(object? sender, PointerPressedEventArgs e) { _pressed = true; Show(); }
    private void Released(object? sender, PointerReleasedEventArgs e) { _pressed = false; Show(); }
    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e) { _pressed = false; Show(); }

    private void Fade()
    {
        CancelFade();
        if (!_active || !_overlay || _pressed || _bar.Opacity <= 0 || _bar.IsPointerOver) return;
        var generation = _animation;
        var topLevel = TopLevel.GetTopLevel(_bar);
        TimeSpan? started = null;
        void Frame(TimeSpan now)
        {
            if (generation != _animation) return;
            started ??= now;
            var progress = Math.Clamp((now - started.Value).TotalMilliseconds / 250, 0, 1);
            _bar.Opacity = 1 - _easing.Ease(progress);
            if (progress < 1 && topLevel is not null) topLevel.RequestAnimationFrame(Frame);
            else { _bar.IsHitTestVisible = false; _bar.Classes.Remove("expanded"); }
        }
        if (topLevel is not null) topLevel.RequestAnimationFrame(Frame);
        else Reset(_active, _overlay);
    }

    public void Dispose()
    {
        Reset(false, _overlay);
        _bar.PointerEntered -= Entered; _bar.PointerExited -= Exited;
        _bar.RemoveHandler(InputElement.PointerPressedEvent, Pressed);
        _bar.RemoveHandler(InputElement.PointerReleasedEvent, Released);
        _bar.PointerCaptureLost -= CaptureLost;
    }
}
