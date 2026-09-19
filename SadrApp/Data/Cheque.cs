using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SadrApp.Data;

/// <summary>Shared constants and display names for cheque direction and status.</summary>
public static class ChequeConsts
{
    public const int DirectionReceived = 1; // دریافتی (از مشتری)
    public const int DirectionIssued = 2;   // پرداختی (به تامین‌کننده)

    public const int StatusInHand = 1;       // در جعبه (نزد ما)
    public const int StatusPassedToBank = 2; // واگذار به بانک
    public const int StatusCashed = 3;       // وصول شده
    public const int StatusBounced = 4;      // برگشت خورده
    public const int StatusSpent = 5;        // خرج شده (واگذار به شخص دیگر)
    public const int StatusCanceled = 6;     // ابطال شده

    public static readonly Dictionary<int, string> DirectionNames = new()
    {
        { DirectionReceived, "دریافتی" }, { DirectionIssued, "پرداختی" }
    };

    public static readonly Dictionary<int, string> StatusNames = new()
    {
        { StatusInHand, "در جعبه" }, { StatusPassedToBank, "واگذار به بانک" },
        { StatusCashed, "وصول شده" }, { StatusBounced, "برگشت خورده" },
        { StatusSpent, "خرج شده" }, { StatusCanceled, "ابطال شده" }
    };
}

/// <summary>
/// A cheque. Direction 1 = received from a customer (دریافتی),
/// direction 2 = issued to a provider (پرداختی). Status meanings are defined in ChequesPage.
/// </summary>
public class Cheque
{
    [Key] public int Id { get; set; }
    public int BankAccountId { get; set; }
    /// <summary>Persian date string (e.g. 1405/06/28); DateG is the Gregorian value.</summary>
    public string Date { get; set; } = "";
    public DateTime DateG { get; set; }
    public string Number { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    /// <summary>Full name of the person the cheque is received from / issued to.</summary>
    public string RecieverFullName { get; set; } = "";
    public string RecieverCode { get; set; } = "";
    public string Describtion { get; set; } = "";
    public decimal Amount { get; set; }
    public int? InvoiceId { get; set; }
    public int? PersonId { get; set; }
    public int Status { get; set; }
    public int Direction { get; set; } = 1;
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(InvoiceId))] public virtual Invoice? Invoice { get; set; }
    [ForeignKey(nameof(PersonId))] public virtual Person? Person { get; set; }
}

/// <summary>
/// Ledger of money movements on a bank account. BankAccounts.CurrentBalance is always the
/// sum of StartBalance plus the signed effect of every non-deleted ledger row, so the live
/// balance can be recalculated from history at any time. Deposit (1) adds, withdrawal (2) subtracts.
/// </summary>
public class BankTransaction
{
    public const int KindDeposit = 1;    // واریز
    public const int KindWithdrawal = 2; // برداشت

    [Key] public int Id { get; set; }
    public int BankAccountId { get; set; }
    /// <summary>1 = deposit (واریز), 2 = withdrawal (برداشت).</summary>
    public int Kind { get; set; }
    /// <summary>Always a positive amount; Kind determines the sign.</summary>
    public decimal Amount { get; set; }
    /// <summary>Persian date string; DateG is the Gregorian value.</summary>
    public string Date { get; set; } = "";
    public DateTime DateG { get; set; }
    /// <summary>Origin: 1 = manual entry, 2 = cheque event.</summary>
    public int Source { get; set; } = SourceManual;
    public const int SourceManual = 1;
    public const int SourceCheque = 2;
    /// <summary>When Source = cheque: the Cheque this row was generated from.</summary>
    public int? ChequeId { get; set; }
    /// <summary>What happened: cashed, bounced, spent (see ChequeLedger.EventNames).</summary>
    public string? EventKind { get; set; }
    public string Describtion { get; set; } = "";
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(BankAccountId))] public virtual BankAccount? BankAccount { get; set; }
    [ForeignKey(nameof(ChequeId))] public virtual Cheque? Cheque { get; set; }
}
