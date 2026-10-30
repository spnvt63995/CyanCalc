using System.Text.RegularExpressions;

namespace CyanCalc.Controls;

// Port of FormatMenu/formatUtil.ts; arbitrary custom formats remain editable in the format field.
internal static class NumberFormats
{
    public static string Increase(string format)
    {
        if (format.Equals("general", StringComparison.OrdinalIgnoreCase)) return "#,##0.000";
        var result = format.Replace(".0", ".00");
        if (!result.Contains('.'))
        {
            if (result.Contains('0')) return result.Replace("0", "0.0");
            if (result.Contains('#')) return Regex.Replace(result, @"#([^#,]|$)", "0.0$1");
        }
        return result;
    }
    public static string Decrease(string format)
    {
        if (format.Equals("general", StringComparison.OrdinalIgnoreCase)) return "#,##0.0";
        return new Regex(@"0\.([^0]|$)").Replace(format.Replace(".0", "."), "0$1", 1);
    }
}
