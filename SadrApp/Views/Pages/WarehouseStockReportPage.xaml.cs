using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;

namespace SadrApp.Views.Pages;

/// <summary>
/// Stock report: for every product, the opening quantity, the confirmed stock
/// (initial + accepted transfers), the pending in/out that still awaits warehouse
/// acceptance, and the projected stock once everything is accepted. Optionally
/// narrowed to a single warehouse; the opening quantity is defined per product,
/// not per warehouse.
/// </summary>
public partial class WarehouseStockReportPage : UserControl
{
    public class StockRow
    {
        public string Product { get; set; } = "";
        public string Code { get; set; } = "";
        public string Initial { get; set; } = "";
        public string Confirmed { get; set; } = "";
        public string PendingIn { get; set; } = "";
        public string PendingOut { get; set; } = "";
        public string Projected { get; set; } = "";
    }

    public WarehouseStockReportPage()
    {
        InitializeComponent();
        BtnRefresh.Click += (_, _) => Load();
        WarehouseFilter.SelectionChanged += (_, _) => Load();
        Loaded += (_, _) => LoadWarehouses();
    }

    private bool _filtersLoaded;

    private async void LoadWarehouses()
    {
        try
        {
            await using var db = SadrDb.New();
            var items = await db.WareHouses.Where(w => !w.Deleted)
                .Select(w => new { Key = (int?)w.Id, Text = w.Name }).ToListAsync();
            items.Insert(0, new { Key = (int?)null, Text = "— همه انبارها —" });
            WarehouseFilter.ItemsSource = items;
            WarehouseFilter.DisplayMemberPath = "Text";
            WarehouseFilter.SelectedValuePath = "Key";
            WarehouseFilter.SelectedValue = null;
            _filtersLoaded = true;
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Load()
    {
        if (!_filtersLoaded) return;
        try
        {
            await using var db = SadrDb.New();
            var wid = WarehouseFilter.SelectedValue as int?;

            var products = await db.Products.Where(p => !p.Deleted)
                .Select(p => new { p.Id, p.Name, p.Code, p.InitialQuantity }).ToListAsync();

            var transfersQuery = db.WarehouseTransfers.Where(t => !t.Deleted);
            if (wid is int w) transfersQuery = transfersQuery.Where(t => t.WareHouseId == w);
            var transfers = await transfersQuery
                .Select(t => new { t.ProductId, t.Direction, t.Quantity, t.Accepted })
                .ToListAsync();
            var byProduct = transfers.ToLookup(
                t => t.ProductId,
                t => (Direction: t.Direction, Quantity: t.Quantity, Accepted: t.Accepted));

            var rows = new ObservableCollection<StockRow>();
            int negative = 0;
            foreach (var p in products)
            {
                var tl = byProduct[p.Id]; // empty sequence when the product has no transfers
                var confIn = tl.Where(t => t.Accepted && t.Direction == WarehouseTransfer.DirectionIn).Sum(t => t.Quantity);
                var confOut = tl.Where(t => t.Accepted && t.Direction == WarehouseTransfer.DirectionOut).Sum(t => t.Quantity);
                var pendIn = tl.Where(t => !t.Accepted && t.Direction == WarehouseTransfer.DirectionIn).Sum(t => t.Quantity);
                var pendOut = tl.Where(t => !t.Accepted && t.Direction == WarehouseTransfer.DirectionOut).Sum(t => t.Quantity);
                var confirmed = p.InitialQuantity + confIn - confOut;
                var projected = confirmed + pendIn - pendOut;
                if (projected < 0) negative++;

                rows.Add(new StockRow
                {
                    Product = p.Name,
                    Code = p.Code,
                    Initial = p.InitialQuantity.ToString("N0"),
                    Confirmed = confirmed.ToString("N0"),
                    PendingIn = pendIn == 0 ? "—" : pendIn.ToString("N0"),
                    PendingOut = pendOut == 0 ? "—" : pendOut.ToString("N0"),
                    Projected = projected.ToString("N0") + (projected < 0 ? " ⚠️" : "")
                });
            }
            StockGrid.ItemsSource = rows;
            TxtSummary.Text = products.Count + " کالا" + (negative > 0 ? " — " + negative + " کالا با موجودی منفی ⚠️" : "");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
