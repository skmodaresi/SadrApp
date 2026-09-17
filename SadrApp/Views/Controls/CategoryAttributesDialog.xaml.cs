using System.Windows;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.ViewModels;

namespace SadrApp.Views.Controls;

/// <summary>
/// Links attribute definitions to one product category (CategoryAttributes rows).
/// Just a link + IsForced flag — no values here.
/// </summary>
public partial class CategoryAttributesDialog : Window
{
    private readonly int _categoryId;

    public CategoryAttributesDialog(int categoryId, string categoryName)
    {
        InitializeComponent();
        _categoryId = categoryId;
        HeaderTitle.Text = $"ویژگی‌های دسته «{categoryName}»";
        Title = $"ویژگی‌های دسته «{categoryName}»";
        BtnNew.Click += (_, _) => NewOrEdit(null);
        BtnEdit.Click += (_, _) => { if (Grid.SelectedItem is RowBase r) NewOrEdit(r.Id); };
        BtnDelete.Click += (_, _) => DeleteRow();
        Grid.MouseDoubleClick += (_, _) => { if (Grid.SelectedItem is RowBase r) NewOrEdit(r.Id); };
        Loaded += (_, _) => Reload();
    }

    private async void Reload()
    {
        try
        {
            await using var db = SadrDb.New();
            // Auto-created product-level links are hidden here — they are managed on the product.
            var rows = await db.CategoryAttributes
                .Where(c => !c.Deleted && c.CategoryId == _categoryId
                            && !EF.Functions.Like(c.Description, AttribConsts.AutoProductLink + "%"))
                .Select(c => new RowBase
                {
                    Id = c.Id,
                    Title = c.Attribute!.Name,
                    Code = c.AttributeId.ToString(),
                    Description = c.Description
                }).ToListAsync();

            // Fill the Forced column after projection (can't compute bool text in SQL)
            var forced = await db.CategoryAttributes
                .Where(c => !c.Deleted && c.CategoryId == _categoryId
                            && !EF.Functions.Like(c.Description, AttribConsts.AutoProductLink + "%"))
                .Select(c => new { c.Id, c.IsForced }).ToListAsync();
            var forcedMap = forced.ToDictionary(x => x.Id, x => x.IsForced);
            foreach (var r in rows)
                r.Extra = forcedMap.TryGetValue(r.Id, out var f) && f ? "بله" : "خیر";

            Grid.ItemsSource = rows;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void NewOrEdit(int? id)
    {
        try
        {
            await using var db = SadrDb.New();
            var existingAttrIds = await db.CategoryAttributes
                .Where(c => !c.Deleted && c.CategoryId == _categoryId && c.Id != id
                            && !EF.Functions.Like(c.Description, AttribConsts.AutoProductLink + "%"))
                .Select(c => c.AttributeId).ToListAsync();

            // Only offer attributes not yet linked to this category
            var candidates = await db.Attributes.Where(a => !a.Deleted && !existingAttrIds.Contains(a.Id))
                .Select(a => new KeyValuePair<int, string>(a.Id, a.Name)).ToListAsync();
            if (candidates.Count == 0 && id is null)
            {
                MessageBox.Show("همه ویژگی‌ها به این دسته متصل هستند. ابتدا ویژگی جدید تعریف کنید.",
                    "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var e = id is null ? null : await db.CategoryAttributes.FirstAsync(x => x.Id == id);
            var currentAttrId = e?.AttributeId;

            var dlg = new FieldEditorWindow(id is null ? "ویژگی جدید برای دسته" : "ویرایش ویژگی دسته", new[]
            {
                FieldSpec.Choice_("ویژگی", candidates, currentAttrId),
                FieldSpec.Check_("الزامی (برای کالاهای این دسته)", e?.IsForced ?? false),
                FieldSpec.Multi_("توضیحات", e?.Description)
            }) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var attrId = dlg.GetChoice(0);
            if (attrId is null)
            {
                MessageBox.Show("انتخاب ویژگی الزامی است.", "اعتبارسنجی", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var now = DateTime.Now;
            if (e is null)
            {
                e = new CategoryAttribute
                {
                    CategoryId = _categoryId,
                    RecordUniqueId = Guid.NewGuid(),
                    CreateDateTime = now
                };
                db.CategoryAttributes.Add(e);
            }
            e.AttributeId = attrId.Value;
            e.IsForced = dlg.GetCheck(1);
            e.Description = dlg.GetText(2) ?? "";
            e.UpdateDateTime = now;
            await db.SaveChangesAsync();
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteRow()
    {
        if (Grid.SelectedItem is not RowBase row) { Info("ابتدا یک ویژگی را انتخاب کنید."); return; }
        if (MessageBox.Show($"ویژگی «{row.Title}» از این دسته حذف شود؟", "تأیید حذف",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var usedByProducts = await db.ProductAttribs.AnyAsync(p => !p.Deleted && p.CategoryAttribId == row.Id);
            if (usedByProducts)
            {
                MessageBox.Show("این ویژگی روی کالاها مقداردهی شده است و قابل حذف نیست.",
                    "حذف ممکن نیست", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var e = await db.CategoryAttributes.FirstAsync(x => x.Id == row.Id);
            e.Deleted = true;
            e.UpdateDateTime = DateTime.Now;
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
