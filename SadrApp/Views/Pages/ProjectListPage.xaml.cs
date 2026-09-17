using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

/// <summary>Projects page: parent/child expandable tree of projects.</summary>
public partial class ProjectListPage : UserControl
{
    private List<ProjectNode> _flat = new();

    public ProjectListPage()
    {
        InitializeComponent();
        PageTitle.Text = "مدیریت پروژه‌ها";
        BtnNew.Click += (_, _) => ShowEditor(null, null);
        BtnAddChild.Click += (_, _) => AddChild();
        BtnEdit.Click += (_, _) => EditSelected();
        BtnTasks.Click += (_, _) => OpenTasks();
        BtnDelete.Click += (_, _) => Delete();
        BtnReload.Click += (_, _) => Load();
        Tree.MouseDoubleClick += (_, _) => EditSelected();
        Loaded += (_, _) => Load();
    }

    private void AddChild()
    {
        if (Tree.SelectedItem is not ProjectNode node) { Info("ابتدا یک پروژه را انتخاب کنید."); return; }
        ShowEditor(null, node.Id);
    }

    private void EditSelected()
    {
        if (Tree.SelectedItem is not ProjectNode node) { Info("ابتدا یک پروژه را انتخاب کنید."); return; }
        ShowEditor(node.Id, null);
    }

    /// <summary>Opens the tasks tab for the selected project.</summary>
    private void OpenTasks()
    {
        if (Tree.SelectedItem is not ProjectNode node) { Info("ابتدا یک پروژه را انتخاب کنید."); return; }
        if (Window.GetWindow(this) is MainWindow main) main.OpenTasksForProject(node.Id);
    }

    private async void Load()
    {
        try
        {
            await using var db = SadrDb.New();
            _flat = await db.Projects.Where(p => !p.Deleted)
                .Select(p => new ProjectNode
                {
                    Id = p.Id,
                    ParentId = p.ParentProjectId,
                    Name = p.Name,
                    Code = p.Code,
                    Manager = p.ManagerPerson != null ? p.ManagerPerson.FirstName + " " + p.ManagerPerson.LastName : "",
                    Progress = p.CurrentProgressPercentage,
                    Status = p.Status,
                    StartDate = p.StartDate,
                    Price = p.ProjectPrice
                })
                .OrderBy(p => p.Name)
                .ToListAsync();

            foreach (var n in _flat)
                n.ChildrenCount = _flat.Count(c => c.ParentId == n.Id);

            Tree.ItemsSource = BuildTree(_flat);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static List<ProjectNode> BuildTree(List<ProjectNode> flat)
    {
        var byId = flat.ToDictionary(n => n.Id);
        var idSet = flat.Select(n => n.Id).ToHashSet();
        var roots = new List<ProjectNode>();
        foreach (var n in flat)
        {
            if (n.ParentId is int pid && pid != n.Id && idSet.Contains(pid) && byId.TryGetValue(pid, out var parent))
                parent.Children.Add(n);
            else
                roots.Add(n);
        }
        return roots;
    }

    private async void ShowEditor(int? id, int? presetParentId)
    {
        try
        {
            await using var db = SadrDb.New();
            var catChoices = await db.ProjectCategories.Where(c => !c.Deleted)
                .Select(c => new KeyValuePair<int, string>(c.Id, c.Name)).ToListAsync();
            var curChoices = await db.Currencies.Where(c => !c.Deleted)
                .Select(c => new KeyValuePair<int, string>(c.Id, c.Name)).ToListAsync();
            var people = await db.People.Where(p => !p.Deleted)
                .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName }).ToListAsync();
            var parentList = await db.Projects.Where(p => !p.Deleted && p.Id != id)
                .Select(p => new { p.Id, p.Name }).ToListAsync();

            var e = id is null ? null : await db.Projects.FirstAsync(x => x.Id == id);
            var eEndDateG = (e != null && !string.IsNullOrWhiteSpace(e.EndDate)) ? e.EndDateG : (DateTime?)null;

            var statusChoices = new List<KeyValuePair<int, string>>
            {
                new(0, "تعریف شده"), new(1, "در حال اجرا"), new(2, "متوقف شده"), new(3, "لغو شده"), new(4, "خاتمه یافته")
            };
            var typeChoices = new List<KeyValuePair<int, string>>
            {
                new(0, "ساخت"), new(1, "تولیدی"), new(2, "خدماتی"), new(3, "پژوهشی"), new(4, "سایر")
            };
            var parentChoices = parentList.Select(p => new KeyValuePair<int, string>(p.Id, p.Name)).ToList();

            var dlg = new FieldEditorWindow(id is null ? "پروژه جدید" : "ویرایش پروژه", new[]
            {
                FieldSpec.Text_("نام پروژه", e?.Name, true),
                FieldSpec.Text_("کد پروژه", e?.Code),
                FieldSpec.Multi_("توضیحات", e?.Description),
                FieldSpec.Choice_("دسته پروژه", catChoices, e?.CategoryId),
                FieldSpec.Choice_("نوع پروژه", typeChoices, e?.ProjectType ?? 0),
                FieldSpec.Choice_("واحد پول", curChoices, e?.CurrencyId),
                FieldSpec.Choice_("پروژه والد", parentChoices, presetParentId ?? e?.ParentProjectId, true),
                FieldSpec.Date_("تاریخ شروع", e is null ? DateTime.Today : e.StartDateG, true),
                FieldSpec.Date_("تاریخ پایان", eEndDateG),
                FieldSpec.Numeric_("مبلغ پروژه", e?.ProjectPrice),
                FieldSpec.Numeric_("بودجه پایه", e?.BaseBudget),
                FieldSpec.Numeric_("درصد پیشرفت", e?.CurrentProgressPercentage),
                FieldSpec.Choice_("مدیر پروژه", people.Select(p => new KeyValuePair<int, string>(p.Id, p.Name)).ToList(), e?.ProjectManagerPersonId),
                FieldSpec.Choice_("کارشناس ثبت‌کننده", people.Select(p => new KeyValuePair<int, string>(p.Id, p.Name)).ToList(), e?.CreatorPersonId),
                FieldSpec.Choice_("وضعیت", statusChoices, e?.Status ?? 0)
            }) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            var newParentId = dlg.GetChoice(6) is > 0 ? dlg.GetChoice(6) : null;

            // جلوگیری از حلقه: والد جدید نباید از نوادگان همین پروژه باشد
            if (id is not null && newParentId is int pid)
            {
                var cursor = pid;
                var guard = 0;
                while (cursor != 0 && guard++ < 1000)
                {
                    if (cursor == id)
                    {
                        MessageBox.Show("والد انتخاب‌شده از زیرپروژه‌های همین پروژه است و باعث حلقه می‌شود.",
                            "انتخاب نامعتبر", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    var next = await db.Projects.AsNoTracking()
                        .Where(p => p.Id == cursor)
                        .Select(p => p.ParentProjectId)
                        .FirstOrDefaultAsync();
                    if (next is null or 0) break;
                    cursor = next.Value;
                }
            }

            var now = DateTime.Now;
            if (e is null)
            {
                e = new Project
                {
                    RecordUniqueId = Guid.NewGuid(),
                    GUID = Guid.NewGuid(),
                    CreateDateTime = now
                };
                db.Projects.Add(e);
            }
            e.Name = dlg.GetText(0)!.Trim();
            e.Code = dlg.GetText(1) ?? "";
            e.Description = dlg.GetText(2) ?? "";
            e.CategoryId = dlg.GetChoice(3) ?? 0;
            e.ProjectType = dlg.GetChoice(4) ?? 0;
            e.CurrencyId = dlg.GetChoice(5) ?? 0;
            e.ParentProjectId = newParentId == e.Id ? null : newParentId;
            e.StartDateG = dlg.GetDate(7) ?? DateTime.Today;
            e.StartDate = PersianDate.ToPersian(e.StartDateG);
            e.EndDateG = dlg.GetDate(8) ?? DateTime.Today;
            e.EndDate = dlg.GetDate(8) is null ? "" : PersianDate.ToPersian(e.EndDateG);
            e.ProjectPrice = dlg.GetNumber(9) ?? 0;
            e.BaseBudget = dlg.GetNumber(10) ?? 0;
            e.CurrentProgressPercentage = (int?)(dlg.GetNumber(11) ?? 0) ?? 0;
            e.ProjectManagerPersonId = dlg.GetChoice(12) ?? 0;
            e.CreatorPersonId = dlg.GetChoice(13) ?? 0;
            e.Status = dlg.GetChoice(14) ?? 0;
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
        if (Tree.SelectedItem is not ProjectNode node) { Info("ابتدا یک پروژه را انتخاب کنید."); return; }
        if (node.ChildrenCount > 0)
        {
            MessageBox.Show("این پروژه زیرپروژه دارد؛ ابتدا زیرپروژه‌ها را حذف یا منتقل کنید.",
                "حذف ممکن نیست", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show($"پروژه «{node.Name}» حذف شود؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var e = await db.Projects.FirstAsync(x => x.Id == node.Id);
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
