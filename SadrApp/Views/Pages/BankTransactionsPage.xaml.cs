using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

/// <summary>
/// Ledger page for BankTransactions: every money movement on a bank account, whether a
/// manual deposit/withdrawal or an event posted by the cheque lifecycle. Shows the live
/// balance (StartBalance + deposits − withdrawals) for the selected account.
/// </summary>
public partial class BankTransactionsPage : UserControl
{
    public BankTransactionsPage()
    {
        InitializeComponent();
        ListCtl.NewClicked += (_, _) => ShowEditor(null);
        ListCtl.EditClicked += (_, _) => Edit();
        ListCtl.DeleteClicked += (_, _) => Delete();
        ListCtl.RefreshClicked += (_, _) => Load();
        ListCtl.RowDoubleClicked += (_, _) => Edit();
        AccountFilter.SelectionChanged += (_, _) => Load();
        BtnRecalc.Click += async (_, _) =>
        {
            try
            {
                if (AccountFilter.SelectedValue is not int accId || accId <= 0) return;
                await using var db = SadrDb.New();
                await ChequeLedger.RecalculateBalanceAsync(db, accId, DateTime.Now);
                Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        Loaded += (_, _) => { FillAccounts(); Load(); };
    }

    private async void FillAccounts()
    {
        try
        {
            await using var db = SadrDb.New();
            var accounts = await db.BankAccounts.Where(a => !a.Deleted)
                .Select(a => new ComboItem { Key = a.Id, Text = a.Name }).ToListAsync();
            AccountFilter.ItemsSource = accounts;
            AccountFilter.DisplayMemberPath = "Text";
            AccountFilter.SelectedValuePath = "Key";
            if (AccountFilter.SelectedValue is null && accounts.Count > 0)
                AccountFilter.SelectedValue = accounts[0].Key;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Load()
    {
        try
        {
            await using var db = SadrDb.New();
            if (AccountFilter.SelectedValue is not int accId || accId <= 0)
            {
                ListCtl.SetRows(Array.Empty<RowBase>());
                TxtBalance.Text = "—";
                return;
            }

            var rows = await db.BankTransactions.Where(t => !t.Deleted && t.BankAccountId == accId)
                .OrderByDescending(t => t.DateG).ThenByDescending(t => t.Id)
                .Select(t => new
                {
                    t.Id, t.Kind, t.Amount, t.Date, t.Source, t.EventKind, t.ChequeId,
                    ChequeNo = t.ChequeId != null && t.Cheque != null ? t.Cheque.Number : "",
                    t.Describtion
                })
                .ToListAsync();

            ListCtl.SetRows(rows.Select(t => new RowBase
            {
                Id = t.Id,
                Title = (t.Kind == BankTransaction.KindDeposit ? "واریز " : "برداشت ") + t.Amount.ToString("N0"),
                Code = t.Date,
                Extra = string.Join(" | ",
                    t.Source == BankTransaction.SourceCheque ? "چک" : "دستی",
                    t.EventKind ?? "",
                    t.ChequeNo.Length > 0 ? "چک " + t.ChequeNo : ""),
                Description = t.Describtion
            }).ToList());
            ListCtl.ShowExtraColumn("منبع | رویداد | چک مرتبط");

            TxtBalance.Text = (await ChequeLedger.BalanceOfAsync(db, accId)).ToString("N0");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Edit()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک تراکنش را انتخاب کنید."); return; }
        ShowEditor(row.Id);
    }

    private async void ShowEditor(int? id)
    {
        try
        {
            await using var db = SadrDb.New();
            var accounts = await db.BankAccounts.Where(a => !a.Deleted)
                .Select(a => new { a.Id, a.Name }).OrderBy(a => a.Name).ToListAsync();
            if (accounts.Count == 0) { Info("ابتدا در صفحه «حساب‌های بانکی» یک حساب تعریف کنید."); return; }
            var accountChoices = accounts.Select(a => new KeyValuePair<int, string>(a.Id, a.Name)).ToList();

            var kindChoices = new List<KeyValuePair<int, string>>
            {
                new(BankTransaction.KindDeposit, "واریز"),
                new(BankTransaction.KindWithdrawal, "برداشت")
            };

            var e = id is null ? null : await db.BankTransactions.FirstAsync(x => x.Id == id);
            if (e is { Source: BankTransaction.SourceCheque })
            {
                Info("این ردیف به‌صورت خودکار از چک ایجاد شده و از این صفحه قابل ویرایش نیست. برای تغییر، وضعیت چک را تغییر دهید.");
                return;
            }

            var dlg = new FieldEditorWindow(id is null ? "تراکنش دستی جدید" : "ویرایش تراکنش دستی", new[]
            {
                FieldSpec.Choice_("حساب بانکی", accountChoices,
                    e?.BankAccountId ?? (AccountFilter.SelectedValue as int? ?? accountChoices[0].Key)),
                FieldSpec.Choice_("نوع تراکنش", kindChoices, e?.Kind ?? BankTransaction.KindDeposit),
                FieldSpec.Numeric_("مبلغ", e?.Amount, true),
                FieldSpec.Date_("تاریخ", e?.DateG, true),
                FieldSpec.Text_("شرح", string.IsNullOrEmpty(e?.Describtion) ? null : e.Describtion)
            }) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            var when = DateTime.Now;
            var tx = e ?? new BankTransaction();
            var oldAccount = e?.BankAccountId ?? 0;
            tx.BankAccountId = dlg.GetChoice(0) ?? accountChoices[0].Key;
            tx.Kind = dlg.GetChoice(1) ?? BankTransaction.KindDeposit;
            tx.Amount = dlg.GetNumber(2) ?? 0;
            tx.DateG = dlg.GetDate(3) ?? DateTime.Today;
            tx.Describtion = dlg.GetText(4) ?? "";
            await ChequeLedger.PostManualAsync(db, tx, when);
            // Moving a row between accounts must refresh both balances.
            if (oldAccount != 0 && oldAccount != tx.BankAccountId)
                await ChequeLedger.RecalculateBalanceAsync(db, oldAccount, when);
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Delete()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک تراکنش را انتخاب کنید."); return; }
        if (MessageBox.Show($"تراکنش «{row.Title}» حذف شود؟ (اثر آن از موجودی حساب نیز برداشته می‌شود)",
                "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var e = await db.BankTransactions.FirstAsync(x => x.Id == row.Id);
            e.Deleted = true;
            e.UpdateDateTime = DateTime.Now;
            await db.SaveChangesAsync();
            await ChequeLedger.RecalculateBalanceAsync(db, e.BankAccountId, DateTime.Now);
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void Info(string m) => MessageBox.Show(m, "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
}
