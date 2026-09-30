using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SadrApp.Infrastructure;

/// <summary>Which UI surface a font setting applies to.</summary>
public static class FontScope
{
    public const string Menu = "Menu";   // menu bar (implicit Menu style)
    public const string Form = "Form";   // window/page titles and headings (H1/H2)
    public const string Label = "Label"; // input labels (FieldLabel)
    public const string Input = "Input"; // text boxes and combos (FieldInput/Input/Combo)
    public const string Grid = "Grid";   // data grids (GridHeader/DataGridRow)

    public static readonly string[] Scopes = { Menu, Form, Label, Input, Grid };

    public static string Title(string scope) => scope switch
    {
        Menu => "منوها",
        Form => "تیتر فرم‌ها و صفحات",
        Label => "برچسب فیلدها",
        Input => "فیلدهای ورودی",
        Grid => "جدول‌ها و گریدها",
        _ => scope
    };
}

/// <summary>Per-user font choices for the five UI surfaces.</summary>
public sealed class FontSettings
{
    public string MenuFamily { get; set; } = "Segoe UI";
    public double MenuSize { get; set; } = 13;
    public string FormFamily { get; set; } = "Segoe UI";
    public double FormSize { get; set; } = 18;
    public string LabelFamily { get; set; } = "Segoe UI";
    public double LabelSize { get; set; } = 12;
    public string InputFamily { get; set; } = "Segoe UI";
    public double InputSize { get; set; } = 13;
    public string GridFamily { get; set; } = "Segoe UI";
    public double GridSize { get; set; } = 13;

    public static FontSettings Defaults() => new();

    public FontSettings Clone() => (FontSettings)MemberwiseClone();
}

/// <summary>
/// Loads/saves the current user's font preferences (%AppData%\SadrApp\fonts-&lt;user&gt;.json)
/// and applies them to the shared application styles (H1, H2, FieldLabel, FieldInput,
/// Input, Combo, GridHeader, DataGridRow, Menu).
///
/// WPF seals a Style as soon as a control starts using it, so the styles are not mutated
/// in place: <see cref="Apply"/> replaces each resource entry with a rebuilt clone. Every
/// window/page opened afterwards picks the new fonts up; the menu bar of already-open
/// windows is refreshed directly through <see cref="ApplyToMenu"/>.
/// </summary>
public static class FontSettingsService
{
    /// <summary>The settings currently in effect (last loaded or applied).</summary>
    public static FontSettings Current { get; private set; } = FontSettings.Defaults();

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SadrApp",
            $"fonts-{Sanitize(UserSession.Username ?? "default")}.json");

    private static string Sanitize(string name) =>
        string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Reads the current user's saved fonts; missing or corrupt values fall back to defaults.</summary>
    public static FontSettings Load()
    {
        var d = FontSettings.Defaults();
        try
        {
            if (File.Exists(FilePath) &&
                JsonSerializer.Deserialize<FontSettings>(File.ReadAllText(FilePath)) is { } loaded)
            {
                loaded.MenuFamily = First(loaded.MenuFamily, d.MenuFamily);
                if (loaded.MenuSize <= 0) loaded.MenuSize = d.MenuSize;
                loaded.FormFamily = First(loaded.FormFamily, d.FormFamily);
                if (loaded.FormSize <= 0) loaded.FormSize = d.FormSize;
                loaded.LabelFamily = First(loaded.LabelFamily, d.LabelFamily);
                if (loaded.LabelSize <= 0) loaded.LabelSize = d.LabelSize;
                loaded.InputFamily = First(loaded.InputFamily, d.InputFamily);
                if (loaded.InputSize <= 0) loaded.InputSize = d.InputSize;
                loaded.GridFamily = First(loaded.GridFamily, d.GridFamily);
                if (loaded.GridSize <= 0) loaded.GridSize = d.GridSize;
                Current = loaded;
            }
            else
            {
                Current = d;
            }
        }
        catch
        {
            Current = d; // corrupt/unreadable file → defaults
        }
        return Current;
    }

    private static string First(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value!;

    /// <summary>Persists the settings for the current user and marks them current.</summary>
    public static void Save(FontSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath,
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        Current = settings;
    }

    /// <summary>Applies settings to the shared application styles (call after Load or Save).</summary>
    public static void Apply(FontSettings s)
    {
        Current = s;
        var res = Application.Current?.Resources;
        if (res is null) return;

        // H2 keeps its hierarchy below H1 (original pair: 18 / 14).
        var form2 = Math.Max(10, Math.Round(s.FormSize - 4));

        CloneWithFonts(res, "H1", s.FormFamily, s.FormSize);
        CloneWithFonts(res, "H2", s.FormFamily, form2);
        CloneWithFonts(res, "FieldLabel", s.LabelFamily, s.LabelSize);
        CloneWithFonts(res, "FieldInput", s.InputFamily, s.InputSize);
        CloneWithFonts(res, "GridHeader", s.GridFamily, s.GridSize);
        CloneWithFonts(res, typeof(DataGridRow), s.GridFamily, s.GridSize);
        CloneWithFonts(res, typeof(Menu), s.MenuFamily, s.MenuSize);

        // Input/Combo derive from FieldInput: rebuild them on top of the new base style
        // so their BasedOn chain does not keep pointing at the previous generation.
        RebuildDerived(res, "Input", typeof(TextBox), "FieldInput", s.InputFamily, s.InputSize);
        RebuildDerived(res, "Combo", typeof(ComboBox), "FieldInput", s.InputFamily, s.InputSize);
    }

    /// <summary>Live font refresh for a menu bar that is already shown on screen.</summary>
    public static void ApplyToMenu(Menu menu)
    {
        menu.FontFamily = new FontFamily(Current.MenuFamily);
        menu.FontSize = Current.MenuSize;
    }

    /// <summary>Refreshes the menu bar of every window that is already open on screen.</summary>
    public static void ApplyToOpenMenus()
    {
        if (Application.Current is null) return;
        foreach (var window in Application.Current.Windows.OfType<Window>())
            foreach (var menu in FindMenus(window))
                ApplyToMenu(menu);
    }

    private static IEnumerable<Menu> FindMenus(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Menu m) yield return m;
            foreach (var inner in FindMenus(child)) yield return inner;
        }
    }

    // --- style helpers --------------------------------------------------

    /// <summary>Copies a style (minus font setters) into a fresh unsealed clone with new fonts.</summary>
    private static void CloneWithFonts(ResourceDictionary res, object key, string family, double size)
    {
        if (res[key] is not Style old) return;
        var clone = new Style { TargetType = old.TargetType };
        if (old.BasedOn is { } based) clone.BasedOn = based;
        foreach (var setter in old.Setters.OfType<Setter>())
        {
            if (IsFontProp(setter.Property)) continue;
            clone.Setters.Add(new Setter(setter.Property, setter.Value));
        }
        AddFontSetters(clone, family, size);
        res[key] = clone;
    }

    /// <summary>Rebuilds a style that only exists to derive from a base style (Input/Combo).</summary>
    private static void RebuildDerived(ResourceDictionary res, string key, Type targetType,
        string baseKey, string family, double size)
    {
        if (res[baseKey] is not Style baseStyle) return;
        var style = new Style { TargetType = targetType, BasedOn = baseStyle };
        AddFontSetters(style, family, size);
        res[key] = style;
    }

    private static void AddFontSetters(Style style, string family, double size)
    {
        var isText = style.TargetType == typeof(TextBlock);
        style.Setters.Add(new Setter(isText ? TextBlock.FontSizeProperty : Control.FontSizeProperty, size));
        if (!string.IsNullOrWhiteSpace(family))
            style.Setters.Add(new Setter(isText ? TextBlock.FontFamilyProperty : Control.FontFamilyProperty,
                new FontFamily(family)));
    }

    private static bool IsFontProp(DependencyProperty p) =>
        p == TextBlock.FontSizeProperty || p == TextBlock.FontFamilyProperty ||
        p == Control.FontSizeProperty || p == Control.FontFamilyProperty;
}
