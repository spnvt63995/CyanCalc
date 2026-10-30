using Avalonia.Controls;
using Avalonia.Media;

namespace CyanCalc.Controls;

// A native Window has its own theme scope; mirror the component that opened it.
internal static class NativeDialogTheme
{
    internal static void Follow(Window dialog, Control owner)
    {
        void Changed(object? sender, EventArgs args)
        {
            dialog.RequestedThemeVariant = owner.ActualThemeVariant;
            var palette = SheetPalette.For(owner.ActualThemeVariant);
            dialog.Background = dialog.SystemDecorations == SystemDecorations.None ? Brushes.Transparent : palette.Surface;
            dialog.Foreground = new SolidColorBrush(Color.Parse(palette.BorderPreview));
        }
        Changed(null, EventArgs.Empty);
        owner.ActualThemeVariantChanged += Changed;
        dialog.Closed += (_, _) => owner.ActualThemeVariantChanged -= Changed;
    }
}
