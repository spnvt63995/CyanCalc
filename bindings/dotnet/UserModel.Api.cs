using System.Text.Json;

namespace CyanCalc;

/// <summary>
/// Desktop and Python/Ruby compatibility conveniences. The WASM-equivalent API
/// is generated in UserModel.Wasm.g.cs; range overloads only convert arguments.
/// </summary>
public sealed partial class UserModel
{
    public object? GetCellValue(int sheet, int row, int column) =>
        ScalarCellValue(Query("get_cell_value", new { sheet, row, column }));
    public object? GetCellValueByRef(string cellRef) =>
        ScalarCellValue(Query("get_cell_value_by_ref", new { cell_ref = cellRef }));
    public string? GetCellFormula(int sheet, int row, int column) =>
        Query("get_cell_formula", new { sheet, row, column }).GetString();
    private static object? ScalarCellValue(JsonElement result) => result.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => result.GetString(),
        JsonValueKind.Number => result.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new InvalidOperationException("IronCalc returned a non-scalar cell value.")
    };
    // Avalonia colors are resolved for rendering; GetCellStyle retains upstream theme references.
    public ExtendedCellStyle GetExtendedCellStyle(int sheet, int row, int column) => Cell(sheet, row, column).ExtendedStyle;
    public (int MinRow, int MaxRow, int MinColumn, int MaxColumn) GetSheetDimensions(int sheet)
    {
        var d = Query<int[]>("sheet_dimensions", new { sheet }); return (d[0], d[1], d[2], d[3]);
    }
    public void MergeCells(int sheet, int startRow, int startColumn, int endRow, int endColumn) =>
        MergeCells(InclusiveArea(sheet, startRow, startColumn, endRow, endColumn));
    public void MergeCellsCenter(int sheet, int startRow, int startColumn, int endRow, int endColumn) =>
        MergeCellsCenter(InclusiveArea(sheet, startRow, startColumn, endRow, endColumn));
    public void MergeCellsAcross(int sheet, int startRow, int startColumn, int endRow, int endColumn) =>
        MergeCellsAcross(InclusiveArea(sheet, startRow, startColumn, endRow, endColumn));
    public void MergeCellsDown(int sheet, int startRow, int startColumn, int endRow, int endColumn) =>
        MergeCellsDown(InclusiveArea(sheet, startRow, startColumn, endRow, endColumn));
    public void UnmergeCells(int sheet, int startRow, int startColumn, int endRow, int endColumn) =>
        UnmergeCells(InclusiveArea(sheet, startRow, startColumn, endRow, endColumn));
    public void UpdateRangeStyle(int sheet, int startRow, int startColumn, int endRow, int endColumn, string stylePath, string value) =>
        UpdateRangeStyle(InclusiveArea(sheet, startRow, startColumn, endRow, endColumn), stylePath, value);
    public void SetAreaWithBorder(int sheet, int startRow, int startColumn, int endRow, int endColumn, JsonElement borderArea) =>
        SetAreaWithBorder(InclusiveArea(sheet, startRow, startColumn, endRow, endColumn), borderArea);
    public void AutoFillRows(int sheet, int startRow, int startColumn, int endRow, int endColumn, int toRow) =>
        AutoFillRows(InclusiveArea(sheet, startRow, startColumn, endRow, endColumn), toRow);
    public void AutoFillColumns(int sheet, int startRow, int startColumn, int endRow, int endColumn, int toColumn) =>
        AutoFillColumns(InclusiveArea(sheet, startRow, startColumn, endRow, endColumn), toColumn);
    public void PasteFromClipboard(int sourceSheet, (int Row1, int Column1, int Row2, int Column2) sourceRange, JsonElement clipboard, bool isCut) =>
        PasteFromClipboard(sourceSheet, new[] { sourceRange.Row1, sourceRange.Column1, sourceRange.Row2, sourceRange.Column2 }, clipboard, isCut);
    public void PasteCsvString(int sheet, int startRow, int startColumn, int endRow, int endColumn, string csv) =>
        PasteCsvText(InclusiveArea(sheet, startRow, startColumn, endRow, endColumn), csv);

    public static UserModel FromICalc(string fileName, string languageId = "en") => FromBytes(File.ReadAllBytes(fileName), languageId);
    public static UserModel FromXlsx(string filePath, string locale = "en", string tz = "UTC", string languageId = "en") =>
        FromXlsx(File.ReadAllBytes(filePath), Path.GetFileNameWithoutExtension(filePath), locale, tz, languageId);
    public void SaveToXlsx(string file) => WriteNew(file, ToXlsx());
    public void SaveToICalc(string file) => WriteNew(file, ToBytes());
    private static void WriteNew(string path, byte[] bytes)
    {
        // Upstream Python file helpers refuse overwrites. Desktop Save is a host operation.
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write); stream.Write(bytes);
    }
    private static CellArea InclusiveArea(int sheet, int startRow, int startColumn, int endRow, int endColumn) =>
        new(sheet, startRow, startColumn, checked(endColumn - startColumn + 1), checked(endRow - startRow + 1));
}

public enum CellType { Number = 1, Text = 2, LogicalValue = 4, ErrorValue = 16, Array = 64, CompoundData = 128 }
