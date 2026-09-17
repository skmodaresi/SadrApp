using System.Windows;
using System.Windows.Controls;

namespace SadrApp.Views.Controls;

public partial class MultiCheckDialog : Window
{
    private readonly List<CheckRow> _rows = new();

    public MultiCheckDialog(string title, IEnumerable<KeyValuePair<int, string>> items, IEnumerable<int> checkedIds)
    {
        InitializeComponent();
        HeaderTitle.Text = title;
        Title = title;
        var checkedSet = new HashSet<int>(checkedIds);
        foreach (var kv in items)
        {
            var row = new CheckRow { Id = kv.Key, Name = kv.Value, IsChecked = checkedSet.Contains(kv.Key) };
            _rows.Add(row);
            var cb = new CheckBox
            {
                Content = row.Name,
                IsChecked = row.IsChecked,
                Margin = new Thickness(4, 3, 4, 3)
            };
            cb.Checked += (_, _) => row.IsChecked = true;
            cb.Unchecked += (_, _) => row.IsChecked = false;
            Items.Items.Add(cb);
        }
        BtnOk.Click += (_, _) =>
        {
            DialogResult = true;
        };
    }

    public List<int> SelectedIds =>
        _rows.Where(r => r.IsChecked).Select(r => r.Id).ToList();

    private class CheckRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsChecked { get; set; }
    }
}
