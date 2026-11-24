// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using YApex.Core.Security;
using Xunit;

namespace YApex.Core.Tests;

public sealed class PathFuzzTests
{
    [Fact]
    public void RandomPathInputsAreRejectedOrNormalizedWithoutEscapingTheValidator()
    {
        var random = new Random(52177);
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789\\/:.?*<>|\" \u0000";
        for (var iteration = 0; iteration < 2_000; iteration++)
        {
            var length = random.Next(0, 128);
            var candidate = new string(Enumerable.Range(0, length)
                .Select(_ => alphabet[random.Next(alphabet.Length)])
                .ToArray());
            try
            {
                var normalized = WindowsProgramPath.Normalize(candidate);
                Assert.True(char.IsAsciiLetter(normalized[0]));
                Assert.False(normalized.Split('\\').Any(segment => segment is "." or ".."));
                Assert.False(normalized.StartsWith("\\\\", StringComparison.Ordinal));
            }
            catch (ArgumentException)
            {
            }
        }
    }
}
