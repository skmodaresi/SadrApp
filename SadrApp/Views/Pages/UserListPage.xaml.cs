using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

public partial class UserListPage : UserControl
{
    public UserListPage()
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
            var rows = await db.Users
                .Select(u => new RowBase
                {
                    Title = u.Username,
                    Code = u.Role,
                    Description = u.Email + " | تأیید ایمیل: " + (u.EmailConfirmed ? "بله" : "خیر")
                }).ToListAsync();
            for (int i = 0; i < rows.Count; i++) rows[i].Id = i + 1; // display order only; Username is the real key
            ListCtl.SetRows(rows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Edit()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک کاربر را انتخاب کنید."); return; }
        ShowEditor(row.Title); // username as key
    }

    private async void ShowEditor(string? username)
    {
        try
        {
            await using var db = SadrDb.New();
            var e = username is null ? null : await db.Users.FirstAsync(x => x.Username == username);
            var people = await db.People.Where(p => !p.Deleted && p.LoginUserId == null)
                .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName }).ToListAsync();
            var personChoices = people.Select(p => new KeyValuePair<int, string>(p.Id, p.Name)).ToList();
            personChoices.Insert(0, new KeyValuePair<int, string>(0, "— بدون اتصال —"));

            var roleNames = await db.Roles.Where(r => !r.Deleted && r.IsActive)
                .Select(r => r.Name).ToListAsync();
            var roleChoices = roleNames.Select((r, i) => new KeyValuePair<int, string>(i, r)).ToList();

            var dlg = new FieldEditorWindow(username is null ? "کاربر جدید" : "ویرایش کاربر", new[]
            {
                FieldSpec.Text_("نام کاربری", e?.Username, true),
                FieldSpec.Text_("ایمیل", e?.Email, true),
                FieldSpec.Text_("رمز عبور " + (e is null ? "" : "(خالی = بدون تغییر)"), "", e is null),
                FieldSpec.Choice_("نقش", roleChoices, null),
                FieldSpec.Choice_("اتصال به شخص", personChoices, 0),
                FieldSpec.Check_("ایمیل تأیید شده", e?.EmailConfirmed ?? false)
            }) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            var chosenRoleIndex = dlg.GetChoice(3) ?? 0;
            var chosenRoleName = roleNames.ElementAtOrDefault(chosenRoleIndex) ?? "User";
            var personId = dlg.GetChoice(4) is > 0 ? dlg.GetChoice(4) : null;

            var pwd = dlg.GetText(2);
            if (e is null)
            {
                if (await db.Users.AnyAsync(u => u.Username == dlg.GetText(0)!.Trim()))
                {
                    MessageBox.Show("این نام کاربری قبلاً ثبت شده است.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                e = new User { Id = Guid.NewGuid() };
                e.PasswordHash = HashPassword(pwd!);
                db.Users.Add(e);
            }
            else if (!string.IsNullOrWhiteSpace(pwd))
            {
                e.PasswordHash = HashPassword(pwd);
            }
            e.Username = dlg.GetText(0)!.Trim();
            e.Email = dlg.GetText(1)!.Trim();
            e.Role = chosenRoleName;
            e.EmailConfirmed = dlg.GetCheck(5);
            await db.SaveChangesAsync();

            if (personId is int pid)
            {
                var person = await db.People.FirstAsync(p => p.Id == pid);
                person.LoginUserId = e.Id;
                await db.SaveChangesAsync();
            }
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Delete()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک کاربر را انتخاب کنید."); return; }
        if (MessageBox.Show($"کاربر «{row.Title}» حذف شود؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var e = await db.Users.FirstAsync(x => x.Username == row.Title);
            var linked = await db.People.FirstOrDefaultAsync(p => p.LoginUserId == e.Id);
            if (linked is not null) linked.LoginUserId = null;
            db.Users.Remove(e);
            await db.SaveChangesAsync();
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    internal static string HashPassword(string password) => SadrApp.Infrastructure.PasswordHasher.Hash(password);

    private static void Info(string m) => MessageBox.Show(m, "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
}
