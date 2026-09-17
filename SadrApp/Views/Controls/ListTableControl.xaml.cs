using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SadrApp.ViewModels;

namespace SadrApp.Views.Controls;

public partial class ListTableControl : UserControl
{
    public event EventHandler? NewClicked;
    public event EventHandler? EditClicked;
    public event EventHandler? DeleteClicked;
    public event EventHandler? RefreshClicked;
    public event EventHandler? RowDoubleClicked;
    public event EventHandler? PrintClicked;

    public ListTableControl()
    {
        InitializeComponent();
        BtnNew.Click += (_, _) => NewClicked?.Invoke(this, EventArgs.Empty);
        BtnEdit.Click += (_, _) => EditClicked?.Invoke(this, EventArgs.Empty);
        BtnDelete.Click += (_, _) => DeleteClicked?.Invoke(this, EventArgs.Empty);
        BtnRefresh.Click += (_, _) => RefreshClicked?.Invoke(this, EventArgs.Empty);
        BtnPrint.Click += (_, _) => PrintClicked?.Invoke(this, EventArgs.Empty);
        Grid.MouseDoubleClick += (_, _) => RowDoubleClicked?.Invoke(this, EventArgs.Empty);
        SearchBox.TextChanged += (_, _) => ApplyFilter();
    }

    public DataGrid InnerGrid => Grid;

    public object? SelectedRow => Grid.SelectedItem;

    public void SetColumns(params DataGridColumn[] columns)
    {
        Grid.Columns.Clear();
        foreach (var c in columns) Grid.Columns.Add(c);
    }

    /// <summary>Shows the hidden extra column with a page-specific header.</summary>
    public void ShowExtraColumn(string header)
    {
        ExtraColumn.Header = header;
        ExtraColumn.Visibility = Visibility.Visible;
    }

    /// <summary>Makes the toolbar print button visible (pages that support printing).</summary>
    public void ShowPrintButton()
    {
        BtnPrint.Visibility = Visibility.Visible;
    }

    public void SetRows(IEnumerable rows)
    {
        Grid.ItemsSource = rows;
        ApplyFilter();
    }

    public void RefreshRows()
    {
        var view = Grid.ItemsSource as ICollectionView;
        view?.Refresh();
    }

    private void ApplyFilter()
    {
        if (Grid.ItemsSource is not ICollectionView view) return;
        var q = SearchBox.Text?.Trim() ?? "";
        if (q.Length == 0)
        {
            view.Filter = null;
            return;
        }
        view.Filter = o =>
        {
            var row = o as RowBase;
            if (row is null) return false;
            var hay = $"{row.Title} {row.Code} {row.Description} {row.Extra}";
            return hay.Contains(q, StringComparison.OrdinalIgnoreCase);
        };
    }
}
