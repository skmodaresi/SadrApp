using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

public partial class BankPage : UserControl
{
    public BankPage()
    {
        InitializeComponent();
        ListCtl.NewClicked += (_, _) => ShowEditor(null);
        ListCtl.EditClicked += (_, _) => Edit();
        ListCtl.DeleteClicked += (_, _) => Delete();
        ListCtl.RefreshClicked += (_, _) => { LoadBanks(); Load(); };
        ListCtl.RowDoubleClicked += (_, _) => Edit();
        BankFilter.SelectionChanged += (_, _) => Load();
        BtnAccounts.Click += (_, _) => ManageAccounts();
        Loaded += (_, _) => { LoadBanks(); Load(); };
    }

    private async void LoadBanks()
    {
        try
        {
            await using var db = SadrDb.New();
            var banks = await db.Banks.Where(b => !b.Deleted)
                .Select(b => new ComboItem { Key = b.Id, Text = b.Name }).ToListAsync();
            banks.Insert(0, new ComboItem { Key = 0, Text = "— همه بانک‌ها —" });
            BankFilter.ItemsSource = banks;
            BankFilter.DisplayMemberPath = "Text";
            BankFilter.SelectedValuePath = "Key";
            if (BankFilter.SelectedValue is null) BankFilter.SelectedValue = 0;
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
            var bankId = BankFilter.SelectedValue as int?;
            var query = db.BankBranches.Where(b => !b.Deleted);
            if (bankId is > 0) query = query.Where(b => b.BankId == bankId);
            var rows = await query.Select(b => new RowBase
            {
                Id = b.Id,
                Title = b.Name,
                Code = b.Code,
                Description = "بانک: " + (b.Bank != null ? b.Bank.Name : "-") + " | تلفن: " + (b.Tel ?? "-")
            }).ToListAsync();
            ListCtl.SetRows(rows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Edit()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک شعبه را انتخاب کنید."); return; }
        ShowEditor(row.Id);
    }

    private async void ShowEditor(int? id)
    {
        try
        {
            await using var db = SadrDb.New();
            var banks = await db.Banks.Where(b => !b.Deleted)
                .Select(b => new KeyValuePair<int, string>(b.Id, b.Name)).ToListAsync();
            if (banks.Count == 0)
            {
                Info("ابتدا در منوی «بانک‌ها» یک بانک تعریف کنید.");
                return;
            }
            var people = await db.People.Where(p => !p.Deleted)
                .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName }).ToListAsync();
            var managerChoices = people.Select(p => new KeyValuePair<int, string>(p.Id, p.Name)).ToList();
            managerChoices.Insert(0, new KeyValuePair<int, string>(0, "— انتخاب نشده —"));

            var e = id is null ? null : await db.BankBranches.FirstAsync(x => x.Id == id);
            var dlg = new FieldEditorWindow(id is null ? "شعبه جدید" : "ویرایش شعبه", new[]
            {
                FieldSpec.Text_("نام شعبه", e?.Name, true),
                FieldSpec.Text_("کد شعبه", e?.Code),
                FieldSpec.Choice_("بانک", banks, e?.BankId ?? banks[0].Key),
                FieldSpec.Text_("تلفن", e?.Tel),
                FieldSpec.Multi_("آدرس", e?.Address),
                FieldSpec.Choice_("مدیر شعبه", managerChoices, e?.ManagerPersonId ?? 0)
            }) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            var now = DateTime.Now;
            if (e is null)
            {
                e = new BankBranch { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now };
                db.BankBranches.Add(e);
            }
            e.Name = dlg.GetText(0)!.Trim();
            e.Code = dlg.GetText(1) ?? "";
            e.BankId = dlg.GetChoice(2) ?? banks[0].Key;
            e.Tel = dlg.GetText(3);
            e.Address = dlg.GetText(4);
            e.ManagerPersonId = dlg.GetChoice(5) is > 0 ? dlg.GetChoice(5) : null;
            e.UpdateDateTime = now;
            await db.SaveChangesAsync();
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Delete()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک شعبه را انتخاب کنید."); return; }
        try
        {
            await using var db = SadrDb.New();
            var used = await db.BankAccounts.AnyAsync(a => !a.Deleted && a.BankBranchId == row.Id);
            if (used) { MessageBox.Show("ابتدا حساب‌های این شعبه را حذف کنید.", "حذف ممکن نیست", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (MessageBox.Show($"شعبه «{row.Title}» حذف شود؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            var e = await db.BankBranches.FirstAsync(x => x.Id == row.Id);
            e.Deleted = true;
            e.UpdateDateTime = DateTime.Now;
            await db.SaveChangesAsync();
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ManageAccounts()
    {
        try
        {
            await using var db = SadrDb.New();
            var branches = await db.BankBranches.Where(b => !b.Deleted)
                .Select(b => new { b.Id, Name = b.Bank!.Name + " - " + b.Name }).ToListAsync();
            if (branches.Count == 0)
            {
                Info("ابتدا یک شعبه بانک تعریف کنید.");
                return;
            }
            var branchChoices = branches.Select(b => new KeyValuePair<int, string>(b.Id, b.Name)).ToList();
            var curChoices = await db.Currencies.Where(c => !c.Deleted)
                .Select(c => new KeyValuePair<int, string>(c.Id, c.Name)).ToListAsync();
            var companies = await db.Companies.Where(c => !c.Deleted)
                .Select(c => new KeyValuePair<int, string>(c.Id, c.FullName)).ToListAsync();
            var people = await db.People.Where(p => !p.Deleted)
                .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName }).ToListAsync();

            var accounts = await db.BankAccounts.Where(a => !a.Deleted)
                .Select(a => new
                {
                    a.Id, a.Name, a.AccountNumber, a.BankBranchId,
                    Currency = a.Currency.Name,
                    Owner = a.PersonId != null ? a.Person.FirstName + " " + a.Person.LastName
                          : a.CompanyId != null ? a.Company.FullName : "-"
                }).ToListAsync();

            var listDlg = new AccountsDialog(
                branchChoices, curChoices, companies,
                people.Select(p => new KeyValuePair<int, string>(p.Id, p.Name)).ToList())
            { Owner = Window.GetWindow(this) };
            listDlg.ShowDialog();
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void Info(string m) => MessageBox.Show(m, "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
}
