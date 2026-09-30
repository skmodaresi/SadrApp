using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SadrApp.Infrastructure;

namespace SadrApp.Views;

/// <summary>
/// Lets the user pick font family + size for menu, headings, labels, inputs and grids —
/// all together (apply-all) or per scope — with a live preview, saved per user.
/// </summary>
public partial class FontSettingsWindow : Window
{
    /// <summary>Editor rows in FontScope.Scopes order: menu, form, label, input, grid.</summary>
    private readonly (ComboBox Family, TextBox Size, string Scope)[] _rows;

    public FontSettingsWindow()
    {
        InitializeComponent();
        _rows = new[]
        {
            (CmbMenuFamily, NumMenuSize, FontScope.Menu),
            (CmbFormFamily, NumFormSize, FontScope.Form),
            (CmbLabelFamily, NumLabelSize, FontScope.Label),
            (CmbInputFamily, NumInputSize, FontScope.Input),
            (CmbGridFamily, NumGridSize, FontScope.Grid)
        };
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var families = Fonts.SystemFontFamilies
            .OrderBy(f => f.Source, StringComparer.OrdinalIgnoreCase)
            .Select(f => f.Source)
            .ToList();

        foreach (var (combo, size, _) in _rows)
        {
            combo.ItemsSource = families;
            combo.SelectionChanged += (_, _) => Preview();
            size.TextChanged += (_, _) => Preview();
        }

        LoadFrom(FontSettingsService.Current);

        // Apply-all: copy one family+size into every scope row.
        CmbAllFamily.ItemsSource = families;
        CmbAllFamily.Text = FontSettingsService.Current.MenuFamily;
        NumAllSize.Text = FontSettingsService.Current.MenuSize.ToString("0.#");
        BtnApplyAll.Click += (_, _) =>
        {
            var family = string.IsNullOrWhiteSpace(CmbAllFamily.Text) ? "Segoe UI" : CmbAllFamily.Text.Trim();
            if (!double.TryParse(NumAllSize.Text, out var size) || size < 8 || size > 72) size = 13;
            size = Math.Round(size, 1);
            foreach (var (combo, sizeBox, _) in _rows)
            {
                if (combo.Items.Contains(family)) combo.SelectedItem = family; else combo.Text = family;
                sizeBox.Text = size.ToString("0.#");
            }
            Preview();
        };

        BtnSave.Click += async (_, _) => await SaveAsync();
        BtnReset.Click += (_, _) => { LoadFrom(FontSettings.Defaults()); Preview(); };
        BtnCancel.Click += (_, _) => DialogResult = false;
        Preview();
    }

    /// <summary>Fills one family combo + size box; unknown families are typed into the combo text.</summary>
    private static void SetRow(ComboBox combo, TextBox sizeBox, string family, double size)
    {
        if (combo.ItemsSource is IEnumerable<string> items && items.Contains(family))
            combo.SelectedItem = family;
        else
            combo.Text = family;
        sizeBox.Text = size.ToString("0.#");
    }

    private void LoadFrom(FontSettings s)
    {
        SetRow(CmbMenuFamily, NumMenuSize, s.MenuFamily, s.MenuSize);
        SetRow(CmbFormFamily, NumFormSize, s.FormFamily, s.FormSize);
        SetRow(CmbLabelFamily, NumLabelSize, s.LabelFamily, s.LabelSize);
        SetRow(CmbInputFamily, NumInputSize, s.InputFamily, s.InputSize);
        SetRow(CmbGridFamily, NumGridSize, s.GridFamily, s.GridSize);
    }

    /// <summary>Reads the five rows; empty/invalid sizes fall back to the current applied value.</summary>
    private (string family, double size)[] Collect()
    {
        var cur = FontSettingsService.Current;
        var sizes = new[] { cur.MenuSize, cur.FormSize, cur.LabelSize, cur.InputSize, cur.GridSize };
        var result = new (string, double)[_rows.Length];
        for (var i = 0; i < _rows.Length; i++)
        {
            var family = string.IsNullOrWhiteSpace(_rows[i].Family.Text)
                ? cur.GetType().GetProperty(FontScope.Scopes[i] + "Family")?.GetValue(cur) as string ?? "Segoe UI"
                : _rows[i].Family.Text.Trim();
            if (!double.TryParse(_rows[i].Size.Text, out var size) || size < 8 || size > 72)
                size = sizes[i];
            result[i] = (family, Math.Round(size, 1));
        }
        return result;
    }

    private void Preview()
    {
        if (_rows is null) return;
        var f = Collect();
        Set(PvMenu, f[0]);   // menu
        Set(PvForm, f[1]);   // headings
        Set(PvLabel, f[2]);  // labels
        Set(PvInput, f[3]);  // inputs (TextBox)
        Set(PvBtn, f[3]);    // button
        Set(PvGridHead, f[4]); // grid header
        Set(PvGrid, f[4]);     // grid rows
    }

    /// <summary>Sets family+size on any element via the shared TextElement attached properties.</summary>
    private static void Set(FrameworkElement fe, (string family, double size) font)
    {
        fe.SetValue(TextElement.FontFamilyProperty, new FontFamily(font.family));
        fe.SetValue(TextElement.FontSizeProperty, font.size);
    }

    private async Task SaveAsync()
    {
        try
        {
            var f = Collect();
            var settings = new FontSettings
            {
                MenuFamily = f[0].family, MenuSize = f[0].size,
                FormFamily = f[1].family, FormSize = f[1].size,
                LabelFamily = f[2].family, LabelSize = f[2].size,
                InputFamily = f[3].family, InputSize = f[3].size,
                GridFamily = f[4].family, GridSize = f[4].size
            };
            FontSettingsService.Save(settings);
            FontSettingsService.Apply(settings);
            FontSettingsService.ApplyToOpenMenus();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "ذخیرهٔ تنظیمات فونت ممکن نشد:\n" + ex.Message,
                "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        await Task.CompletedTask;
    }
}
