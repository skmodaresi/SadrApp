using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SadrApp.Infrastructure;

namespace SadrApp.Data;

/// <summary>AccountTransactions.Type values (direction of the money movement).</summary>
public static class AccountTransactionTypeConsts
{
    public const int Receipt = 1;  // دریافت (money in at the row's target)
    public const int Payment = 2;  // پرداخت (money out of the row's target)
    public const int Transfer = 3; // انتقال (money in at the row's target; post the outgoing side as a Payment)

    public static readonly Dictionary<int, string> Names = new()
    {
        { Receipt, "دریافت" }, { Payment, "پرداخت" }, { Transfer, "انتقال" }
    };
}

/// <summary>AccountTransactions.TransActionSystem values (where the money lives).</summary>
public static class TransactionSystemConsts
{
    public const int Cash = 1;      // صندوق
    public const int Bank = 2;      // حساب بانکی

    public static readonly Dictionary<int, string> Names = new()
    {
        { Cash, "صندوق" }, { Bank, "بانک" }
    };
}

/// <summary>A cash box (صندوق): like a bank account but for physical cash.</summary>
[Table("Cashes")]
public class Cash
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Optional box code (کد صندوق); unique among live cash boxes when set.</summary>
    public string? Code { get; set; }
    public string? Description { get; set; }
    public decimal StartBalance { get; set; }
    /// <summary>Live balance: StartBalance + received − paid (kept consistent like the bank ledger).</summary>
    public decimal CurrentBalance { get; set; }
    public int Status { get; set; }
    public int? OwnerPersonId { get; set; }
    public int? OwnerCompanyId { get; set; }
    /// <summary>The person responsible for this cash box (required by schema).</summary>
    public int ResponcePersonId { get; set; }
    /// <summary>Native SQL json column (like BankAccounts.SignPeople): a JSON array of names.</summary>
    public string? SignPeople { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(OwnerPersonId))] public virtual Person? OwnerPerson { get; set; }
    [ForeignKey(nameof(OwnerCompanyId))] public virtual Company? OwnerCompany { get; set; }
    [ForeignKey(nameof(ResponcePersonId))] public virtual Person? ResponcePerson { get; set; }
}

/// <summary>
/// A money movement between a bank account and/or a cash box. Exactly one side is the
/// active account (TransActionSystem = Cash → CashId is the subject, BankAccountId still
/// holds the linked account for transfers); Receipt adds to the subject's balance,
/// Payment subtracts, Transfer moves money between the two. Invoices and task reports
/// attach to these rows through InvoiceMoneyTransactions / TaskReportMoneyTransactions.
/// </summary>
[Table("AccountTransactions")]
public class AccountTransaction
{
    [Key] public int Id { get; set; }
    /// <summary>Set when the money lives in a bank account; null for cash-side rows.</summary>
    public int? BankAccountId { get; set; }
    /// <summary>Set when the money lives in a cash box; null for bank-side rows.</summary>
    public int? CashId { get; set; }
    /// <summary>Persian yyyy/MM/dd display text; DateG is the value.</summary>
    public string Date { get; set; } = "";
    public DateTime DateG { get; set; }
    /// <summary>Always positive; Type determines the direction.</summary>
    public decimal Value { get; set; }
    public string Description { get; set; } = "";
    public int? InvoiceId { get; set; }
    public int? PersonId { get; set; }
    /// <summary>Company (sic: schema column name).</summary>
    public int? CompnayId { get; set; }
    /// <summary>1 = receipt, 2 = payment, 3 = transfer (AccountTransactionTypeConsts).</summary>
    public int Type { get; set; }
    /// <summary>1 = cash box, 2 = bank account (TransactionSystemConsts).</summary>
    public int TransActionSystem { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(BankAccountId))] public virtual BankAccount? BankAccount { get; set; }
    [ForeignKey(nameof(CashId))] public virtual Cash? Cash { get; set; }
    [ForeignKey(nameof(InvoiceId))] public virtual Invoice? Invoice { get; set; }
    [ForeignKey(nameof(PersonId))] public virtual Person? Person { get; set; }
}

/// <summary>Link: a task report's cost movement references the AccountTransaction that moved the money.</summary>
[Table("TaskReportMoneyTransactions")]
public class TaskReportMoneyTransaction
{
    [Key] public int Id { get; set; }
    public int TaskReportId { get; set; }
    public int AccountTransactionId { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(TaskReportId))] public virtual TaskReport? TaskReport { get; set; }
    [ForeignKey(nameof(AccountTransactionId))] public virtual AccountTransaction? AccountTransaction { get; set; }
}

/// <summary>Link: an invoice can have several money transactions (partial payments, installments).</summary>
[Table("InvoiceMoneyTransactions")]
public class InvoiceMoneyTransaction
{
    [Key] public int Id { get; set; }
    public int InvoiceId { get; set; }
    public int AccountTransactionId { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(InvoiceId))] public virtual Invoice? Invoice { get; set; }
    [ForeignKey(nameof(AccountTransactionId))] public virtual AccountTransaction? AccountTransaction { get; set; }
}

/// <summary>
/// Single source of truth for cash-box balances, mirroring ChequeLedger's approach:
/// CurrentBalance = StartBalance + receipts − payments, recalculated from
/// AccountTransactions history after every posting.
/// </summary>
public static class CashLedger
{
    /// <summary>Posts the transaction (insert or update), then recalculates both affected balances.</summary>
    public static async Task PostAsync(SadrDbContext db, AccountTransaction t)
    {
        if (t.Id == 0) db.AccountTransactions.Add(t);
        await db.SaveChangesAsync();
        await RecalculateAsync(db, t.BankAccountId, t.CashId);
    }

    /// <summary>Soft-deletes the row, undoes its balance effect, and clears invoice/report links.</summary>
    public static async Task RemoveAsync(SadrDbContext db, AccountTransaction t)
    {
        t.Deleted = true;
        await db.SaveChangesAsync();

        var invLinks = await db.InvoiceMoneyTransactions
            .Where(l => !l.Deleted && l.AccountTransactionId == t.Id).ToListAsync();
        foreach (var l in invLinks) l.Deleted = true;
        var repLinks = await db.TaskReportMoneyTransactions
            .Where(l => !l.Deleted && l.AccountTransactionId == t.Id).ToListAsync();
        foreach (var l in repLinks) l.Deleted = true;
        await db.SaveChangesAsync();

        await RecalculateAsync(db, t.BankAccountId, t.CashId);
    }

    /// <summary>
    /// Recomputes balances from history for the given bank account and/or cash box
    /// (start + receipts/transfers in − payments out). Pass null/0 to skip a side.
    /// </summary>
    public static async Task RecalculateAsync(SadrDbContext db, int? bankAccountId, int? cashId)
    {
        if (bankAccountId is > 0)
        {
            var bankStart = await db.BankAccounts.Where(a => a.Id == bankAccountId)
                .Select(a => (decimal?)a.StartBalance).SingleOrDefaultAsync() ?? 0m;
            var bankIn = await db.AccountTransactions.Where(t =>
                    !t.Deleted && t.BankAccountId == bankAccountId &&
                    (t.Type == AccountTransactionTypeConsts.Receipt || t.Type == AccountTransactionTypeConsts.Transfer))
                .SumAsync(t => (decimal?)t.Value) ?? 0m;
            var bankOut = await db.AccountTransactions.Where(t =>
                    !t.Deleted && t.BankAccountId == bankAccountId && t.Type == AccountTransactionTypeConsts.Payment)
                .SumAsync(t => (decimal?)t.Value) ?? 0m;
            await db.BankAccounts.Where(a => a.Id == bankAccountId).ExecuteUpdateAsync(
                s => s.SetProperty(a => a.CurrentBalance, bankStart + bankIn - bankOut));
        }

        if (cashId is > 0)
        {
            var cashStart = await db.Cashes.Where(c => c.Id == cashId)
                .Select(c => (decimal?)c.StartBalance).SingleOrDefaultAsync() ?? 0m;
            var cashIn = await db.AccountTransactions.Where(t =>
                    !t.Deleted && t.CashId == cashId &&
                    (t.Type == AccountTransactionTypeConsts.Receipt || t.Type == AccountTransactionTypeConsts.Transfer))
                .SumAsync(t => (decimal?)t.Value) ?? 0m;
            var cashOut = await db.AccountTransactions.Where(t =>
                    !t.Deleted && t.CashId == cashId && t.Type == AccountTransactionTypeConsts.Payment)
                .SumAsync(t => (decimal?)t.Value) ?? 0m;
            await db.Cashes.Where(c => c.Id == cashId).ExecuteUpdateAsync(
                s => s.SetProperty(c => c.CurrentBalance, cashStart + cashIn - cashOut));
        }
    }
}
