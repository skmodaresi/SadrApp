using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

/// <summary>
/// Warehouse transfers: manual in/out registrations plus the transfers auto-generated
/// from buy/sell invoices. Rows start pending and only a warehouse keeper (or admin)
/// can accept them with the real physical transfer date, which may differ from the
/// invoice date. Stock = product initial quantity + accepted transfers only.
/// </summary>
public partial class WarehouseTransfersPage : UserControl
{
    public WarehouseTransfersPage()
    {
        InitializeComponent();
        ListCtl.NewClicked += (_, _) => ShowEditor(null);
        ListCtl.EditClicked += (_, _) => Edit();
        ListCtl.DeleteClicked += (_, _) => Delete();
        ListCtl.RefreshClicked += (_, _) => Load();
        ListCtl.RowDoubleClicked += (_, _) => Edit();
        DirectionFilter.SelectionChanged += (_, _) => Load();
        StatusFilter.SelectionChanged += (_, _) => Load();
        BtnAccept.Click += (_, _) => Accept(true);
        BtnUnaccept.Click += (_, _) => Accept(false);
        Loaded += (_, _) =>
        {
            if (DirectionFilter.Items.Count == 0)
            {
                DirectionFilter.ItemsSource = new[]
                {
                    new { Key = 0, Text = "— همه —" },
                    new { Key = 1, Text = "ورود" },
                    new { Key = 2, Text = "خروج" }
                };
                DirectionFilter.DisplayMemberPath = "Text";
                DirectionFilter.SelectedValuePath = "Key";
                DirectionFilter.SelectedValue = 0;
            }
            if (StatusFilter.Items.Count == 0)
            {
                StatusFilter.ItemsSource = new[]
                {
                    new { Key = 0, Text = "— همه —" },
                    new { Key = 1, Text = "در انتظار تأیید" },
                    new { Key = 2, Text = "تأیید شده" }
                };
                StatusFilter.DisplayMemberPath = "Text";
                StatusFilter.SelectedValuePath = "Key";
                StatusFilter.SelectedValue = 0;
            }
            Load();
        };
    }

    private async void Load()
    {
        try
        {
            await using var db = SadrDb.New();
            var dir = DirectionFilter.SelectedValue as int? ?? 0;
            var st = StatusFilter.SelectedValue as int? ?? 0;

            var productNames = await db.Products.Where(p => !p.Deleted)
                .Select(p => new { p.Id, p.Name }).ToDictionaryAsync(p => p.Id, p => p.Name);
            var warehouseNames = await db.WareHouses.Where(w => !w.Deleted)
                .Select(w => new { w.Id, w.Name }).ToDictionaryAsync(w => w.Id, w => w.Name);
            var invoiceNumbers = await db.Invoices
                .Select(i => new { i.Id, i.InvoiceNumber }).ToDictionaryAsync(i => i.Id, i => i.InvoiceNumber);
            var personNames = await db.People.Where(p => !p.Deleted)
                .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName }).ToDictionaryAsync(p => p.Id, p => p.Name);

            var query = db.WarehouseTransfers.Where(t => !t.Deleted);
            if (dir > 0) query = query.Where(t => t.Direction == dir);
            if (st == 1) query = query.Where(t => !t.Accepted);
            if (st == 2) query = query.Where(t => t.Accepted);

            var rows = await query.OrderByDescending(t => t.TransferDateG).ThenByDescending(t => t.Id)
                .Select(t => new
                {
                    t.Id, t.ProductId, t.WareHouseId, t.Direction, t.Quantity,
                    t.TransferDateG, t.Accepted, t.InvoiceId, t.PersonId, t.Describtion
                })
                .ToListAsync();

            ListCtl.SetRows(rows.Select(t => new RowBase
            {
                Id = t.Id,
                Title = (productNames.TryGetValue(t.ProductId, out var pn) ? pn : "کالای #" + t.ProductId)
                        + " — " + (t.Direction == WarehouseTransfer.DirectionIn ? "ورود " : "خروج ")
                        + t.Quantity.ToString("N0"),
                Code = t.TransferDateG.ToString("yyyy/MM/dd"),
                Extra = string.Join(" | ",
                    t.Direction == WarehouseTransfer.DirectionIn ? "ورود" : "خروج",
                    t.Accepted ? "تأیید شده" : "در انتظار تأیید",
                    t.WareHouseId is int wid && warehouseNames.TryGetValue(wid, out var wn) ? wn : "",
                    t.InvoiceId is int iid && invoiceNumbers.TryGetValue(iid, out var num) ? "فاکتور " + num : "بدون فاکتور",
                    t.PersonId is int pid && personNames.TryGetValue(pid, out var pnm) ? pnm : "")
                    .Trim(" |".ToCharArray()),
                Description = t.Describtion
            }).ToList());
            ListCtl.ShowExtraColumn("جهت | وضعیت | انبار | فاکتور | طرف حساب");

            var isKeeper = UserSession.CanAcceptTransfers;
            BtnAccept.IsEnabled = isKeeper;
            BtnUnaccept.IsEnabled = isKeeper;
            TxtRoleHint.Text = isKeeper
                ? ""
                : "ثبت برای همه آزاد است؛ تأیید انجام حواله فقط توسط انباردار/مدیر انجام می‌شود.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Edit()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک حواله را انتخاب کنید."); return; }
        ShowEditor(row.Id);
    }

    private async void ShowEditor(int? id)
    {
        try
        {
            await using var db = SadrDb.New();
            var productChoices = await db.Products.Where(p => !p.Deleted)
                .Select(p => new KeyValuePair<int, string>(p.Id, p.Name)).ToListAsync();
            if (productChoices.Count == 0) { Info("ابتدا در صفحه «کالاها» یک کالا تعریف کنید."); return; }

            var warehouseChoices = await db.WareHouses.Where(w => !w.Deleted)
                .Select(w => new KeyValuePair<int, string>(w.Id, w.Name)).ToListAsync();
            warehouseChoices.Insert(0, new KeyValuePair<int, string>(0, "— انتخاب نشده —"));

            var personChoices = await db.People.Where(p => !p.Deleted)
                .Select(p => new KeyValuePair<int, string>(p.Id, p.FirstName + " " + p.LastName)).ToListAsync();
            personChoices.Insert(0, new KeyValuePair<int, string>(0, "— انتخاب نشده —"));

            var directionChoices = new List<KeyValuePair<int, string>>
            {
                new(WarehouseTransfer.DirectionIn, "ورود (به انبار)"),
                new(WarehouseTransfer.DirectionOut, "خروج (از انبار)")
            };

            var e = id is null ? null : await db.WarehouseTransfers.FirstAsync(x => x.Id == id);
            var wasAccepted = e?.Accepted ?? false;

            var dlg = new FieldEditorWindow(id is null ? "حواله انبار جدید" : "ویرایش حواله انبار", new[]
            {
                FieldSpec.Choice_("نوع حواله", directionChoices, e?.Direction ?? WarehouseTransfer.DirectionIn),
                FieldSpec.Choice_("کالا", productChoices, e?.ProductId ?? productChoices[0].Key),
                FieldSpec.Numeric_("تعداد", e?.Quantity),
                FieldSpec.Date_("تاریخ انجام واقعی حواله", e?.TransferDateG ?? DateTime.Today),
                FieldSpec.Choice_("انبار", warehouseChoices, e?.WareHouseId ?? 0),
                FieldSpec.Choice_("شخص مرتبط", personChoices, e?.PersonId ?? 0),
                FieldSpec.Multi_("توضیحات", e?.Describtion)
            }) { Owner = Window.GetWindow(this) };

            if (e is not null && wasAccepted)
                Info("توجه: این حواله تأیید شده است و موجودی انبار را تغییر داده. پس از ویرایش، دوباره تأییدش کنید.");

            if (dlg.ShowDialog() != true) return;

            var now = DateTime.Now;
            if (e is null)
            {
                e = new WarehouseTransfer { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now, CreateUserId = UserSession.CurrentUserId };
                db.WarehouseTransfers.Add(e);
            }
            e.Direction = dlg.GetChoice(0) ?? WarehouseTransfer.DirectionIn;
            e.ProductId = dlg.GetChoice(1) ?? productChoices[0].Key;
            e.Quantity = dlg.GetNumber(2) ?? 0;
            if (e.Quantity <= 0) { Info("تعداد باید بزرگ‌تر از صفر باشد."); return; }
            e.TransferDateG = dlg.GetDate(3) ?? DateTime.Today; // exact physical transfer date
            e.TransferDate = PersianDate.ToPersian(e.TransferDateG);
            e.WareHouseId = dlg.GetChoice(4) is > 0 ? dlg.GetChoice(4) : null;
            e.PersonId = dlg.GetChoice(5) is > 0 ? dlg.GetChoice(5) : null;
            e.Describtion = dlg.GetText(6) ?? "";
            e.Accepted = false; // edits return the row to the warehouse queue
            e.AcceptedByUserId = null;
            e.UpdateDateTime = now;
            e.UpdateUserId = UserSession.CurrentUserId;
            await db.SaveChangesAsync();
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Accepts (or un-accepts) the selected transfer — warehouse keeper / admin only.</summary>
    private async void Accept(bool accept)
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک حواله را انتخاب کنید."); return; }
        if (!UserSession.CanAcceptTransfers)
        {
            MessageBox.Show("تأیید انجام حواله فقط توسط انباردار یا مدیر سیستم انجام می‌شود.",
                "دسترسی", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var what = accept ? "تأیید" : "لغو تأیید";
        if (MessageBox.Show($"حواله «{row.Title}» {what} شود؟",
                "تأیید", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var e = await db.WarehouseTransfers.FirstAsync(x => x.Id == row.Id);
            if (accept)
            {
                e.Accepted = true;
                e.AcceptedByUserId = UserSession.CurrentUserId;
            }
            else
            {
                e.Accepted = false;
                e.AcceptedByUserId = null;
            }
            e.UpdateDateTime = DateTime.Now;
            e.UpdateUserId = UserSession.CurrentUserId;
            await db.SaveChangesAsync();
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Delete()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک حواله را انتخاب کنید."); return; }
        if (MessageBox.Show($"حواله «{row.Title}» حذف شود؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var e = await db.WarehouseTransfers.FirstAsync(x => x.Id == row.Id);
            if (e.Accepted)
            {
                if (!UserSession.CanAcceptTransfers)
                {
                    MessageBox.Show("حذف حواله‌های تأییدشده فقط توسط انباردار یا مدیر سیستم انجام می‌شود.",
                        "دسترسی", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (MessageBox.Show("این حواله تأیید شده و روی موجودی اثر گذاشته است. مطمئنید حذف شود؟",
                        "حذف حواله تأییدشده", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }
            e.Deleted = true;
            e.UpdateDateTime = DateTime.Now;
            e.UpdateUserId = UserSession.CurrentUserId;
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
