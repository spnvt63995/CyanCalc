using System.Text.Json;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace CyanCalc.Controls;

/// <summary>Original name-menu/link parsers and a separate host address helper.</summary>
public sealed partial class WorkbookView
{
    private bool _nameMenuPreservesEdit;

    internal Button CreateNameMenuButton(Func<bool>? canManageNames = null)
    {
        var button = IconButton("ChevronDown", () => { }, Text.Key("formula_bar.manage_named_ranges"));
        button.Name = "DefinedNameMenuButton";
        var menu = WorkbookMenus.Create(anchored: true);
        // FormulaBar uses its own 172px rows inside a 182px popover.
        menu.Classes.Add("name-menu");
        menu.MinWidth = 182;
        menu.MaxWidth = 400;
        menu.HorizontalOffset = -4;
        button.ContextMenu = menu;
        CellEditor? returnEditor = null;

        void PreserveEdit()
        {
            returnEditor = State.Editing is not null ? ActiveEditor : null;
            _nameMenuPreservesEdit = true;
        }

        button.AddHandler(PointerPressedEvent, (_, _) => PreserveEdit(), RoutingStrategies.Tunnel);
        button.Click += (_, _) =>
        {
            PreserveEdit();
            menu.ItemsSource = BuildNameMenuItems(canManageNames?.Invoke() ?? true);
            menu.Open(button);
        };
        menu.Closed += (_, _) =>
        {
            _nameMenuPreservesEdit = false;
            if (returnEditor is not null && State.Editing is not null) returnEditor.Focus();
            else button.Focus();
            returnEditor = null;
        };
        button.PointerReleased += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (menu.IsOpen) return;
            _nameMenuPreservesEdit = false;
            if (returnEditor is not null && State.Editing is not null) returnEditor.Focus();
            returnEditor = null;
        });
        return button;
    }

    private Control[] BuildNameMenuItems(bool canManageNames)
    {
        var snapshot = Snapshot;
        var session = Session;
        var names = session?.GetDefinedNameList() ?? [];
        var items = new List<Control>();
        foreach (var name in names)
        {
            var scope = name.Scope is { } index && snapshot is not null && index >= 0 && index < snapshot.Worksheets.Length
                ? snapshot.Worksheets[index].Name : "Workbook";
            var item = MenuItem($"{name.Name} · {scope}   {name.Formula}", () =>
            {
                // FormulaBarMenu invokes the displayed row's formula without a
                // workbook revision or catalogue-identity policy.
                if (!ReferenceEquals(Session, session)) return;
                Run(() => NavigateNamedRange(name.Formula));
            });
            SetMenuExample(item, name.Name, name.Formula);
            item.Icon = new WorkbookIcon("Tag", 12);
            Text.Compose(t => $"{name.Name}, {(name.Scope is null ? t["name_manager_dialog.workbook"] : scope)}, {name.Formula}").Bind(item, global::Avalonia.Automation.AutomationProperties.NameProperty);
            items.Add(item);
        }
        if (items.Count > 0) items.Add(new Separator());
        items.Add(Text.Key("formula_bar.manage_named_ranges").Bind(new MenuItem { IsEnabled = canManageNames }, global::Avalonia.Controls.MenuItem.HeaderProperty));
        ((MenuItem)items[^1]).Click += (_, _) => ShowNames();
        foreach (var item in items.OfType<MenuItem>()) item.Classes.Add("name-menu-item");
        return items.ToArray();
    }

    /// <summary>Resolves a direct address or name using the selected sheet's scope first.</summary>
    internal bool TryNavigateAddressOrName(string input)
    {
        if (Session is null || Snapshot is null) return false;
        var address = input.Trim();
        if (TryResolveReference(address, Snapshot.View.Sheet, out var direct)) return NavigateTo(direct);
        var (name, sheet) = NameInSheet(address, Snapshot.View.Sheet);
        if (sheet < 0) { Report(Text.Key("native.name_sheet_hidden")); return false; }
        var candidates = MatchingNames(name, sheet);
        if (candidates.Length == 1) return TryNavigateName(candidates[0]);
        Report(candidates.Length > 1 ? Text["native.name_ambiguous"] : Text["native.enter_reference"]);
        return false;
    }

    private DefinedName[] MatchingNames(string name, int sheet)
    {
        var names = Session!.GetDefinedNameList().Where(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        var scoped = names.Where(x => x.Scope == sheet).ToArray();
        return scoped.Length > 0 ? scoped : names.Where(x => x.Scope is null).ToArray();
    }

    private (string Name, int Sheet) NameInSheet(string input, int selectedSheet)
    {
        if (Snapshot is null) return (input, selectedSheet);
        for (var sheet = 0; sheet < Snapshot.Worksheets.Length; sheet++)
        {
            var name = Snapshot.Worksheets[sheet].Name;
            var quoted = "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'!";
            var usual = ReferenceUtils.QuoteName(name) + "!";
            if (input.StartsWith(usual, StringComparison.OrdinalIgnoreCase))
                return (input[usual.Length..], Snapshot.Worksheets[sheet].State.Equals("visible", StringComparison.OrdinalIgnoreCase) ? sheet : -1);
            if (input.StartsWith(quoted, StringComparison.OrdinalIgnoreCase))
                return (input[quoted.Length..], Snapshot.Worksheets[sheet].State.Equals("visible", StringComparison.OrdinalIgnoreCase) ? sheet : -1);
        }
        return (input, selectedSheet);
    }

    internal bool TryNavigateName(DefinedName name)
    {
        return NavigateNamedRange(name.Formula);
    }

    // FormulaBarMenu and NamedRanges use Editor/util.tsx parseRangeInSheet:
    // inspect the first token, require a sheet name, preserve endpoint order.
    // Do not add full-formula, visibility, revision or scope-fallback policies.
    private bool NavigateNamedRange(string formula)
    {
        if (Session is not { } model) return false;
        var token = ReferenceUtils.GetTokens(formula)[0].GetProperty("token");
        if (token.ValueKind != JsonValueKind.Object) return false;
        int row, column, endRow, endColumn;
        JsonElement reference;
        if (token.TryGetProperty("Range", out reference))
        {
            row = reference.GetProperty("left").GetProperty("row").GetInt32();
            column = reference.GetProperty("left").GetProperty("column").GetInt32();
            endRow = reference.GetProperty("right").GetProperty("row").GetInt32();
            endColumn = reference.GetProperty("right").GetProperty("column").GetInt32();
        }
        else if (token.TryGetProperty("Reference", out reference))
        {
            row = endRow = reference.GetProperty("row").GetInt32();
            column = endColumn = reference.GetProperty("column").GetInt32();
        }
        else return false;
        var name = reference.GetProperty("sheet");
        if (name.ValueKind != JsonValueKind.String) return false;
        var sheet = Array.FindIndex(model.GetWorksheetsProperties(), item => item.Name == name.GetString());
        if (sheet < 0) return false;
        model.SetSelectedSheet(sheet);
        model.SetSelectedCell(row, column);
        model.SetSelectedRange(row, column, endRow, endColumn);
        return true;
    }

    // WorksheetCanvas/cellLinks.ts followInternalLink deliberately has different
    // parsing from the name menu: first matching name, normalized endpoints, and
    // an invalid range end falls back to the first cell. Preserve those quirks.
    internal bool TryFollowInternalLink(string location)
    {
        if (Session is not { } model) return false;
        string sheetName, cellRef;
        var separator = location.LastIndexOf('!');
        if (separator >= 0)
        {
            sheetName = location[..separator]; cellRef = location[(separator + 1)..];
        }
        else
        {
            var name = model.GetDefinedNameList().FirstOrDefault(entry => entry.Name.Equals(location, StringComparison.OrdinalIgnoreCase));
            if (name is null) return false;
            separator = name.Formula.LastIndexOf('!');
            if (separator < 0) return false;
            sheetName = name.Formula[..separator];
            if (sheetName.StartsWith('=')) sheetName = sheetName[1..];
            cellRef = name.Formula[(separator + 1)..];
        }
        if (sheetName.Length >= 2 && sheetName[0] == '\'' && sheetName[^1] == '\'') sheetName = sheetName[1..^1];
        var parts = cellRef.Replace("$", "").Split(':');
        var first = System.Text.RegularExpressions.Regex.Match(parts[0], @"^([A-Za-z]+)([0-9]+)$");
        if (!first.Success) return false;
        var last = parts.Length == 2 ? System.Text.RegularExpressions.Regex.Match(parts[1], @"^([A-Za-z]+)([0-9]+)$") : first;
        if (!last.Success) last = first;
        var sheet = Array.FindIndex(model.GetWorksheetsProperties(), item => item.Name == sheetName);
        if (sheet < 0) return false;
        int column, columnEnd;
        try
        {
            column = ReferenceUtils.ColumnNumberFromName(first.Groups[1].Value.ToUpperInvariant());
            columnEnd = ReferenceUtils.ColumnNumberFromName(last.Groups[1].Value.ToUpperInvariant());
        }
        catch (IronCalcException) { return false; }
        if (!int.TryParse(first.Groups[2].Value, out var row) || !int.TryParse(last.Groups[2].Value, out var rowEnd)) return false;
        (row, rowEnd) = (Math.Min(row, rowEnd), Math.Max(row, rowEnd));
        (column, columnEnd) = (Math.Min(column, columnEnd), Math.Max(column, columnEnd));
        if (row < 1 || rowEnd > CellAddress.LastRow || column < 1 || columnEnd > CellAddress.LastColumn) return false;
        model.SetSelectedSheet(sheet);
        model.SetSelectedCell(row, column);
        model.SetSelectedRange(row, column, rowEnd, columnEnd);
        if (Worksheet.Layout.CellRect(row, column) is null) model.SetTopLeftVisibleCell(row, column);
        return true;
    }

    /// <summary>Host address navigation; the original name-menu and link parsers are separate above.</summary>
    internal bool TryNavigateReference(string reference, int? defaultSheet = null)
    {
        if (!TryResolveNavigationTarget(reference, defaultSheet, out var target))
        {
            Report(Text.Key("native.invalid_destination"));
            return false;
        }
        return NavigateTo(target);
    }

    /// <summary>Read-only destination check for editing an internal cell link.</summary>
    internal bool IsValidNavigationReference(string reference, int? defaultSheet = null) =>
        TryResolveNavigationTarget(reference, defaultSheet, out _);

    internal bool TryResolveNavigationTarget(string input, int? defaultSheet, out CellArea target)
    {
        if (TryResolveReference(input, defaultSheet ?? Snapshot?.View.Sheet, out target)) return true;
        if (Session is null || Snapshot is null) return false;
        var (name, sheet) = NameInSheet(input.Trim(), defaultSheet ?? Snapshot.View.Sheet);
        if (sheet < 0) return false;
        var matches = MatchingNames(name, sheet);
        return matches.Length == 1 && TryResolveReference(matches[0].Formula, matches[0].Scope ?? sheet, out target);
    }

    private bool TryResolveReference(string input, int? defaultSheet, out CellArea target)
    {
        target = new CellArea(0, 1, 1, 1, 1);
        if (Session is null || Snapshot is null) return false;
        var formula = input.Trim();
        if (formula.StartsWith('=')) formula = formula[1..].Trim();
        if (!LooksLikeReference(formula)) return false;

        // The engine tokenizer already understands escaped sheet names, absolute
        // coordinates and range syntax. Require exactly one complete reference token:
        // a formula whose first token is a range may still contain more arithmetic.
        JsonElement[] tokens;
        try { tokens = Session.Query("tokens", new { formula }).EnumerateArray().ToArray(); }
        catch (IronCalcException) { return false; }
        if (tokens.Length != 1 || tokens[0].GetProperty("start").GetInt32() != 0
            || tokens[0].GetProperty("end").GetInt32() != formula.EnumerateRunes().Count()) return false;

        var token = tokens[0].GetProperty("token");
        if (token.ValueKind != JsonValueKind.Object) return false;
        int row, column, endRow, endColumn;
        JsonElement reference;
        if (token.TryGetProperty("Reference", out reference))
        {
            row = endRow = reference.GetProperty("row").GetInt32();
            column = endColumn = reference.GetProperty("column").GetInt32();
        }
        else if (token.TryGetProperty("Range", out reference))
        {
            row = reference.GetProperty("left").GetProperty("row").GetInt32();
            column = reference.GetProperty("left").GetProperty("column").GetInt32();
            endRow = reference.GetProperty("right").GetProperty("row").GetInt32();
            endColumn = reference.GetProperty("right").GetProperty("column").GetInt32();
        }
        else return false;

        var sheetName = reference.GetProperty("sheet");
        var sheet = sheetName.ValueKind == JsonValueKind.String
            ? Array.FindIndex(Snapshot.Worksheets, x => x.Name == sheetName.GetString())
            : defaultSheet ?? -1;
        if (sheet < 0 || sheet >= Snapshot.Worksheets.Length || row < 1 || column < 1
            || endRow < 1 || endColumn < 1 || row > CellAddress.LastRow || endRow > CellAddress.LastRow
            || column > CellAddress.LastColumn || endColumn > CellAddress.LastColumn) return false;
        if (!Snapshot.Worksheets[sheet].State.Equals("visible", StringComparison.OrdinalIgnoreCase)) return false;
        target = new CellArea(sheet, Math.Min(row, endRow), Math.Min(column, endColumn),
            Math.Abs(endColumn - column) + 1, Math.Abs(endRow - row) + 1);
        return true;
    }

    private bool LooksLikeReference(string formula)
    {
        if (Snapshot is null) return false;
        var delimiter = formula.LastIndexOf('!');
        var address = delimiter < 0 ? formula : formula[(delimiter + 1)..];
        var endpoints = address.Split(':');
        if (endpoints.Length is < 1 or > 2 || !CellAddress.TryParse(endpoints[0], out _, out _)
            || endpoints.Length == 2 && !CellAddress.TryParse(endpoints[1], out _, out _)) return false;
        if (delimiter < 0) return true;
        var prefix = formula[..delimiter];
        return Snapshot.Worksheets.Any(sheet =>
            prefix.Equals(ReferenceUtils.QuoteName(sheet.Name), StringComparison.OrdinalIgnoreCase)
            || prefix.Equals("'" + sheet.Name.Replace("'", "''", StringComparison.Ordinal) + "'", StringComparison.OrdinalIgnoreCase));
    }

    private bool NavigateTo(CellArea target)
    {
        if (Session is null || Snapshot is null) return false;
        if (Snapshot.View.Sheet != target.Sheet) Session.SetSelectedSheet(target.Sheet);
        Session.SetSelectedCell(target.Row, target.Column);
        if (target.Width > 1 || target.Height > 1)
            Session.SetSelectedRange(target.Row, target.Column, target.Row + target.Height - 1, target.Column + target.Width - 1);
        ScrollTo(Math.Max(Snapshot.FrozenRows + 1, target.Row), Math.Max(Snapshot.FrozenColumns + 1, target.Column));
        FocusCell();
        return true;
    }
}
