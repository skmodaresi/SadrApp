using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

public partial class CompanyListPage : UserControl
{
    public CompanyListPage()
    {
        InitializeComponent();
        ListCtl.NewClicked += (_, _) => ShowEditor(null);
        ListCtl.EditClicked += (_, _) => Edit();
        ListCtl.DeleteClicked += (_, _) => Delete();
        ListCtl.RefreshClicked += (_, _) => Load();
        ListCtl.RowDoubleClicked += (_, _) => Edit();
        Loaded += (_, _) => Load();
    }

    private async void Load()
    {
        try
        {
            await using var db = SadrDb.New();
            var rows = await db.Companies.Where(c => !c.Deleted)
                .Select(c => new RowBase
                {
                    Id = c.Id,
                    Title = c.FullName,
                    Code = c.Code,
                    Description = "مدیر: " + (c.Manager != null ? c.Manager.FirstName + " " + c.Manager.LastName : "-")
                        + " | تلفن: " + c.Tel
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
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک شرکت را انتخاب کنید."); return; }
        ShowEditor(row.Id);
    }

    private async void ShowEditor(int? id)
    {
        try
        {
            await using var db = SadrDb.New();
            var e = id is null ? null : await db.Companies.FirstAsync(x => x.Id == id);
            var people = await db.People.Where(p => !p.Deleted)
                .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName }).ToListAsync();
            var managerChoices = people.Select(p => new KeyValuePair<int, string>(p.Id, p.Name)).ToList();
            managerChoices.Insert(0, new KeyValuePair<int, string>(0, "— انتخاب نشده —"));

            var dlg = new FieldEditorWindow(id is null ? "شرکت جدید" : "ویرایش شرکت", new[]
            {
                FieldSpec.Text_("نام کامل شرکت", e?.FullName, true),
                FieldSpec.Text_("کد", e?.Code),
                FieldSpec.Text_("شناسه ملی", e?.NationalId),
                FieldSpec.Text_("شماره ثبت", e?.RegisterId),
                FieldSpec.Text_("تلفن", e?.Tel),
                FieldSpec.Text_("ایمیل", e?.Email),
                FieldSpec.Multi_("آدرس", e?.Address),
                FieldSpec.Choice_("مدیر شرکت", managerChoices, e?.ManagerId ?? 0)
            }) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            var now = DateTime.Now;
            if (e is null)
            {
                e = new Company { GUID = Guid.NewGuid(), RecordUniqueId = Guid.NewGuid(), CreateDateTime = now };
                db.Companies.Add(e);
            }
            e.FullName = dlg.GetText(0)!.Trim();
            e.Code = dlg.GetText(1) ?? "";
            e.NationalId = dlg.GetText(2) ?? "";
            e.RegisterId = dlg.GetText(3) ?? "";
            e.Tel = dlg.GetText(4) ?? "";
            e.Email = dlg.GetText(5);
            e.Address = dlg.GetText(6) ?? "";
            e.ManagerId = dlg.GetChoice(7) is > 0 ? dlg.GetChoice(7) : null;
            e.UpdateDateTime = now;
            await db.SaveChangesAsync();
            // Keep the CustomersProviders mirror in sync for invoices.
            await CustomersProviderService.SyncCompanyAsync(db, e);
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Delete()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک شرکت را انتخاب کنید."); return; }
        if (MessageBox.Show($"«{row.Title}» حذف شود؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var e = await db.Companies.FirstAsync(x => x.Id == row.Id);
            e.Deleted = true;
            e.UpdateDateTime = DateTime.Now;
            await db.SaveChangesAsync();
            await CustomersProviderService.SyncCompanyAsync(db, e); // mirror follows (stays alive if referenced)
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void Info(string m) => MessageBox.Show(m, "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
}
