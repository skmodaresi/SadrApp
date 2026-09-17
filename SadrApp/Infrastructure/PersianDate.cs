using System.Globalization;

namespace SadrApp.Infrastructure;

/// <summary>
/// Persian (Jalali) date helpers: display Gregorian dates as yyyy/MM/dd Persian
/// and parse Persian text into Gregorian DateTime for storage.
/// </summary>
public static class PersianDate
{
    private static readonly PersianCalendar Cal = new();

    public static string Today => ToPersian(DateTime.Now);

    public static string ToPersian(DateTime? g)
    {
        if (g is null) return string.Empty;
        var d = g.Value;
        return $"{Cal.GetYear(d):0000}/{Cal.GetMonth(d):00}/{Cal.GetDayOfMonth(d):00}";
    }

    public static string ToPersianLong(DateTime? g)
    {
        if (g is null) return string.Empty;
        var d = g.Value;
        return $"{Cal.GetYear(d)}/{Cal.GetMonth(d):00}/{Cal.GetDayOfMonth(d):00} - {d:HH:mm}";
    }

    /// <summary>
    /// Parses a Jalali date string like 1404/05/01 into a Gregorian DateTime.
    /// Uses PersianCalendar directly — culture-based parsing would misread the
    /// Jalali year as a Gregorian one on .NET 5+.
    /// </summary>
    public static DateTime? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().Replace('-', '/').Replace('\\', '/').Replace('.', '/');
        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3) return null;
        if (!int.TryParse(parts[0], out var y) || !int.TryParse(parts[1], out var m)) return null;
        var d = parts.Length == 3 && int.TryParse(parts[2], out var dd) ? dd : 1;
        try
        {
            if (y < 100) y += 1400;              // allow short forms like 04/05/01
            if (y is < 1300 or > 1500) return null;
            if (m is < 1 or > 12) return null;
            if (d < 1 || d > Cal.GetDaysInMonth(y, m)) return null;
            return Cal.ToDateTime(y, m, d, 0, 0, 0, 0);
        }
        catch
        {
            return null;
        }
    }
}
