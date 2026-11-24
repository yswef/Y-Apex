// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Security.Cryptography;
using System.Text;

namespace YApex.Core.Security;

public static class WindowsProgramPath
{
    private static readonly HashSet<char> InvalidCharacters = ['<', '>', '"', '|', '?', '*'];

    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length is < 4 or > 32_767 || path.Any(char.IsControl))
        {
            throw new ArgumentException("The program path has an invalid length or contains control characters.", nameof(path));
        }

        var candidate = path.Trim().Replace('/', '\\');
        if (candidate.StartsWith("\\\\", StringComparison.Ordinal) ||
            candidate.StartsWith("\\?\\", StringComparison.Ordinal) ||
            candidate.StartsWith("\\.\\", StringComparison.Ordinal) ||
            !IsDriveRooted(candidate))
        {
            throw new ArgumentException("Use an absolute local Windows drive path.", nameof(path));
        }

        var segments = candidate[3..].Split('\\');
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new ArgumentException("The program path contains an empty or traversal segment.", nameof(path));
        }

        if (segments.Any(segment => segment.EndsWith(' ') || segment.EndsWith('.') || segment.Any(InvalidCharacters.Contains) || segment.Contains(':') || IsReservedDeviceName(segment)))
        {
            throw new ArgumentException("The program path contains an invalid Windows path segment.", nameof(path));
        }

        return char.ToUpperInvariant(candidate[0]) + candidate[1..];
    }

    public static string GetStableHash(string normalizedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedPath);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath.ToUpperInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsReservedDeviceName(string segment)
    {
        var extensionSeparator = segment.IndexOf('.');
        var stem = (extensionSeparator < 0 ? segment : segment[..extensionSeparator]).TrimEnd(' ', '.');
        var upperStem = stem.ToUpperInvariant();
        return upperStem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
               (upperStem.Length == 4 &&
                upperStem[3] is >= '1' and <= '9' &&
                (upperStem.StartsWith("COM", StringComparison.Ordinal) || upperStem.StartsWith("LPT", StringComparison.Ordinal)));
    }

    private static bool IsDriveRooted(string path) =>
        path.Length >= 4 &&
        char.IsAsciiLetter(path[0]) &&
        path[1] == ':' &&
        path[2] == '\\';
}
