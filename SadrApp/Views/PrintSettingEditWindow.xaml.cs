using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;

namespace SadrApp.Views;

/// <summary>Editor dialog for one InvoicePrintSetting row (fields + logo picker).</summary>
public partial class PrintSettingEditWindow : Window
{
    private readonly PrintSettingInfo _info;
    private byte[]? _logo;
    private bool _logoChanged;

    public byte[]? LogoBytes => _logo;      // null + LogoChanged=false = keep current logo
    public bool LogoChanged => _logoChanged;

    public PrintSettingEditWindow(PrintSettingInfo info)
    {
        InitializeComponent();
        _info = info;
        TblName.Text = info.Name;
        CmbLayout.SelectedIndex = info.Layout;
        TblTitle.Text = info.TitleOverride ?? "";
        TblFooter.Text = info.FooterNote ?? "";
        ChkDiscount.IsChecked = info.ShowDiscountColumn;
        if (info.Logo is { Length: > 0 }) SetLogoPreview(info.Logo);
    }

    private void SetLogoPreview(byte[] bytes)
    {
        try
        {
            var img = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            LogoPreview.Source = img;
        }
        catch { LogoPreview.Source = null; }
    }

    private void BtnPickLogo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "انتخاب لوگو",
            Filter = "تصویر (PNG, JPG)|*.png;*.jpg;*.jpeg"
        };
        if (dlg.ShowDialog() != true) return;

        var bytes = PrintSettingsService.ReadLogoFile(dlg.FileName, out var error);
        if (bytes is null)
        {
            MessageBox.Show(this, error, "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _logo = bytes;
        _logoChanged = true;
        SetLogoPreview(bytes);
    }

    private void BtnClearLogo_Click(object sender, RoutedEventArgs e)
    {
        _logo = null;
        _logoChanged = true;
        LogoPreview.Source = null;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TblName.Text))
        {
            MessageBox.Show(this, "نام تنظیم را وارد کنید.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _info.Name = TblName.Text.Trim();
        _info.Layout = CmbLayout.SelectedIndex < 0 ? 0 : CmbLayout.SelectedIndex;
        _info.TitleOverride = string.IsNullOrWhiteSpace(TblTitle.Text) ? null : TblTitle.Text.Trim();
        _info.FooterNote = string.IsNullOrWhiteSpace(TblFooter.Text) ? null : TblFooter.Text.Trim();
        _info.ShowDiscountColumn = ChkDiscount.IsChecked == true;
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
