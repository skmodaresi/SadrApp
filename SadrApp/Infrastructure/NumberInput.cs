using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace SadrApp.Infrastructure;

/// <summary>
/// Attaches thousands-separator formatting ("12,500") to numeric TextBoxes.
/// The user types plain digits; separators are inserted live. Parsing tolerates
/// any separators/spaces the user may type or paste, so saved values stay plain.
/// Persian/Arabic digits are normalized to ASCII on entry.
/// </summary>
public static class NumberInput
{
    public const char Sep = ',';

    // UI thread only — guards ApplyText against reentrant TextChanged storms.
    private static bool _updating;

    /// <summary>Live thousands-separator behavior for a TextBox. Non-numeric keystrokes are blocked.</summary>
    public static void Attach(System.Windows.Controls.TextBox tb, bool allowDecimal = true, bool allowNegative = false)
    {
        tb.TextAlignment = TextAlignment.Right;

        tb.PreviewTextInput += (_, e) =>
        {
            e.Handled = e.Text.Any(c => !char.IsAsciiDigit(NormalizeDigit(c)))
                        || (e.Text.Contains('.') && (!allowDecimal || tb.Text.Contains('.')))
                        || (e.Text.Contains('-') && (!allowNegative || tb.Text.Length != 0));
        };

        tb.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space) e.Handled = true;
        };

        // Paste: sanitize to grouped digits (plus allowed signs).
        DataObject.AddPastingHandler(tb, (_, e) =>
        {
            if (_updating) return;
            if (e.DataObject?.GetData(DataFormats.UnicodeText) is not string s) return;
            var clean = Group(Clean(s, allowDecimal, allowNegative));
            if (clean == s) return;
            e.CancelCommand();
            ApplyText(tb, clean);
        });

        tb.TextChanged += (_, _) => Regroup(tb, allowDecimal);
        Regroup(tb, allowDecimal); // pre-filled values (editors) get separators too
    }

    /// <summary>Re-groups the current text with thousands separators, preserving the caret.</summary>
    public static void Regroup(System.Windows.Controls.TextBox tb, bool allowDecimal)
    {
        if (_updating) return;
        var raw = Group(Clean(tb.Text, allowDecimal, allowNegative: false));
        if (raw == tb.Text) return;
        ApplyText(tb, raw);
    }

    private static void ApplyText(System.Windows.Controls.TextBox tb, string text)
    {
        _updating = true;
        try
        {
            var caret = tb.CaretIndex;
            int digitsBeforeCaret = tb.Text.Take(caret).Count(char.IsAsciiDigit);

            tb.Text = text;
            // Place the caret after the same number of digits it was before.
            int seen = 0, idx = 0;
            for (; idx < text.Length && seen < digitsBeforeCaret; idx++)
                if (char.IsAsciiDigit(text[idx])) seen++;
            tb.CaretIndex = Math.Min(idx, text.Length);
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>
    /// Keeps only digits (and optionally one decimal point / leading minus), normalizing
    /// Persian/Arabic digits; separators, spaces and letters are dropped.
    /// A trailing "." survives so the user can keep typing decimals.
    /// </summary>
    public static string Clean(string? text, bool allowDecimal = true, bool allowNegative = false)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new System.Text.StringBuilder(text.Length);
        bool dot = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = NormalizeDigit(text[i]);
            if (char.IsAsciiDigit(c)) sb.Append(c);
            else if (c == '.' && allowDecimal && !dot && sb.Length > 0) { dot = true; sb.Append(c); }
            else if (c == '-' && allowNegative && sb.Length == 0 && text.IndexOf('-') == i) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Inserts thousands separators in the integer part; keeps the decimal part as-is.</summary>
    public static string Group(string cleanText)
    {
        if (string.IsNullOrEmpty(cleanText)) return cleanText;
        bool neg = cleanText.StartsWith("-");
        var body = neg ? cleanText[1..] : cleanText;
        var dot = body.IndexOf('.');
        var intPart = dot >= 0 ? body[..dot] : body;
        var fracPart = dot >= 0 ? body[dot..] : "";

        var sb = new System.Text.StringBuilder(intPart.Length + intPart.Length / 3 + 2);
        for (int i = 0; i < intPart.Length; i++)
        {
            if (i > 0 && (intPart.Length - i) % 3 == 0) sb.Append(Sep);
            sb.Append(intPart[i]);
        }
        return (neg ? "-" : "") + sb.ToString() + fracPart;
    }

    /// <summary>Strips separators/spaces (and normalizes Persian digits) before decimal parsing.</summary>
    public static decimal? ParseDecimal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = Strip(text);
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>Same as ParseDecimal for double values (percent columns).</summary>
    public static double? ParseDouble(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = Strip(text);
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static string Strip(string text) => new string(text.Select(NormalizeDigit)
        .Where(c => char.IsAsciiDigit(c) || c == '.' || c == '-' || c == '+').ToArray())
        .TrimEnd('.'); // "12." while typing still parses as 12

    private static char NormalizeDigit(char c) => c switch
    {
        >= '\u06F0' and <= '\u06F9' => (char)(c - '\u06F0' + '0'), // Persian ۰-۹
        >= '\u0660' and <= '\u0669' => (char)(c - '\u0660' + '0'), // Arabic ٠-٩
        _ => c
    };
}
