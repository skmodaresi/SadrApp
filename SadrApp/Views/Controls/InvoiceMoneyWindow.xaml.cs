using System.Windows;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.Views.Pages;

namespace SadrApp.Views.Controls;

/// <summary>
/// Money transactions attached to one invoice — several receipts/payments per invoice are
/// supported (partial payments, installments). Each entry creates an AccountTransaction
/// row plus an InvoiceMoneyTransactions link, and moves cash/bank balances.
/// </summary>
public partial class InvoiceMoneyWindow : Window
{
    private readonly int _invoiceId;
    private readonly string _invoiceNumber;

    public InvoiceMoneyWindow(int invoiceId, string invoiceNumber)
    {
        InitializeComponent();
        _invoiceId = invoiceId;
        _invoiceNumber = invoiceNumber;
        BtnNew.Click += async (_, _) => await AddAsync();
        BtnDelete.Click += async (_, _) => await DeleteAsync();
        BtnClose.Click += (_, _) => Close();
        Loaded += (_, _) => Load();
    }

    private async void Load()
    {
        try
        {
            await using var db = SadrDb.New();
            var bankNames = await db.BankAccounts.Where(a => !a.Deleted)
                .Select(a => new { a.Id, a.Name }).ToDictionaryAsync(a => a.Id, a => a.Name);
            var cashNames = await db.Cashes.Where(c => !c.Deleted)
                .Select(c => new { c.Id, c.Name }).ToDictionaryAsync(c => c.Id, c => c.Name);
            var personNames = await db.People.Where(p => !p.Deleted)
                .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName }).ToDictionaryAsync(p => p.Id, p => p.Name);

            var rows = await (from l in db.InvoiceMoneyTransactions
                              join t in db.AccountTransactions on l.AccountTransactionId equals t.Id
                              where !l.Deleted && !t.Deleted && l.InvoiceId == _invoiceId
                              orderby t.DateG descending, t.Id descending
                              select new
                              {
                                  t.Id, t.DateG, t.Value, t.Type, t.TransActionSystem,
                                  t.BankAccountId, t.CashId, t.PersonId, t.Description
                              }).ToListAsync();

            Grid.ItemsSource = rows.Select(r => new MoneyRow
            {
                Id = r.Id,
                DateText = r.DateG.ToString("yyyy/MM/dd"),
                Kind = AccountTransactionTypeConsts.Names.GetValueOrDefault(r.Type, ""),
                Amount = r.Value.ToString("N0"),
                Target = r.TransActionSystem == TransactionSystemConsts.Cash
                    ? cashNames.TryGetValue(r.CashId ?? 0, out var cn) ? cn : "صندوق #" + (r.CashId ?? 0)
                    : bankNames.TryGetValue(r.BankAccountId ?? 0, out var bn) ? bn : "حساب #" + (r.BankAccountId ?? 0),
                Person = r.PersonId is int pid && personNames.TryGetValue(pid, out var pn) ? pn : "",
                Description = r.Description
            }).ToList();

            var total = rows.Sum(r => r.Type == AccountTransactionTypeConsts.Receipt ? r.Value : -r.Value);
            TxtBalance.Text = "جمع خالص: " + total.ToString("N0") + " ريال";
            TxtTitle.Text = $"تراکنش‌های مالی فاکتور {_invoiceNumber}";

            // Invoice total vs paid (receipts/transfers minus payments) and remaining.
            var inv = await db.Invoices.Where(i => i.Id == _invoiceId)
                .Select(i => (decimal?)i.TotalPrice).SingleOrDefaultAsync() ?? 0m;
            var remaining = Math.Max(0, inv - total);
            TxtTotals.Text = $"جمع فاکتور: {inv:N0} ريال | پرداختی: {Math.Max(0, total):N0} ريال | باقیمانده: {remaining:N0} ريال";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task AddAsync()
    {
        try
        {
            await using var db = SadrDb.New();
            var t = await AccountTransactionsPage.ShowEditorDialog(this, db, _invoiceId, null, null);
            if (t is null) return;
            await db.AccountTransactions.AddAsync(t);
            await db.SaveChangesAsync();
            db.InvoiceMoneyTransactions.Add(new InvoiceMoneyTransaction
            {
                InvoiceId = _invoiceId,
                AccountTransactionId = t.Id,
                RecordUniqueId = Guid.NewGuid(),
                CreateDateTime = DateTime.Now
            });
            await db.SaveChangesAsync();
            await CashLedger.RecalculateAsync(db, t.BankAccountId, t.CashId);
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task DeleteAsync()
    {
        if (Grid.SelectedItem is not MoneyRow row) { Info("ابتدا یک تراکنش را انتخاب کنید."); return; }
        if (MessageBox.Show("این تراکنش از فاکتور حذف شود؟ اثر آن روی موجودی صندوق/حساب برمی‌گردد.",
                "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var link = await db.InvoiceMoneyTransactions
                .FirstAsync(l => !l.Deleted && l.InvoiceId == _invoiceId && l.AccountTransactionId == row.Id);
            link.Deleted = true;

            var t = await db.AccountTransactions.FirstAsync(x => x.Id == row.Id);
            t.Deleted = true;
            await db.SaveChangesAsync();
            await CashLedger.RecalculateAsync(db, t.BankAccountId, t.CashId);
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void Info(string m) => MessageBox.Show(m, "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
}

public class MoneyRow
{
    public int Id { get; set; }
    public string DateText { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Amount { get; set; } = "";
    public string Target { get; set; } = "";
    public string Person { get; set; } = "";
    public string Description { get; set; } = "";
}
