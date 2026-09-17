using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

/// <summary>
/// Task management page: tasks of one project (or all projects) with status filter,
/// full editor (dates, responsible person, budget, progress, prerequisites) and
/// progress/cost reporting.
/// </summary>
public partial class TaskListPage : UserControl
{
    private int? _fixedProjectId;

    public TaskListPage(int? fixedProjectId = null)
    {
        InitializeComponent();
        _fixedProjectId = fixedProjectId;
        PageTitle.Text = fixedProjectId is null ? "مدیریت وظایف پروژه‌ها" : "وظایف پروژه";
        ListCtl.NewClicked += (_, _) => ShowEditor(null);
        ListCtl.EditClicked += (_, _) => Edit();
        ListCtl.DeleteClicked += (_, _) => Delete();
        ListCtl.RefreshClicked += (_, _) => Load();
        ListCtl.RowDoubleClicked += (_, _) => Edit();
        ListCtl.ShowExtraColumn("مسئول");
        BtnReports.Click += (_, _) => ShowReports();
        CmbProject.SelectionChanged += (_, _) => Load();
        CmbStatus.SelectionChanged += (_, _) => Load();
        Loaded += (_, _) => { if (!_loaded) { _loaded = true; Init(); } };
    }

    private bool _loaded;

    private async void Init()
    {
        try
        {
            await using var db = SadrDb.New();
            var projects = await db.Projects.Where(p => !p.Deleted)
                .OrderBy(p => p.Name)
                .Select(p => new ComboItem { Key = p.Id, Text = p.Name })
                .ToListAsync();

            var items = new List<ComboItem> { new() { Key = null, Text = "— همه پروژه‌ها —" } };
            items.AddRange(projects);

            CmbProject.ItemsSource = items;
            CmbProject.DisplayMemberPath = "Text";
            CmbProject.SelectedValuePath = "Key";
            CmbProject.SelectedValue = _fixedProjectId;



            CmbStatus.ItemsSource = new List<ComboItem>
            {
                new() { Key = null, Text = "— همه وضعیت‌ها —" },
                new() { Key = (int)TaskStatusConsts.NotStarted, Text = TaskStatusConsts.Label(TaskStatusConsts.NotStarted) },
                new() { Key = (int)TaskStatusConsts.InProgress, Text = TaskStatusConsts.Label(TaskStatusConsts.InProgress) },
                new() { Key = (int)TaskStatusConsts.Paused, Text = TaskStatusConsts.Label(TaskStatusConsts.Paused) },
                new() { Key = (int)TaskStatusConsts.Done, Text = TaskStatusConsts.Label(TaskStatusConsts.Done) },
                new() { Key = (int)TaskStatusConsts.Failed, Text = TaskStatusConsts.Label(TaskStatusConsts.Failed) },
            };
            CmbStatus.DisplayMemberPath = "Text";
            CmbStatus.SelectedValuePath = "Key";
            CmbStatus.SelectedValue = null;

            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowReports()
    {
        if (ListCtl.SelectedRow is not TaskRow row) { Info("ابتدا یک وظیفه را انتخاب کنید."); return; }
        new TaskReportsWindow(row.Id) { Owner = Window.GetWindow(this) }.ShowDialog();
        Load(); // گزارش جدید ممکن است شمارنده را تغییر داده باشد
    }

    private async void Load()
    {
        try
        {
            await using var db = SadrDb.New();
            var projectId = CmbProject.SelectedValue as int?;
            var status = CmbStatus.SelectedValue as int?;

            var query = db.ProjectTasks.AsNoTracking()
                .Where(t => !t.Deleted);

            if (projectId is int pid) query = query.Where(t => t.ProjectId == pid);
            if (status is int st) query = query.Where(t => t.Status == st);

            var rows = await query
                .OrderByDescending(t => t.Id)
                .Select(t => new TaskRow
                {
                    Id = t.Id,
                    Name = t.Name,
                    ProjectName = t.Project != null ? t.Project.Name : "",
                    Responser = t.ResponcePerson != null ? t.ResponcePerson.FirstName + " " + t.ResponcePerson.LastName : "",
                    StartDate = t.StartDate,
                    DueDate = t.DueDate,
                    Progress = t.ProgressPersentage,
                    Status = t.Status,
                    Budget = t.StartBudget,
                    ReportCount = t.Reports.Count(r => !r.Deleted)
                })
                .ToListAsync();

            ListCtl.SetRows(rows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Edit()
    {
        if (ListCtl.SelectedRow is not TaskRow row) { Info("ابتدا یک وظیفه را انتخاب کنید."); return; }
        ShowEditor(row.Id);
    }

    private async void ShowEditor(int? id)
    {
        try
        {
            var projectId = CmbProject.SelectedValue as int? ?? _fixedProjectId;

            await using var db = SadrDb.New();
            var projects = await db.Projects.Where(p => !p.Deleted)
                .Select(p => new KeyValuePair<int, string>(p.Id, p.Name)).ToListAsync();
            var people = await db.People.Where(p => !p.Deleted)
                .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName }).ToListAsync();
            var peopleChoices = people.Select(p => new KeyValuePair<int, string>(p.Id, p.Name)).ToList();

            var e = id is null ? null : await db.ProjectTasks.AsNoTracking().FirstAsync(x => x.Id == id);
            if (e is not null) projectId = e.ProjectId;

            // نامِ پیش‌نیازها نمایش داده می‌شود؛ شناسه‌ها در بانک ذخیره می‌شوند
            var projectTasks = await db.ProjectTasks.AsNoTracking()
                .Where(t => !t.Deleted && t.Id != id)
                .Select(t => new { t.Id, t.Name, t.ProjectId })
                .ToListAsync();
            var tasksByProject = projectTasks
                .GroupBy(t => t.ProjectId)
                .ToDictionary(g => g.Key, g => g.Select(t => new KeyValuePair<int, string>(t.Id, t.Name)).ToList());

            var statusChoices = new List<KeyValuePair<int, string>>
            {
                new(TaskStatusConsts.NotStarted, TaskStatusConsts.Label(TaskStatusConsts.NotStarted)),
                new(TaskStatusConsts.InProgress, TaskStatusConsts.Label(TaskStatusConsts.InProgress)),
                new(TaskStatusConsts.Paused, TaskStatusConsts.Label(TaskStatusConsts.Paused)),
                new(TaskStatusConsts.Done, TaskStatusConsts.Label(TaskStatusConsts.Done)),
                new(TaskStatusConsts.Failed, TaskStatusConsts.Label(TaskStatusConsts.Failed)),
            };

            // پیش‌نیازها فقط از وظایف همان پروژه انتخاب می‌شوند و با نام نمایش داده می‌شوند

            var dlg = new FieldEditorWindow(id is null ? "وظیفه جدید" : "ویرایش وظیفه", new[]
            {
                FieldSpec.Choice_("پروژه", projects, projectId),
                FieldSpec.Text_("نام وظیفه", e?.Name, true),
                FieldSpec.Date_("تاریخ شروع", e?.StartDateG ?? DateTime.Today, true),
                FieldSpec.Date_("تاریخ سررسید", e?.DueDateG ?? DateTime.Today.AddDays(1), true),
                FieldSpec.Numeric_("تعداد روز کاری", e is null ? 1 : e.WorkingDays),
                FieldSpec.Choice_("مسئول انجام", peopleChoices, e?.ResponcePersonId),
                FieldSpec.Choice_("وضعیت", statusChoices, e?.Status ?? TaskStatusConsts.NotStarted),
                FieldSpec.Numeric_("درصد پیشرفت وظیفه", e?.ProgressPersentage),
                FieldSpec.Numeric_("سهم از پیشرفت پروژه ٪", e?.ProjectProgressPercentage),
                FieldSpec.MultiChoice_("وظایف پیش‌نیاز", tasksByProject, 0, e?.PreviousTasks,
                    initialMasterKey: e?.ProjectId ?? 0,
                    emptyMessage: "هیچ وظیفه پیش‌نیازی علامت نخورده است."),
                FieldSpec.Numeric_("بودجه اولیه", e?.StartBudget),
                FieldSpec.Numeric_("بودجه نهایی", e?.FinalBudget),
                FieldSpec.Multi_("توضیحات", e?.Description),
            }) { Owner = Window.GetWindow(this) };

            if (dlg.ShowDialog() != true) return;

            var newProjectId = dlg.GetChoice(0) ?? 0;
            if (newProjectId == 0)
            {
                MessageBox.Show("انتخاب پروژه برای وظیفه الزامی است.", "اعتبارسنجی", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if ((dlg.GetChoice(5) ?? 0) == 0)
            {
                MessageBox.Show("انتخاب مسئول انجام وظیفه الزامی است.", "اعتبارسنجی", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var status = dlg.GetChoice(6) ?? TaskStatusConsts.NotStarted;

            // امنیت در برابر تغییر پروژه: فقط وظایف همان پروژه ذخیره می‌شوند
            var sameProjectTaskIds = tasksByProject.TryGetValue(newProjectId, out var sameList)
                ? sameList.Select(kv => kv.Key).ToHashSet() : new HashSet<int>();
            var checkedIds = dlg.GetMultiChoice(9).Where(sameProjectTaskIds.Contains).ToList();
            var now = DateTime.Now;

            if (e is null)
            {
                e = new ProjectTask { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now };
                db.ProjectTasks.Add(e);
            }

            e.ProjectId = newProjectId;
            e.Name = dlg.GetText(1)!.Trim();
            e.StartDateG = dlg.GetDate(2) ?? DateTime.Today;
            e.StartDate = PersianDate.ToPersian(e.StartDateG);
            e.DueDateG = dlg.GetDate(3) ?? DateTime.Today;
            e.DueDate = PersianDate.ToPersian(e.DueDateG);
            e.WorkingDays = (int)(dlg.GetNumber(4) ?? 1);
            e.ResponcePersonId = dlg.GetChoice(5) ?? 0;
            e.Status = status;

            // وضعیت و تاریخ‌ها با هم هماهنگ می‌شوند
            if (status == TaskStatusConsts.InProgress && e.StartedDateG is null)
            {
                e.StartedDateG = now;
                e.StartedDate = PersianDate.ToPersian(now);
            }
            if (status == TaskStatusConsts.Done)
            {
                e.EndedDateG ??= now;
                e.EndedDate = PersianDate.ToPersian(e.EndedDateG.Value);
                if (string.IsNullOrWhiteSpace(e.DoneDescription))
                    e.DoneDescription = dlg.GetText(12);
            }
            if (status == TaskStatusConsts.Failed && e.EndedDateG is null)
            {
                e.EndedDateG = now;
                e.EndedDate = PersianDate.ToPersian(now);
                e.FailDescription ??= dlg.GetText(12);
            }

            e.ProgressPersentage = (int)(dlg.GetNumber(7) ?? 0);
            e.ProjectProgressPercentage = (int)(dlg.GetNumber(8) ?? 0);
            e.PreviousTasks = string.Join(",", checkedIds);
            e.StartBudget = dlg.GetNumber(10) ?? 0;
            e.FinalBudget = dlg.GetNumber(11);

            // «انجام شده» بدون درصد پیشرفت = ۱۰۰٪
            if ((status is TaskStatusConsts.Done or TaskStatusConsts.Failed)
                && dlg.GetNumber(7) is null && string.IsNullOrWhiteSpace(dlg.GetText(7)))
                e.ProgressPersentage = 100;

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
        if (ListCtl.SelectedRow is not TaskRow row) { Info("ابتدا یک وظیفه را انتخاب کنید."); return; }
        if (MessageBox.Show($"وظیفه «{row.Name}» حذف شود؟", "تأیید حذف",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await using var db = SadrDb.New();
            var e = await db.ProjectTasks.FirstAsync(x => x.Id == row.Id);
            e.Deleted = true;
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

/// <summary>Grid row for the tasks page.</summary>
public class TaskRow : RowBase
{
    public string Name { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string Responser { get; set; } = "";
    public string StartDate { get; set; } = "";
    public string DueDate { get; set; } = "";
    public int Progress { get; set; }
    public int Status { get; set; }
    public decimal Budget { get; set; }
    public int ReportCount { get; set; }

    public override string Title => Name ?? "";
    public override string Code => Id.ToString();
    public override string Description =>
        $"{TaskStatusConsts.Label(Status)} | پیشرفت {Progress}٪ | شروع {StartDate} | سررسید {DueDate} | بودجه {Budget:N0} | {ReportCount} گزارش";
    public override string Extra => Responser;
}
