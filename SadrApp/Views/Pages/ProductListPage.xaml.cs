using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

public partial class ProductListPage : UserControl
{
    public ProductListPage()
    {
        InitializeComponent();
        ListCtl.NewClicked += (_, _) => ShowEditor(null);
        ListCtl.EditClicked += (_, _) => Edit();
        ListCtl.DeleteClicked += (_, _) => Delete();
        ListCtl.RefreshClicked += (_, _) => Load();
        ListCtl.RowDoubleClicked += (_, _) => Edit();
        BtnExtraCats.Click += (_, _) => ManageExtraCategories();
        BtnAttribs.Click += (_, _) => ManageAttribs();
        BtnPrices.Click += (_, _) => ManagePrices();
        Loaded += (_, _) => Load();
    }

    private async void Load()
    {
        try
        {
            await using var db = SadrDb.New();
            var currencies = await db.Currencies
                .Select(c => new { c.Id, c.Name }).ToListAsync();
            var currencyNames = currencies.ToDictionary(c => c.Id, c => c.Name);

            // Latest defined price per product (most recent SetDate, then Id) shown in the list.
            // Select only the small columns first, then pick the latest in memory — GroupBy→First
            // inside a server query breaks the EF projector here.
            var priceRows = await db.Prices.Where(x => !x.Deleted)
                .Select(x => new { x.ProductId, x.PriceAmount, x.CurrencyId, x.SetDateG, x.Id })
                .ToListAsync();
            var priceMap = priceRows
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.SetDateG).ThenByDescending(x => x.Id).First());

            var rows = await db.Products.Where(p => !p.Deleted)
                .Select(p => new RowBase
                {
                    Id = p.Id,
                    Title = p.Name,
                    Code = p.Code,
                    Description = (p.Description ?? "") + " | دسته: " + (p.MainCategory != null ? p.MainCategory.Name : "-")
                                  + " | واحد: " + (p.MainUnit != null ? p.MainUnit.Name : "-")
                                  + (p.Brand != null ? " | برند: " + p.Brand.Name : "")
                }).ToListAsync();
            foreach (var r in rows)
            {
                if (priceMap.TryGetValue(r.Id, out var pr))
                    r.Extra = pr.PriceAmount.ToString("N0")
                              + (pr.CurrencyId is int cid && currencyNames.TryGetValue(cid, out var cn) ? " " + cn : "");
            }
            ListCtl.SetRows(rows);
            ListCtl.ShowExtraColumn("آخرین قیمت");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Edit()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک کالا را انتخاب کنید."); return; }
        ShowEditor(row.Id);
    }

    private async void ShowEditor(int? id)
    {
        try
        {
            await using var db = SadrDb.New();
            var catChoices = await db.ProductCategories.Where(c => !c.Deleted)
                .Select(c => new KeyValuePair<int, string>(c.Id, c.Name)).ToListAsync();
            var unitChoices = await db.Units.Where(u => !u.Deleted)
                .Select(u => new KeyValuePair<int, string>(u.Id, u.Name)).ToListAsync();
            var brandChoices = await db.Brands.Where(b => !b.Deleted)
                .Select(b => new KeyValuePair<int, string>(b.Id, b.Name)).ToListAsync();

            var e = id is null ? null : await db.Products.Include(p => p.ExtraCategories).FirstAsync(x => x.Id == id);

            FieldEditorWindow dlg = new FieldEditorWindow(id is null ? "کالای جدید" : "ویرایش کالا", new[]
            {
                FieldSpec.Text_("نام کالا", e?.Name, true),
                FieldSpec.Text_("کد کالا", e?.Code, true),
                FieldSpec.Multi_("توضیحات", e?.Description),
                FieldSpec.Choice_("دسته اصلی", catChoices, e?.MainCategoryId),
                FieldSpec.Choice_("واحد اصلی", unitChoices, e?.MainUnitId),
                FieldSpec.Choice_("برند", brandChoices, e?.BrandId, optional: true)
            }) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            var now = DateTime.Now;
            if (e is null)
            {
                e = new Product { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now };
                db.Products.Add(e);
            }
            e.Name = dlg.GetText(0)!.Trim();
            e.Code = dlg.GetText(1)!.Trim();
            e.Description = dlg.GetText(2) ?? "";
            e.MainCategoryId = dlg.GetChoice(3) ?? 0;
            e.MainUnitId = dlg.GetChoice(4) ?? 0;
            e.BrandId = dlg.GetChoice(5);
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
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک کالا را انتخاب کنید."); return; }
        if (MessageBox.Show($"کالای «{row.Title}» حذف شود؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var e = await db.Products.FirstAsync(x => x.Id == row.Id);
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

    private void ManageAttribs()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک کالا را انتخاب کنید."); return; }
        new ProductAttribsDialog(row.Id, row.Title) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void ManagePrices()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک کالا را انتخاب کنید."); return; }
        new ProductPricesDialog(row.Id, row.Title) { Owner = Window.GetWindow(this) }.ShowDialog();
        Load(); // last-price column may have changed
    }

    private async void ManageExtraCategories()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک کالا را انتخاب کنید، سپس دسته‌های تکمیلی آن را مدیریت کنید."); return; }
        try
        {
            await using var db = SadrDb.New();
            var productId = row.Id;
            var allCats = await db.ProductCategories.Where(c => !c.Deleted)
                .Select(c => new KeyValuePair<int, string>(c.Id, c.Name)).ToListAsync();
            var current = await db.ProductExtraCategories.Where(x => !x.Deleted && x.ProductId == productId)
                .Select(x => x.CategoryId).ToListAsync();

            var dlg = new MultiCheckDialog("دسته‌های تکمیلی کالا", allCats, current) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            var now = DateTime.Now;
            var selected = dlg.SelectedIds;
            var toAdd = selected.Except(current).ToList();
            var toRemove = current.Except(selected).ToList();

            foreach (var catId in toAdd)
            {
                db.ProductExtraCategories.Add(new ProductExtraCategory
                {
                    ProductId = productId, CategoryId = catId, Deleted = false,
                    RecordUniqueId = Guid.NewGuid(), CreateDateTime = now, UpdateDateTime = now
                });
            }
            var links = await db.ProductExtraCategories
                .Where(x => !x.Deleted && x.ProductId == productId && toRemove.Contains(x.CategoryId)).ToListAsync();
            foreach (var link in links) { link.Deleted = true; link.UpdateDateTime = now; }
            await db.SaveChangesAsync();
            Info("دسته‌های تکمیلی به‌روزرسانی شد.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void Info(string m) => MessageBox.Show(m, "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
}
