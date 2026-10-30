using Avalonia.Controls;
using CyanCalc.Controls;

namespace CyanCalc.Desktop;

/// <summary>The desktop shell handles explicit external link requests from the reusable control.</summary>
internal static class WorkbookLinkHost
{
    internal static void Bind(WorkbookView view, Window owner, Action<LocalizedText> report)
    {
        view.OpenExternalLink = uri => _ = OpenAsync(uri);

        async Task OpenAsync(Uri uri)
        {
            try
            {
                if (!await owner.Launcher.LaunchUriAsync(uri)) report(view.Text.Key("native.open_link_failed", ("error", uri)));
            }
            catch (Exception error) { report(view.Text.Key("native.open_link_failed", ("error", error.Message))); }
        }
    }
}
