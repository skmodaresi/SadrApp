using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

/// <summary>Management page for the Cashes table: cash boxes with start balance, owners, and signatories.</summary>
public partial class CashesPage : UserControl
{
    public CashesPage()
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
            var people = await db.People.Where(p => !p.Deleted)
                .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName }).ToDictionaryAsync(p => p.Id, p => p.Name);
            var companies = await db.Companies.Where(c => !c.Deleted)
                .Select(c => new { c.Id, c.FullName }).ToDictionaryAsync(c => c.Id, c => c.FullName);

            var rows = await db.Cashes.Where(c => !c.Deleted)
                .Select(c => new
                {
                    c.Id, c.Name, c.StartBalance, c.CurrentBalance, c.Status,
                    c.OwnerPersonId, c.OwnerCompanyId, c.ResponcePersonId, c.Description
                })
                .ToListAsync();

            ListCtl.SetRows(rows.Select(c => new RowBase
            {
                Id = c.Id,
                Title = c.Name,
                Code = c.Status == 1 ? "فعال" : "غیرفعال",
                Extra = "موجودی: " + c.CurrentBalance.ToString("N0"),
                Description = string.Join(" | ",
                    c.OwnerPersonId is int op && people.TryGetValue(op, out var on) ? "مالک: " + on : "",
                    c.OwnerCompanyId is int oc && companies.TryGetValue(oc, out var cn) ? "شرکت: " + cn : "",
                    people.TryGetValue(c.ResponcePersonId, out var rn) ? "مسئول: " + rn : "",
                    c.Description ?? "").Trim(" |".ToCharArray())
            }).ToList());
            ListCtl.ShowExtraColumn("موجودی");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Edit()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک صندوق را انتخاب کنید."); return; }
        ShowEditor(row.Id);
    }

    private async void ShowEditor(int? id)
    {
        try
        {
            await using var db = SadrDb.New();
            var personChoices = await db.People.Where(p => !p.Deleted)
                .Select(p => new KeyValuePair<int, string>(p.Id, p.FirstName + " " + p.LastName)).ToListAsync();
            if (personChoices.Count == 0) { Info("ابتدا حداقل یک شخص تعریف کنید (برای مسئول صندوق)."); return; }
            var companyChoices = await db.Companies.Where(c => !c.Deleted)
                .Select(c => new KeyValuePair<int, string>(c.Id, c.FullName)).ToListAsync();
            companyChoices.Insert(0, new KeyValuePair<int, string>(0, "— انتخاب نشده —"));

            var statusChoices = new List<KeyValuePair<int, string>>
            {
                new(1, "فعال"), new(0, "غیرفعال")
            };

            var e = id is null ? null : await db.Cashes.FirstAsync(x => x.Id == id);

            // SignPeople is a native SQL json column: parse the stored array (or legacy plain text)
            // into one name per line for the editor, exactly like bank accounts.
            var signLines = "";
            var rawSign = e?.SignPeople;
            if (!string.IsNullOrWhiteSpace(rawSign))
            {
                try { signLines = string.Join(Environment.NewLine, JsonSerializer.Deserialize<List<string>>(rawSign) ?? []); }
                catch (JsonException) { signLines = rawSign; }
            }

            var dlg = new FieldEditorWindow(id is null ? "صندوق جدید" : "ویرایش صندوق", new[]
            {
                FieldSpec.Text_("نام صندوق", e?.Name, true),
                FieldSpec.Choice_("مسئول صندوق", personChoices, e?.ResponcePersonId ?? personChoices[0].Key),
                FieldSpec.Choice_("مالک (شخص)", personChoices, e?.OwnerPersonId ?? 0),
                FieldSpec.Choice_("مالک (شرکت)", companyChoices, e?.OwnerCompanyId ?? 0),
                FieldSpec.Choice_("وضعیت", statusChoices, e?.Status ?? 1),
                FieldSpec.Numeric_("موجودی اولیه", e?.StartBalance),
                FieldSpec.Multi_("صاحبان امضا (هر نام در یک خط)", signLines),
                FieldSpec.Multi_("توضیحات", e?.Description)
            }) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            var now = DateTime.Now;
            if (e is null)
            {
                e = new Cash { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now, CreateUserId = UserSession.CurrentUserId };
                db.Cashes.Add(e);
            }
            e.Name = dlg.GetText(0)!.Trim();
            e.ResponcePersonId = dlg.GetChoice(1) ?? personChoices[0].Key;
            e.OwnerPersonId = dlg.GetChoice(2) is > 0 ? dlg.GetChoice(2) : null;
            e.OwnerCompanyId = dlg.GetChoice(3) is > 0 ? dlg.GetChoice(3) : null;
            e.Status = dlg.GetChoice(4) ?? 1;
            e.StartBalance = dlg.GetNumber(5) ?? 0;
            if (id is null) e.CurrentBalance = e.StartBalance; // edits never reset the live balance
            e.SignPeople = JsonSerializer.Serialize(
                (dlg.GetText(6) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0).ToList());
            e.Description = dlg.GetText(7);
            e.UpdateDateTime = now;
            e.UpdateUserId = UserSession.CurrentUserId;
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
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک صندوق را انتخاب کنید."); return; }
        if (MessageBox.Show($"صندوق «{row.Title}» حذف شود؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var e = await db.Cashes.FirstAsync(x => x.Id == row.Id);
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

    private static void Info(string m) => MessageBox.Show(m, "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
}
