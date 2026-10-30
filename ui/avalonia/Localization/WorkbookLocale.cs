namespace CyanCalc.Controls;

internal static class WorkbookLocale
{
    internal static string DisplayName(string locale) => locale switch
    {
        "en" => "en-US", "es" => "es-ES", "fr" => "fr-FR", "de" => "de-DE", "it" => "it-IT", _ => locale
    };
}
