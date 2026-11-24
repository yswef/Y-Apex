// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Security.Cryptography;
using System.Text;
using YApex.Core.Domain;

namespace YApex.Core.Security;

public static class RuleFactory
{
    public const string OwnershipMarker = "YSWEF:Y-APEX:MANAGED:V1";

    public static IReadOnlyList<ManagedFirewallRule> CreateProgramRules(
        string programPath,
        FirewallAction action,
        ProgramFileIdentity? identity = null)
    {
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        var normalizedPath = WindowsProgramPath.Normalize(programPath);
        var fileHash = NormalizeFileHash(identity, normalizedPath);
        var signature = identity?.Signature ?? SignatureState.Unknown;
        return
        [
            CreateRule(normalizedPath, FirewallDirection.Inbound, action, fileHash, signature),
            CreateRule(normalizedPath, FirewallDirection.Outbound, action, fileHash, signature)
        ];
    }

    public static bool IsOwnedRule(ManagedFirewallRule rule) =>
        rule.Name.StartsWith("Y-Apex ", StringComparison.Ordinal) &&
        rule.Description.StartsWith(OwnershipMarker + ";", StringComparison.Ordinal);

    private static ManagedFirewallRule CreateRule(
        string programPath,
        FirewallDirection direction,
        FirewallAction action,
        string fileHash,
        SignatureState signature)
    {
        var identity = string.Join('|', programPath.ToUpperInvariant(), direction.ToString());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        var id = new Guid(hash.AsSpan(0, 16));
        var name = $"Y-Apex {id:D} {direction}";
        var description = $"{OwnershipMarker};rule={id:D};path={WindowsProgramPath.GetStableHash(programPath)};file={fileHash};signature={signature}";
        return new ManagedFirewallRule(id, name, description, programPath, direction, action, true, fileHash == "unknown" ? null : fileHash, signature);
    }

    private static string NormalizeFileHash(ProgramFileIdentity? identity, string normalizedPath)
    {
        if (identity is null)
        {
            return "unknown";
        }

        if (!string.Equals(WindowsProgramPath.Normalize(identity.ProgramPath), normalizedPath, StringComparison.OrdinalIgnoreCase) ||
            !Enum.IsDefined(identity.Signature))
        {
            throw new ArgumentException("The file identity belongs to another program path or has an invalid signature state.", nameof(identity));
        }

        if (identity.Sha256 is null)
        {
            if (identity.Signature == SignatureState.Valid)
            {
                throw new ArgumentException("A verified program identity must include a SHA-256 hash.", nameof(identity));
            }

            return "unknown";
        }

        if (identity.Sha256.Length != 64 || !identity.Sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("The file SHA-256 hash is invalid.", nameof(identity));
        }

        return identity.Sha256.ToLowerInvariant();
    }
}
