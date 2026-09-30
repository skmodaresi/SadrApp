using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;

namespace SadrApp.Views.Pages;

/// <summary>One row of the balances dashboard (a cash box or a bank account).</summary>
public sealed class BalanceRow
{
    public string KindLabel { get; init; } = "";
    public string Name { get; init; } = "";
    public string Detail { get; init; } = "";
    public decimal StartBalance { get; init; }
    public decimal CurrentBalance { get; init; }
    public string StartText => StartBalance.ToString("N0");
    public string CurrentText => CurrentBalance.ToString("N0");
    public Brush RowColor => CurrentBalance < 0 ? Brushes.Firebrick : Brushes.Black;
}

/// <summary>
/// One-page overview of all cash boxes and bank accounts side by side, with
/// cash/bank/grand totals. Loaded fresh every time the tab becomes visible so
/// the numbers always reflect the latest ledger state.
/// </summary>
public partial class BalancesDashboardPage : UserControl
{
    public BalancesDashboardPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Load();
    }

    private async void Load()
    {
        try
        {
            await using var db = SadrDb.New();

            var cashes = await db.Cashes.Where(c => !c.Deleted)
                .Select(c => new { c.Name, c.StartBalance, c.CurrentBalance, Responsible = c.ResponcePersonId })
                .ToListAsync();
            var accounts = await db.BankAccounts.Where(a => !a.Deleted)
                .Select(a => new { a.Name, a.StartBalance, a.CurrentBalance, a.BankBranchId })
                .ToListAsync();

            var branchNames = await db.BankBranches
                .Select(b => new { b.Id, b.Name }).ToDictionaryAsync(b => b.Id, b => b.Name);
            var people = await db.People.Where(p => !p.Deleted)
                .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName })
                .ToDictionaryAsync(p => p.Id, p => p.Name);

            var rows = new List<BalanceRow>();
            rows.AddRange(cashes.Select(c => new BalanceRow
            {
                KindLabel = "💰 صندوق",
                Name = c.Name,
                Detail = people.TryGetValue(c.Responsible, out var p) ? "مسئول: " + p : "",
                StartBalance = c.StartBalance,
                CurrentBalance = c.CurrentBalance
            }));
            rows.AddRange(accounts.Select(a => new BalanceRow
            {
                KindLabel = "🏦 حساب بانکی",
                Name = a.Name,
                Detail = branchNames.TryGetValue(a.BankBranchId, out var b) ? "شعبه: " + b : "",
                StartBalance = a.StartBalance,
                CurrentBalance = a.CurrentBalance
            }));

            Grid.ItemsSource = rows.OrderByDescending(r => r.KindLabel).ThenByDescending(r => r.CurrentBalance);

            var cashTotal = cashes.Sum(c => c.CurrentBalance);
            var bankTotal = accounts.Sum(a => a.CurrentBalance);
            TxtCashTotal.Text = "جمع صندوق‌ها: " + cashTotal.ToString("N0");
            TxtBankTotal.Text = "جمع حساب‌های بانکی: " + bankTotal.ToString("N0");
            TxtGrandTotal.Text = "جمع کل: " + (cashTotal + bankTotal).ToString("N0") + " ريال";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری موجودی‌ها", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
