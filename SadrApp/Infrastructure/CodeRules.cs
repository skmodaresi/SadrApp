using System.Text;

namespace SadrApp.Infrastructure;

/// <summary>
/// Shared validation for code fields (کد):
/// - normalizes Persian (۰-۹) and Arabic (٠-٩) digits to Latin digits
/// - validates the Iranian national ID (کد ملی) with the standard checksum algorithm
/// - Persian error messages for required / duplicate / invalid codes
/// </summary>
public static class CodeRules
{
    // ---- پیام‌های خطا ----
    public const string MsgCodeRequired = "وارد کردن کد الزامی است.";
    public const string MsgCodeDuplicate = "این کد قبلاً ثبت شده است. کد تکراری مجاز نیست.";
    public const string MsgDetailCodeDuplicate = "این کد قبلاً در حساب‌های تفصیلی استفاده شده است؛ حساب تفصیلی خودکار ایجاد نمی‌شود.";
    public const string MsgNationalIdRequired = "وارد کردن کد ملی الزامی است.";
    public const string MsgNationalIdDigits = "کد ملی باید دقیقاً ۱۰ رقم باشد.";
    public const string MsgNationalIdInvalid = "کد ملی وارد شده معتبر نیست.";
    public const string MsgNationalIdRepeated = "کد ملی نمی‌تواند ارقام تکراری باشد.";
    public const string MsgLegalIdDigits = "شناسه ملی باید دقیقاً ۱۱ رقم باشد.";
    public const string MsgLegalIdInvalid = "شناسه ملی وارد شده معتبر نیست.";

    /// <summary>
    /// Converts Persian/Arabic digits to Latin, removes whitespace and trims.
    /// Returns "" for null/whitespace input.
    /// </summary>
    public static string NormalizeDigits(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (char.IsWhiteSpace(ch)) continue;
            sb.Append(ch switch
            {
                >= '۰' and <= '۹' => (char)(ch - '۰' + '0'),
                >= '٠' and <= '٩' => (char)(ch - '٠' + '0'),
                _ => ch
            });
        }
        return sb.ToString();
    }

    /// <summary>Standard Iranian national ID (کد ملی) checksum; accepts Persian digits too.</summary>
    public static bool IsValidIranianNationalId(string? input) => ValidateNationalId(input) is null;

    /// <summary>
    /// Returns null when the national ID is valid, otherwise the Persian error message.
    /// Algorithm: 10 digits; sum = Σ digit[i] × (10−i) for i = 0..8, mod 11;
    /// remainder &lt; 2 → control digit must equal remainder, otherwise must equal 11 − remainder.
    /// </summary>
    public static string? ValidateNationalId(string? input)
    {
        var code = NormalizeDigits(input);
        if (code.Length == 0) return MsgNationalIdRequired;
        if (code.Length != 10 || code.Any(c => c is < '0' or > '9')) return MsgNationalIdDigits;
        if (code.Distinct().Count() == 1) return MsgNationalIdRepeated;

        var check = code[9] - '0';
        var sum = 0;
        for (var i = 0; i < 9; i++)
            sum += (code[i] - '0') * (10 - i);
        sum %= 11;
        return (sum < 2 ? check == sum : check + sum == 11) ? null : MsgNationalIdInvalid;
    }

    /// <summary>Legal entity ID (شناسه ملی, 11 digits) checksum; accepts Persian digits too.</summary>
    public static bool IsValidIranianLegalNationalId(string? input) => ValidateLegalNationalId(input) is null;

    /// <summary>
    /// Returns null when the legal entity ID (شناسه ملی) is valid, otherwise the Persian error
    /// message. Empty input is allowed (the field is optional in the Companies editor).
    /// Algorithm: 11 digits; d = digit[0] + 2;
    /// sum = Σ (d + digit[i]) × weight[i mod 5] for i = 0..9 with weights {29, 27, 23, 19, 17},
    /// mod 11 (10 → 0) must equal digit[10]; the six middle digits (positions 3..8) must not all be zero.
    /// </summary>
    public static string? ValidateLegalNationalId(string? input)
    {
        var code = NormalizeDigits(input);
        if (code.Length == 0) return null; // اختیاری — خالی مجاز است
        if (code.Length != 11 || code.Any(c => c is < '0' or > '9')) return MsgLegalIdDigits;

        var digits = code.Select(c => c - '0').ToArray();
        if (digits.All(d => d == 0)) return MsgLegalIdInvalid;
        if (digits.Skip(3).Take(6).All(d => d == 0)) return MsgLegalIdInvalid;

        var d = digits[0] + 2;
        var weights = new[] { 29, 27, 23, 19, 17 };
        var sum = 0;
        for (var i = 0; i < 10; i++)
            sum += (d + digits[i]) * weights[i % 5];
        sum %= 11;
        if (sum == 10) sum = 0;
        return sum == digits[10] ? null : MsgLegalIdInvalid;
    }
}
