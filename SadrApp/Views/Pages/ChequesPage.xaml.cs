using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

/// <summary>
/// Management page for the Cheques table: cheques received from customers (دریافتی) and
/// cheques issued to providers (پرداختی), with status tracking, quick status actions
/// (posting ledger events via ChequeLedger), and CRUD via the shared field editor.
/// </summary>
public partial class ChequesPage : UserControl
{
    public const int DirectionReceived = ChequeConsts.DirectionReceived;
    public const int DirectionIssued = ChequeConsts.DirectionIssued;

    public const int StatusInHand = ChequeConsts.StatusInHand;
    public const int StatusPassedToBank = ChequeConsts.StatusPassedToBank;
    public const int StatusCashed = ChequeConsts.StatusCashed;
    public const int StatusBounced = ChequeConsts.StatusBounced;
    public const int StatusSpent = ChequeConsts.StatusSpent;
    public const int StatusCanceled = ChequeConsts.StatusCanceled;

    public static Dictionary<int, string> DirectionNames => ChequeConsts.DirectionNames;
    public static Dictionary<int, string> StatusNames => ChequeConsts.StatusNames;

    public ChequesPage()
    {
        InitializeComponent();
        ListCtl.NewClicked += (_, _) => ShowEditor(null);
        ListCtl.EditClicked += (_, _) => Edit();
        ListCtl.DeleteClicked += (_, _) => Delete();
        ListCtl.RefreshClicked += (_, _) => Load();
        ListCtl.RowDoubleClicked += (_, _) => Edit();
        DirectionFilter.SelectionChanged += (_, _) => Load();
        StatusFilter.SelectionChanged += (_, _) => Load();
        Loaded += (_, _) => { FillFilters(); Load(); };
        ListCtl.InnerGrid.SelectionChanged += (_, _) => UpdateQuickActions();
        BtnMarkCashed.Click += (_, _) => MarkStatus(StatusCashed, ChequeLedger.EventCashed, "وصول شد");
        BtnMarkBounced.Click += (_, _) => MarkStatus(StatusBounced, ChequeLedger.EventBounced, "برگشت خورده");
        BtnMarkSpent.Click += (_, _) => MarkStatus(StatusSpent, ChequeLedger.EventSpent, "خرج شد");
    }

    private void FillFilters()
    {
        if (DirectionFilter.ItemsSource is null)
        {
            DirectionFilter.ItemsSource = new List<KeyValuePair<int, string>>
            {
                new(0, "— همه جهت‌ها —"),
                new(DirectionReceived, "دریافتی (از مشتری)"),
                new(DirectionIssued, "پرداختی (به تامین‌کننده)")
            };
            DirectionFilter.DisplayMemberPath = "Value";
            DirectionFilter.SelectedValuePath = "Key";
            DirectionFilter.SelectedValue = 0;
        }
        if (StatusFilter.ItemsSource is null)
        {
            var statuses = new List<KeyValuePair<int, string>> { new(0, "— همه وضعیت‌ها —") };
            statuses.AddRange(StatusNames.Select(kv => new KeyValuePair<int, string>(kv.Key, kv.Value)));
            StatusFilter.ItemsSource = statuses;
            StatusFilter.DisplayMemberPath = "Value";
            StatusFilter.SelectedValuePath = "Key";
            StatusFilter.SelectedValue = 0;
        }
    }

    private async void Load()
    {
        try
        {
            await using var db = SadrDb.New();

            var dir = DirectionFilter.SelectedValue as int? ?? 0;
            var status = StatusFilter.SelectedValue as int? ?? 0;

            var query = db.Cheques.Where(c => !c.Deleted);
            if (dir > 0) query = query.Where(c => c.Direction == dir);
            if (status > 0) query = query.Where(c => c.Status == status);

            var rows = await query
                .OrderBy(c => c.DateG)
                .Select(c => new
                {
                    c.Id, c.Amount, c.Number, c.Direction, c.Status, c.Date, c.DateG,
                    Person = c.PersonId != null ? c.Person.FirstName + " " + c.Person.LastName : "",
                    c.BankAccountId, c.RecieverFullName, c.Describtion
                })
                .ToListAsync();

            var accountNames = await db.BankAccounts.Where(a => !a.Deleted)
                .Select(a => new { a.Id, a.Name }).ToDictionaryAsync(a => a.Id, a => a.Name);

            // Fixed positions: 0=جهت 1=وضعیت 2=حساب 3=گیرنده 4=شخص 5=سررسید
            ListCtl.SetRows(rows.Select(c => new RowBase
            {
                Id = c.Id,
                Title = c.Amount.ToString("N0") + " - " + DirectionNames.GetValueOrDefault(c.Direction, ""),
                Code = c.Number,
                Extra = string.Join(" | ",
                    DirectionNames.GetValueOrDefault(c.Direction, ""),
                    StatusNames.GetValueOrDefault(c.Status, ""),
                    accountNames.TryGetValue(c.BankAccountId, out var bn) ? bn : "حساب #" + c.BankAccountId,
                    c.RecieverFullName,
                    c.Person,
                    c.Date),
                Description = c.Describtion
            }).ToList());
            ListCtl.ShowExtraColumn("جهت | وضعیت | حساب بانکی | گیرنده/پرداخت‌کننده | شخص | سررسید");
            UpdateQuickActions();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateQuickActions()
    {
        if (ListCtl.SelectedRow is not RowBase row || row.Id <= 0)
        {
            HideQuickActions();
            return;
        }
        var seg = row.Extra.Split(" | ");
        var dirName = seg.ElementAtOrDefault(0);
        var statusName = seg.ElementAtOrDefault(1);
        var isReceived = dirName == DirectionNames[DirectionReceived];
        var isIssued = dirName == DirectionNames[DirectionIssued];

        // وصول always applies until done; برگشت only for received; خرج only for issued.
        BtnMarkCashed.Visibility = statusName != StatusNames[StatusCashed] ? Visibility.Visible : Visibility.Collapsed;
        BtnMarkBounced.Visibility = isReceived && statusName != StatusNames[StatusBounced] ? Visibility.Visible : Visibility.Collapsed;
        BtnMarkSpent.Visibility = isIssued && statusName != StatusNames[StatusSpent] ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideQuickActions()
    {
        foreach (var b in new[] { BtnMarkCashed, BtnMarkBounced, BtnMarkSpent })
            b.Visibility = Visibility.Collapsed;
    }

    /// <summary>Quick status change from the floating action bar; posts ledger events accordingly.</summary>
    private async void MarkStatus(int newStatus, string eventKind, string eventTitle)
    {
        if (ListCtl.SelectedRow is not RowBase row || row.Id <= 0) return;
        if (MessageBox.Show($"وضعیت چک «{row.Code}» به «{eventTitle}» تغییر کند؟",
                "تغییر وضعیت چک", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var c = await db.Cheques.FirstAsync(x => x.Id == row.Id);
            c.Status = newStatus;
            c.UpdateDateTime = DateTime.Now;
            await ChequeLedger.SyncChequeAsync(db, c, eventKind, DateTime.Now);
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در تغییر وضعیت", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Edit()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک چک را انتخاب کنید."); return; }
        ShowEditor(row.Id);
    }

    private async void ShowEditor(int? id)
    {
        try
        {
            await using var db = SadrDb.New();

            var people = await db.People.Where(p => !p.Deleted)
                .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName })
                .OrderBy(p => p.Name).ToListAsync();
            if (people.Count == 0) { Info("ابتدا اشخاص را در صفحه «اشخاص» تعریف کنید."); return; }
            var personChoices = people.Select(p => new KeyValuePair<int, string>(p.Id, p.Name)).ToList();
            personChoices.Insert(0, new KeyValuePair<int, string>(0, "— انتخاب نشده —"));

            var accounts = await db.BankAccounts.Where(a => !a.Deleted)
                .Select(a => new { a.Id, a.Name }).OrderBy(a => a.Name).ToListAsync();
            if (accounts.Count == 0) { Info("ابتدا در صفحه «حساب‌های بانکی» یک حساب تعریف کنید."); return; }
            var accountChoices = accounts.Select(a => new KeyValuePair<int, string>(a.Id, a.Name)).ToList();

            var invoices = await db.Invoices.Where(i => !i.Deleted)
                .Select(i => new { i.Id, i.InvoiceNumber }).OrderByDescending(i => i.Id).Take(300).ToListAsync();
            var invoiceChoices = invoices.Select(i => new KeyValuePair<int, string>(i.Id, "فاکتور " + i.InvoiceNumber)).ToList();
            invoiceChoices.Insert(0, new KeyValuePair<int, string>(0, "— بدون فاکتور —"));

            var dirChoices = new List<KeyValuePair<int, string>>
            {
                new(DirectionReceived, "دریافتی (از مشتری)"),
                new(DirectionIssued, "پرداختی (به تامین‌کننده)")
            };
            var statusChoices = StatusNames.Select(kv => new KeyValuePair<int, string>(kv.Key, kv.Value)).ToList();

            var e = id is null ? null : await db.Cheques.FirstAsync(x => x.Id == id);

            var dlg = new FieldEditorWindow(id is null ? "چک جدید" : "ویرایش چک", new[]
            {
                FieldSpec.Choice_("نوع چک", dirChoices, e?.Direction ?? DirectionReceived),
                FieldSpec.Numeric_("مبلغ چک", e?.Amount, true),
                FieldSpec.Text_("شماره چک", e?.Number),
                FieldSpec.Text_("سریال چک", e?.SerialNumber),
                FieldSpec.Text_("گیرنده/پرداخت‌کننده", string.IsNullOrEmpty(e?.RecieverFullName) ? null : e.RecieverFullName, true),
                FieldSpec.Text_("کد طرف حساب", string.IsNullOrEmpty(e?.RecieverCode) ? null : e.RecieverCode),
                FieldSpec.Date_("تاریخ سررسید", e?.DateG, true),
                FieldSpec.Choice_("شخص مرتبط", personChoices, e?.PersonId ?? 0),
                FieldSpec.Choice_("حساب بانکی ما", accountChoices, e?.BankAccountId ?? accountChoices[0].Key),
                FieldSpec.Choice_("فاکتور مرتبط", invoiceChoices, e?.InvoiceId ?? 0),
                FieldSpec.Choice_("وضعیت چک", statusChoices, e?.Status ?? StatusInHand),
                FieldSpec.Multi_("توضیحات", string.IsNullOrEmpty(e?.Describtion) ? null : e.Describtion)
            }) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            var now = DateTime.Now;
            var statusChanged = e is not null && e.Status != (dlg.GetChoice(10) ?? e.Status);
            if (e is null)
            {
                e = new Cheque { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now, Direction = DirectionReceived };
                db.Cheques.Add(e);
            }
            e.Direction = dlg.GetChoice(0) ?? DirectionReceived;
            e.Amount = dlg.GetNumber(1) ?? 0;
            e.Number = dlg.GetText(2)?.Trim() ?? "";
            e.SerialNumber = dlg.GetText(3)?.Trim() ?? "";
            e.RecieverFullName = dlg.GetText(4)?.Trim() ?? "";
            // Default the receiver name from the linked person when left empty.
            if (e.RecieverFullName.Length == 0 && dlg.GetChoice(7) is > 0)
                e.RecieverFullName = personChoices.First(p => p.Key == dlg.GetChoice(7)).Value;
            e.RecieverCode = dlg.GetText(5)?.Trim() ?? "";
            e.DateG = dlg.GetDate(6) ?? DateTime.Today;
            e.Date = PersianDate.ToPersian(e.DateG);
            e.PersonId = dlg.GetChoice(7) is > 0 ? dlg.GetChoice(7) : null;
            e.BankAccountId = dlg.GetChoice(8) ?? accountChoices[0].Key;
            e.InvoiceId = dlg.GetChoice(9) is > 0 ? dlg.GetChoice(9) : null;
            e.Status = dlg.GetChoice(10) ?? StatusInHand;
            e.Describtion = dlg.GetText(11) ?? "";
            e.UpdateDateTime = now;
            await db.SaveChangesAsync();

            // Keep the ledger in sync when the status was changed through the editor.
            if (statusChanged)
            {
                var kind = e.Status == StatusCashed ? ChequeLedger.EventCashed
                         : e.Status == StatusBounced ? ChequeLedger.EventBounced
                         : e.Status == StatusSpent ? ChequeLedger.EventSpent : null;
                if (kind is not null)
                    await ChequeLedger.SyncChequeAsync(db, e, kind, now);
            }
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Delete()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک چک را حذف کنید."); return; }
        if (MessageBox.Show($"چک شماره «{row.Code}» به مبلغ {row.Title.Split(" - ")[0]} حذف شود؟",
                "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var e = await db.Cheques.FirstAsync(x => x.Id == row.Id);
            e.Deleted = true;
            e.UpdateDateTime = DateTime.Now;
            // Removing a posted cheque must also remove its money effect.
            var posted = await db.BankTransactions
                .Where(t => t.ChequeId == e.Id && !t.Deleted).ToListAsync();
            foreach (var t in posted) { t.Deleted = true; t.UpdateDateTime = DateTime.Now; }
            await db.SaveChangesAsync();
            await ChequeLedger.RecalculateBalanceAsync(db, e.BankAccountId, DateTime.Now);
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void Info(string m) => MessageBox.Show(m, "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
}
