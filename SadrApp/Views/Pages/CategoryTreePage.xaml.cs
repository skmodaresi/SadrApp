using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

/// <summary>Nested category management for both ProductCategories and ProjectCategories.</summary>
public partial class CategoryTreePage : UserControl
{
    private readonly bool _isProduct;
    private List<CategoryNode> _flat = new();

    public CategoryTreePage(bool isProduct, string title)
    {
        InitializeComponent();
        _isProduct = isProduct;
        PageTitle.Text = title;
        TreeCtl.AddRootClicked += (_, _) => Add(null);
        TreeCtl.AddChildClicked += (_, _) => Add(TreeCtl.SelectedNode);
        TreeCtl.RenameClicked += (_, _) => Rename();
        TreeCtl.DeleteClicked += (_, _) => Delete();
        TreeCtl.ReloadClicked += (_, _) => Load();
        TreeCtl.ShowAttribsButton(_isProduct); // ویژگی فقط برای دسته کالا معنا دارد
        TreeCtl.AttribsClicked += (_, _) => ManageAttribs();
        Loaded += (_, _) => Load();
    }

    private void ManageAttribs()
    {
        var node = TreeCtl.SelectedNode;
        if (node is null) { MessageBox.Show("ابتدا یک دسته را انتخاب کنید.", "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        new CategoryAttributesDialog(node.Id, node.Name) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private async void Load()
    {
        try
        {
            await using var db = SadrDb.New();
            List<CategoryNode> flat;
            if (_isProduct)
                flat = await db.ProductCategories.Where(c => !c.Deleted)
                    .Select(c => new CategoryNode { Id = c.Id, ParentId = c.ParentId, Name = c.Name, Code = c.Code ?? "", Description = c.Description ?? "" })
                    .OrderBy(c => c.Name).ToListAsync();
            else
                flat = await db.ProjectCategories.Where(c => !c.Deleted)
                    .Select(c => new CategoryNode { Id = c.Id, ParentId = c.ParentId, Name = c.Name, Code = c.Code ?? "", Description = c.Description ?? "" })
                    .OrderBy(c => c.Name).ToListAsync();
            _flat = flat;
            TreeCtl.SetRoots(BuildTree(flat));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static List<CategoryNode> BuildTree(List<CategoryNode> flat)
    {
        var byId = flat.ToDictionary(c => c.Id);
        var roots = new List<CategoryNode>();
        foreach (var node in flat)
        {
            if (node.ParentId is int pid && byId.TryGetValue(pid, out var parent) && !ReferenceEquals(parent, node))
                parent.Children.Add(node);
            else
                roots.Add(node);
        }
        return roots;
    }

    private async void Add(CategoryNode? parent)
    {
        var dlg = new FieldEditorWindow(parent is null ? "افزودن دسته اصلی" : "افزودن زیردسته", new[]
        {
            FieldSpec.Text_("نام دسته", "", true),
            FieldSpec.Text_("کد"),
            FieldSpec.Multi_("توضیحات")
        }) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        try
        {
            await using var db = SadrDb.New();
            var now = DateTime.Now;
            if (_isProduct)
            {
                db.ProductCategories.Add(new ProductCategory
                {
                    Name = dlg.GetText(0)!.Trim(),
                    Code = dlg.GetText(1),
                    Description = dlg.GetText(2),
                    ParentId = parent?.Id,
                    RecordUniqueId = Guid.NewGuid(),
                    CreateDateTime = now,
                    UpdateDateTime = now,
                    Deleted = false
                });
            }
            else
            {
                db.ProjectCategories.Add(new ProjectCategory
                {
                    Name = dlg.GetText(0)!.Trim(),
                    Code = dlg.GetText(1),
                    Description = dlg.GetText(2),
                    ParentId = parent?.Id,
                    RecordUniqueId = Guid.NewGuid(),
                    CreateDateTime = now,
                    UpdateDateTime = now,
                    Deleted = false
                });
            }
            await db.SaveChangesAsync();
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Rename()
    {
        var node = TreeCtl.SelectedNode;
        if (node is null) { MessageBox.Show("ابتدا یک دسته را انتخاب کنید.", "اطلاع"); return; }

        var dlg = new FieldEditorWindow("ویرایش دسته", new[]
        {
            FieldSpec.Text_("نام دسته", node.Name, true),
            FieldSpec.Text_("کد", node.Code),
            FieldSpec.Multi_("توضیحات", node.Description)
        }) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        try
        {
            await using var db = SadrDb.New();
            var now = DateTime.Now;
            if (_isProduct)
            {
                var e = await db.ProductCategories.FirstAsync(c => c.Id == node.Id);
                e.Name = dlg.GetText(0)!.Trim(); e.Code = dlg.GetText(1); e.Description = dlg.GetText(2);
                e.UpdateDateTime = now;
            }
            else
            {
                var e = await db.ProjectCategories.FirstAsync(c => c.Id == node.Id);
                e.Name = dlg.GetText(0)!.Trim(); e.Code = dlg.GetText(1); e.Description = dlg.GetText(2);
                e.UpdateDateTime = now;
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
        var node = TreeCtl.SelectedNode;
        if (node is null) { MessageBox.Show("ابتدا یک دسته را انتخاب کنید.", "اطلاع"); return; }

        var descendants = new List<int>();
        CollectChildren(node.Id, descendants);
        if (descendants.Count > 0)
        {
            MessageBox.Show("این دسته زیرشاخه دارد. ابتدا زیرشاخه‌ها را حذف کنید.", "حذف ممکن نیست", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var used = await IsUsed(node.Id);
        if (used)
        {
            MessageBox.Show("این دسته در آیتم‌ها استفاده شده و حذف آن مجاز نیست.", "حذف ممکن نیست", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"«{node.Name}» حذف شود؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            await using var db = SadrDb.New();
            var now = DateTime.Now;
            if (_isProduct)
            {
                var e = await db.ProductCategories.FirstAsync(c => c.Id == node.Id);
                e.Deleted = true; e.UpdateDateTime = now;
            }
            else
            {
                var e = await db.ProjectCategories.FirstAsync(c => c.Id == node.Id);
                e.Deleted = true; e.UpdateDateTime = now;
            }
            await db.SaveChangesAsync();
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CollectChildren(int id, List<int> into)
    {
        foreach (var c in _flat.Where(x => x.ParentId == id))
        {
            into.Add(c.Id);
            CollectChildren(c.Id, into);
        }
    }

    private async Task<bool> IsUsed(int id) =>
        _isProduct
            ? await IsUsedProduct(id)
            : await IsUsedProject(id);

    private async Task<bool> IsUsedProduct(int id)
    {
        await using var db = SadrDb.New();
        return await db.Products.AnyAsync(p => !p.Deleted && p.MainCategoryId == id)
            || await db.ProductExtraCategories.AnyAsync(p => !p.Deleted && p.CategoryId == id);
    }

    private async Task<bool> IsUsedProject(int id)
    {
        await using var db = SadrDb.New();
        return await db.Projects.AnyAsync(p => !p.Deleted && p.CategoryId == id);
    }
}
