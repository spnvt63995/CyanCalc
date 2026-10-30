using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;

namespace CyanCalc.Controls;

// Shared presentation from the pinned Button, Input, Switch and right-drawer CSS.
internal static class WorkbookChrome
{
    internal static void Attach(Control root)
    {
        WorkbookDialog.Attach(root);
        root.Styles.Add(new StyleInclude(new Uri("avares://CyanCalc.Avalonia/"))
            { Source = new Uri("avares://CyanCalc.Avalonia/Workbook/WorkbookChromeStyles.axaml") });
    }
}
