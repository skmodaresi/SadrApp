using System.Windows;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.ViewModels;

namespace SadrApp.Views.Controls;

public partial class AccountsDialog : Window
{
    private readonly List<KeyValuePair<int, string>> _branches;
    private readonly List<KeyValuePair<int, string>> _currencies;
    private readonly List<KeyValuePair<int, string>> _companies;
    private readonly List<KeyValuePair<int, string>> _people;

    public AccountsDialog(
        List<KeyValuePair<int, string>> branches,
        List<KeyValuePair<int, string>> currencies,
        List<KeyValuePair<int, string>> companies,
        List<KeyValuePair<int, string>> people)
    {
        InitializeComponent();
        _branches = branches;
        _currencies = currencies;
        _companies = companies;
        _people = people;
        BtnNew.Click += (_, _) => NewOrEdit(null);
        BtnEdit.Click += (_, _) => { if (Grid.SelectedItem is RowBase r) NewOrEdit(r.Id); };
        BtnDelete.Click += (_, _) => DeleteRow();
        Grid.MouseDoubleClick += (_, _) => { if (Grid.SelectedItem is RowBase r) NewOrEdit(r.Id); };
        ReloadGrid();
    }

    private class AccountRow : RowBase
    {
        public string Owner { get; set; } = "-";
        public string Currency { get; set; } = "-";
        public string Balance { get; set; } = "";
    }

    private async void ReloadGrid()
    {
        try
        {
            await using var db = SadrDb.New();
            var rows = await db.BankAccounts.Where(a => !a.Deleted)
                .Select(a => new AccountRow
                {
                    Id = a.Id,
                    Title = a.Name,
                    Code = a.AccountNumber,
                    Owner = a.PersonId != null ? a.Person.FirstName + " " + a.Person.LastName
                          : a.CompanyId != null ? a.Company.FullName : "-",
                    Currency = a.Currency.Name,
                    Balance = a.CurrentBalance.ToString("N0")
                }).ToListAsync();
            Grid.ItemsSource = rows;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void NewOrEdit(int? id)
    {
        try
        {
            await using var db = SadrDb.New();
            var e = id is null ? null : await db.BankAccounts.FirstAsync(x => x.Id == id);
            var ownerType = new List<KeyValuePair<int, string>> { new(0, "شخص"), new(1, "شرکت") };

            var dlg = new FieldEditorWindow(id is null ? "حساب بانکی جدید" : "ویرایش حساب بانکی", new[]
            {
                FieldSpec.Text_("نام/عنوان حساب", e?.Name, true),
                FieldSpec.Text_("شماره حساب", e?.AccountNumber, true),
                FieldSpec.Text_("شماره کارت", e?.CardNumber),
                FieldSpec.Text_("شماره شبا", e?.IdNumber),
                FieldSpec.Choice_("شعبه بانک", _branches, e?.BankBranchId ?? _branches.FirstOrDefault().Key),
                FieldSpec.Choice_("واحد پول", _currencies, e?.CurrencyId ?? _currencies.FirstOrDefault().Key),
                FieldSpec.Choice_("نوع مالکیت", ownerType, e?.CompanyId != null ? 1 : 0),
                FieldSpec.Choice_("شخص مالک", _people, e?.PersonId, optional: true),
                FieldSpec.Choice_("شرکت مالک", _companies, e?.CompanyId, optional: true),
                FieldSpec.Numeric_("موجودی اولیه", e?.StartBalance),
                FieldSpec.Multi_("توضیحات", e?.Description)
            }) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var now = DateTime.Now;
            if (e is null)
            {
                e = new BankAccount { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now };
                db.BankAccounts.Add(e);
            }
            e.Name = dlg.GetText(0)!.Trim();
            e.AccountNumber = dlg.GetText(1)!.Trim();
            e.CardNumber = dlg.GetText(2);
            e.IdNumber = dlg.GetText(3) ?? "";
            e.BankBranchId = dlg.GetChoice(4) ?? 0;
            e.CurrencyId = dlg.GetChoice(5) ?? 0;
            var ownerIsCompany = (dlg.GetChoice(6) ?? 0) == 1;
            e.PersonId = ownerIsCompany ? null : dlg.GetChoice(7);
            e.CompanyId = ownerIsCompany ? dlg.GetChoice(8) : null;
            e.StartBalance = dlg.GetNumber(9) ?? 0;
            e.CurrentBalance = e.StartBalance;
            e.Description = dlg.GetText(10);
            e.UpdateDateTime = now;
            await db.SaveChangesAsync();
            ReloadGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteRow()
    {
        if (Grid.SelectedItem is not RowBase row) return;
        if (MessageBox.Show($"حساب «{row.Title}» حذف شود؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var e = await db.BankAccounts.FirstAsync(x => x.Id == row.Id);
            e.Deleted = true;
            e.UpdateDateTime = DateTime.Now;
            await db.SaveChangesAsync();
            ReloadGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
