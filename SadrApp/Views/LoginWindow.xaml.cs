using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;

namespace SadrApp.Views;

public partial class LoginWindow : Window
{
    private int _attempts;

    public LoginWindow()
    {
        InitializeComponent();
        // WPF's PasswordBox reverses stored character order under RTL flow direction.
        // The XAML attribute alone proved insufficient here — enforce LTR in code too.
        TxtPassword.FlowDirection = FlowDirection.LeftToRight;
        TxtReveal.FlowDirection = FlowDirection.LeftToRight;
        BtnLogin.Click += async (_, _) => await TryLoginAsync();
        BtnExit.Click += (_, _) => { DialogResult = false; Close(); };
        TxtPassword.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { await TryLoginAsync(); } };
        // Two-way sync between the masked PasswordBox and the reveal TextBox.
        // Both directions must be guarded with _syncing: an unguarded write-back like
        // TxtPassword.Password = TxtReveal.Text on every keystroke resets the PasswordBox
        // caret to position 0, so each next character inserts at the FRONT and the typed
        // text ends up reversed ("admin" -> "nimda").
        TxtReveal.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            _syncing = true;
            TxtPassword.Password = TxtReveal.Text;
            _syncing = false;
        };
        TxtPassword.PasswordChanged += (_, _) =>
        {
            if (_syncing) return;
            _syncing = true;
            TxtReveal.Text = TxtPassword.Password;
            _syncing = false;
        };
    }

    private bool _syncing;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private async Task TryLoginAsync()
    {
        ErrorText.Text = " ";
        var username = TxtUsername.Text.Trim();
        var password = TxtPassword.Password;

        if (username.Length == 0 || password.Length == 0)
        {
            ErrorText.Text = "نام کاربری و رمز عبور را وارد کنید.";
            return;
        }

        BtnLogin.IsEnabled = false;
        try
        {
            await using var db = SadrDb.New();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
            {
                _attempts++;
                ErrorText.Text = $"نام کاربری یا رمز عبور نادرست است. (تلاش {_attempts} از 5)";
                if (_attempts >= 5)
                {
                    MessageBox.Show("پنج تلاش ناموفق — برنامه بسته می‌شود.", "ورود ناموفق",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    DialogResult = false;
                    Close();
                }
                return;
            }

            UserSession.SignIn(user.Id, user.Username, user.Role);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = "خطا در اتصال به پایگاه داده: " + ex.Message;
        }
        finally
        {
            BtnLogin.IsEnabled = true;
        }
    }

    private void ChkShow_Changed(object sender, RoutedEventArgs e)
    {
        if (TxtPassword is null || TxtReveal is null) return; // XAML still loading
        _syncing = true;
        bool show = ChkShow.IsChecked == true;
        TxtReveal.Text = TxtPassword.Password;
        TxtReveal.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        TxtPassword.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (show) TxtReveal.Focus(); else TxtPassword.Focus();
        _syncing = false;
    }
}
