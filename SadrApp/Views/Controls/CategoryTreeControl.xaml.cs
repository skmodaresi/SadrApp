using System.Windows;
using System.Windows.Controls;
using SadrApp.ViewModels;

namespace SadrApp.Views.Controls;

public partial class CategoryTreeControl : UserControl
{
    public event EventHandler? AddRootClicked;
    public event EventHandler? AddChildClicked;
    public event EventHandler? RenameClicked;
    public event EventHandler? DeleteClicked;
    public event EventHandler? AttribsClicked;
    public event EventHandler? ReloadClicked;

    public CategoryTreeControl()
    {
        InitializeComponent();
        BtnAddRoot.Click += (_, _) => AddRootClicked?.Invoke(this, EventArgs.Empty);
        BtnAddChild.Click += (_, _) => AddChildClicked?.Invoke(this, EventArgs.Empty);
        BtnRename.Click += (_, _) => RenameClicked?.Invoke(this, EventArgs.Empty);
        BtnDelete.Click += (_, _) => DeleteClicked?.Invoke(this, EventArgs.Empty);
        BtnAttribs.Click += (_, _) => AttribsClicked?.Invoke(this, EventArgs.Empty);
        BtnReload.Click += (_, _) => ReloadClicked?.Invoke(this, EventArgs.Empty);
    }

    public CategoryNode? SelectedNode => Tree.SelectedItem as CategoryNode;

    public void ShowAttribsButton(bool visible) => BtnAttribs.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void SetRoots(System.Collections.IEnumerable roots) => Tree.ItemsSource = roots;
}
