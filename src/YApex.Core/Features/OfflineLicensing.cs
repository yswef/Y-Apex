// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Text.Json;
using YApex.Core.Security;

namespace YApex.Core.Features;

public enum ProFeature
{
    LongTermHistory,
    Profiles,
    Scheduling
}

public sealed record LicensePayload(string Product, string Licensee, DateTimeOffset ExpiresAt, IReadOnlyList<string> Features);

public sealed record LicenseVerificationResult(bool IsValid, LicensePayload? Payload, string Message);

public static class OfflineLicenseVerifier
{
    private const int MaximumLicenseBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static LicenseVerificationResult Verify(
        string payloadBase64,
        string signatureBase64,
        ReadOnlySpan<byte> publicKey,
        DateTimeOffset now)
    {
        if (!TryDecode(payloadBase64, out var payloadBytes) ||
            !TryDecode(signatureBase64, out var signatureBytes) ||
            payloadBytes.Length > MaximumLicenseBytes)
        {
            return new LicenseVerificationResult(false, null, "The license encoding is invalid or too large.");
        }

        if (!Ed25519SignatureVerifier.Verify(payloadBytes, signatureBytes, publicKey))
        {
            return new LicenseVerificationResult(false, null, "The Ed25519 signature is invalid.");
        }

        try
        {
            var payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonOptions);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Product) || string.IsNullOrWhiteSpace(payload.Licensee))
            {
                return new LicenseVerificationResult(false, null, "The signed license payload is incomplete.");
            }

            if (payload.ExpiresAt <= now)
            {
                return new LicenseVerificationResult(false, payload, "The license has expired.");
            }

            return new LicenseVerificationResult(true, payload, "The license signature is valid.");
        }
        catch (JsonException)
        {
            return new LicenseVerificationResult(false, null, "The signed license payload is not valid JSON.");
        }
    }

    private static bool TryDecode(string encoded, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length > MaximumLicenseBytes * 2)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(encoded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public static class ProFeatureAccess
{
    public static bool IsAvailable(ProFeature feature) => Enum.IsDefined(feature);

    public static bool IsLicenseRequired(ProFeature feature)
    {
        _ = feature;
        return false;
    }
}
