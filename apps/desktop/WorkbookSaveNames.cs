namespace CyanCalc.Desktop;

internal static class WorkbookSaveNames
{
    // The picker owns the output extension; the workbook name remains unchanged.
    internal static string SuggestedStem(string? name)
    {
        var stem = name ?? "Workbook";
        while (stem.EndsWith(".ic", StringComparison.OrdinalIgnoreCase) ||
               stem.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            stem = stem[..stem.LastIndexOf('.')];
        return stem.Length == 0 ? "Workbook" : stem;
    }
}
