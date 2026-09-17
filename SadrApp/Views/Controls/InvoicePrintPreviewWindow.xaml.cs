using System.Diagnostics;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;

namespace SadrApp.Views.Controls;

/// <summary>
/// Shows the rendered invoice page (PNG produced by InvoicePrinter), lets the user
/// switch between the saved print layouts live, save it as a PDF file or send it
/// to a printer.
/// </summary>
public partial class InvoicePrintPreviewWindow : Window
{
    private readonly int _invoiceId;
    private readonly string _invoiceNumber;
    private byte[] _pdf;
    private byte[] _previewPng;
    private bool _switching;

    public InvoicePrintPreviewWindow(int invoiceId, string invoiceNumber, byte[] pdf, byte[] previewPng,
        List<PrintSettingInfo> settings, int selectedSettingId)
    {
        InitializeComponent();
        _invoiceId = invoiceId;
        _invoiceNumber = invoiceNumber;
        _pdf = pdf;
        _previewPng = previewPng;

        CmbLayout.ItemsSource = settings;
        CmbLayout.DisplayMemberPath = nameof(PrintSettingInfo.Display);
        CmbLayout.SelectedItem = settings.FirstOrDefault(s => s.Id == selectedSettingId);
        CmbLayout.SelectionChanged += async (_, _) => await SwitchLayoutAsync();

        PreviewImage.Source = LoadImage(_previewPng);
        ZoomSlider.ValueChanged += (_, _) => ApplyZoom();
        BtnSavePdf.Click += BtnSavePdf_Click;
        BtnPrint.Click += BtnPrint_Click;
        BtnClose.Click += (_, _) => Close();
        ApplyZoom();
    }

    private static BitmapImage LoadImage(byte[] png)
    {
        var img = new BitmapImage();
        using var ms = new MemoryStream(png);
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    private void ApplyZoom()
    {
        double z = ZoomSlider.Value;
        PreviewImage.Width = double.NaN;   // Stretch=None: NaN = natural pixel size
        PreviewImage.Height = double.NaN;
        PreviewImage.LayoutTransform = new ScaleTransform(z, z);
        LblZoom.Text = (int)Math.Round(z * 100) + "٪";
    }

    private async Task SwitchLayoutAsync()
    {
        if (_switching) return;
        if (CmbLayout.SelectedItem is not PrintSettingInfo s) return;
        _switching = true;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            await using var db = SadrDb.New();
            var model = await InvoicePrinter.LoadInvoiceModelAsync(db, _invoiceId);
            model.Opt = PrintOptions.From(s);
            _pdf = InvoicePrinter.BuildPdf(model);
            _previewPng = InvoicePrinter.RenderPreviewBytes(_pdf);
            PreviewImage.Source = LoadImage(_previewPng);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطا در تغییر چیدمان", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            _switching = false;
        }
    }

    private void BtnSavePdf_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "ذخیره فاکتور به صورت PDF",
            Filter = "PDF Files|*.pdf|All Files|*.*",
            FileName = _invoiceNumber + ".pdf"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            File.WriteAllBytes(dlg.FileName, _pdf);
            if (MessageBox.Show(this, "PDF ذخیره شد.\nفایل باز شود؟", "ذخیره موفق",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطا در ذخیره PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnPrint_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;

            // The preview PNG is rendered at 150 dpi; draw it exactly one A4 page wide.
            const double a4Width = 794;   // px @96dpi
            const double a4Height = 1123;

            var page = new FixedPage
            {
                Width = a4Width,
                Height = a4Height,
                FlowDirection = FlowDirection.LeftToRight
            };

            var img = new System.Windows.Controls.Image { Source = LoadImage(_previewPng) };
            // scale to fit inside the printable area
            double paW = dlg.PrintableAreaWidth, paH = dlg.PrintableAreaHeight;
            double scale = Math.Min(paW / a4Width, paH / a4Height);
            if (scale < 1)
            {
                img.Width = a4Width * scale;
                img.Height = a4Height * scale;
            }
            FixedPage.SetLeft(img, 0);
            FixedPage.SetTop(img, 0);
            page.Children.Add(img);

            var doc = new FixedDocument();
            var pc = new PageContent { Child = page };
            doc.Pages.Add(pc);

            dlg.PrintDocument(doc.DocumentPaginator, "فاکتور " + _invoiceNumber);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطا در چاپ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
