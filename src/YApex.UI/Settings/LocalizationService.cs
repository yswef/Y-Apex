// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.IO;
using System.Text.Json;
using System.Windows;

namespace YApex.UI.Settings;

public static class LocalizationService
{
    private static IReadOnlyDictionary<string, string> _values = new Dictionary<string, string>();

    public static string CurrentLanguage { get; private set; } = "en";

    public static void Apply(string language)
    {
        var selected = language is "ar" or "en" ? language : "en";
        var path = Path.Combine(AppContext.BaseDirectory, "locales", selected + ".json");
        var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("The selected locale file is empty.");
        _values = loaded;
        CurrentLanguage = selected;
        var resources = Application.Current.Resources;
        foreach (var pair in loaded)
        {
            resources[pair.Key] = pair.Value;
        }

        resources["TextFlowDirection"] = selected == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    public static string Get(string key) => _values.TryGetValue(key, out var value) ? value : key;
}
