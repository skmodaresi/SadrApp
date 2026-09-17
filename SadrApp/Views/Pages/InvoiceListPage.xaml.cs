using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

public partial class InvoiceListPage : UserControl
{
    /// <summary>Fixed type filter, or null for the "all types" page with the combo enabled.</summary>
    private readonly int? _fixedType;

    public InvoiceListPage(int? fixedType = null)
    {
        InitializeComponent();
        _fixedType = fixedType;
        ListCtl.NewClicked += (_, _) => ShowEditor(null, null);
        ListCtl.EditClicked += (_, _) => Edit();
        ListCtl.DeleteClicked += (_, _) => Delete();
        ListCtl.RefreshClicked += (_, _) => Load();
        ListCtl.RowDoubleClicked += (_, _) => Edit();
        TypeFilter.SelectionChanged += (_, _) => Load();
        ListCtl.PrintClicked += (_, _) => Print();
        Loaded += (_, _) => { if (!initialized) { initialized = true; Init(); } };
    }

    private async void Print()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک فاکتور را انتخاب کنید."); return; }
        try
        {
            IsEnabled = false;
            await SadrApp.Infrastructure.InvoicePrintService.OpenPreviewAsync(row.Id, Window.GetWindow(this));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در چاپ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private bool initialized;

    private void Init()
    {
        if (_fixedType is int ft)
        {
            // Dedicated page (پیش‌فاکتور/فروش/خرید): type is fixed, combo hidden.
            TypeFilter.ItemsSource = new[]
            {
                new ComboItem { Key = ft, Text = InvoiceTypeConsts.Label(ft) }
            };
            TypeFilter.DisplayMemberPath = "Text";
            TypeFilter.SelectedValuePath = "Key";
            TypeFilter.SelectedValue = ft;
            TypeFilter.Visibility = Visibility.Collapsed;
        }
        else
        {
            TypeFilter.ItemsSource = new[]
            {
                new ComboItem { Key = -1, Text = "— همه انواع —" },
                new ComboItem { Key = InvoiceTypeConsts.Sell, Text = InvoiceTypeConsts.Label(InvoiceTypeConsts.Sell) },
                new ComboItem { Key = InvoiceTypeConsts.Buy, Text = InvoiceTypeConsts.Label(InvoiceTypeConsts.Buy) },
                new ComboItem { Key = InvoiceTypeConsts.PreInvoice, Text = InvoiceTypeConsts.Label(InvoiceTypeConsts.PreInvoice) }
            };
            TypeFilter.DisplayMemberPath = "Text";
            TypeFilter.SelectedValuePath = "Key";
            TypeFilter.SelectedValue = -1;
        }
        ListCtl.ShowExtraColumn("توضیحات");
        ListCtl.ShowPrintButton();
        Load();
    }

    private async void Load()
    {
        try
        {
            await using var db = SadrDb.New();
            var t = TypeFilter.SelectedValue as int? ?? -1;
            var query = db.Invoices.Where(i => !i.Deleted);
            if (t >= 0) query = query.Where(i => i.InvoiceType == t);

            var rows = await query.OrderByDescending(i => i.Id).Select(i => new RowBase
            {
                Id = i.Id,
                Title = InvoiceTypeConsts.Icon(i.InvoiceType) + " " + i.InvoiceNumber,
                Code = i.InvoiceDate,
                Description = (i.InvoiceType == InvoiceTypeConsts.Buy
                                    ? (i.Provider != null ? i.Provider.Name : "—")
                                    : (i.Customer != null ? i.Customer.Name : "—"))
                              + " | جمع: " + i.TotalPrice.ToString("N0")
                              + " | " + InvoiceStatusConsts.Label(i.Status),
                Extra = i.Description
            }).ToListAsync();
            ListCtl.SetRows(rows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری فاکتورها", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Edit()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک فاکتور را انتخاب کنید."); return; }
        ShowEditor(row.Id, null);
    }

    private void ShowEditor(int? id, int? type)
    {
        try
        {
            if (id is null && type is null)
            {
                type = _fixedType ?? (TypeFilter.SelectedValue is int t && t >= 0 ? t : InvoiceTypeConsts.Sell);
            }
            var dlg = new InvoiceEditorWindow(id, type ?? InvoiceTypeConsts.Sell) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
                Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Delete()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک فاکتور را انتخاب کنید."); return; }
        if (MessageBox.Show($"فاکتور «{row.Title}» حذف شود؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var inv = await db.Invoices.Include(i => i.Details).FirstAsync(i => i.Id == row.Id);
            inv.Deleted = true;
            foreach (var d in inv.Details) d.Deleted = true;
            await db.SaveChangesAsync(); // audit fields stamped automatically
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void Info(string m) => MessageBox.Show(m, "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
}
