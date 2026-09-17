using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;

namespace SadrApp.Views;

/// <summary>Row wrapper for the settings grid with Persian display columns.</summary>
public sealed class PrintSettingRow
{
    public int Id;
    public PrintSettingInfo Info = new();

    public string Name => Info.Name;
    public string LayoutLabel => InvoiceLayoutConsts.Label(Info.Layout);
    public string TitleLabel => string.IsNullOrWhiteSpace(Info.TitleOverride) ? "—" : Info.TitleOverride!;
    public string FooterLabel => string.IsNullOrWhiteSpace(Info.FooterNote) ? "—" : Info.FooterNote!;
    public string DiscountLabel => Info.ShowDiscountColumn ? "نمایش" : "مخفی";
    public string LogoLabel => Info.Logo is { Length: > 0 } ? "✓" : "—";
    public string DefaultLabel => Info.IsDefault ? "⭐" : "";
}

public partial class PrintSettingsWindow : Window
{
    public PrintSettingsWindow()
    {
        InitializeComponent();
        BtnAdd.Click += async (_, _) => await AddAsync();
        BtnEdit.Click += async (_, _) => await EditAsync();
        BtnDelete.Click += async (_, _) => await DeleteAsync();
        BtnSetDefault.Click += async (_, _) => await SetDefaultAsync();
        Loaded += (_, _) => LoadRows();
    }

    private void LoadRows()
    {
        try
        {
            using var db = SadrDb.New();
            var list = PrintSettingsService.LoadList(db)
                .Select(s => new PrintSettingRow { Id = s.Id, Info = s })
                .ToList();
            Grid.ItemsSource = list;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطا در خواندن تنظیمات", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private PrintSettingRow? Selected => Grid.SelectedItem as PrintSettingRow;

    private async Task AddAsync()
    {
        var info = new PrintSettingInfo { Name = "چیدمان جدید", Layout = 0, ShowDiscountColumn = true };
        var dlg = new PrintSettingEditWindow(info) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        try
        {
            await using var db = SadrDb.New();
            PrintSettingsService.Create(db, info, dlg.LogoBytes);
            LoadRows();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task EditAsync()
    {
        if (Selected is null) return;
        // snapshot so cancelling doesn't leak edits
        var info = new PrintSettingInfo
        {
            Id = Selected.Id, Name = Selected.Info.Name, Layout = Selected.Info.Layout,
            IsDefault = Selected.Info.IsDefault, TitleOverride = Selected.Info.TitleOverride,
            FooterNote = Selected.Info.FooterNote, ShowDiscountColumn = Selected.Info.ShowDiscountColumn,
            Logo = Selected.Info.Logo
        };
        var dlg = new PrintSettingEditWindow(info) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        try
        {
            await using var db = SadrDb.New();
            PrintSettingsService.Update(db, info.Id, info, dlg.LogoBytes, dlg.LogoChanged);
            LoadRows();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task DeleteAsync()
    {
        if (Selected is null) return;
        var count = 0;
        try
        {
            using var db = SadrDb.New();
            count = PrintSettingsService.LoadList(db).Count;
        }
        catch { }
        if (count <= 1)
        {
            MessageBox.Show(this, "حداقل یک تنظیم چاپ باید باقی بماند.", "حذف ممکن نیست",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show(this, $"«{Selected.Name}» حذف شود؟", "حذف تنظیم چاپ",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            await using var db = SadrDb.New();
            PrintSettingsService.Delete(db, Selected.Id);
            LoadRows();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SetDefaultAsync()
    {
        if (Selected is null) return;
        try
        {
            await using var db = SadrDb.New();
            PrintSettingsService.SetDefault(db, Selected.Id);
            LoadRows();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
