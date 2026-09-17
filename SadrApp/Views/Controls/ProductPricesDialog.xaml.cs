using System.Windows;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.ViewModels;

namespace SadrApp.Views.Controls;

/// <summary>
/// Manages the Price history of one product: add/edit/delete dated price records with
/// currency and a default row discount. The latest record is what the invoice editor
/// suggests and what the product list shows as «آخرین قیمت».
/// </summary>
public partial class ProductPricesDialog : Window
{
    private readonly int _productId;

    public ProductPricesDialog(int productId, string productName)
    {
        InitializeComponent();
        _productId = productId;
        HeaderTitle.Text = $"قیمت‌گذاری کالا «{productName}»";
        Title = $"قیمت‌گذاری کالا «{productName}»";
        BtnAdd.Click += (_, _) => Edit(null);
        BtnEdit.Click += (_, _) => EditSelected();
        BtnDelete.Click += (_, _) => Delete();
        BtnReload.Click += (_, _) => Reload();
        Grid.MouseDoubleClick += (_, _) => EditSelected();
        Loaded += (_, _) => Reload();
    }

    private class PriceRow : RowBase
    {
        public int PriceId { get; set; }
        public string Extra2 { get; set; } = "";
        public string Status { get; set; } = "";
    }

    private async void Reload()
    {
        try
        {
            await using var db = SadrDb.New();
            var currencyNames = await db.Currencies.AsNoTracking()
                .Where(c => !c.Deleted).Select(c => new { c.Id, c.Name }).ToListAsync();
            var currencyMap = currencyNames.ToDictionary(c => c.Id, c => c.Name);

            var prices = await db.Prices.AsNoTracking()
                .Where(p => !p.Deleted && p.ProductId == _productId)
                .OrderByDescending(p => p.SetDateG).ThenByDescending(p => p.Id)
                .ToListAsync();

            var rows = prices.Select((p, i) => new PriceRow
            {
                PriceId = p.Id,
                Id = p.Id,
                Title = PersianDate.ToPersian(p.SetDateG),
                Code = p.PriceAmount.ToString("N0"),
                Extra = currencyMap.TryGetValue(p.CurrencyId, out var cn) ? cn : $"#{p.CurrencyId}",
                Extra2 = p.DefaultDiscountPersentage > 0
                    ? p.DefaultDiscountPersentage.ToString("0.##") + "٪"
                    : "—",
                Description = p.EndDateG == default ? "—" : PersianDate.ToPersian(p.EndDateG),
                Status = i == 0 ? "آخرین" : "قدیمی"
            }).ToList();

            Grid.ItemsSource = rows;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditSelected()
    {
        if (Grid.SelectedItem is not PriceRow row) { Info("ابتدا یک قیمت را انتخاب کنید."); return; }
        Edit(row.PriceId);
    }

    /// <summary>Add (priceId null) or edit one Price record via the shared field editor.</summary>
    private async void Edit(int? priceId)
    {
        try
        {
            await using var db = SadrDb.New();
            Price? existing = null;
            if (priceId is int pid)
            {
                existing = await db.Prices.FirstAsync(p => p.Id == pid);
            }

            var currencyChoices = await db.Currencies.AsNoTracking()
                .Where(c => !c.Deleted)
                .Select(c => new KeyValuePair<int, string>(c.Id, c.Name)).ToListAsync();
            var unitChoices = await db.Units.AsNoTracking()
                .Where(u => !u.Deleted)
                .Select(u => new KeyValuePair<int, string>(u.Id, u.Name)).ToListAsync();
            if (currencyChoices.Count == 0) { Info("ابتدا واحدهای پول را تعریف کنید."); return; }
            if (unitChoices.Count == 0) { Info("ابتدا واحدها را تعریف کنید."); return; }

            var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == _productId);
            var dlg = new FieldEditorWindow("قیمت کالا", new[]
            {
                FieldSpec.Text_("کالا", product.Name, required: false, width: 320),
                FieldSpec.Numeric_("مبلغ", existing?.PriceAmount, required: true),
                FieldSpec.Choice_("واحد پول", currencyChoices, existing?.CurrencyId),
                FieldSpec.Choice_("واحد", unitChoices, existing?.UnitId),
                FieldSpec.Numeric_("تخفیف پیش‌فرض (٪)", existing is null
                    ? null : (decimal?)existing.DefaultDiscountPersentage, required: false),
                FieldSpec.Date_("تاریخ ثبت", existing?.SetDateG ?? DateTime.Today, required: true),
                FieldSpec.Date_("تاریخ پایان", existing is null || existing.EndDateG == default
                    ? null : existing.EndDateG, required: false)
            })
            { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var amount = dlg.GetNumber(1);
            if (amount is null or 0) { Info("مبلغ قیمت الزامی است."); return; }
            var currencyId = dlg.GetChoice(2);
            if (currencyId is null) { Info("واحد پول الزامی است."); return; }
            var unitId = dlg.GetChoice(3);
            if (unitId is null) { Info("واحد الزامی است."); return; }
            var setG = dlg.GetDate(5) ?? DateTime.Today;

            var now = DateTime.Now;
            if (existing is null)
            {
                existing = new Price
                {
                    ProductId = _productId,
                    RecordUniqueId = Guid.NewGuid(),
                    CreateDateTime = now
                };
                db.Prices.Add(existing);
            }
            existing.PriceAmount = amount.Value;
            existing.CurrencyId = currencyId.Value;
            existing.UnitId = unitId.Value;
            existing.DefaultDiscountPersentage = (double)(dlg.GetNumber(4) ?? 0);
            existing.SetDateG = setG;
            existing.SetDate = PersianDate.ToPersian(setG);
            var endG = dlg.GetDate(6);
            existing.EndDateG = endG ?? default;
            existing.EndDate = endG is null ? "" : PersianDate.ToPersian(endG.Value);
            existing.Status = 1;
            existing.UpdateDateTime = now;
            await db.SaveChangesAsync();
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Delete()
    {
        if (Grid.SelectedItem is not PriceRow row) { Info("ابتدا یک قیمت را انتخاب کنید."); return; }
        try
        {
            await using var db = SadrDb.New();
            var existing = await db.Prices.FirstAsync(p => p.Id == row.PriceId);
            if (MessageBox.Show($"قیمت ثبت‌شده در تاریخ {row.Title} حذف شود؟", "تأیید حذف",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            existing.Deleted = true;
            existing.UpdateDateTime = DateTime.Now;
            await db.SaveChangesAsync();
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void Info(string m) => MessageBox.Show(m, "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
}
