using System.Windows;

namespace SadrApp.Views;

public partial class StartupDialog : Window
{
    public StartupDialog(string error)
    {
        InitializeComponent();
        ErrorText.Text = error;
        BtnSettings.Click += (_, _) => { DialogResult = true; };
        BtnExit.Click += (_, _) => { DialogResult = false; };
    }
}
