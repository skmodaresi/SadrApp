using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SadrApp.Infrastructure;

namespace SadrApp.Views.Controls;

/// <summary>One editable field definition for the generic editor window.</summary>
public class FieldSpec
{
    public string Label { get; set; } = "";
    public bool Required { get; set; }
    public int TextWidth { get; set; } = 320;
    public string? Text { get; set; }
    public string? Watermark { get; set; }
    public bool IsMultiLine { get; set; }
    public bool IsNumeric { get; set; }
    public bool IsPassword { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsDate { get; set; }

    /// <summary>Max characters the input accepts (0 = unlimited). Use for nvarchar(n) columns.</summary>
    public int MaxLength { get; set; }

    /// <summary>When set, renders a combo bound to key/value pairs.</summary>
    public List<KeyValuePair<int, string>>? Choices { get; set; }
    public int? SelectedKey { get; set; }
    public bool ChoicesAreOptional { get; set; }

    /// <summary>When set, renders one check box per choice (multi-select, comma-separated output).</summary>
    public List<KeyValuePair<int, string>>? MultiChoices { get; set; }
    public List<int> SelectedKeys { get; set; } = new();
    /// <summary>Message shown when nothing is checked (only with Required).</summary>
    public string? EmptyMessage { get; set; }

    /// <summary>Index of a Choice_ field that controls which options this multi-choice shows.</summary>
    public int? MasterChoiceIndex { get; set; }
    /// <summary>Options per master-combo key, used together with MasterChoiceIndex.</summary>
    public Dictionary<int, List<KeyValuePair<int, string>>>? ChoicesByMaster { get; set; }
    /// <summary>Master key used for the first fill when the master combo has no selection yet.</summary>
    public int InitialMasterKey { get; set; }

    /// <summary>When set, renders a check box.</summary>
    public bool? CheckValue { get; set; }

    /// <summary>Initial Gregorian value for date fields.</summary>
    public DateTime? DateValue { get; set; }

    public FieldSpec() { }

    public static FieldSpec Text_(string label, string? value = null, bool required = false, int width = 320, int maxLength = 0) =>
        new() { Label = label, Text = value, Required = required, TextWidth = width, MaxLength = maxLength };

    public static FieldSpec Numeric_(string label, decimal? value = null, bool required = false) =>
        new() { Label = label, IsNumeric = true, Text = value?.ToString(CultureInfo.InvariantCulture) ?? "", Required = required };

    public static FieldSpec Multi_(string label, string? value = null, bool required = false) =>
        new() { Label = label, Text = value, Required = required, IsMultiLine = true, TextWidth = 420 };

    public static FieldSpec Choice_(string label, List<KeyValuePair<int, string>> choices, int? selected = null, bool optional = false) =>
        new() { Label = label, Choices = choices, SelectedKey = selected, ChoicesAreOptional = optional };

    /// <summary>Multi-select via check boxes; persisted as comma-separated keys. The user sees the value texts.</summary>
    public static FieldSpec MultiChoice_(string label, List<KeyValuePair<int, string>> choices,
        string? commaSeparatedKeys = null, bool required = false, string? emptyMessage = null) =>
        new()
        {
            Label = label,
            MultiChoices = choices,
            SelectedKeys = ParseKeys(commaSeparatedKeys),
            Required = required,
            EmptyMessage = emptyMessage,
            TextWidth = 420
        };

    /// <summary>Multi-select whose option list follows another (master) choice field, e.g. tasks of the selected project.</summary>
    public static FieldSpec MultiChoice_(string label, Dictionary<int, List<KeyValuePair<int, string>>> choicesByMaster,
        int masterChoiceIndex, string? commaSeparatedKeys = null, int initialMasterKey = 0,
        bool required = false, string? emptyMessage = null) =>
        new()
        {
            Label = label,
            ChoicesByMaster = choicesByMaster,
            MasterChoiceIndex = masterChoiceIndex,
            InitialMasterKey = initialMasterKey,
            MultiChoices = new List<KeyValuePair<int, string>>(),
            SelectedKeys = ParseKeys(commaSeparatedKeys),
            Required = required,
            EmptyMessage = emptyMessage,
            TextWidth = 420
        };

    private static List<int> ParseKeys(string? commaSeparatedKeys) =>
        (commaSeparatedKeys ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => int.TryParse(s, out _))
            .Select(int.Parse).Distinct().ToList();

    public static FieldSpec Check_(string label, bool value = false) =>
        new() { Label = label, CheckValue = value };

    public static FieldSpec Date_(string label, DateTime? value = null, bool required = false) =>
        new() { Label = label, IsDate = true, DateValue = value, Required = required };
}

public partial class FieldEditorWindow : Window
{
    private const double LabelColumnWidth = 150;

    private readonly List<FieldSpec> _specs;
    private readonly List<FrameworkElement> _inputs = new();

    public FieldEditorWindow(string title, IEnumerable<FieldSpec> specs)
    {
        InitializeComponent();
        _specs = specs.ToList();
        HeaderTitle.Text = title;
        Title = title;
        BuildFields();
        BtnSave.Click += (_, _) => Save();

        // Ctrl+S = ذخیره (Esc را دکمه انصرف با IsCancel انجام می‌دهد)
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                Save();
                e.Handled = true;
            }
        };
    }

    private void BuildFields()
    {
        // Two field-pairs per row: [label|input|label|input] — long forms stay compact.
        FieldsPanel.ColumnDefinitions.Clear();
        FieldsPanel.RowDefinitions.Clear();
        FieldsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelColumnWidth) });
        FieldsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        FieldsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelColumnWidth) });
        FieldsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Rows are created on demand: paired fields share a row, multiline fields get a full row.
        int row = -1;
        bool rightUsed = false;  // current row's right (first) slot occupied
        bool leftUsed = false;   // current row's left (second) slot occupied

        for (int i = 0; i < _specs.Count; i++)
        {
            var spec = _specs[i];
            bool fullRow = spec.IsMultiLine || spec.MultiChoices is not null;

            bool needNewRow = row < 0 || leftUsed || (fullRow && rightUsed);
            if (needNewRow) { row++; rightUsed = false; leftUsed = false; }

            int colBase;
            if (fullRow)
            {
                colBase = 0;         // label far right, input stretches over the rest
                leftUsed = true;     // row is done after a full-row field
            }
            else if (rightUsed)
            {
                colBase = 2;         // left pair
                leftUsed = true;
            }
            else
            {
                colBase = 0;         // right pair
                rightUsed = true;
            }

            while (FieldsPanel.RowDefinitions.Count <= row)
                FieldsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = spec.Required ? spec.Label + " *" : spec.Label,
                Style = (Style)FindResource("FieldLabel"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 3)
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, colBase);
            if (fullRow) Grid.SetColumnSpan(label, 1);
            FieldsPanel.Children.Add(label);

            var host = new Grid { Margin = new Thickness(10, 2, 0, 2) };
            Grid.SetRow(host, row);
            Grid.SetColumn(host, colBase + 1);
            if (fullRow) Grid.SetColumnSpan(host, 3); // input stretches across label+input of the far column
            FieldsPanel.Children.Add(host);

            FrameworkElement input;
            if (spec.CheckValue is not null)
            {
                input = new CheckBox { Content = spec.Label, IsChecked = spec.CheckValue, VerticalAlignment = VerticalAlignment.Center };
            }
            else if (spec.IsDate)
            {
                input = new PersianDatePicker
                {
                    Value = spec.DateValue,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
            }
            else if (spec.MultiChoices is not null)
            {
                // Multi-select: one check box per choice; SelectedKeys holds the checked keys.
                var sp = new StackPanel { Orientation = Orientation.Vertical };
                input = sp;

                int? initialMasterKey = null;
                if (spec.MasterChoiceIndex is int mi0 && mi0 >= 0 && mi0 < _specs.Count
                    && _inputs[mi0] is ComboBox initialMaster)
                    initialMasterKey = (initialMaster.SelectedValue as int?) ?? spec.InitialMasterKey;
                var initialChoices = initialMasterKey is int imk
                        ? ChoicesForMaster(spec, imk)
                        : spec.MultiChoices ?? new List<KeyValuePair<int, string>>();
                FillMultiChoices(sp, spec, initialChoices, keepChecked: false);

                if (spec.MasterChoiceIndex is int mi && mi >= 0 && mi < _specs.Count
                    && _inputs[mi] is ComboBox master)
                    master.SelectionChanged += (_, _) =>
                        FillMultiChoices(sp, spec, ChoicesForMaster(spec, master.SelectedValue as int?), keepChecked: true);
            }
            else if (spec.Choices is not null)
            {
                var cmb = new ComboBox { Style = (Style)FindResource("Combo"), HorizontalAlignment = HorizontalAlignment.Stretch };
                var items = new List<object>();
                if (spec.ChoicesAreOptional) items.Add(new ComboItem { Key = null, Text = "— انتخاب نشده —" });
                foreach (var kv in spec.Choices) items.Add(new ComboItem { Key = kv.Key, Text = kv.Value });
                cmb.ItemsSource = items;
                cmb.DisplayMemberPath = "Text";
                cmb.SelectedValuePath = "Key";
                cmb.SelectedValue = spec.SelectedKey;
                input = cmb;
            }
            else
            {
                var tb = new TextBox
                {
                    Style = (Style)FindResource("Input"),
                    Text = spec.Text ?? "",
                    MaxLength = spec.MaxLength,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (spec.IsMultiLine)
                {
                    tb.AcceptsReturn = true;
                    tb.TextWrapping = TextWrapping.Wrap;
                    tb.Height = 56;
                    tb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                }
                if (spec.IsNumeric)
                {
                    // Live thousands separators; parsing stays separator-tolerant (NumberInput.ParseDecimal).
                    NumberInput.Attach(tb);
                }
                if (spec.IsPassword) tb.FontFamily = new FontFamily("Consolas");
                if (spec.IsReadOnly) tb.IsEnabled = false;
                input = tb;
            }

            host.Children.Add(input);
            HookEnterToAdvance(input, spec.IsMultiLine);
            _inputs.Add(input);
        }
    }

    /// <summary>Option list of a master-bound multi-choice for the given master key.</summary>
    private List<KeyValuePair<int, string>> ChoicesForMaster(FieldSpec spec, int? masterKey) =>
        masterKey is int k && spec.ChoicesByMaster is not null && spec.ChoicesByMaster.TryGetValue(k, out var list)
            ? list
            : new List<KeyValuePair<int, string>>();

    /// <summary>Rebuilds the check boxes of a multi-choice field for the given options.</summary>
    private void FillMultiChoices(StackPanel sp, FieldSpec spec, List<KeyValuePair<int, string>> choices, bool keepChecked)
    {
        var checkedKeys = keepChecked ? CurrentCheckedKeys(sp) : new HashSet<int>(spec.SelectedKeys);
        sp.Children.Clear();
        foreach (var kv in choices)
        {
            var cbx = new CheckBox
            {
                Content = kv.Value,
                IsChecked = checkedKeys.Contains(kv.Key),
                Margin = new Thickness(0, 3, 0, 3),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            cbx.Tag = kv.Key;
            sp.Children.Add(cbx);
        }
        if (choices.Count == 0)
            sp.Children.Add(new TextBlock
            {
                Text = "—",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x91, 0xA5)),
                Margin = new Thickness(0, 3, 0, 3)
            });
    }

    private static HashSet<int> CurrentCheckedKeys(StackPanel sp) =>
        sp.Children.OfType<CheckBox>().Where(c => c.IsChecked == true)
            .Select(c => (int)c.Tag!).ToHashSet();

    /// <summary>Enter (or Ctrl+Enter on multiline) jumps to the next field.</summary>
    private void HookEnterToAdvance(FrameworkElement input, bool isMultiLine)
    {
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (isMultiLine && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return; // Enter = newline
            input.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        };
    }

    private void Save()
    {
        for (int i = 0; i < _specs.Count; i++)
        {
            var spec = _specs[i];
            if (spec.CheckValue is not null) continue;
            if (spec.MultiChoices is not null)
            {
                if (spec.Required && GetMultiChoice(i).Count == 0)
                {
                    MessageBox.Show(spec.EmptyMessage ?? $"مقدار «{spec.Label}» الزامی است.", "اعتبارسنجی",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (spec.MasterChoiceIndex is not null)
                {
                    // Pre-checks must belong to the master's current option list.
                    var mi = spec.MasterChoiceIndex.Value;
                    var masterKey = mi >= 0 && mi < _specs.Count && _inputs[mi] is ComboBox m
                        ? m.SelectedValue as int? : null;
                    var valid = ChoicesForMaster(spec, masterKey).Select(c => c.Key).ToHashSet();
                    var kept = GetMultiChoice(i).Where(valid.Contains).ToList();
                    if (kept.Count != GetMultiChoice(i).Count)
                        MessageBox.Show("برخی پیش‌نیازهای علامت‌خورده به پروژهٔ فعلی تعلق ندارند و کنار گذاشته شدند.",
                            "وظایف پیش‌نیاز", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                continue;
            }
            if (spec.IsDate)
            {
                if (spec.Required && _inputs[i] is PersianDatePicker p && p.Value is null)
                {
                    MessageBox.Show($"مقدار «{spec.Label}» الزامی است.", "اعتبارسنجی", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                continue;
            }
            var value = GetStringValue(spec, _inputs[i]);
            if (spec.Required && string.IsNullOrWhiteSpace(value))
            {
                MessageBox.Show($"مقدار «{spec.Label}» الزامی است.", "اعتبارسنجی", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        DialogResult = true;
    }

    public string? GetText(int index)
    {
        var spec = _specs[index];
        return GetStringValue(spec, _inputs[index]);
    }

    public decimal? GetNumber(int index) =>
        NumberInput.ParseDecimal(GetText(index));

    public bool GetCheck(int index) => _inputs[index] is CheckBox cb && cb.IsChecked == true;

    public int? GetChoice(int index) => _inputs[index] is ComboBox cmb ? cmb.SelectedValue as int? : null;

    /// <summary>Checked keys of a multi-choice field, in the dialog's display order.</summary>
    public List<int> GetMultiChoice(int index) =>
        _inputs[index] is StackPanel sp
            ? sp.Children.OfType<CheckBox>().Where(c => c.IsChecked == true)
                .Select(c => (int)c.Tag!).ToList()
            : new List<int>();

    /// <summary>Comma-separated checked keys of a multi-choice field (storage format).</summary>
    public string GetMultiChoiceText(int index) => string.Join(",", GetMultiChoice(index));

    /// <summary>Gregorian value of a date field (null when empty).</summary>
    public DateTime? GetDate(int index) => _inputs[index] is PersianDatePicker p ? p.Value : null;

    private static string? GetStringValue(FieldSpec spec, FrameworkElement input) =>
        input switch
        {
            TextBox tb => tb.Text,
            ComboBox => null,
            CheckBox => null,
            PersianDatePicker => null,
            _ => null
        };
}
