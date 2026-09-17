using System.Windows;
using SadrApp.Data;

namespace SadrApp.Infrastructure;

/// <summary>
/// Glue between the UI and InvoicePrinter: loads data, applies the chosen print setting
/// (layout/logo/title/footer/discount column), builds the PDF, renders the preview image
/// and opens the preview window (save PDF / print from there).
/// </summary>
public static class InvoicePrintService
{
    /// <summary>Preview with a specific saved setting (null = app default).</summary>
    public static async Task OpenPreviewAsync(int invoiceId, Window? owner, int? settingId = null)
    {
        await using var db = SadrDb.New();

        PrintSettingInfo? setting;
        if (settingId is int sid)
            setting = PrintSettingsService.LoadList(db).FirstOrDefault(s => s.Id == sid);
        else
            setting = PrintSettingsService.LoadDefault(db);

        await OpenPreviewAsync(db, invoiceId, owner, setting);
    }

    /// <summary>Preview with an already-loaded setting (used by the preview's layout switcher).</summary>
    public static async Task OpenPreviewAsync(SadrApp.Data.SadrDbContext db, int invoiceId, Window? owner, PrintSettingInfo? setting)
    {
        var model = await InvoicePrinter.LoadInvoiceModelAsync(db, invoiceId);
        model.Opt = PrintOptions.From(setting);

        byte[] pdf = InvoicePrinter.BuildPdf(model);
        byte[] png = InvoicePrinter.RenderPreviewBytes(pdf);

        var settings = PrintSettingsService.LoadList(db);
        var win = new Views.Controls.InvoicePrintPreviewWindow(invoiceId, model.InvoiceNumber, pdf, png, settings, setting?.Id ?? settings.FirstOrDefault()?.Id ?? 0);
        if (owner is not null)
        {
            win.Owner = owner;
        }
        win.ShowDialog();
    }
}
