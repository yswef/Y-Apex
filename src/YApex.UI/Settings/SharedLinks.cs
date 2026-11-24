// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Text.Json;

namespace YApex.UI.Settings;

public sealed record WalletLink(string Name, string Address);

public sealed record SharedLinks(
    string? BuyMeACoffee,
    string? Issues,
    string? Changelog,
    string? License,
    string? Privacy,
    string? Repository,
    IReadOnlyList<WalletLink> Wallets)
{
    public static SharedLinks Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "config", "shared-links.json");
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 64 * 1024 || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return Empty;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            var wallets = new List<WalletLink>();
            if (root.TryGetProperty("wallets", out var walletArray) && walletArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var wallet in walletArray.EnumerateArray().Take(16))
                {
                    if (wallet.ValueKind != JsonValueKind.Object ||
                        !wallet.TryGetProperty("name", out var name) ||
                        !wallet.TryGetProperty("address", out var address) ||
                        name.ValueKind != JsonValueKind.String || address.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var walletName = name.GetString();
                    var walletAddress = address.GetString();
                    if (!string.IsNullOrWhiteSpace(walletName) && IsSafeWalletAddress(walletAddress))
                    {
                        wallets.Add(new WalletLink(walletName, walletAddress!));
                    }
                }
            }

            return new SharedLinks(
                ReadHttps(root, "buyMeACoffee"),
                ReadHttps(root, "issues"),
                ReadHttps(root, "changelog"),
                ReadHttps(root, "license"),
                ReadHttps(root, "privacy"),
                ReadHttps(root, "repo"),
                wallets);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Empty;
        }
    }

    private static SharedLinks Empty { get; } = new(null, null, null, null, null, null, Array.Empty<WalletLink>());

    private static string? ReadHttps(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps &&
               string.IsNullOrEmpty(uri.UserInfo) &&
               uri.Host.Length > 0
            ? uri.AbsoluteUri
            : null;
    }

    private static bool IsSafeWalletAddress(string? address) =>
        !string.IsNullOrWhiteSpace(address) &&
        address.Length <= 512 &&
        !address.Any(char.IsControl);
}
