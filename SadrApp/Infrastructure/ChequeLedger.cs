using Microsoft.EntityFrameworkCore;
using SadrApp.Data;

namespace SadrApp.Infrastructure;

/// <summary>
/// Single source of truth for bank balances and cheque-ledger posting.
/// Every balance query goes through RecalculateBalance, so BankAccounts.CurrentBalance is
/// always StartBalance plus the signed effect of every non-deleted ledger row. Posting rules:
/// وصول شد → deposit on a received cheque / withdrawal on an issued cheque;
/// خرج شد → withdrawal (value leaves our account);
/// برگشت خورده → any of the above is undone (event row + its effect removed).
/// </summary>
public static class ChequeLedger
{
    /// <summary>Event names stored in BankTransactions.EventKind.</summary>
    public const string EventCashed = "وصول شد";
    public const string EventBounced = "برگشت خورده";
    public const string EventSpent = "خرج شد";

    public static readonly Dictionary<string, string> EventNames = new()
    {
        { EventCashed, "وصول شد" }, { EventBounced, "برگشت خورده" }, { EventSpent, "خرج شد" }
    };

    /// <summary>True when the cheque's current status means money has hit our account.</summary>
    public static bool IsPosted(Cheque c) =>
        c.Status == ChequeConsts.StatusCashed || c.Status == ChequeConsts.StatusSpent;

    /// <summary>Balance effect sign for a posted cheque: +1 received, -1 issued.</summary>
    public static int Sign(Cheque c) =>
        c.Direction == ChequeConsts.DirectionReceived ? 1 : -1;

    /// <summary>
    /// Brings the ledger in line with the cheque's new status, then recalculates the
    /// account balance. Safe to call repeatedly (idempotent per event).
    /// </summary>
    public static async Task SyncChequeAsync(SadrDbContext db, Cheque c, string eventKind, DateTime when)
    {
        // Remove rows for events that no longer apply (e.g. cashed then bounced).
        var stale = await db.BankTransactions
            .Where(t => t.ChequeId == c.Id && !t.Deleted && t.EventKind != null && t.EventKind != eventKind)
            .ToListAsync();
        foreach (var t in stale)
        {
            t.Deleted = true;
            t.UpdateDateTime = when;
        }

        if (eventKind != EventBounced)
        {
            var exists = await db.BankTransactions.AnyAsync(t =>
                t.ChequeId == c.Id && !t.Deleted && t.EventKind == eventKind);
            if (!exists)
            {
                db.BankTransactions.Add(new BankTransaction
                {
                    BankAccountId = c.BankAccountId,
                    Kind = Sign(c) > 0 ? BankTransaction.KindDeposit : BankTransaction.KindWithdrawal,
                    Amount = c.Amount,
                    Date = c.Date,
                    DateG = when,
                    Source = BankTransaction.SourceCheque,
                    ChequeId = c.Id,
                    EventKind = eventKind,
                    Describtion = $"چک شماره {c.Number} ({eventKind})",
                    RecordUniqueId = Guid.NewGuid(),
                    CreateDateTime = when
                });
            }
        }

        await db.SaveChangesAsync();
        await RecalculateBalanceAsync(db, c.BankAccountId, when);
    }

    /// <summary>Adds or updates a manual ledger row, then recalculates the account balance.</summary>
    public static async Task PostManualAsync(SadrDbContext db, BankTransaction t, DateTime when)
    {
        t.Source = BankTransaction.SourceManual;
        t.Date = PersianDate.ToPersian(t.DateG);
        if (t.Id == 0)
        {
            t.RecordUniqueId = Guid.NewGuid();
            t.CreateDateTime = when;
            db.BankTransactions.Add(t);
        }
        else
        {
            t.UpdateDateTime = when;
        }
        await db.SaveChangesAsync();
        await RecalculateBalanceAsync(db, t.BankAccountId, when);
    }

    /// <summary>
    /// Recomputes the account's current balance from StartBalance + ledger history and
    /// persists it.
    /// </summary>
    public static async Task RecalculateBalanceAsync(SadrDbContext db, int bankAccountId, DateTime when)
    {
        var start = await db.BankAccounts.Where(a => a.Id == bankAccountId)
            .Select(a => (decimal?)a.StartBalance).SingleOrDefaultAsync() ?? 0m;
        var deposits = await db.BankTransactions
            .Where(t => t.BankAccountId == bankAccountId && !t.Deleted && t.Kind == BankTransaction.KindDeposit)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var withdrawals = await db.BankTransactions
            .Where(t => t.BankAccountId == bankAccountId && t.Kind == BankTransaction.KindWithdrawal && !t.Deleted)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        await db.BankAccounts.Where(a => a.Id == bankAccountId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.CurrentBalance, start + deposits - withdrawals)
                .SetProperty(a => a.UpdateDateTime, when));
    }

    /// <summary>Current balance of an account, computed from ledger history.</summary>
    public static async Task<decimal> BalanceOfAsync(SadrDbContext db, int bankAccountId)
    {
        var start = await db.BankAccounts.Where(a => a.Id == bankAccountId)
            .Select(a => (decimal?)a.StartBalance).SingleOrDefaultAsync() ?? 0m;
        var deposits = await db.BankTransactions
            .Where(t => t.BankAccountId == bankAccountId && !t.Deleted && t.Kind == BankTransaction.KindDeposit)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var withdrawals = await db.BankTransactions
            .Where(t => t.BankAccountId == bankAccountId && !t.Deleted && t.Kind == BankTransaction.KindWithdrawal)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;
        return start + deposits - withdrawals;
    }
}
