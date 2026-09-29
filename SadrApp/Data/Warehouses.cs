using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SadrApp.Infrastructure;

namespace SadrApp.Data;

/// <summary>Role names stored in Users.Role (the Roles table keeps the display catalog).</summary>
public static class RoleConsts
{
    public const string Admin = "Admin";
    public const string Warehouse = "Warehouse"; // انباردار — transfers: register + accept
    public const string Sales = "Sales";         // فروشنده — invoices only
}

/// <summary>
/// One product movement. Direction 1 = ورود (into stock, e.g. buy invoices, returns),
/// 2 = خروج (out of stock, e.g. sell invoices, shrinkage). A transfer is created pending
/// (Accepted = false) when registered — including automatically from buy/sell invoices —
/// and only moves stock after a warehouse keeper accepts it with the real transfer date
/// (which may differ from the invoice date). Some transfers have no invoice at all.
/// </summary>
[Table("WarehouseTransfers")]
public class WarehouseTransfer
{
    public const int DirectionIn = 1;  // ورود
    public const int DirectionOut = 2; // خروج

    [Key] public int Id { get; set; }
    public int ProductId { get; set; }
    public int? WareHouseId { get; set; }
    /// <summary>1 = into stock, 2 = out of stock (see DirectionIn/DirectionOut).</summary>
    public int Direction { get; set; }
    /// <summary>Always positive; Direction determines the sign on stock.</summary>
    public decimal Quantity { get; set; }
    /// <summary>Persian yyyy/MM/dd display text of the real transfer date; TransferDateG is the value.</summary>
    public string TransferDate { get; set; } = "";
    /// <summary>The exact physical movement date — set at acceptance, not necessarily the invoice date.</summary>
    public DateTime TransferDateG { get; set; }
    /// <summary>Pending until a warehouse keeper accepts the transfer as really done.</summary>
    public bool Accepted { get; set; }
    public Guid? AcceptedByUserId { get; set; }
    /// <summary>Buy/sell invoices create a pending transfer per product line on save.</summary>
    public int? InvoiceId { get; set; }
    /// <summary>Optional link for manual returns.</summary>
    public int? PersonId { get; set; }
    public string Describtion { get; set; } = "";
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(ProductId))] public virtual Product? Product { get; set; }
    [ForeignKey(nameof(WareHouseId))] public virtual WareHouse? WareHouse { get; set; }
    [ForeignKey(nameof(InvoiceId))] public virtual Invoice? Invoice { get; set; }
    [ForeignKey(nameof(PersonId))] public virtual Person? Person { get; set; }
}

/// <summary>
/// Single source of truth for stock levels. Current stock of a product =
/// InitialQuantity (opening value from the product form) plus signed, **accepted**
/// transfers only — pending rows do not move stock until a warehouse keeper confirms
/// the goods really arrived/left. With a warehouse id, only that warehouse's transfers
/// count (the opening quantity is defined per product, not per warehouse).
/// </summary>
public static class WarehouseStock
{
    /// <summary>Opening + accepted in − accepted out, optionally limited to one warehouse.</summary>
    public static async Task<decimal> CurrentAsync(SadrDbContext db, int productId, int? warehouseId = null)
    {
        var initial = await db.Products.Where(p => p.Id == productId)
            .Select(p => (decimal?)p.InitialQuantity).SingleOrDefaultAsync() ?? 0m;
        return initial + await NetAcceptedAsync(db, productId, warehouseId);
    }

    /// <summary>Signed effect of accepted transfers (initial stock excluded), optionally per warehouse.</summary>
    public static async Task<decimal> NetAcceptedAsync(SadrDbContext db, int productId, int? warehouseId = null)
    {
        var q = Filter(db.WarehouseTransfers, productId, warehouseId);
        var ins = await q.Where(t => t.Accepted && t.Direction == WarehouseTransfer.DirectionIn)
            .SumAsync(t => (decimal?)t.Quantity) ?? 0m;
        var outs = await q.Where(t => t.Accepted && t.Direction == WarehouseTransfer.DirectionOut)
            .SumAsync(t => (decimal?)t.Quantity) ?? 0m;
        return ins - outs;
    }

    /// <summary>Accepted + pending effects, for showing what stock would become.</summary>
    public static async Task<decimal> NetIncludingPendingAsync(SadrDbContext db, int productId, int? warehouseId = null)
    {
        var q = Filter(db.WarehouseTransfers, productId, warehouseId);
        var ins = await q.Where(t => t.Direction == WarehouseTransfer.DirectionIn)
            .SumAsync(t => (decimal?)t.Quantity) ?? 0m;
        var outs = await q.Where(t => t.Direction == WarehouseTransfer.DirectionOut)
            .SumAsync(t => (decimal?)t.Quantity) ?? 0m;
        return ins - outs;
    }

    private static IQueryable<WarehouseTransfer> Filter(
        IQueryable<WarehouseTransfer> q, int productId, int? warehouseId)
    {
        q = q.Where(t => t.ProductId == productId && !t.Deleted);
        if (warehouseId is int wid) q = q.Where(t => t.WareHouseId == wid);
        return q;
    }

    /// <summary>
    /// Brings the invoice's auto-generated transfers in line with the invoice's product
    /// lines (buy → ورود, sell → خروج). New/changed lines create **pending** transfers
    /// dated with the invoice date; a warehouse keeper later accepts them with the real
    /// physical date. Removing a line soft-deletes its still-pending transfer; accepted
    /// transfers are never auto-deleted (physical goods already moved).
    /// </summary>
    public static async Task SyncInvoiceTransfersAsync(SadrDbContext db, Invoice inv, Guid? userId)
    {
        var direction = inv.InvoiceType == InvoiceTypeConsts.Buy
            ? WarehouseTransfer.DirectionIn
            : WarehouseTransfer.DirectionOut;
        var activeLines = inv.Details.Where(d => !d.Deleted).ToList();

        var existing = await db.WarehouseTransfers
            .Where(t => t.InvoiceId == inv.Id && !t.Deleted)
            .ToListAsync();

        var now = DateTime.Now;
        var persianDate = PersianDate.ToPersian(inv.InvoiceDateG);
        var party = inv.InvoiceType == InvoiceTypeConsts.Buy
            ? inv.ProviderId : inv.CustomerId;

        // 1) soft-delete pending transfers whose line disappeared; count accepted ones
        foreach (var t in existing.Where(t => t.Direction == direction))
        {
            if (!activeLines.Any(d => d.ProductId == t.ProductId) && !t.Accepted)
            {
                t.Deleted = true;
                t.UpdateDateTime = now;
                t.UpdateUserId = userId;
            }
        }

        // 2) upsert one pending transfer per line (accepted rows are left untouched)
        foreach (var d in activeLines)
        {
            var t = existing.FirstOrDefault(x => x.ProductId == d.ProductId && x.Direction == direction && !x.Accepted);
            if (t is null)
            {
                t = new WarehouseTransfer
                {
                    RecordUniqueId = Guid.NewGuid(),
                    CreateDateTime = now,
                    CreateUserId = userId
                };
                db.WarehouseTransfers.Add(t);
            }
            t.ProductId = d.ProductId;
            t.Direction = direction;
            t.Quantity = d.Amount;
            t.TransferDateG = inv.InvoiceDateG;
            t.TransferDate = persianDate;
            t.Accepted = false; // re-save always returns it to the warehouse queue
            t.AcceptedByUserId = null;
            t.InvoiceId = inv.Id;
            t.PersonId = party;
            t.Describtion = (inv.InvoiceType == InvoiceTypeConsts.Buy ? "ورود از فاکتور خرید " : "خروج از فاکتور فروش ") + inv.InvoiceNumber;
            t.Deleted = false;
            t.UpdateDateTime = now;
            t.UpdateUserId = userId;
        }

        await db.SaveChangesAsync();
    }
}
