using System.Text.Json;
using System.Text.RegularExpressions;

namespace CyanCalc;

public sealed record CellArea(int Sheet, int Row, int Column, int Width, int Height)
{
    public static CellArea FromView(SelectedView view)
    {
        var r = view.Range;
        return new(view.Sheet, Math.Min(r[0], r[2]), Math.Min(r[1], r[3]), Math.Abs(r[3] - r[1]) + 1, Math.Abs(r[2] - r[0]) + 1);
    }
    public string Address => $"{CellAddress.Format(Row, Column)}:{CellAddress.Format(Row + Height - 1, Column + Width - 1)}";
}

public sealed record SelectedView
{
    public int Sheet { get; init; }
    public int Row { get; init; } = 1;
    public int Column { get; init; } = 1;
    public int[] Range { get; init; } = [1, 1, 1, 1];
    public int TopRow { get; init; } = 1;
    public int LeftColumn { get; init; } = 1;
}

/// <summary>A merged rectangle; rows/columns are 1-based and the top-left cell owns its content.</summary>
public sealed record MergedCell(int Row, int Column, int Width, int Height)
{
    public int LastRow => Row + Height - 1;
    public int LastColumn => Column + Width - 1;
    public bool Contains(int row, int column) => row >= Row && row <= LastRow && column >= Column && column <= LastColumn;
    public CellArea Area(int sheet) => new(sheet, Row, Column, Width, Height);
}

public sealed record WorksheetInfo
{
    public string Name { get; init; } = "";
    public int SheetId { get; init; }
    public string State { get; init; } = "visible";
    public JsonElement Color { get; init; }
}

public sealed record WorkbookSnapshot
{
    public string Name { get; init; } = "";
    public SelectedView View { get; init; } = new();
    public bool CanUndo { get; init; }
    public bool CanRedo { get; init; }
    public WorksheetInfo[] Worksheets { get; init; } = [];
    public MergedCell[] MergedCells { get; init; } = [];
    public int FrozenRows { get; init; }
    public int FrozenColumns { get; init; }
    public bool ShowGridLines { get; init; }
    public string Locale { get; init; } = "en";
    public string Timezone { get; init; } = "UTC";
    public string Language { get; init; } = "en";
    public CellSnapshot Cell { get; init; } = new();
}

public sealed record CellSnapshot
{
    public int Row { get; init; }
    public int Column { get; init; }
    public string Content { get; init; } = "";
    public string Display { get; init; } = "";
    public int CellType { get; init; }
    public ExtendedCellStyle ExtendedStyle { get; init; } = new();
    public JsonElement ArrayStructure { get; init; }
}

public sealed record ExtendedCellStyle
{
    public CellStyle Style { get; init; } = new();
    public ConditionalIcon? Icon { get; init; }
    public ConditionalDataBar? DataBar { get; init; }
    public ConditionalRating? Rating { get; init; }
}

public sealed record ConditionalIcon(string Icon, string? Color, bool ShowValue);
public sealed record ConditionalDataBar(string? PositiveColor, string? NegativeColor, bool IsGradient, double Value, double AxisPosition, bool ShowValue);
public sealed record ConditionalRating(string Icon, int Count, int Max, string? Color, bool ShowValue);
public sealed record CellStyle
{
    public bool ReadOnly { get; init; }
    public bool QuotePrefix { get; init; }
    public CellFont Font { get; init; } = new();
    public CellFill Fill { get; init; } = new();
    public CellBorder Border { get; init; } = new();
    public string NumFmt { get; init; } = "General";
    public CellAlignment? Alignment { get; init; }
}
public sealed record CellFont
{
    public bool B { get; init; }
    public bool I { get; init; }
    public bool U { get; init; }
    public bool Strike { get; init; }
    public double Sz { get; init; } = 12;
    public string? Color { get; init; }
    public string Name { get; init; } = "Arial";
}
public sealed record CellFill(string? Color = null);
public sealed record CellAlignment(string Horizontal = "general", string Vertical = "bottom", bool WrapText = false);
public sealed record BorderItem(string Style, string? Color);
public sealed record CellBorder
{
    public BorderItem? Left { get; init; }
    public BorderItem? Right { get; init; }
    public BorderItem? Top { get; init; }
    public BorderItem? Bottom { get; init; }
    public BorderItem? Diagonal { get; init; }
    public bool DiagonalUp { get; init; }
    public bool DiagonalDown { get; init; }
}
public sealed record SheetDimensions(double[] Rows, double[] Columns);
public sealed record DefinedName(string Name, int? Scope, string Formula);

public static partial class CellAddress
{
    public const int LastRow = 1_048_576;
    public const int LastColumn = 16_384;
    public static string ColumnName(int column)
    {
        if (column is < 1 or > LastColumn) throw new ArgumentOutOfRangeException(nameof(column));
        var result = "";
        for (var value = column; value > 0; value = (value - 1) / 26)
            result = (char)('A' + (value - 1) % 26) + result;
        return result;
    }
    public static string Format(int row, int column) => $"{ColumnName(column)}{row}";
    public static bool TryParse(string text, out int row, out int column)
    {
        row = column = 0;
        var match = AddressPattern().Match(text.Trim());
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out row)) return false;
        foreach (char letter in match.Groups[1].Value.ToUpperInvariant()) column = column * 26 + letter - 'A' + 1;
        return row is >= 1 and <= LastRow && column is >= 1 and <= LastColumn;
    }
    [GeneratedRegex(@"^\$?([A-Za-z]{1,3})\$?([1-9][0-9]{0,6})$")]
    private static partial Regex AddressPattern();
}
