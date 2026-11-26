using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace CyanGraph;

static class Program
{
    internal static string ProfileFile { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyanGraph", "questions.json");

    [STAThread]
    public static void Main(string[] args)
    {
        if (args is ["--profile", var file]) ProfileFile = Path.GetFullPath(file);
        AppBuilder.Configure<CyanGraphApp>()
            .UsePlatformDetect().With(new AvaloniaNativePlatformOptions { OverlayPopups = true })
            .StartWithClassicDesktopLifetime(args);
    }
}

public sealed class CyanGraphApp : Application
{
    public CyanGraphApp()
    {
        Name = "CyanGraph";
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
    }
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new QueryWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
