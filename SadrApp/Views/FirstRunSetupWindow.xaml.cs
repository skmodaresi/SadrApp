using System.Windows;
using SadrApp.Infrastructure;

namespace SadrApp.Views;

/// <summary>
/// First-run wizard shown when the Users table is empty: collects the main admin
/// username/password and creates the account via DbBootstrapper.CreateAdminUser.
/// </summary>
public partial class FirstRunSetupWindow : Window
{
    public FirstRunSetupWindow()
    {
        InitializeComponent();
        BtnCreate.Click += (_, _) => Create();
        BtnExit.Click += (_, _) => { DialogResult = false; Close(); };
        Loaded += (_, _) => TxtPassword.Focus();
    }

    private void Create()
    {
        ErrorText.Text = " ";
        var username = TxtUsername.Text.Trim();
        var password = TxtPassword.Password;
        var password2 = TxtPassword2.Password;

        if (username.Length < 3)
        {
            ErrorText.Text = "نام کاربری باید حداقل ۳ نویسه باشد.";
            return;
        }
        if (password.Length < 6)
        {
            ErrorText.Text = "رمز عبور باید حداقل ۶ نویسه باشد.";
            return;
        }
        if (password != password2)
        {
            ErrorText.Text = "رمز عبور و تکرار آن یکسان نیستند.";
            return;
        }

        try
        {
            DbBootstrapper.CreateAdminUser(username, password, TxtEmail.Text);
            MessageBox.Show("کاربر مدیر ایجاد شد. اکنون می‌توانید وارد سامانه شوید.",
                "راه‌اندازی اولیه", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = "خطا در ایجاد کاربر: " + ex.Message;
        }
    }
}
