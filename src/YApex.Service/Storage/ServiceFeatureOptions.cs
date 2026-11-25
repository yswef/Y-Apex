// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Text.Json;

namespace YApex.Service.Storage;

public sealed record ServiceFeatureOptions(bool ExperimentalWfpEnabled = false);

public static class ServiceFeatureOptionsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        MaxDepth = 8,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static ServiceFeatureOptions Load()
    {
        var path = Path.Combine(ServiceDataPaths.RootDirectory, "features.json");
        try
        {
            ServiceDataPaths.EnsurePrivateDirectory();
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 16 * 1024 || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return new ServiceFeatureOptions();
            }

            return JsonSerializer.Deserialize<ServiceFeatureOptions>(File.ReadAllText(path), JsonOptions)
                ?? new ServiceFeatureOptions();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new ServiceFeatureOptions();
        }
    }
}
