using Avalonia.Controls;
using CyanCalc;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CyanCalc.Controls.Preview;

// Keep display state fresh without replacing focused buttons or scroll position.
// The panel's visual lifetime also bounds the model event subscription.
internal static class PreviewPanelBinding
{
    internal static void Bind(StackPanel panel, UserModel model, Action refresh)
    {
        var subscribed = false;
        void Changed(object? sender, EventArgs args)
        {
            if (Dispatcher.UIThread.CheckAccess()) { if (subscribed) refresh(); }
            else Dispatcher.UIThread.Post(() => { if (subscribed) refresh(); });
        }
        void Attach()
        {
            if (subscribed) return;
            subscribed = true; model.Changed += Changed; refresh();
        }
        panel.AttachedToVisualTree += (_, _) => Attach();
        panel.DetachedFromVisualTree += (_, _) => { subscribed = false; model.Changed -= Changed; };
        if (panel.GetVisualRoot() is not null) Attach();
    }
}
