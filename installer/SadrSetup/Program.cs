using System.Security.Principal;

namespace SadrSetup;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Installing machine-wide components (registry HKLM, Program Files) needs elevation.
        if (!IsAdmin())
        {
            MessageBox.Show("برای نصب، برنامه را با دسترسی Run as administrator اجرا کنید.",
                "دسترسی لازم است", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }

    private static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
