using System.IO;
using Microsoft.EntityFrameworkCore;
using PDFtoImage;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SadrApp.Data;
using SkiaSharp;

namespace SadrApp.Infrastructure;

/// <summary>Per-print choices (layout, logo, title, footer, discount column) taken from the DB setting.</summary>
public sealed class PrintOptions
{
    public int Layout = InvoiceLayoutConsts.Classic;
    public string? TitleOverride;
    public string? FooterNote;
    public bool ShowDiscountColumn = true;
    public byte[]? Logo;

    public static PrintOptions From(PrintSettingInfo? s) => s is null
        ? new PrintOptions()
        : new PrintOptions
        {
            Layout = s.Layout,
            TitleOverride = s.TitleOverride,
            FooterNote = s.FooterNote,
            ShowDiscountColumn = s.ShowDiscountColumn,
            Logo = s.Logo
        };
}

/// <summary>All data shown on the printed invoice (kept UI-free for testability).</summary>
public sealed class InvoicePrintModel
{
    public string Title = "";
    public string InvoiceNumber = "";
    public string Code = "";
    public string InvoiceDate = "";
    public string Status = "";
    public string PartyTitle = "";
    public string PartyType = "";
    public string CodeLabel = "کد";
    public string PartyCode = "";
    public string PartyPhone = "";
    public string CompanyName = "";
    public string CompanyPhone = "";
    public string CompanyAddress = "";
    public string Description = "";
    public decimal TotalDiscount;
    public decimal TotalPrice;

    public PrintOptions Opt = new();

    /// <summary>Printed title: the user override when set, otherwise the invoice-type label.</summary>
    public string ResolvedTitle =>
        string.IsNullOrWhiteSpace(Opt.TitleOverride) ? Title : Opt.TitleOverride!;

    public List<InvoicePrintRow> Rows = new();
}

public sealed class InvoicePrintRow
{
    public int Index;
    public string ProductName = "";
    public string UnitName = "";
    public decimal Amount;
    public decimal UnitPrice;
    public double Extra;
    public double Discount;
    public decimal RowDiscount;
    public decimal Total;
}

/// <summary>
/// Builds a Persian/RTL A4 invoice PDF with QuestPDF (Community license) and
/// renders a PNG preview via PDFtoImage for the preview window / printing.
/// Three predefined layouts: Classic (official form), Modern (banded card),
/// Compact (dense single page).
/// </summary>
public static class InvoicePrinter
{
    static InvoicePrinter() => QuestPDF.Settings.License = LicenseType.Community;

    /// <summary>Body rows: B Nazanin. Titles and bold words: B Zar. Falls back to Tahoma when a B font is not installed.</summary>
    private static readonly string BodyFont = ResolveFont("B Nazanin", "Tahoma");
    private static readonly string TitleFont = ResolveFont("B Zar", "Tahoma");

    private static string ResolveFont(string preferred, string fallback)
    {
        try
        {
            foreach (var f in System.Windows.Media.Fonts.SystemFontFamilies)
                if (string.Equals(f.Source, preferred, StringComparison.OrdinalIgnoreCase))
                    return preferred;
        }
        catch { }
        return fallback;
    }

    // ---------- data ----------

    public static async Task<InvoicePrintModel> LoadInvoiceModelAsync(SadrDbContext db, int invoiceId)
    {
        var inv = await db.Invoices
            .Include(i => i.Customer).ThenInclude(c => c!.Person)
            .Include(i => i.Customer).ThenInclude(c => c!.Company)
            .Include(i => i.Provider).ThenInclude(c => c!.Person)
            .Include(i => i.Provider).ThenInclude(c => c!.Company)
            .Include(i => i.Details).ThenInclude(d => d.Product)
            .Include(i => i.Details).ThenInclude(d => d.Unit)
            .Include(i => i.Details).ThenInclude(d => d.Currency)
            .FirstAsync(i => i.Id == invoiceId);

        var company = await db.Companies.Where(c => !c.Deleted).OrderBy(c => c.Id).FirstOrDefaultAsync();

        var m = new InvoicePrintModel
        {
            Title = InvoiceTypeConsts.Label(inv.InvoiceType),
            InvoiceNumber = inv.InvoiceNumber,
            Code = inv.Code ?? "",
            InvoiceDate = string.IsNullOrEmpty(inv.InvoiceDate) ? PersianDate.ToPersian(inv.InvoiceDateG) : inv.InvoiceDate,
            Status = InvoiceStatusConsts.Label(inv.Status),
            CompanyName = company?.FullName ?? "",
            CompanyPhone = company?.Tel ?? "",
            CompanyAddress = company?.Address ?? "",
            Description = inv.Description ?? "",
            TotalDiscount = inv.TotalDiscount,
            TotalPrice = inv.TotalPrice
        };

        if (inv.InvoiceType == InvoiceTypeConsts.Buy)
        {
            m.PartyType = "تأمین‌کننده";
            FillParty(m, inv.Provider);
        }
        else
        {
            m.PartyType = "مشتری";
            FillParty(m, inv.Customer);
        }

        int idx = 1;
        foreach (var d in inv.Details.Where(x => !x.Deleted).OrderBy(x => x.Id))
        {
            m.Rows.Add(new InvoicePrintRow
            {
                Index = idx++,
                ProductName = d.Product?.Name ?? ("کالای #" + d.ProductId),
                UnitName = d.Unit?.Name ?? "",
                Amount = d.Amount,
                UnitPrice = d.UnitPriceAmount ?? 0m,
                Extra = d.ExtraPersentage ?? 0,
                Discount = d.DiscountPersentage ?? 0,
                RowDiscount = d.TotalDiscount ?? 0m,
                Total = d.TotalPriceAmount
            });
        }
        return m;
    }

    private static void FillParty(InvoicePrintModel m, CustomersProvider? cp)
    {
        if (cp is null) return;
        m.PartyTitle = cp.Name;

        if (cp.Person is { } p)
        {
            m.CodeLabel = "کد/کد ملی";
            m.PartyCode = p.Code ?? "";
            m.PartyPhone = p.PhoneNumber ?? "";
        }
        else if (cp.Company is { } c)
        {
            m.CodeLabel = "شناسه ملی";
            m.PartyCode = c.NationalId is { Length: > 0 } nid ? nid : c.Code;
            m.PartyPhone = c.Tel ?? "";
        }
    }

    // ---------- pdf ----------

    public static byte[] BuildPdf(InvoicePrintModel m, PrintOptions? opt = null)
    {
        if (opt is not null) m.Opt = opt;
        bool compact = m.Opt.Layout == InvoiceLayoutConsts.Compact;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(compact ? 12 : 18);
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily(BodyFont).FontSize(compact ? 10f : 11.5f));

                page.Content().Column(col =>
                {
                    switch (m.Opt.Layout)
                    {
                        case InvoiceLayoutConsts.Modern: ModernHeader(m, col); break;
                        case InvoiceLayoutConsts.Compact: CompactHeader(m, col); break;
                        default: ClassicHeader(m, col); break;
                    }

                    col.Item().PaddingTop(compact ? 6 : 12).Table(t => ItemsTable(m, t));

                    switch (m.Opt.Layout)
                    {
                        case InvoiceLayoutConsts.Modern: ModernTotals(m, col); break;
                        case InvoiceLayoutConsts.Compact: CompactTotals(m, col); break;
                        default: ClassicTotals(m, col); break;
                    }

                    // ===== description =====
                    if (m.Description.Length > 0)
                        col.Item().PaddingTop(compact ? 5 : 10).Border(1).BorderColor("#C9D2E3").Background("#FCFDFE")
                            .Padding(6).Text(txt =>
                            {
                                txt.Span("توضیحات: ").Bold().FontFamily(TitleFont).FontSize(11);
                                txt.Span(m.Description).FontSize(11);
                            });

                    // ===== signatures (not on the compact slip) =====
                    if (!compact)
                        col.Item().PaddingTop(14).Row(row =>
                        {
                            foreach (var label in new[] { "مهر و امضای فروشنده", "مهر و امضای خریدار" })
                            {
                                row.RelativeItem().PaddingHorizontal(4).Border(1).BorderColor("#C9D2E3")
                                    .Background("#FCFDFE").Height(64).Column(c =>
                                    {
                                        c.Item().PaddingTop(4).Text(label).FontSize(9.5f).FontColor("#44506B").AlignCenter();
                                    });
                            }
                        });
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text(string.IsNullOrWhiteSpace(m.Opt.FooterNote)
                            ? "صادر شده توسط نرم‌افزار صدر" : m.Opt.FooterNote!)
                        .FontSize(8.5f).FontColor("#8A94A8");
                    row.ConstantItem(150).Text(txt =>
                    {
                        txt.Span("صفحه ").FontSize(8.5f).FontColor("#8A94A8");
                        txt.CurrentPageNumber().FontSize(8.5f).FontColor("#8A94A8");
                        txt.Span(" از ").FontSize(8.5f).FontColor("#8A94A8");
                        txt.TotalPages().FontSize(8.5f).FontColor("#8A94A8");
                    });
                });
            });
        }).GeneratePdf();
    }

    // ---------- layout: classic (official form) ----------

    private static void ClassicHeader(InvoicePrintModel m, ColumnDescriptor col)
    {
        col.Item().Row(row =>
        {
            if (m.Opt.Logo is { Length: > 0 } logo)
                row.ConstantItem(120).PaddingBottom(4).Height(64).Image(logo);
            row.RelativeItem().Column(c =>
            {
                c.Item().Text(m.CompanyName).Bold().FontFamily(TitleFont).FontSize(16).FontColor("#1F3A5F");
                if (m.CompanyPhone.Length > 0)
                    c.Item().PaddingTop(2).Text("تلفن: " + m.CompanyPhone).FontSize(9.5f).FontColor("#44506B");
                if (m.CompanyAddress.Length > 0)
                    c.Item().PaddingTop(2).Text("نشانی: " + m.CompanyAddress).FontSize(9.5f).FontColor("#44506B");
            });
            row.ConstantItem(170).PaddingLeft(10).Column(c =>
            {
                c.Item().Background("#1F3A5F").Padding(6).Text(m.ResolvedTitle).Bold().FontFamily(TitleFont).FontSize(14).FontColor("#FFFFFF").AlignCenter();
                c.Item().Border(1).BorderColor("#C9D2E3").Background("#F7F9FD").Padding(6)
                    .Text("شماره: " + m.InvoiceNumber).FontSize(11).AlignCenter();
            });
        });

        col.Item().PaddingTop(10).LineHorizontal(1).LineColor("#2F6FED");

        var info = new (string Label, string Value)[]
        {
            ("تاریخ", m.InvoiceDate),
            ("کد فاکتور", m.Code.Length > 0 ? m.Code : "—"),
            ("وضعیت", m.Status),
            (m.PartyType, m.PartyTitle.Length > 0 ? m.PartyTitle : "—"),
            (m.CodeLabel, m.PartyCode.Length > 0 ? m.PartyCode : "—"),
            ("تلفن طرف حساب", m.PartyPhone.Length > 0 ? m.PartyPhone : "—")
        };
        col.Item().PaddingTop(10).Row(row =>
        {
            foreach (var (label, value) in info)
            {
                row.RelativeItem().PaddingHorizontal(2).Border(1).BorderColor("#C9D2E3").Column(c =>
                {
                    c.Item().PaddingHorizontal(5).PaddingTop(3).Text(label).FontSize(8.5f).FontColor("#8A94A8");
                    c.Item().PaddingHorizontal(5).PaddingBottom(3).Text(value).Bold().FontFamily(TitleFont).FontSize(10);
                });
            }
        });
    }

    private static void ClassicTotals(InvoicePrintModel m, ColumnDescriptor col)
    {
        col.Item().PaddingTop(10).Width(280).AlignRight().Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn();
                c.ConstantColumn(120);
            });
            t.Cell().Background("#EFF3FA").Border(1).BorderColor("#C9D2E3").Padding(4)
                .Text("تخفیف کل").FontSize(10.5f).AlignCenter();
            t.Cell().Background("#EFF3FA").Border(1).BorderColor("#C9D2E3").Padding(4)
                .Text(m.TotalDiscount.ToString("N0")).FontSize(10.5f).AlignCenter();
            t.Cell().Background("#DCE7FA").Border(1).BorderColor("#C9D2E3").Padding(4)
                .Text("جمع کل (ریال)").Bold().FontFamily(TitleFont).FontSize(11).AlignCenter();
            t.Cell().Background("#DCE7FA").Border(1).BorderColor("#C9D2E3").Padding(4)
                .Text(m.TotalPrice.ToString("N0")).Bold().FontFamily(TitleFont).FontSize(11).AlignCenter();
        });

        col.Item().PaddingTop(6).Width(280).AlignRight().Text(txt =>
        {
            txt.Span("مبلغ به حروف: ").Bold().FontFamily(TitleFont).FontSize(10);
            txt.Span(PersianWords.AmountToWords(m.TotalPrice)).FontSize(10);
        });
    }

    // ---------- layout: modern (banded card) ----------

    private static void ModernHeader(InvoicePrintModel m, ColumnDescriptor col)
    {
        col.Item().Border(1).BorderColor("#2F6FED").Background("#FFFFFF").Padding(12).Column(c =>
        {
            if (m.Opt.Logo is { Length: > 0 } logo)
                c.Item().MaxHeight(56).Image(logo);
            c.Item().Text(m.ResolvedTitle).Bold().FontFamily(TitleFont).FontSize(22).FontColor("#2F6FED").AlignCenter();
            if (m.CompanyName.Length > 0)
                c.Item().PaddingTop(2).Text(m.CompanyName).FontSize(10.5f).FontColor("#44506B").AlignCenter();
            if (m.CompanyPhone.Length > 0 || m.CompanyAddress.Length > 0)
                c.Item().PaddingTop(1).Text(string.Join("  |  ",
                        new[] { m.CompanyPhone.Length > 0 ? "تلفن: " + m.CompanyPhone : "",
                                m.CompanyAddress.Length > 0 ? m.CompanyAddress : "" }
                            .Where(s => s.Length > 0)))
                    .FontSize(9).FontColor("#8A94A8").AlignCenter();
        });

        col.Item().PaddingTop(8).Row(row =>
        {
            void Chip(string label, string value)
            {
                row.RelativeItem().PaddingHorizontal(3).Background("#EFF3FA").Border(1).BorderColor("#C9D2E3")
                    .Padding(5).Column(c =>
                    {
                        c.Item().Text(label).FontSize(8).FontColor("#8A94A8").AlignCenter();
                        c.Item().Text(value).Bold().FontFamily(TitleFont).FontSize(9.5f).AlignCenter();
                    });
            }
            Chip("شماره", m.InvoiceNumber);
            Chip("تاریخ", m.InvoiceDate);
            Chip("وضعیت", m.Status);
            Chip(m.PartyType, m.PartyTitle.Length > 0 ? m.PartyTitle : "—");
        });
    }

    private static void ModernTotals(InvoicePrintModel m, ColumnDescriptor col)
    {
        col.Item().PaddingTop(8).Row(row =>
        {
            row.RelativeItem().AlignLeft().Column(c =>
            {
                c.Item().Text(txt =>
                {
                    txt.Span("مبلغ به حروف: ").Bold().FontFamily(TitleFont).FontSize(9.5f);
                    txt.Span(PersianWords.AmountToWords(m.TotalPrice)).FontSize(9.5f);
                });
                if (m.PartyPhone.Length > 0)
                    c.Item().PaddingTop(2).Text(m.PartyType + ": " + m.PartyTitle + " — تلفن: " + m.PartyPhone)
                        .FontSize(8.5f).FontColor("#8A94A8");
            });
            row.ConstantItem(200).Column(c =>
            {
                c.Item().Row(r2 =>
                {
                    r2.RelativeItem().PaddingVertical(2).Text("تخفیف کل").FontSize(9.5f).FontColor("#44506B");
                    r2.ConstantItem(90).PaddingVertical(2).Text(m.TotalDiscount.ToString("N0")).FontSize(9.5f).AlignRight();
                });
                c.Item().BorderTop(1).BorderColor("#C9D2E3").PaddingTop(3).Row(r2 =>
                {
                    r2.RelativeItem().Text("جمع کل (ریال)").Bold().FontFamily(TitleFont).FontSize(11);
                    r2.ConstantItem(90).Text(m.TotalPrice.ToString("N0")).Bold().FontFamily(TitleFont).FontSize(12).FontColor("#2F6FED").AlignRight();
                });
            });
        });
    }

    // ---------- layout: compact (dense slip) ----------

    private static void CompactHeader(InvoicePrintModel m, ColumnDescriptor col)
    {
        col.Item().Row(row =>
        {
            if (m.Opt.Logo is { Length: > 0 } logo)
                row.ConstantItem(90).Height(48).Image(logo);
            row.RelativeItem().Column(c =>
            {
                c.Item().Text(m.CompanyName).Bold().FontFamily(TitleFont).FontSize(13).FontColor("#1F3A5F");
                if (m.CompanyPhone.Length > 0 || m.CompanyAddress.Length > 0)
                    c.Item().Text(string.Join(" | ",
                            new[] { m.CompanyPhone.Length > 0 ? "ت: " + m.CompanyPhone : "",
                                    m.CompanyAddress.Length > 0 ? m.CompanyAddress : "" }
                                .Where(s => s.Length > 0)))
                        .FontSize(8).FontColor("#8A94A8");
            });
            row.ConstantItem(150).Column(c =>
            {
                c.Item().Text(m.ResolvedTitle).Bold().FontFamily(TitleFont).FontSize(13).FontColor("#1F3A5F").AlignCenter();
                c.Item().Text("شماره: " + m.InvoiceNumber + "   تاریخ: " + m.InvoiceDate)
                    .FontSize(8.5f).AlignCenter();
                c.Item().Text("وضعیت: " + m.Status).FontSize(8.5f).FontColor("#44506B").AlignCenter();
            });
        });

        col.Item().PaddingTop(6).LineHorizontal(0.75f).LineColor("#1F3A5F");

        col.Item().PaddingTop(5).Text(txt =>
        {
            txt.Span(m.PartyType + ": ").Bold().FontFamily(TitleFont).FontSize(9.5f);
            txt.Span(m.PartyTitle.Length > 0 ? m.PartyTitle : "—").FontSize(9.5f);
            txt.Span("    " + m.CodeLabel + ": ").FontSize(9).FontColor("#44506B");
            txt.Span(m.PartyCode.Length > 0 ? m.PartyCode : "—").FontSize(9);
            if (m.PartyPhone.Length > 0)
            {
                txt.Span("    تلفن: ").FontSize(9).FontColor("#44506B");
                txt.Span(m.PartyPhone).FontSize(9);
            }
            if (m.Code.Length > 0)
            {
                txt.Span("    کد فاکتور: ").FontSize(9).FontColor("#44506B");
                txt.Span(m.Code).FontSize(9);
            }
        });
    }

    private static void CompactTotals(InvoicePrintModel m, ColumnDescriptor col)
    {
        col.Item().PaddingTop(6).Width(240).AlignRight().Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn();
                c.ConstantColumn(100);
            });
            t.Cell().Border(0.5f).BorderColor("#C9D2E3").Padding(3)
                .Text("تخفیف کل").FontSize(9).AlignCenter();
            t.Cell().Border(0.5f).BorderColor("#C9D2E3").Padding(3)
                .Text(m.TotalDiscount.ToString("N0")).FontSize(9).AlignCenter();
            t.Cell().Background("#1F3A5F").Padding(3)
                .Text("جمع کل (ریال)").Bold().FontFamily(TitleFont).FontSize(9.5f).FontColor("#FFFFFF").AlignCenter();
            t.Cell().Background("#1F3A5F").Padding(3)
                .Text(m.TotalPrice.ToString("N0")).Bold().FontFamily(TitleFont).FontSize(9.5f).FontColor("#FFFFFF").AlignCenter();
        });

        col.Item().PaddingTop(4).Width(240).AlignRight()
            .Text("مبلغ به حروف: " + PersianWords.AmountToWords(m.TotalPrice)).FontSize(8.5f);
    }

    // ---------- items table (shared, respects ShowDiscountColumn) ----------

    private static void ItemsTable(InvoicePrintModel m, TableDescriptor t)
    {
        bool showDisc = m.Opt.ShowDiscountColumn;
        bool compact = m.Opt.Layout == InvoiceLayoutConsts.Compact;

        t.ColumnsDefinition(c =>
        {
            c.ConstantColumn(26);   // #
            c.RelativeColumn();     // کالا
            c.ConstantColumn(48);   // واحد
            c.ConstantColumn(44);   // مقدار
            c.ConstantColumn(76);   // قیمت واحد
            if (showDisc)
            {
                c.ConstantColumn(40);   // اضافه ٪
                c.ConstantColumn(40);   // تخفیف ٪
            }
            c.ConstantColumn(showDisc ? 68 : 84);   // جمع ردیف
        });

        t.Header(h =>
        {
            void H(string s) =>
                h.Cell().Background("#1F3A5F").Border(0.5f).BorderColor("#152744").Padding(compact ? 3 : 4)
                    .Text(s).Bold().FontFamily(TitleFont).FontSize(compact ? 8.5f : 9.5f).FontColor("#FFFFFF").AlignCenter();
            H("#");
            H("شرح کالا");
            H("واحد");
            H("مقدار");
            H("قیمت واحد");
            if (showDisc) { H("اضافه ٪"); H("تخفیف ٪"); }
            H("جمع ردیف");
        });

        void BodyCells(InvoicePrintRow r, string bg)
        {
            BodyCell(t, r.Index.ToString(), bg, compact);
            BodyCell(t, r.ProductName, bg, compact);
            BodyCell(t, r.UnitName, bg, compact);
            BodyCell(t, r.Amount.ToString("0.###"), bg, compact);
            BodyCell(t, r.UnitPrice.ToString("N0"), bg, compact);
            if (showDisc)
            {
                BodyCell(t, r.Extra != 0 ? r.Extra.ToString("0.##") : "—", bg, compact);
                BodyCell(t, r.Discount != 0 ? r.Discount.ToString("0.##") : "—", bg, compact);
            }
            BodyCell(t, r.Total.ToString("N0"), bg, compact);
        }

        bool shade = false;
        foreach (var r in m.Rows)
        {
            var bg = shade ? "#EFF3FA" : "#FFFFFF";
            shade = !shade;
            BodyCells(r, bg);
        }

        // empty filler rows keep the classic/modern form looking like a form
        int colCount = showDisc ? 8 : 6;
        if (!compact)
            for (int i = m.Rows.Count; i < 8; i++)
            {
                var bg = shade ? "#EFF3FA" : "#FFFFFF";
                shade = !shade;
                for (int c2 = 0; c2 < colCount; c2++) BodyCell(t, "", bg, compact);
            }
    }

    private static void BodyCell(TableDescriptor t, string text, string bg, bool compact)
    {
        t.Cell().Background(bg).Border(0.5f).BorderColor("#C9D2E3").Padding(compact ? 3 : 4)
            .Text(text).FontSize(compact ? 9f : 10.5f).AlignCenter();
    }

    // ---------- preview rendering ----------

    /// <summary>Renders page 1 of the PDF to PNG bytes for the preview window and printing.</summary>
    public static byte[] RenderPreviewBytes(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using var bmp = Conversion.ToImage(ms, page: 0, options: new RenderOptions { Dpi = 150 });
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
