using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

/// <summary>Generic CRUD page for simple lookup tables.</summary>
public partial class SimpleListPage : UserControl
{
    public enum Kind { Brands, Units, Currencies, Banks, Warehouses, Roles, Attributes, CustomersProviders }

    private readonly Kind _kind;

    public SimpleListPage(Kind kind, string title)
    {
        InitializeComponent();
        _kind = kind;
        PageTitle.Text = title;

        ListCtl.NewClicked += (_, _) => New();
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
            List<RowBase> rows = _kind switch
            {
                Kind.Brands => await db.Brands.Where(x => !x.Deleted)
                    .Select(b => new RowBase { Id = b.Id, Title = b.Name, Code = b.Code, Description = b.Description ?? "" }).ToListAsync(),
                Kind.Units => await db.Units.Where(x => !x.Deleted)
                    .Select(u => new RowBase { Id = u.Id, Title = u.Name, Code = "", Description = u.Description }).ToListAsync(),
                Kind.Currencies => await db.Currencies.Where(x => !x.Deleted)
                    .Select(c => new RowBase { Id = c.Id, Title = c.Name, Code = c.Code ?? "", Description = c.Description ?? "" }).ToListAsync(),
                Kind.Banks => await db.Banks.Where(x => !x.Deleted)
                    .Select(b => new RowBase { Id = b.Id, Title = b.Name, Code = b.Code, Description = b.Description ?? "" }).ToListAsync(),
                Kind.Warehouses => await db.WareHouses.Where(x => !x.Deleted)
                    .Select(w => new RowBase { Id = w.Id, Title = w.Name, Code = w.Code ?? "", Description = w.Description }).ToListAsync(),
                Kind.Roles => await db.Roles.Where(x => !x.Deleted)
                    .Select(r => new RowBase { Id = r.Id, Title = r.Name, Code = r.IsActive ? "فعال" : "غیرفعال", Description = r.Description }).ToListAsync(),
                Kind.Attributes => await db.Attributes.Where(x => !x.Deleted)
                    .Select(a => new RowBase { Id = a.Id, Title = a.Name, Code = "", Description = a.Description }).ToListAsync(),
                Kind.CustomersProviders => await db.CustomersProviders.Where(x => !x.Deleted)
                    .OrderBy(x => x.Name)
                    .Select(c => new RowBase
                    {
                        Id = c.Id,
                        Title = c.Name,
                        Code = c.Code,
                        Description = (c.PersonId != null ? "از شخص" : c.CompanyId != null ? "از شرکت" : "مستقل")
                                      + (string.IsNullOrEmpty(c.Description) ? "" : " | " + c.Description)
                    }).ToListAsync(),
                _ => new List<RowBase>()
            };
            ListCtl.SetRows(rows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void New() => ShowEditor(null);

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
            FieldEditorWindow dlg;
            switch (_kind)
            {
                case Kind.Brands:
                {
                    var e = id is null ? null : await db.Brands.FirstAsync(x => x.Id == id);
                    dlg = new FieldEditorWindow("برند", new[]
                    {
                        FieldSpec.Text_("نام برند", e?.Name, true),
                        FieldSpec.Text_("کد", e?.Code),
                        FieldSpec.Multi_("توضیحات", e?.Description)
                    }) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() != true) return;
                    var now = DateTime.Now;
                    if (e is null)
                        db.Brands.Add(e = new Brand { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now });
                    e.Name = dlg.GetText(0)!.Trim(); e.Code = dlg.GetText(1) ?? ""; e.Description = dlg.GetText(2);
                    e.UpdateDateTime = now;
                    break;
                }
                case Kind.Units:
                {
                    var e = id is null ? null : await db.Units.FirstAsync(x => x.Id == id);
                    var units = await db.Units.Where(u => !u.Deleted && u.Id != id)
                        .Select(u => new { u.Id, u.Name }).ToListAsync();
                    var parentChoices = units.Select(u => new KeyValuePair<int, string>(u.Id, u.Name)).ToList();
                    parentChoices.Insert(0, new KeyValuePair<int, string>(0, "— بدون والد —"));
                    dlg = new FieldEditorWindow("واحد شمارش", new[]
                    {
                        FieldSpec.Text_("نام واحد", e?.Name, true),
                        FieldSpec.Multi_("توضیحات", e?.Description),
                        FieldSpec.Choice_("واحد والد", parentChoices, e?.ParentUnitId ?? 0),
                        FieldSpec.Numeric_("نرخ تبدیل به والد", e?.ParentPercentRel)
                    }) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() != true) return;
                    var now = DateTime.Now;
                    if (e is null)
                        db.Units.Add(e = new Unit { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now });
                    e.Name = dlg.GetText(0)!.Trim(); e.Description = dlg.GetText(1) ?? "";
                    e.ParentUnitId = dlg.GetChoice(2) is > 0 ? dlg.GetChoice(2) : null;
                    e.ParentPercentRel = dlg.GetNumber(3) ?? 0;
                    e.UpdateDateTime = now;
                    break;
                }
                case Kind.Currencies:
                {
                    var e = id is null ? null : await db.Currencies.FirstAsync(x => x.Id == id);
                    dlg = new FieldEditorWindow("واحد پول", new[]
                    {
                        FieldSpec.Text_("نام واحد پول", e?.Name, true),
                        FieldSpec.Text_("کد", e?.Code),
                        FieldSpec.Multi_("توضیحات", e?.Description)
                    }) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() != true) return;
                    var now = DateTime.Now;
                    if (e is null)
                        db.Currencies.Add(e = new Currency { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now });
                    e.Name = dlg.GetText(0)!.Trim(); e.Code = dlg.GetText(1); e.Description = dlg.GetText(2);
                    e.UpdateDateTime = now;
                    break;
                }
                case Kind.Banks:
                {
                    var e = id is null ? null : await db.Banks.FirstAsync(x => x.Id == id);
                    dlg = new FieldEditorWindow("بانک", new[]
                    {
                        FieldSpec.Text_("نام بانک", e?.Name, true),
                        FieldSpec.Text_("کد", e?.Code),
                        FieldSpec.Multi_("توضیحات", e?.Description)
                    }) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() != true) return;
                    var now = DateTime.Now;
                    if (e is null)
                        db.Banks.Add(e = new Bank { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now });
                    e.Name = dlg.GetText(0)!.Trim(); e.Code = dlg.GetText(1) ?? ""; e.Description = dlg.GetText(2);
                    e.UpdateDateTime = now;
                    break;
                }
                case Kind.Warehouses:
                {
                    var e = id is null ? null : await db.WareHouses.FirstAsync(x => x.Id == id);
                    dlg = new FieldEditorWindow("انبار", new[]
                    {
                        FieldSpec.Text_("نام انبار", e?.Name, true),
                        FieldSpec.Text_("کد", e?.Code),
                        FieldSpec.Text_("تلفن", e?.Tel),
                        FieldSpec.Multi_("آدرس", e?.Address),
                        FieldSpec.Multi_("توضیحات", e?.Description),
                        FieldSpec.Check_("فعال", e?.Active ?? true)
                    }) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() != true) return;
                    var now = DateTime.Now;
                    if (e is null)
                        db.WareHouses.Add(e = new WareHouse { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now });
                    e.Name = dlg.GetText(0)!.Trim(); e.Code = dlg.GetText(1); e.Tel = dlg.GetText(2);
                    e.Address = dlg.GetText(3) ?? ""; e.Description = dlg.GetText(4) ?? "";
                    e.Active = dlg.GetCheck(5);
                    e.UpdateDateTime = now;
                    break;
                }
                case Kind.Roles:
                {
                    var e = id is null ? null : await db.Roles.FirstAsync(x => x.Id == id);
                    dlg = new FieldEditorWindow("نقش کاربری", new[]
                    {
                        FieldSpec.Text_("نام نقش", e?.Name, true),
                        FieldSpec.Multi_("توضیحات", e?.Description),
                        FieldSpec.Check_("فعال", e?.IsActive ?? true)
                    }) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() != true) return;
                    var now = DateTime.Now;
                    if (e is null)
                        db.Roles.Add(e = new Role { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now });
                    e.Name = dlg.GetText(0)!.Trim(); e.Description = dlg.GetText(1) ?? "";
                    e.IsActive = dlg.GetCheck(2);
                    e.UpdateDateTime = now;
                    break;
                }
                case Kind.Attributes:
                {
                    var e = id is null ? null : await db.Attributes.FirstAsync(x => x.Id == id);
                    dlg = new FieldEditorWindow("ویژگی کالا", new[]
                    {
                        FieldSpec.Text_("نام ویژگی", e?.Name, true),
                        FieldSpec.Multi_("توضیحات", e?.Description)
                    }) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() != true) return;
                    var now = DateTime.Now;
                    if (e is null)
                        db.Attributes.Add(e = new AttributeDef { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now });
                    e.Name = dlg.GetText(0)!.Trim(); e.Description = dlg.GetText(1) ?? "";
                    e.UpdateDateTime = now;
                    break;
                }
                case Kind.CustomersProviders:
                {
                    var e = id is null ? null : await db.CustomersProviders.FirstAsync(x => x.Id == id);
                    bool isAuto = e is not null && (e.PersonId != null || e.CompanyId != null);

                    // Pickers exclude people/companies that already have a mirrored row.
                    var people = await db.People.Where(p => !p.Deleted)
                        .Where(p => !db.CustomersProviders.Any(c => c.PersonId == p.Id))
                        .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName }).ToListAsync();
                    var companies = await db.Companies.Where(c => !c.Deleted)
                        .Where(c => !db.CustomersProviders.Any(x => x.CompanyId == c.Id))
                        .Select(c => new { c.Id, c.FullName }).ToListAsync();
                    var personChoices = people.Select(p => new KeyValuePair<int, string>(p.Id, p.Name)).ToList();
                    personChoices.Insert(0, new KeyValuePair<int, string>(0, "— بدون اتصال (مستقل) —"));
                    var companyChoices = companies.Select(c => new KeyValuePair<int, string>(c.Id, c.FullName)).ToList();
                    companyChoices.Insert(0, new KeyValuePair<int, string>(0, "— بدون اتصال (مستقل) —"));

                    var fields = new List<FieldSpec>
                    {
                        FieldSpec.Text_("نام", e?.Name, !isAuto)
                    };
                    if (isAuto) fields[0].IsReadOnly = true; // managed by the source person/company
                    if (!isAuto)
                    {
                        fields.Add(FieldSpec.Text_("کد", e?.Code));
                        fields.Add(FieldSpec.Choice_("شخص مرتبط", personChoices, 0));
                        fields.Add(FieldSpec.Choice_("شرکت مرتبط", companyChoices, 0));
                    }
                    fields.Add(FieldSpec.Multi_("توضیحات", e?.Description));
                    if (isAuto)
                    {
                        var note = FieldSpec.Text_("منبع", e?.PersonId != null
                            ? "خودکار از «اشخاص» — نام در صفحه اشخاص ویرایش می‌شود."
                            : "خودکار از «شرکت‌ها» — نام در صفحه شرکت‌ها ویرایش می‌شود.");
                        note.IsReadOnly = true;
                        fields.Add(note);
                    }

                    dlg = new FieldEditorWindow("مشتری / تأمین‌کننده", fields) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() != true) return;
                    var now = DateTime.Now;
                    if (e is null)
                        db.CustomersProviders.Add(e = new CustomersProvider { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now });
                    if (!isAuto)
                    {
                        e.Name = dlg.GetText(0)!.Trim();
                        e.Code = dlg.GetText(1) ?? "";
                        var pid = dlg.GetChoice(2);
                        var cid = dlg.GetChoice(3);
                        e.PersonId = pid is > 0 ? pid : null;
                        e.CompanyId = cid is > 0 ? cid : null;
                        e.Description = dlg.GetText(4);
                    }
                    else
                    {
                        e.Description = dlg.GetText(1);
                    }
                    e.UpdateDateTime = now;
                    break;
                }
                default: return;
            }
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
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک ردیف را انتخاب کنید."); return; }
        if (MessageBox.Show($"«{row.Title}» حذف شود؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            switch (_kind)
            {
                case Kind.Brands:
                    (await db.Brands.FirstAsync(x => x.Id == row.Id)).Deleted = true;
                    break;
                case Kind.Units:
                {
                    var used = await db.Products.AnyAsync(p => !p.Deleted && p.MainUnitId == row.Id);
                    if (used) { Warn("این واحد در کالاها استفاده شده است."); return; }
                    (await db.Units.FirstAsync(x => x.Id == row.Id)).Deleted = true;
                    break;
                }
                case Kind.Currencies:
                {
                    var used = await db.Projects.AnyAsync(p => !p.Deleted && p.CurrencyId == row.Id)
                            || await db.BankAccounts.AnyAsync(a => !a.Deleted && a.CurrencyId == row.Id);
                    if (used) { Warn("این واحد پول در پروژه‌ها یا حساب‌های بانکی استفاده شده است."); return; }
                    (await db.Currencies.FirstAsync(x => x.Id == row.Id)).Deleted = true;
                    break;
                }
                case Kind.Banks:
                {
                    var used = await db.BankBranches.AnyAsync(b => !b.Deleted && b.BankId == row.Id);
                    if (used) { Warn("ابتدا شعب این بانک را حذف کنید."); return; }
                    (await db.Banks.FirstAsync(x => x.Id == row.Id)).Deleted = true;
                    break;
                }
                case Kind.Warehouses:
                    (await db.WareHouses.FirstAsync(x => x.Id == row.Id)).Deleted = true;
                    break;
                case Kind.CustomersProviders:
                {
                    var used = await db.Invoices.AnyAsync(i => !i.Deleted && (i.CustomerId == row.Id || i.ProviderId == row.Id))
                            || await db.DetailAccounts.AnyAsync(d => !d.Deleted && d.CustomerProviderId == row.Id);
                    if (used) { Warn("این مشتری/تأمین‌کننده در فاکتورها یا حساب‌های تفصیلی استفاده شده است."); return; }
                    (await db.CustomersProviders.FirstAsync(x => x.Id == row.Id)).Deleted = true;
                    break;
                }
                case Kind.Roles:
                    (await db.Roles.FirstAsync(x => x.Id == row.Id)).Deleted = true;
                    break;
                case Kind.Attributes:
                {
                    var used = await db.CategoryAttributes.AnyAsync(c => !c.Deleted && c.AttributeId == row.Id)
                            || await db.ProductAttribs.AnyAsync(p => !p.Deleted && p.AttribId == row.Id);
                    if (used) { Warn("این ویژگی در دسته‌ها یا کالاها استفاده شده است."); return; }
                    (await db.Attributes.FirstAsync(x => x.Id == row.Id)).Deleted = true;
                    break;
                }
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
    private static void Warn(string m) => MessageBox.Show(m, "حذف ممکن نیست", MessageBoxButton.OK, MessageBoxImage.Warning);
}
