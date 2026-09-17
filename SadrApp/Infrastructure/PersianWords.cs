namespace SadrApp.Infrastructure;

/// <summary>
/// Converts numeric amounts to Persian words (عدد به حروف) — used on the printed
/// invoice so the total appears in words beneath the numeric total.
/// </summary>
public static class PersianWords
{
    private static readonly string[] Ones =
        { "", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه" };

    private static readonly string[] Teens =
        { "ده", "یازده", "دوازده", "سیزده", "چهارده", "پانزده", "شانزده", "هفده", "هجده", "نوزده" };

    private static readonly string[] Tens =
        { "", "", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود" };

    private static readonly string[] Hundreds =
        { "", "صد", "دویست", "سیصد", "چهارصد", "پانصد", "ششصد", "هفتصد", "هشتصد", "نهصد" };

    private static readonly string[] Scales = { "", " هزار", " میلیون", " میلیارد" };

    /// <summary>Converts a non-negative integer below 1,000,000,000,000 to Persian words.</summary>
    public static string NumberToWords(long n)
    {
        if (n == 0) return "صفر";
        if (n < 0) return "منفی " + NumberToWords(-n);

        // split into groups of three digits, most significant last
        var groups = new List<int>();
        while (n > 0)
        {
            groups.Add((int)(n % 1000));
            n /= 1000;
        }

        var parts = new List<string>();
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i] == 0) continue;
            var scale = i < Scales.Length ? Scales[i] : "";
            // 1000 is «هزار» not «یک هزار» — common Persian style
            parts.Add(groups[i] == 1 && i == 1 ? "هزار" : ThreeDigitToWords(groups[i]) + scale);
        }
        return string.Join(" و ", parts);
    }

    /// <summary>Words for 0..999.</summary>
    public static string ThreeDigitToWords(int n)
    {
        var parts = new List<string>();
        int h = n / 100, rest = n % 100;
        if (h > 0) parts.Add(Hundreds[h]);
        if (rest >= 10 && rest < 20) parts.Add(Teens[rest - 10]);
        else
        {
            int t = rest / 10, o = rest % 10;
            if (t > 0) parts.Add(Tens[t]);
            if (o > 0) parts.Add(Ones[o]);
        }
        return string.Join(" و ", parts);
    }

    /// <summary>
    /// Amount in words: integer part + currency; when the amount has a fraction,
    /// the two-decimal part is spelled out as «X صدم ریال».
    /// </summary>
    public static string AmountToWords(decimal amount, string currency = "ریال")
    {
        if (amount < 0) return "منفی " + AmountToWords(-amount, currency);

        long whole = (long)Math.Truncate(amount);
        decimal frac = Math.Round(amount - whole, 2);
        string words = NumberToWords(whole) + " " + currency;

        if (frac > 0)
        {
            int cents = (int)Math.Round(frac * 100);
            if (cents > 0)
                words += " و " + NumberToWords(cents) + " صدم " + currency;
        }
        return words;
    }
}
