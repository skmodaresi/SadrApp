using System.Windows;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.Views.Pages;

namespace SadrApp.Views.Controls;

/// <summary>Progress/cost reports logged against a project task.</summary>
public partial class TaskReportsWindow : Window
{
    private readonly int _taskId;

    public TaskReportsWindow(int taskId)
    {
        InitializeComponent();
        _taskId = taskId;
        BtnNew.Click += (_, _) => ShowEditor(null);
        BtnEdit.Click += (_, _) => Edit();
        BtnDelete.Click += (_, _) => Delete();
        Loaded += (_, _) => { LoadHeader(); Load(); };
    }

    private async void LoadHeader()
    {
        try
        {
            await using var db = SadrDb.New();
            var t = await db.ProjectTasks.AsNoTracking()
                .Where(x => x.Id == _taskId)
                .Select(x => new { x.Name, x.StartDate, x.DueDate, x.ProgressPersentage, Project = x.Project != null ? x.Project.Name : "" })
                .FirstOrDefaultAsync();
            if (t is null) return;
            TaskTitle.Text = $"📋 {t.Name}";
            TaskMeta.Text = $"پروژه: {t.Project} | شروع: {t.StartDate} | سررسید: {t.DueDate} | پیشرفت: {t.ProgressPersentage}٪";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Load()
    {
        try
        {
            await using var db = SadrDb.New();
            var people = await db.People.AsNoTracking()
                .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName })
                .ToDictionaryAsync(p => p.Id, p => p.Name);

            var rows = await db.TaskReports.AsNoTracking()
                .Where(r => !r.Deleted && r.TaskId == _taskId)
                .OrderByDescending(r => r.Id)
                .Select(r => new { r.Id, r.ReportDate, r.Description, r.ReporterUserId, r.Cost })
                .ToListAsync();

            Grid.ItemsSource = rows.Select(r => new ReportRow
            {
                Id = r.Id,
                DateText = r.ReportDate,
                Description = r.Description,
                Cost = r.Cost,
                Reporter = r.ReporterUserId == 0 ? "—" :
                    (people.TryGetValue(r.ReporterUserId, out var n) ? n : $"#{r.ReporterUserId}")
            }).ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Edit()
    {
        if (Grid.SelectedItem is not ReportRow row) { Info("ابتدا یک گزارش را انتخاب کنید."); return; }
        ShowEditor(row.Id);
    }

    private async void ShowEditor(int? id)
    {
        try
        {
            await using var db = SadrDb.New();
            var people = await db.People.Where(p => !p.Deleted)
                .Select(p => new KeyValuePair<int, string>(p.Id, p.FirstName + " " + p.LastName)).ToListAsync();

            var e = id is null ? null : await db.TaskReports.FirstAsync(x => x.Id == id);

            // پیش‌فرض گزارش‌دهنده: شخص متصل به کاربر جاری
            var defaultReporter = e?.ReporterUserId;
            if (defaultReporter is null or 0)
            {
                defaultReporter = await db.People
                    .Where(p => !p.Deleted && p.LoginUserId == UserSession.CurrentUserId)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync();
            }

            var dlg = new FieldEditorWindow(id is null ? "گزارش جدید" : "ویرایش گزارش", new[]
            {
                FieldSpec.Date_("تاریخ گزارش", e?.CreateDateTime ?? DateTime.Now, true),
                FieldSpec.Choice_("گزارش‌دهنده", people, defaultReporter),
                FieldSpec.Numeric_("هزینه این گزارش (ريال)", e?.Cost),
                FieldSpec.Multi_("شرح گزارش", e?.Description, required: true),
            }) { Owner = this };

            if (dlg.ShowDialog() != true) return;

            var now = DateTime.Now;
            if (e is null)
            {
                e = new TaskReport { RecordUniqueId = Guid.NewGuid(), TaskId = _taskId, CreateDateTime = now };
                db.TaskReports.Add(e);
            }
            var d = dlg.GetDate(0) ?? now;
            e.ReportDate = PersianDate.ToPersian(d);
            e.ReporterUserId = dlg.GetChoice(1) ?? 0;
            e.Cost = dlg.GetNumber(2) ?? 0;
            e.Description = dlg.GetText(3) ?? "";
            // FreeTaskId has a NOT NULL + FK but is unused for project-task reports; FK disabled at startup.
            e.FreeTaskId = 0;

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
        if (Grid.SelectedItem is not ReportRow row) { Info("ابتدا یک گزارش را انتخاب کنید."); return; }
        if (MessageBox.Show("این گزارش حذف شود؟", "تأیید حذف",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await using var db = SadrDb.New();
            var e = await db.TaskReports.FirstAsync(x => x.Id == row.Id);
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

public class ReportRow
{
    public int Id { get; set; }
    public string DateText { get; set; } = "";
    public string Reporter { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Cost { get; set; }
}
