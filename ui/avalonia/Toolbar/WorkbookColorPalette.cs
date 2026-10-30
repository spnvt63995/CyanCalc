using System.Text.Json;
using Avalonia.Input;

namespace CyanCalc.Controls;

// Values stay typed: a workbook-theme reference must never become a fixed RGB
// when it is selected again from Recent colors.
internal readonly record struct PickerColor(int? Slot, double Tint, string? Hex)
{
    internal static PickerColor Theme(int slot, double tint) => new(slot, tint, null);
    internal static PickerColor Rgb(string hex) => new(null, 0, hex);
    internal string StyleValue => Slot is int slot
        ? JsonSerializer.Serialize(new[] { (double)slot, Tint }) : Hex!;
}

internal readonly record struct PickerPosition(int Row, int Column);

internal static class WorkbookColorPalette
{
    internal static readonly string[] SlotNames =
        ["Light 1", "Dark 1", "Light 2", "Dark 2", "Accent 1", "Accent 2", "Accent 3", "Accent 4", "Accent 5", "Accent 6"];

    internal static readonly string[] StandardHex =
        ["#800000", "#FF0000", "#FFA500", "#FFFF00", "#92D050",
         "#00B050", "#00B0F0", "#0070C0", "#002060", "#7030A0"];

    internal static readonly PickerColor[] ThemeColors = BuildThemeColors();

    private static PickerColor[] BuildThemeColors()
    {
        var colors = new List<PickerColor>();
        for (var slot = 0; slot < 10; slot++) colors.Add(PickerColor.Theme(slot, 0));
        for (var row = 0; row < 5; row++)
            for (var slot = 0; slot < 10; slot++)
            {
                var tints = slot switch
                {
                    0 => new[] { -.05, -.15, -.25, -.35, -.5 },
                    1 => new[] { .5, .35, .25, .15, .05 },
                    2 => new[] { -.1, -.25, -.5, -.75, -.9 },
                    _ => new[] { .8, .6, .4, -.25, -.5 }
                };
                colors.Add(PickerColor.Theme(slot, tints[row]));
            }
        return colors.ToArray();
    }

    internal static bool Same(PickerColor left, PickerColor right) =>
        left.Slot == right.Slot && (left.Slot is not null
            ? left.Tint == right.Tint
            : string.Equals(left.Hex, right.Hex, StringComparison.OrdinalIgnoreCase));

    internal static void Remember(List<PickerColor> recent, PickerColor color)
    {
        if (recent.Any(item => Same(item, color))) return;
        recent.Insert(0, color);
        if (recent.Count > 29) recent.RemoveRange(29, recent.Count - 29);
    }

    internal static bool ValidHex(string? value) => value is { Length: 4 or 7 }
        && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    // Mirrors the pinned web picker's data-nav-row/col lookup. Only Left/Right
    // can cross to an adjacent row, and neither wraps the entire palette.
    internal static int Next(IReadOnlyList<PickerPosition> positions, int current, Key key)
    {
        if (current < 0 || current >= positions.Count) return current;
        var position = positions[current];
        var row = position.Row + (key == Key.Up ? -1 : key == Key.Down ? 1 : 0);
        var column = position.Column + (key == Key.Left ? -1 : key == Key.Right ? 1 : 0);
        for (var i = 0; i < positions.Count; i++)
            if (positions[i] == new PickerPosition(row, column)) return i;
        if (key == Key.Left)
        {
            var previous = -1;
            for (var i = 0; i < positions.Count; i++)
                if (positions[i].Row == position.Row - 1
                    && (previous < 0 || positions[i].Column > positions[previous].Column)) previous = i;
            if (previous >= 0) return previous;
        }
        if (key == Key.Right)
            for (var i = 0; i < positions.Count; i++)
                if (positions[i] == new PickerPosition(position.Row + 1, 0)) return i;
        return current;
    }

    internal static bool IsLight(string hex)
    {
        var color = global::Avalonia.Media.Color.Parse(hex);
        return .2126 * color.R + .7152 * color.G + .0722 * color.B > 160;
    }
}
