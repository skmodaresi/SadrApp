using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

/// <summary>
/// Money transactions: receipts, payments, and transfers between bank accounts and cash
/// boxes. Every save posts through CashLedger.PostAsync so both balances stay consistent.
/// The same dialog is opened from the invoice editor (linked via
/// InvoiceMoneyTransactions, several per invoice allowed) and from task reports.
/// </summary>
public partial class AccountTransactionsPage : UserControl
{
    private bool _filtersLoaded;

    public AccountTransactionsPage()
    {
        InitializeComponent();
        ListCtl.NewClicked += (_, _) => ShowEditor(null, preselect: null);
        ListCtl.EditClicked += (_, _) => Edit();
        ListCtl.DeleteClicked += (_, _) => Delete();
        ListCtl.RefreshClicked += (_, _) => Load();
        ListCtl.RowDoubleClicked += (_, _) => Edit();
        TypeFilter.SelectionChanged += (_, _) => Load();
        SystemFilter.SelectionChanged += (_, _) => Load();
        Loaded += (_, _) =>
        {
            if (_filtersLoaded) return;
            _filtersLoaded = true;
            TypeFilter.ItemsSource = new[]
            {
                new { Key = 0, Text = "— همه —" },
                new { Key = AccountTransactionTypeConsts.Receipt, Text = "دریافت" },
                new { Key = AccountTransactionTypeConsts.Payment, Text = "پرداخت" },
                new { Key = AccountTransactionTypeConsts.Transfer, Text = "انتقال" }
            };
            TypeFilter.DisplayMemberPath = "Text";
            TypeFilter.SelectedValuePath = "Key";
            TypeFilter.SelectedValue = 0;

            SystemFilter.ItemsSource = new[]
            {
                new { Key = 0, Text = "— همه —" },
                new { Key = TransactionSystemConsts.Cash, Text = "صندوق" },
                new { Key = TransactionSystemConsts.Bank, Text = "بانک" }
            };
            SystemFilter.DisplayMemberPath = "Text";
            SystemFilter.SelectedValuePath = "Key";
            SystemFilter.SelectedValue = 0;
            Load();
        };
    }

    private async void Load()
    {
        if (!_filtersLoaded) return;
        try
        {
            await using var db = SadrDb.New();
            var type = TypeFilter.SelectedValue as int? ?? 0;
            var sys = SystemFilter.SelectedValue as int? ?? 0;

            var bankNames = await db.BankAccounts.Where(a => !a.Deleted)
                .Select(a => new { a.Id, a.Name }).ToDictionaryAsync(a => a.Id, a => a.Name);
            var cashNames = await db.Cashes.Where(c => !c.Deleted)
                .Select(c => new { c.Id, c.Name, c.Code })
                .ToDictionaryAsync(c => c.Id,
                    c => string.IsNullOrWhiteSpace(c.Code) ? c.Name : $"{c.Name} ({c.Code})");
            var invoiceNumbers = await db.Invoices
                .Select(i => new { i.Id, i.InvoiceNumber }).ToDictionaryAsync(i => i.Id, i => i.InvoiceNumber);
            var personNames = await db.People.Where(p => !p.Deleted)
                .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName }).ToDictionaryAsync(p => p.Id, p => p.Name);

            var query = db.AccountTransactions.Where(t => !t.Deleted);
            if (type > 0) query = query.Where(t => t.Type == type);
            if (sys > 0) query = query.Where(t => t.TransActionSystem == sys);

            var rows = await query.OrderByDescending(t => t.DateG).ThenByDescending(t => t.Id)
                .Select(t => new
                {
                    t.Id, t.BankAccountId, t.CashId, t.DateG, t.Value, t.Description,
                    t.InvoiceId, t.PersonId, t.Type, t.TransActionSystem
                })
                .ToListAsync();

            ListCtl.SetRows(rows.Select(t => new RowBase
            {
                Id = t.Id,
                Title = (t.Type == AccountTransactionTypeConsts.Receipt ? "دریافت " :
                         t.Type == AccountTransactionTypeConsts.Payment ? "پرداخت " : "انتقال ")
                        + t.Value.ToString("N0"),
                Code = t.DateG.ToString("yyyy/MM/dd"),
                Extra = string.Join(" | ",
                    AccountTransactionTypeConsts.Names.GetValueOrDefault(t.Type, ""),
                    TransactionSystemConsts.Names.GetValueOrDefault(t.TransActionSystem, ""),
                    t.TransActionSystem == TransactionSystemConsts.Cash
                        ? cashNames.TryGetValue(t.CashId ?? 0, out var cn) ? cn : "صندوق #" + (t.CashId ?? 0)
                        : bankNames.TryGetValue(t.BankAccountId ?? 0, out var bn) ? bn : "حساب #" + (t.BankAccountId ?? 0),
                    t.InvoiceId is int iid && invoiceNumbers.TryGetValue(iid, out var num) ? "فاکتور " + num : "",
                    t.PersonId is int pid && personNames.TryGetValue(pid, out var pn) ? pn : "").Trim(" |".ToCharArray()),
                Description = t.Description
            }).ToList());
            ListCtl.ShowExtraColumn("نوع | موضوع | حساب/صندوق | فاکتور | طرف حساب");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Editor for a money transaction. When <paramref name="preselect"/> is given (invoice
    /// editor flow), the dialog opens with that account chosen and returns without saving —
    /// the caller posts and links it.
    /// </summary>
    public static async Task<AccountTransaction?> ShowEditorDialog(
        Window owner, SadrDbContext db, int? invoiceId, int? preselectCashId, int? preselectBankAccountId)
    {
        var banks = await db.BankAccounts.Where(a => !a.Deleted)
            .Select(a => new KeyValuePair<int, string>(a.Id, a.Name)).ToListAsync();
        var cashRows = await db.Cashes.Where(c => !c.Deleted)
            .Select(c => new { c.Id, c.Name, c.Code }).ToListAsync();
        var cashes = cashRows.Select(c => new KeyValuePair<int, string>(c.Id,
            string.IsNullOrWhiteSpace(c.Code) ? c.Name : $"{c.Name} ({c.Code})")).ToList();
        if (banks.Count == 0 && cashes.Count == 0)
        {
            MessageBox.Show("ابتدا یک حساب بانکی یا صندوق تعریف کنید.", "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        var people = await db.People.Where(p => !p.Deleted)
            .Select(p => new KeyValuePair<int, string>(p.Id, p.FirstName + " " + p.LastName)).ToListAsync();
        people.Insert(0, new KeyValuePair<int, string>(0, "— انتخاب نشده —"));

        var typeChoices = AccountTransactionTypeConsts.Names
            .Select(kv => new KeyValuePair<int, string>(kv.Key, kv.Value)).ToList();

        var initialSystem = preselectCashId is not null ? TransactionSystemConsts.Cash
            : preselectBankAccountId is not null ? TransactionSystemConsts.Bank
            : TransactionSystemConsts.Cash;

        var dlg = new FieldEditorWindow("تراکنش مالی جدید", new[]
        {
            FieldSpec.Choice_("نوع تراکنش", typeChoices, AccountTransactionTypeConsts.Receipt),
            FieldSpec.Choice_("موضوع تراکنش",
                new List<KeyValuePair<int, string>>
                {
                    new(TransactionSystemConsts.Cash, "صندوق"),
                    new(TransactionSystemConsts.Bank, "حساب بانکی")
                }, initialSystem),
            FieldSpec.Choice_("صندوق", cashes, preselectCashId ?? cashes.FirstOrDefault().Key),
            FieldSpec.Choice_("حساب بانکی", banks, preselectBankAccountId ?? banks.FirstOrDefault().Key),
            FieldSpec.Numeric_("مبلغ (ريال)", null, true),
            FieldSpec.Date_("تاریخ", DateTime.Today),
            FieldSpec.Choice_("شخص مرتبط", people, 0),
            FieldSpec.Multi_("توضیحات", invoiceId is int iid ? "پرداخت فاکتور #" + iid : "")
        }) { Owner = owner };

        if (dlg.ShowDialog() != true) return null;

        var type = dlg.GetChoice(0) ?? AccountTransactionTypeConsts.Receipt;
        var system = dlg.GetChoice(1) ?? TransactionSystemConsts.Cash;
        var cashId = dlg.GetChoice(2) ?? 0;
        var bankId = dlg.GetChoice(3) ?? 0;

        var t = new AccountTransaction
        {
            Type = type,
            TransActionSystem = system,
            // The unused side stays null: a transaction lives in either a cash box or a
            // bank account (columns are optional; the wrong FK is dropped at startup).
            CashId = system == TransactionSystemConsts.Cash && cashId > 0 ? cashId : null,
            BankAccountId = system == TransactionSystemConsts.Bank && bankId > 0 ? bankId : null,
            Value = dlg.GetNumber(4) ?? 0,
            DateG = dlg.GetDate(5) ?? DateTime.Today,
            PersonId = dlg.GetChoice(6) is > 0 ? dlg.GetChoice(6) : null,
            Description = dlg.GetText(7) ?? "",
            InvoiceId = invoiceId
        };
        t.Date = PersianDate.ToPersian(t.DateG);
        return t;
    }

    private async void ShowEditor(int? id, object? preselect)
    {
        try
        {
            await using var db = SadrDb.New();
            var t = await ShowEditorDialog(Window.GetWindow(this)!, db, null, null, null);
            if (t is null) return;
            await CashLedger.PostAsync(db, t);
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Edit()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک تراکنش را انتخاب کنید."); return; }
        try
        {
            await using var db = SadrDb.New();
            var t = await db.AccountTransactions.FirstAsync(x => x.Id == row.Id);
            var oldBankId = t.BankAccountId;
            var oldCashId = t.CashId;

            var banks = await db.BankAccounts.Where(a => !a.Deleted)
                .Select(a => new KeyValuePair<int, string>(a.Id, a.Name)).ToListAsync();
            var cashRows = await db.Cashes.Where(c => !c.Deleted)
                .Select(c => new { c.Id, c.Name, c.Code }).ToListAsync();
            var cashes = cashRows.Select(c => new KeyValuePair<int, string>(c.Id,
                string.IsNullOrWhiteSpace(c.Code) ? c.Name : $"{c.Name} ({c.Code})")).ToList();
            var people = await db.People.Where(p => !p.Deleted)
                .Select(p => new KeyValuePair<int, string>(p.Id, p.FirstName + " " + p.LastName)).ToListAsync();
            people.Insert(0, new KeyValuePair<int, string>(0, "— انتخاب نشده —"));
            var typeChoices = AccountTransactionTypeConsts.Names
                .Select(kv => new KeyValuePair<int, string>(kv.Key, kv.Value)).ToList();

            var dlg = new FieldEditorWindow("ویرایش تراکنش مالی", new[]
            {
                FieldSpec.Choice_("نوع تراکنش", typeChoices, t.Type),
                FieldSpec.Choice_("موضوع تراکنش",
                    new List<KeyValuePair<int, string>>
                    {
                        new(TransactionSystemConsts.Cash, "صندوق"),
                        new(TransactionSystemConsts.Bank, "حساب بانکی")
                    }, t.TransActionSystem),
                FieldSpec.Choice_("صندوق", cashes, t.CashId),
                FieldSpec.Choice_("حساب بانکی", banks, t.BankAccountId),
                FieldSpec.Numeric_("مبلغ (ريال)", t.Value, true),
                FieldSpec.Date_("تاریخ", t.DateG),
                FieldSpec.Choice_("شخص مرتبط", people, t.PersonId ?? 0),
                FieldSpec.Multi_("توضیحات", t.Description)
            }) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            t.Type = dlg.GetChoice(0) ?? t.Type;
            t.TransActionSystem = dlg.GetChoice(1) ?? t.TransActionSystem;
            // keep only the side matching the transaction system; clear the other
            var chosenCash = dlg.GetChoice(2) ?? 0;
            var chosenBank = dlg.GetChoice(3) ?? 0;
            t.CashId = t.TransActionSystem == TransactionSystemConsts.Cash && chosenCash > 0 ? chosenCash : null;
            t.BankAccountId = t.TransActionSystem == TransactionSystemConsts.Bank && chosenBank > 0 ? chosenBank : null;
            t.Value = dlg.GetNumber(4) ?? t.Value;
            t.DateG = dlg.GetDate(5) ?? t.DateG;
            t.Date = PersianDate.ToPersian(t.DateG);
            t.PersonId = dlg.GetChoice(6) is > 0 ? dlg.GetChoice(6) : null;
            t.Description = dlg.GetText(7) ?? "";
            await db.SaveChangesAsync();

            // both the old and the new account/cash pair may have changed
            await CashLedger.RecalculateAsync(db, oldBankId, oldCashId);
            await CashLedger.RecalculateAsync(db, t.BankAccountId, t.CashId);
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Delete()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک تراکنش را انتخاب کنید."); return; }
        if (MessageBox.Show("این تراکنش حذف شود؟ اثر آن روی موجودی صندوق/حساب برمی‌گردد.",
                "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var t = await db.AccountTransactions.FirstAsync(x => x.Id == row.Id);
            await CashLedger.RemoveAsync(db, t);
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void Info(string m) => MessageBox.Show(m, "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
}
