// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Globalization;
using System.Text.Json;
using YApex.Core.Security;

namespace YApex.UI.Settings;

public sealed record UiSettings(
    string Language,
    string Theme,
    IReadOnlyList<string>? SeenProgramPathHashes = null);

public static class UiSettingsStore
{
    private const int MaximumSeenProgramPathHashes = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static UiSettings Load()
    {
        var fallback = new UiSettings(
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? "ar" : "en",
            "system");
        try
        {
            EnsureDirectory();
            if (!File.Exists(SettingsPath))
            {
                return fallback;
            }

            var info = new FileInfo(SettingsPath);
            if (info.Length > 16 * 1024 || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return fallback;
            }

            var settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(SettingsPath), JsonOptions);
            return settings is not null && IsSupportedLanguage(settings.Language) && IsSupportedTheme(settings.Theme) &&
                   IsValidSeenProgramPathHashes(settings.SeenProgramPathHashes)
                ? settings with { SeenProgramPathHashes = settings.SeenProgramPathHashes ?? Array.Empty<string>() }
                : fallback;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return fallback;
        }
    }

    public static void Save(UiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!IsSupportedLanguage(settings.Language) || !IsSupportedTheme(settings.Theme) ||
            !IsValidSeenProgramPathHashes(settings.SeenProgramPathHashes))
        {
            throw new ArgumentException("The UI settings contain an unsupported language or theme.", nameof(settings));
        }

        EnsureDirectory();
        var temporaryPath = SettingsPath + ".new";
        var contents = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(temporaryPath, contents);
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YSWEF",
        "Y-Apex",
        "ui-settings.json");

    private static void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("The Y-Apex settings directory could not be resolved.");
        Directory.CreateDirectory(directory);
        if ((new DirectoryInfo(directory).Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The Y-Apex settings directory must not be a symbolic link or reparse point.");
        }
    }

    private static bool IsSupportedLanguage(string language) => language is "ar" or "en";

    private static bool IsSupportedTheme(string theme) => theme is "system" or "light" or "dark";

    private static bool IsValidSeenProgramPathHashes(IReadOnlyList<string>? hashes) =>
        hashes is null ||
        (hashes.Count <= MaximumSeenProgramPathHashes &&
         hashes.All(hash => hash is { Length: 64 } && hash.All(Uri.IsHexDigit)));
}
