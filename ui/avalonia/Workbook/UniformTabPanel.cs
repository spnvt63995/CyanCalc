using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace CyanCalc.Controls;

// Keep tab headers equally wide while letting TabControl use its normal
// directional focus and selection handling.
internal sealed class UniformTabPanel : UniformGrid, INavigableContainer
{
    public UniformTabPanel() => Rows = 1;

    IInputElement? INavigableContainer.GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        var index = from is Control control ? Children.IndexOf(control) : -1;
        index = direction switch
        {
            NavigationDirection.First => 0,
            NavigationDirection.Last => Children.Count - 1,
            NavigationDirection.Next or NavigationDirection.Right => index + 1,
            NavigationDirection.Previous or NavigationDirection.Left => index - 1,
            _ => -1
        };

        // Match the non-wrapping horizontal navigation of TabControl's
        // default WrapPanel.
        return index >= 0 && index < Children.Count ? Children[index] : null;
    }
}
