// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Globalization;
using YApex.Core.Domain;

namespace YApex.UI.Models;

public sealed class UsageRow(ProgramUsage value, long maximumBytes)
{
    public ProgramUsage Value { get; } = value;

    public string ProgramName => Value.ProgramName;

    public string ProgramPath => Value.ProgramPath;

    public string SentText => FormatBytes(Value.BytesSent);

    public string ReceivedText => FormatBytes(Value.BytesReceived);

    public string TotalText => FormatBytes(Value.TotalBytes);

    public double RelativeUsage => maximumBytes <= 0
        ? 0
        : Math.Clamp((double)Value.TotalBytes / maximumBytes * 100d, 0d, 100d);

    public static string FormatBytes(long bytes)
    {
        var value = Math.Max(0, bytes);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var quantity = (double)value;
        var unitIndex = 0;
        while (quantity >= 1024d && unitIndex < units.Length - 1)
        {
            quantity /= 1024d;
            unitIndex++;
        }

        return unitIndex == 0
            ? string.Format(CultureInfo.CurrentCulture, "{0:N0} {1}", quantity, units[unitIndex])
            : string.Format(CultureInfo.CurrentCulture, "{0:N1} {1}", quantity, units[unitIndex]);
    }
}
