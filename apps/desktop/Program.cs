using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using CyanCalc.Controls;

namespace CyanCalc.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => AppBuilder.Configure<LabApplication>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
}

public sealed class LabApplication : Application
{
    public override void Initialize()
    {
        Name = "CyanCalc";
        RequestedThemeVariant = ThemeVariant.Default;
        Styles.Add(new FluentTheme());
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            var args = desktop.Args ?? [];
            var library = Array.IndexOf(args, "--library");
            var libraryPath = library >= 0 && library + 1 < args.Length ? args[library + 1] : WorkbookLibrary.DefaultPath;
            try
            {
                desktop.MainWindow = new WorkbookWindow(libraryPath);
            }
            catch (Exception error)
            {
                var text = new WorkbookText();
                try { text.Language = WorkbookLibrary.ReadPreferences(libraryPath).Language; }
                catch { /* No readable language setting: use the default catalog. */ }
                desktop.MainWindow = new Window { Title = "CyanCalc — " + text["native.library_open_failed_title"], Width = 580, Height = 200,
                    Content = new TextBlock { Text = text["native.library_open_failed"] + "\n\n" + error.Message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(24) } };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
