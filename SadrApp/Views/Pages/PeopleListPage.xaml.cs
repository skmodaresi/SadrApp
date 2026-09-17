using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

public partial class PeopleListPage : UserControl
{
    public PeopleListPage()
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
            var rows = await db.People.Where(p => !p.Deleted)
                .Select(p => new RowBase
                {
                    Id = p.Id,
                    Title = p.FirstName + " " + p.LastName,
                    Code = p.Code ?? "",
                    Description = (p.Title ?? "") + " | تلفن: " + (p.PhoneNumber ?? "-") + " | " + (p.IsActive == false ? "غیرفعال" : "فعال")
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
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک شخص را انتخاب کنید."); return; }
        ShowEditor(row.Id);
    }

    private async void ShowEditor(int? id)
    {
        try
        {
            await using var db = SadrDb.New();
            var e = id is null ? null : await db.People.FirstAsync(x => x.Id == id);
            var genderChoices = new List<KeyValuePair<int, string>> { new(0, "مرد"), new(1, "زن") };

            var dlg = new FieldEditorWindow(id is null ? "شخص جدید" : "ویرایش شخص", new[]
            {
                FieldSpec.Text_("نام", e?.FirstName, true),
                FieldSpec.Text_("نام میانی", e?.MiddleName),
                FieldSpec.Text_("نام خانوادگی", e?.LastName, true),
                FieldSpec.Text_("عنوان/لقب", e?.Title),
                FieldSpec.Text_("کد", e?.Code),
                FieldSpec.Choice_("جنسیت", genderChoices, e?.Gender),
                FieldSpec.Text_("نام پدر", e?.FatherName),
                FieldSpec.Date_("تاریخ تولد", PersianDate.Parse(e?.BirthDate)),
                FieldSpec.Text_("شماره شناسنامه/کد ملی", e?.CertNumber),
                FieldSpec.Text_("تلفن همراه", e?.PhoneNumber),
                FieldSpec.Text_("ایمیل", e?.Email),
                FieldSpec.Multi_("آدرس", e?.Address),
                FieldSpec.Check_("فعال", e?.IsActive ?? true)
            }) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            var now = DateTime.Now;
            if (e is null)
            {
                e = new Person { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now };
                db.People.Add(e);
            }
            e.FirstName = dlg.GetText(0)!.Trim();
            e.MiddleName = dlg.GetText(1);
            e.LastName = dlg.GetText(2)!.Trim();
            e.Title = dlg.GetText(3);
            e.Code = dlg.GetText(4);
            e.Gender = dlg.GetChoice(5);
            e.FatherName = dlg.GetText(6);
            e.BirthDate = dlg.GetDate(7) is null ? "" : PersianDate.ToPersian(dlg.GetDate(7));
            e.CertNumber = dlg.GetText(8);
            e.PhoneNumber = dlg.GetText(9);
            e.Email = dlg.GetText(10);
            e.Address = dlg.GetText(11);
            e.IsActive = dlg.GetCheck(12);
            e.UpdateDateTime = now;
            await db.SaveChangesAsync();
            // Keep the CustomersProviders mirror in sync for invoices.
            await CustomersProviderService.SyncPersonAsync(db, e);
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Delete()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک شخص را انتخاب کنید."); return; }
        if (MessageBox.Show($"«{row.Title}» حذف شود؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var e = await db.People.FirstAsync(x => x.Id == row.Id);
            e.Deleted = true;
            e.IsActive = false;
            e.UpdateDateTime = DateTime.Now;
            await db.SaveChangesAsync();
            await CustomersProviderService.SyncPersonAsync(db, e); // mirror follows (stays alive if referenced)
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void Info(string m) => MessageBox.Show(m, "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
}
