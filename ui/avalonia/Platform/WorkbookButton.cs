using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace CyanCalc.Controls;

// Avalonia 11 macOS AXPress compatibility. Reassess on Avalonia >= 12.0.5
// after verifying disabled and enabled actions through native accessibility.
// Upstream fix: https://github.com/AvaloniaUI/Avalonia/pull/21599
internal class WorkbookButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);
    protected override AutomationPeer OnCreateAutomationPeer() => MacOSButtonAutomationPeer.Create(this);
}

internal sealed class MacOSButtonAutomationPeer(Button owner) : ButtonAutomationPeer(owner), IInvokeProvider
{
    private static readonly bool NeedsGuard = OperatingSystem.IsMacOS() &&
        typeof(Button).Assembly.GetName().Version?.Major == 11;

    internal static AutomationPeer Create(Button owner) => new MacOSButtonAutomationPeer(owner);

    protected override string GetClassNameCore() =>
        Owner.GetType() == typeof(WorkbookButton) ? nameof(Button) : base.GetClassNameCore();

    void IInvokeProvider.Invoke()
    {
        // IsEnabled includes disabled ancestors and Command.CanExecute, just as
        // the native guard in Avalonia 12 does. Enabled actions use the stock peer.
        if (!NeedsGuard || IsEnabled()) base.Invoke();
    }
}
