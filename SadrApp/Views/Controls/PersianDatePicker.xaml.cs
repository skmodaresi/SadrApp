using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SadrApp.Infrastructure;

namespace SadrApp.Views.Controls;

/// <summary>
/// Persian (Jalali) calendar date picker: editable yyyy/MM/dd text box plus a
/// drop-down calendar grid. Value is a Gregorian DateTime.
/// </summary>
public partial class PersianDatePicker : UserControl
{
    private static readonly PersianCalendar Cal = new();
    private static readonly string[] MonthNames =
    {
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    };

    private DateTime? _selected;
    private int _viewYear, _viewMonth;
    private bool _updatingText;

    public event EventHandler? ValueChanged;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(DateTime?), typeof(PersianDatePicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public DateTime? Value
    {
        get => (DateTime?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Watermark { get; set; } = "";

    public PersianDatePicker()
    {
        InitializeComponent();
        var now = DateTime.Today;
        _viewYear = Cal.GetYear(now);
        _viewMonth = Cal.GetMonth(now);

        BtnCalendar.Click += (_, _) => { SyncViewFromValue(); ShowPopup(); };
        BtnPrevMonth.Click += (_, _) => ShiftMonth(-1);
        BtnNextMonth.Click += (_, _) => ShiftMonth(1);
        BtnPrevYear.Click += (_, _) => { _viewYear--; RenderGrid(); };
        BtnNextYear.Click += (_, _) => { _viewYear++; RenderGrid(); };

        TextBox.LostFocus += (_, _) => CommitText();
        TextBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitText(); e.Handled = true; }
        };
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        if (!string.IsNullOrEmpty(Watermark)) TextBox.Tag = Watermark;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PersianDatePicker p) p.SyncTextFromValue();
    }

    private void SyncTextFromValue()
    {
        _selected = Value;
        _updatingText = true;
        TextBox.Text = PersianDate.ToPersian(_selected);
        _updatingText = false;
    }

    private void SyncViewFromValue()
    {
        var v = Value ?? DateTime.Today;
        _viewYear = Cal.GetYear(v);
        _viewMonth = Cal.GetMonth(v);
        RenderGrid();
    }

    private void ShowPopup()
    {
        RenderGrid();
        Popup.IsOpen = true;
    }

    private void ShiftMonth(int delta)
    {
        _viewMonth += delta;
        if (_viewMonth > 12) { _viewMonth = 1; _viewYear++; }
        if (_viewMonth < 1) { _viewMonth = 12; _viewYear--; }
        RenderGrid();
    }

    private void RenderGrid()
    {
        TitleText.Text = $"{MonthNames[_viewMonth - 1]} {_viewYear}";
        DaysGrid.Children.Clear();

        var daysInMonth = Cal.GetDaysInMonth(_viewYear, _viewMonth);
        var firstDay = Cal.ToDateTime(_viewYear, _viewMonth, 1, 0, 0, 0, 0);
        // DayOfWeek.Saturday == first column in Persian calendar
        int offset = ((int)firstDay.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7;

        for (int i = 0; i < offset; i++)
            DaysGrid.Children.Add(new TextBlock { Width = 36, Height = 30 });

        var selectedKey = Value.HasValue
            ? (Cal.GetYear(Value.Value), Cal.GetMonth(Value.Value), Cal.GetDayOfMonth(Value.Value))
            : ((int, int, int)?)null;

        for (int day = 1; day <= daysInMonth; day++)
        {
            var dayLocal = day;
            var btn = new Button
            {
                Content = dayLocal.ToString(),
                Style = (Style)Resources["DayBtn"],
                Tag = false
            };
            if (selectedKey == (_viewYear, _viewMonth, dayLocal))
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xED));
                btn.Foreground = Brushes.White;
            }
            btn.Click += (_, _) =>
            {
                var g = Cal.ToDateTime(_viewYear, _viewMonth, dayLocal, 0, 0, 0, 0);
                Value = g;
                _selected = g;
                _updatingText = true;
                TextBox.Text = PersianDate.ToPersian(g);
                _updatingText = false;
                Popup.IsOpen = false;
                ValueChanged?.Invoke(this, EventArgs.Empty);
            };
            DaysGrid.Children.Add(btn);
        }
    }

    /// <summary>Parses the text box content; on success updates Value. Invalid text is rejected.</summary>
    public void CommitText()
    {
        if (_updatingText) return;
        var text = TextBox.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            Value = null;
            ValueChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        var parsed = PersianDate.Parse(text);
        if (parsed is null)
        {
            MessageBox.Show("تاریخ نامعتبر است. قالب صحیح: ۱۴۰۴/۰۵/۰۱", "تاریخ", MessageBoxButton.OK, MessageBoxImage.Warning);
            _updatingText = true;
            TextBox.Text = PersianDate.ToPersian(Value);
            _updatingText = false;
            return;
        }
        Value = parsed;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
