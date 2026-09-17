using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

/// <summary>
/// Shared management page for the four accounting levels:
/// AccountGroups → GeneralAccounts → SubSidiaryAccounts → DetailAccounts.
/// Each level has its own list plus parent filters that cascade
/// (a ledger account's group filter follows the selected group, etc.).
/// Persian naming: گروه → کل → معین → تفصیلی.
/// </summary>
public partial class AccountingPage : UserControl
{
    public enum Level { Groups, Generals, Subsidiaries, Details }

    private readonly Level _level;
    private bool _loaded;

    public AccountingPage(Level level, string title)
    {
        InitializeComponent();
        _level = level;
        PageTitle.Text = title;

        ListCtl.NewClicked += (_, _) => ShowEditor(null);
        ListCtl.EditClicked += (_, _) => Edit();
        ListCtl.DeleteClicked += (_, _) => Delete();
        ListCtl.RefreshClicked += (_, _) => { LoadParents(); Load(); };
        ListCtl.RowDoubleClicked += (_, _) => Edit();

        Filter1.SelectionChanged += (_, _) => Load();
        Filter2.SelectionChanged += (_, _) => Load();
        Loaded += (_, _) => { if (!_loaded) { _loaded = true; LoadParents(); Load(); } };
    }

    /// <summary>Loads the parent combos. Filter2 only applies on the Details level.</summary>
    private async void LoadParents()
    {
        try
        {
            await using var db = SadrDb.New();
            switch (_level)
            {
                case Level.Generals:
                {
                    var groups = await db.AccountGroups.Where(g => !g.Deleted)
                        .OrderBy(g => g.Code).Select(g => new ComboItem { Key = g.Id, Text = g.Code + " - " + g.Name }).ToListAsync();
                    groups.Insert(0, new ComboItem { Key = 0, Text = "— همه گروه‌ها —" });
                    Filter1.ItemsSource = groups;
                    Filter1Label.Text = "گروه:";
                    Filter1.DisplayMemberPath = "Text"; Filter1.SelectedValuePath = "Key";
                    if (Filter1.SelectedValue is null) Filter1.SelectedValue = 0;
                    break;
                }
                case Level.Subsidiaries:
                {
                    var generals = await db.GeneralAccounts.Where(g => !g.Deleted)
                        .OrderBy(g => g.Code).Select(g => new ComboItem { Key = g.Id, Text = g.Code + " - " + g.Name }).ToListAsync();
                    generals.Insert(0, new ComboItem { Key = 0, Text = "— همه حساب‌های کل —" });
                    Filter1.ItemsSource = generals;
                    Filter1Label.Text = "حساب کل:";
                    Filter1.DisplayMemberPath = "Text"; Filter1.SelectedValuePath = "Key";
                    if (Filter1.SelectedValue is null) Filter1.SelectedValue = 0;
                    break;
                }
                case Level.Details:
                {
                    var generals = await db.GeneralAccounts.Where(g => !g.Deleted)
                        .OrderBy(g => g.Code).Select(g => new ComboItem { Key = g.Id, Text = g.Code + " - " + g.Name }).ToListAsync();
                    generals.Insert(0, new ComboItem { Key = 0, Text = "— همه حساب‌های کل —" });
                    Filter1.ItemsSource = generals;
                    Filter1Label.Text = "حساب کل:";
                    Filter1.DisplayMemberPath = "Text"; Filter1.SelectedValuePath = "Key";
                    if (Filter1.SelectedValue is null) Filter1.SelectedValue = 0;

                    var subs = await db.SubSidiaryAccounts.Where(s => !s.Deleted)
                        .OrderBy(s => s.Code).Select(s => new ComboItem { Key = s.Id, Text = s.Code + " - " + s.Name }).ToListAsync();
                    subs.Insert(0, new ComboItem { Key = 0, Text = "— همه حساب‌های معین —" });
                    Filter2.ItemsSource = subs;
                    Filter2Label.Text = "حساب معین:";
                    Filter2.DisplayMemberPath = "Text"; Filter2.SelectedValuePath = "Key";
                    if (Filter2.SelectedValue is null) Filter2.SelectedValue = 0;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری فیلترها", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Load()
    {
        try
        {
            await using var db = SadrDb.New();
            List<RowBase> rows = _level switch
            {
                Level.Groups => await db.AccountGroups.Where(x => !x.Deleted)
                    .OrderBy(x => x.Code).Select(x => new RowBase
                    {
                        Id = x.Id, Title = x.Name, Code = x.Code,
                        Description = x.Description,
                        Extra = AccountTypeConsts.Label(x.AccountType)
                    }).ToListAsync(),

                Level.Generals => await BuildGeneralRows(db),

                Level.Subsidiaries => await BuildSubsidiaryRows(db),

                Level.Details => await BuildDetailRows(db),

                _ => new List<RowBase>()
            };
            ListCtl.SetRows(rows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<List<RowBase>> BuildGeneralRows(SadrDbContext db)
    {
        var f1 = Filter1.SelectedValue as int?;
        var query = db.GeneralAccounts.Where(x => !x.Deleted);
        if (f1 is > 0) query = query.Where(x => x.AccountGroupId == f1);
        return await query.OrderBy(x => x.Code).Select(x => new RowBase
        {
            Id = x.Id,
            Title = x.Name,
            Code = x.Code,
            Description = x.Description ?? "",
            Extra = x.Group.Code + " - " + x.Group.Name
        }).ToListAsync();
    }

    private async Task<List<RowBase>> BuildSubsidiaryRows(SadrDbContext db)
    {
        var f1 = Filter1.SelectedValue as int?;
        var query = db.SubSidiaryAccounts.Where(x => !x.Deleted);
        if (f1 is > 0) query = query.Where(x => x.GeneralAccountId == f1);
        return await query.OrderBy(x => x.Code).Select(x => new RowBase
        {
            Id = x.Id,
            Title = x.Name,
            Code = x.Code,
            Description = x.Description ?? "",
            Extra = x.General.Code + " - " + x.General.Name
        }).ToListAsync();
    }

    private async Task<List<RowBase>> BuildDetailRows(SadrDbContext db)
    {
        var f1 = Filter1.SelectedValue as int?;
        var f2 = Filter2.SelectedValue as int?;
        var query = db.DetailAccounts.Where(x => !x.Deleted);
        if (f2 is > 0) query = query.Where(x => x.SubSidiaryAccountId == f2);
        if (f1 is > 0) query = query.Where(x => x.SubSidiary.GeneralAccountId == f1);
        return await query.OrderBy(x => x.Code).Select(x => new RowBase
        {
            Id = x.Id,
            Title = x.Name,
            Code = x.Code,
            Description = x.Description,
            Extra = x.SubSidiary.Code + " - " + x.SubSidiary.Name
        }).ToListAsync();
    }

    private void Edit()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک ردیف را انتخاب کنید."); return; }
        ShowEditor(row.Id);
    }

    private async void ShowEditor(int? id)
    {
        try
        {
            await using var db = SadrDb.New();
            var now = DateTime.Now;
            switch (_level)
            {
                case Level.Groups:
                {
                    var e = id is null ? null : await db.AccountGroups.FirstAsync(x => x.Id == id);
                    var typeChoices = new List<KeyValuePair<int, string>>
                    {
                        new(AccountTypeConsts.Debit, AccountTypeConsts.Label(AccountTypeConsts.Debit)),
                        new(AccountTypeConsts.Credit, AccountTypeConsts.Label(AccountTypeConsts.Credit)),
                        new(AccountTypeConsts.Both, AccountTypeConsts.Label(AccountTypeConsts.Both))
                    };
                    var dlg = new FieldEditorWindow(id is null ? "گروه حساب جدید" : "ویرایش گروه حساب", new[]
                    {
                        FieldSpec.Text_("نام گروه", e?.Name, true),
                        FieldSpec.Text_("کد (حداکثر ۴ رقم)", e?.Code, false, 320, 4),
                        FieldSpec.Choice_("نوع حساب", typeChoices, e?.AccountType ?? AccountTypeConsts.Both),
                        FieldSpec.Multi_("توضیحات", e?.Description)
                    }) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() != true) return;

                    var code = dlg.GetText(1)?.Trim() ?? "";
                    if (code.Length > 4) { Warn("کد گروه حساب حداکثر ۴ رقم است."); return; }
                    var dup = await db.AccountGroups.AnyAsync(x => !x.Deleted && x.Code == code && x.Id != id);
                    if (dup) { Warn("این کد برای گروه دیگری استفاده شده است."); return; }

                    if (e is null)
                        db.AccountGroups.Add(e = new AccountGroup { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now });
                    e.Name = dlg.GetText(0)!.Trim();
                    e.Code = code;
                    e.AccountType = dlg.GetChoice(2) ?? AccountTypeConsts.Both;
                    e.Description = dlg.GetText(3) ?? "";
                    e.UpdateDateTime = now;
                    break;
                }
                case Level.Generals:
                {
                    var e = id is null ? null : await db.GeneralAccounts.FirstAsync(x => x.Id == id);
                    var groups = await db.AccountGroups.Where(g => !g.Deleted)
                        .OrderBy(g => g.Code)
                        .Select(g => new KeyValuePair<int, string>(g.Id, g.Code + " - " + g.Name)).ToListAsync();
                    if (groups.Count == 0) { Info("ابتدا در «گروه‌های حساب» یک گروه تعریف کنید."); return; }
                    var typeChoices = TypeChoices();
                    var dlg = new FieldEditorWindow(id is null ? "حساب کل جدید" : "ویرایش حساب کل", new[]
                    {
                        FieldSpec.Text_("نام حساب", e?.Name, true),
                        FieldSpec.Text_("کد (حداکثر ۸ رقم)", e?.Code, false, 320, 8),
                        FieldSpec.Choice_("گروه حساب", groups, e?.AccountGroupId ?? groups[0].Key),
                        FieldSpec.Choice_("نوع حساب", typeChoices, e?.AccountType ?? AccountTypeConsts.Both),
                        FieldSpec.Multi_("توضیحات", e?.Description)
                    }) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() != true) return;

                    var code = dlg.GetText(1)?.Trim() ?? "";
                    if (code.Length > 8) { Warn("کد حساب کل حداکثر ۸ رقم است."); return; }
                    var dup = await db.GeneralAccounts.AnyAsync(x => !x.Deleted && x.Code == code && x.Id != id);
                    if (dup) { Warn("این کد برای حساب دیگری استفاده شده است."); return; }

                    if (e is null)
                        db.GeneralAccounts.Add(e = new GeneralAccount { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now });
                    e.Name = dlg.GetText(0)!.Trim();
                    e.Code = code;
                    e.AccountGroupId = dlg.GetChoice(2) ?? groups[0].Key;
                    e.AccountType = dlg.GetChoice(3) ?? AccountTypeConsts.Both;
                    e.Description = dlg.GetText(4);
                    e.UpdateDateTime = now;
                    break;
                }
                case Level.Subsidiaries:
                {
                    var e = id is null ? null : await db.SubSidiaryAccounts.FirstAsync(x => x.Id == id);
                    var generals = await db.GeneralAccounts.Where(g => !g.Deleted)
                        .OrderBy(g => g.Code)
                        .Select(g => new KeyValuePair<int, string>(g.Id, g.Code + " - " + g.Name)).ToListAsync();
                    if (generals.Count == 0) { Info("ابتدا در «حساب‌های معین اول» یک حساب تعریف کنید."); return; }
                    var typeChoices = TypeChoices();
                    var dlg = new FieldEditorWindow(id is null ? "حساب معین جدید" : "ویرایش حساب معین", new[]
                    {
                        FieldSpec.Text_("نام حساب", e?.Name, true),
                        FieldSpec.Text_("کد (حداکثر ۱۲ رقم)", e?.Code, false, 320, 12),
                        FieldSpec.Choice_("حساب کل", generals, e?.GeneralAccountId ?? generals[0].Key),
                        FieldSpec.Choice_("نوع حساب", typeChoices, e?.AccountType ?? AccountTypeConsts.Both),
                        FieldSpec.Multi_("توضیحات", e?.Description)
                    }) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() != true) return;

                    var code = dlg.GetText(1)?.Trim() ?? "";
                    if (code.Length > 12) { Warn("کد حساب معین حداکثر ۱۲ رقم است."); return; }
                    var dup = await db.SubSidiaryAccounts.AnyAsync(x => !x.Deleted && x.Code == code && x.Id != id);
                    if (dup) { Warn("این کد برای حساب دیگری استفاده شده است."); return; }

                    if (e is null)
                        db.SubSidiaryAccounts.Add(e = new SubSidiaryAccount { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now });
                    e.Name = dlg.GetText(0)!.Trim();
                    e.Code = code;
                    e.GeneralAccountId = dlg.GetChoice(2) ?? generals[0].Key;
                    e.AccountType = dlg.GetChoice(3) ?? AccountTypeConsts.Both;
                    e.Description = dlg.GetText(4);
                    e.UpdateDateTime = now;
                    break;
                }
                case Level.Details:
                {
                    var e = id is null ? null : await db.DetailAccounts.FirstAsync(x => x.Id == id);
                    var subs = await db.SubSidiaryAccounts.Where(s => !s.Deleted)
                        .OrderBy(s => s.Code)
                        .Select(s => new KeyValuePair<int, string>(s.Id, s.Code + " - " + s.Name)).ToListAsync();
                    if (subs.Count == 0) { Info("ابتدا در «حساب‌های معین دوم» یک حساب تعریف کنید."); return; }

                    var cps = await db.CustomersProviders.Where(c => !c.Deleted)
                        .OrderBy(c => c.Code)
                        .Select(c => new KeyValuePair<int, string>(c.Id, c.Code + " - " + c.Name)).ToListAsync();
                    cps.Insert(0, new KeyValuePair<int, string>(0, "— بدون اتصال —"));
                    var typeChoices = TypeChoices();

                    var dlg = new FieldEditorWindow(id is null ? "حساب تفصیلی جدید" : "ویرایش حساب تفصیلی", new[]
                    {
                        FieldSpec.Text_("نام حساب", e?.Name, true),
                        FieldSpec.Text_("کد (حداکثر ۱۲ رقم)", e?.Code, false, 320, 12),
                        FieldSpec.Choice_("حساب معین", subs, e?.SubSidiaryAccountId ?? subs[0].Key),
                        FieldSpec.Choice_("نوع حساب", typeChoices, e?.AccountType ?? AccountTypeConsts.Both),
                        FieldSpec.Choice_("اتصال به مشتری/تأمین‌کننده", cps, e?.CustomerProviderId ?? 0),
                        FieldSpec.Multi_("توضیحات", e?.Description)
                    }) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() != true) return;

                    var code = dlg.GetText(1)?.Trim() ?? "";
                    if (code.Length > 12) { Warn("کد حساب تفصیلی حداکثر ۱۲ رقم است."); return; }
                    var dup = await db.DetailAccounts.AnyAsync(x => !x.Deleted && x.Code == code && x.Id != id);
                    if (dup) { Warn("این کد برای حساب دیگری استفاده شده است."); return; }

                    if (e is null)
                        db.DetailAccounts.Add(e = new DetailAccount { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now });
                    e.Name = dlg.GetText(0)!.Trim();
                    e.Code = code;
                    e.SubSidiaryAccountId = dlg.GetChoice(2) ?? subs[0].Key;
                    e.AccountType = dlg.GetChoice(3) ?? AccountTypeConsts.Both;
                    e.CustomerProviderId = dlg.GetChoice(4) is > 0 ? dlg.GetChoice(4) : null;
                    e.Description = dlg.GetText(5) ?? "";
                    e.UpdateDateTime = now;
                    break;
                }
            }
            await db.SaveChangesAsync();
            LoadParents();
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static List<KeyValuePair<int, string>> TypeChoices() => new()
    {
        new(AccountTypeConsts.Debit, AccountTypeConsts.Label(AccountTypeConsts.Debit)),
        new(AccountTypeConsts.Credit, AccountTypeConsts.Label(AccountTypeConsts.Credit)),
        new(AccountTypeConsts.Both, AccountTypeConsts.Label(AccountTypeConsts.Both))
    };

    private async void Delete()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک ردیف را انتخاب کنید."); return; }
        if (MessageBox.Show($"«{row.Title}» حذف شود؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            switch (_level)
            {
                case Level.Groups:
                {
                    var used = await db.GeneralAccounts.AnyAsync(g => !g.Deleted && g.AccountGroupId == row.Id);
                    if (used) { Warn("ابتدا حساب‌های کل این گروه را حذف کنید."); return; }
                    (await db.AccountGroups.FirstAsync(x => x.Id == row.Id)).Deleted = true;
                    break;
                }
                case Level.Generals:
                {
                    var used = await db.SubSidiaryAccounts.AnyAsync(s => !s.Deleted && s.GeneralAccountId == row.Id);
                    if (used) { Warn("ابتدا حساب‌های معین این حساب کل را حذف کنید."); return; }
                    (await db.GeneralAccounts.FirstAsync(x => x.Id == row.Id)).Deleted = true;
                    break;
                }
                case Level.Subsidiaries:
                {
                    var used = await db.DetailAccounts.AnyAsync(d => !d.Deleted && d.SubSidiaryAccountId == row.Id);
                    if (used) { Warn("ابتدا حساب‌های تفصیلی این حساب معین را حذف کنید."); return; }
                    (await db.SubSidiaryAccounts.FirstAsync(x => x.Id == row.Id)).Deleted = true;
                    break;
                }
                case Level.Details:
                    (await db.DetailAccounts.FirstAsync(x => x.Id == row.Id)).Deleted = true;
                    break;
            }
            await db.SaveChangesAsync();
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void Info(string m) => MessageBox.Show(m, "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
    private static void Warn(string m) => MessageBox.Show(m, "حذف/ذخیره ممکن نیست", MessageBoxButton.OK, MessageBoxImage.Warning);
}
