using System.Windows;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.ViewModels;

namespace SadrApp.Views.Controls;

/// <summary>
/// Manages ProductAttrib rows for one product.
/// Two kinds of rows:
/// 1. Category attributes — come from CategoryAttributes of the product's main + extra categories.
/// 2. Product-level extra attributes — defined directly on the product (linked to its main
///    category via an auto-created, marker-tagged CategoryAttributes row).
/// </summary>
public partial class ProductAttribsDialog : Window
{
    private readonly int _productId;

    public ProductAttribsDialog(int productId, string productName)
    {
        InitializeComponent();
        _productId = productId;
        HeaderTitle.Text = $"ویژگی‌های کالا «{productName}»";
        Title = $"ویژگی‌های کالا «{productName}»";
        BtnAddExtra.Click += (_, _) => AddExtra();
        BtnEdit.Click += (_, _) => EditValue();
        BtnDelete.Click += (_, _) => DeleteValue();
        BtnReload.Click += (_, _) => Reload();
        Grid.MouseDoubleClick += (_, _) => EditValue();
        Loaded += (_, _) => Reload();
    }

    private class AttribRow : RowBase
    {
        public int CategoryAttribId { get; set; }
        public int AttribId { get; set; }
        public bool Forced { get; set; }
        public bool IsProductExtra { get; set; }
    }

    private async void Reload()
    {
        try
        {
            await using var db = SadrDb.New();
            var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == _productId);

            // Category ids: main + extra categories of this product
            var catIds = new List<int> { product.MainCategoryId };
            catIds.AddRange(await db.ProductExtraCategories
                .Where(e => !e.Deleted && e.ProductId == _productId)
                .Select(e => e.CategoryId).ToListAsync());
            catIds = catIds.Distinct().ToList();

            var catLinks = await (
                from ca in db.CategoryAttributes
                where !ca.Deleted && catIds.Contains(ca.CategoryId)
                      && !EF.Functions.Like(ca.Description, AttribConsts.AutoProductLink + "%")
                join a in db.Attributes on ca.AttributeId equals a.Id
                where !a.Deleted
                select new { ca.Id, ca.IsForced, ca.CategoryId, AttribId = a.Id, AttribName = a.Name })
                .ToListAsync();

            var catNames = await db.ProductCategories
                .Where(c => catIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name }).ToListAsync();
            var catNameMap = catNames.ToDictionary(c => c.Id, c => c.Name);

            var values = await db.ProductAttribs
                .Where(v => !v.Deleted && v.ProductId == _productId)
                .Select(v => new { v.Id, v.CategoryAttribId, v.Value })
                .ToListAsync();
            var valueMap = values.GroupBy(v => (int)v.CategoryAttribId).ToDictionary(g => g.Key, g => g.First().Value);

            var rows = new List<AttribRow>();

            // 1) Category-linked attributes
            foreach (var l in catLinks)
            {
                rows.Add(new AttribRow
                {
                    CategoryAttribId = l.Id,
                    AttribId = l.AttribId,
                    Id = l.Id,
                    Title = l.AttribName,
                    Code = catNameMap.TryGetValue(l.CategoryId, out var n) ? n : "-",
                    Extra = l.IsForced ? "بله" : "خیر",
                    Forced = l.IsForced,
                    Description = valueMap.TryGetValue(l.Id, out var v) ? v : ""
                });
            }

            // 2) Product-level extra attributes: values whose CategoryAttribId is not in catLinks
            var linkedIds = catLinks.Select(l => l.Id).ToHashSet();
            var extraValues = values.Where(v => !linkedIds.Contains(v.CategoryAttribId)).ToList();
            if (extraValues.Count > 0)
            {
                var extraLinkIds = extraValues.Select(v => v.CategoryAttribId).ToList();
                var extraInfos = await (
                    from ca in db.CategoryAttributes
                    where extraLinkIds.Contains(ca.Id)
                    join a in db.Attributes on ca.AttributeId equals a.Id
                    where !a.Deleted
                    select new { ca.Id, AttribId = a.Id, AttribName = a.Name })
                    .ToListAsync();
                var infoMap = extraInfos.ToDictionary(x => x.Id, x => (x.AttribId, x.AttribName));

                foreach (var v in extraValues)
                {
                    if (!infoMap.TryGetValue(v.CategoryAttribId, out var info)) continue;
                    rows.Add(new AttribRow
                    {
                        CategoryAttribId = v.CategoryAttribId,
                        AttribId = info.AttribId,
                        Id = v.CategoryAttribId,
                        Title = info.AttribName,
                        Code = "کالا",
                        Extra = "اختیاری",
                        Forced = false,
                        IsProductExtra = true,
                        Description = v.Value
                    });
                }
            }

            Grid.ItemsSource = rows.OrderBy(r => r.IsProductExtra).ThenBy(r => r.Title).ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void EditValue()
    {
        if (Grid.SelectedItem is not AttribRow row) { Info("ابتدا یک ویژگی را انتخاب کنید."); return; }
        try
        {
            await using var db = SadrDb.New();
            var link = await db.CategoryAttributes.AsNoTracking().FirstAsync(c => c.Id == row.CategoryAttribId);
            var attrName = await db.Attributes.Where(a => a.Id == row.AttribId).Select(a => a.Name).FirstAsync();

            var existing = await db.ProductAttribs
                .FirstOrDefaultAsync(v => !v.Deleted && v.ProductId == _productId && v.CategoryAttribId == row.CategoryAttribId);

            var dlg = new FieldEditorWindow($"مقدار ویژگی «{attrName}»", new[]
            {
                FieldSpec.Text_("مقدار", existing?.Value ?? "", link.IsForced)
            }) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var value = dlg.GetText(0)?.Trim() ?? "";
            if (link.IsForced && value.Length == 0)
            {
                MessageBox.Show("این ویژگی برای این دسته الزامی است و باید مقدار داشته باشد.",
                    "اعتبارسنجی", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var now = DateTime.Now;
            if (existing is null)
            {
                existing = new ProductAttrib
                {
                    ProductId = _productId,
                    CategoryAttribId = row.CategoryAttribId,
                    AttribId = link.AttributeId,
                    RecordUniqueId = Guid.NewGuid(),
                    CreateDateTime = now
                };
                db.ProductAttribs.Add(existing);
            }
            existing.Value = value;
            existing.UpdateDateTime = now;
            await db.SaveChangesAsync();
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteValue()
    {
        if (Grid.SelectedItem is not AttribRow row) { Info("ابتدا یک ویژگی را انتخاب کنید."); return; }
        try
        {
            await using var db = SadrDb.New();
            var existing = await db.ProductAttribs
                .FirstOrDefaultAsync(v => !v.Deleted && v.ProductId == _productId && v.CategoryAttribId == row.CategoryAttribId);
            if (existing is null) { Info("برای این ویژگی مقداری ثبت نشده است."); return; }

            if (MessageBox.Show($"مقدار ویژگی «{row.Title}» حذف شود؟", "تأیید حذف",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            existing.Deleted = true;
            existing.UpdateDateTime = DateTime.Now;

            // Product-level extra: also remove its auto-created category link + guard the attribute
            if (row.IsProductExtra)
            {
                var link = await db.CategoryAttributes.FirstAsync(c => c.Id == row.CategoryAttribId);
                link.Deleted = true;
                link.UpdateDateTime = DateTime.Now;
            }

            await db.SaveChangesAsync();
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Adds a product-level extra attribute: creates a marked CategoryAttributes
    /// link on the product's main category, then stores the value row.</summary>
    private async void AddExtra()
    {
        try
        {
            await using var db = SadrDb.New();
            var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == _productId);

            // Attributes not already shown for this product
            var usedLinkIds = await db.ProductAttribs
                .Where(v => !v.Deleted && v.ProductId == _productId)
                .Select(v => v.CategoryAttribId).ToListAsync();
            var usedAttrIds = await db.CategoryAttributes
                .Where(c => usedLinkIds.Contains(c.Id))
                .Select(c => c.AttributeId).ToListAsync();

            var candidates = await db.Attributes
                .Where(a => !a.Deleted && !usedAttrIds.Contains(a.Id))
                .Select(a => new KeyValuePair<int, string>(a.Id, a.Name)).ToListAsync();
            if (candidates.Count == 0)
            {
                Info("همه ویژگی‌ها به این کالا اختصاص داده شده‌اند.");
                return;
            }

            var dlg = new FieldEditorWindow("ویژگی تکمیلی کالا", new[]
            {
                FieldSpec.Choice_("ویژگی", candidates),
                FieldSpec.Text_("مقدار", "", false)
            }) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var attrId = dlg.GetChoice(0);
            if (attrId is null)
            {
                MessageBox.Show("انتخاب ویژگی الزامی است.", "اعتبارسنجی", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var value = dlg.GetText(1)?.Trim() ?? "";

            var now = DateTime.Now;

            // Auto-create the marked link on the main category (FK target for ProductAttrib)
            var autoLink = await db.CategoryAttributes
                .FirstOrDefaultAsync(c => !c.Deleted && c.CategoryId == product.MainCategoryId
                                          && c.AttributeId == attrId.Value
                                          && EF.Functions.Like(c.Description, AttribConsts.AutoProductLink + "%"));
            if (autoLink is null)
            {
                autoLink = new CategoryAttribute
                {
                    CategoryId = product.MainCategoryId,
                    AttributeId = attrId.Value,
                    Description = AttribConsts.AutoProductLink,
                    IsForced = false,
                    RecordUniqueId = Guid.NewGuid(),
                    CreateDateTime = now
                };
                db.CategoryAttributes.Add(autoLink);
                await db.SaveChangesAsync(); // need the Id for the value row
            }

            var existing = await db.ProductAttribs
                .FirstOrDefaultAsync(v => !v.Deleted && v.ProductId == _productId && v.CategoryAttribId == autoLink.Id);
            if (existing is null)
            {
                existing = new ProductAttrib
                {
                    ProductId = _productId,
                    CategoryAttribId = autoLink.Id,
                    AttribId = attrId.Value,
                    RecordUniqueId = Guid.NewGuid(),
                    CreateDateTime = now
                };
                db.ProductAttribs.Add(existing);
            }
            existing.Value = value;
            existing.UpdateDateTime = now;
            await db.SaveChangesAsync();
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void Info(string m) => MessageBox.Show(m, "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
}
