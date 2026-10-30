using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CyanCalc;

/// <summary>One native workbook and its editing history. A view does not own this session.</summary>
public sealed partial class UserModel : IDisposable
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };
    private readonly object _gate = new();
    private readonly WorkbookHandle _handle;
    private bool _disposed;
    public event EventHandler? Changed;
    public Guid Identity { get; } = Guid.NewGuid();
    public long Revision { get; private set; }

    public UserModel() : this("Workbook") { }

    private UserModel(bool nativeHandleOnly)
    {
        _handle = NativeMethods.Create();
        if (_handle.IsInvalid) throw new InvalidOperationException("IronCalc could not create a native workbook.");
    }

    /// <summary>Matches the pinned WASM Model constructor. Sheets are zero-based; rows and columns are one-based.</summary>
    public UserModel(string name, string locale = "en", string tz = "UTC", string languageId = "en") : this(true)
    {
        try { Query("new", new { name, locale, timezone = tz, language_id = languageId }); }
        catch { _handle.Dispose(); throw; }
    }

    private static UserModel FromNative(string operation, object arguments)
    {
        var model = new UserModel(true);
        try { model.Query(operation, arguments); return model; }
        catch { model.Dispose(); throw; }
    }
    private static JsonElement? NullableJson(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value;
    // The upstream [sheet,row,column] vector has an idiomatic named CLR tuple representation.
    private static (int Sheet, int Row, int Column) SelectedCellTuple(int[] cell) => (cell[0], cell[1], cell[2]);

    public JsonElement Query(string operation, object? arguments = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var request = arguments is null ? new JsonObject() : JsonSerializer.SerializeToNode(arguments, JsonOptions)!.AsObject();
            request["op"] = operation;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
            var pointer = NativeMethods.Call(_handle, bytes, (nuint)bytes.Length);
            if (pointer == 0) throw new InvalidOperationException("IronCalc returned an empty native response.");
            try
            {
                using var response = JsonDocument.Parse(Marshal.PtrToStringUTF8(pointer)!);
                var root = response.RootElement;
                if (!root.GetProperty("ok").GetBoolean())
                    throw new IronCalcException(operation, root.GetProperty("error").GetString() ?? "Unknown engine error");
                return root.GetProperty("value").Clone();
            }
            finally { NativeMethods.FreeString(pointer); }
        }
    }

    public T Query<T>(string operation, object? arguments = null) =>
        Query(operation, arguments).Deserialize<T>(JsonOptions) ?? throw new InvalidOperationException($"No {typeof(T).Name} returned by {operation}");

    public void Execute(string operation, object? arguments = null)
    {
        Query(operation, arguments);
        if (operation is not ("select_cell" or "select_range" or "select_area" or "select_sheet"
            or "expand_selection" or "navigate" or "navigate_edge" or "set_viewport" or "scroll"
            or "pause_evaluation" or "resume_evaluation"
            or "setSelectedCell" or "setSelectedRange" or "setSelectedSheet" or "onAreaSelecting"
            or "onExpandSelectedRange" or "onArrowRight" or "onArrowLeft" or "onArrowUp" or "onArrowDown"
            or "onPageDown" or "onPageUp" or "onNavigateToEdgeInDirection" or "setTopLeftVisibleCell"
            or "setWindowWidth" or "setWindowHeight" or "pauseEvaluation" or "resumeEvaluation")) Revision++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public WorkbookSnapshot Snapshot() => Query<WorkbookSnapshot>("state");
    public CellSnapshot Cell(int sheet, int row, int column) => Query<CellSnapshot>("cell", new { sheet, row, column });
    public CellSnapshot[] Cells(int sheet, int[] rows, int[] columns) => Query<CellSnapshot[]>("cells", new { sheet, rows, columns });
    public SheetDimensions Dimensions(int sheet, int[] rows, int[] columns) => Query<SheetDimensions>("dimensions", new { sheet, rows, columns });
    public void SetStyle(CellArea area, string path, string value) => UpdateRangeStyle(area, path, value);
    public void LoadBytes(byte[] bytes, string languageId = "en") =>
        Execute("fromBytes", new { bytes = Convert.ToBase64String(bytes), language_id = languageId });
    public void LoadXlsx(byte[] bytes, string name, string locale = "en", string timezone = "UTC", string languageId = "en") =>
        Execute("fromXlsx", new { bytes = Convert.ToBase64String(bytes), name, locale, timezone, language_id = languageId });

    public static UserModel CreateSample()
    {
        var session = new UserModel();
        try
        {
            session.Query("set_name", new { name = "CyanCalc Sample" });
            var rows = new[]
            {
                new[] { "품목", "수량", "단가", "금액" },
                new[] { "노트", "12", "1500", "=B2*C2" },
                new[] { "펜", "7", "2000", "=B3*C3" },
                new[] { "합계", "=SUM(B2:B3)", "", "=SUM(D2:D3)" }
            };
            for (int row = 0; row < rows.Length; row++)
                for (int column = 0; column < rows[row].Length; column++)
                    session.Query("set_user_input", new { sheet = 0, row = row + 1, column = column + 1, input = rows[row][column] });
            session.Query("column_width", new { sheet = 0, column = 1, end = 1, width = 180 });
            session.Query("column_width", new { sheet = 0, column = 2, end = 4, width = 120 });
            foreach (var row in new[] { 1, 4 })
            {
                var area = new CellArea(0, row, 1, 4, 1);
                session.Query("style", new { area, path = "font.b", value = "true" });
                session.Query("style", new { area, path = "fill.color", value = row == 1 ? "#E9EFF7" : "#F2F5F9" });
            }
            session.Query("style", new { area = new CellArea(0, 2, 3, 2, 3), path = "num_fmt", value = "#,##0" });
            session.Query("reset_history");
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Changed = null;
            _handle.Dispose();
        }
    }
}

public sealed class IronCalcException(string operation, string message) : Exception(message)
{
    public string Operation { get; } = operation;
}
