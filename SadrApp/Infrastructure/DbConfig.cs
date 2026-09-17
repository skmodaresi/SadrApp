using System.IO;
using System.Text;
using System.Xml.Linq;

namespace SadrApp.Infrastructure;

/// <summary>
/// Saves/loads the SQL Server connection string from Connection.dat next to the exe.
/// The file is plain XML so it can be edited manually on the customer machine.
/// </summary>
public static class DbConfig
{
    public static string ConfigPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Connection.dat");

    private const string Fallback =
        @"Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=SadrApp;Integrated Security=True;Pooling=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Command Timeout=0";

    public static string Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var doc = XDocument.Load(ConfigPath);
                var cs = doc.Root?.Element("ConnectionString")?.Value;
                if (!string.IsNullOrWhiteSpace(cs)) return cs;
            }
        }
        catch
        {
            // fall through to default
        }
        return Fallback;
    }

    public static void Save(string connectionString)
    {
        var doc = new XDocument(
            new XComment("تنظیمات اتصال به پایگاه داده SQL Server - این فایل را روی سیستم مشتری ویرایش کنید"),
            new XElement("DatabaseConnection",
                new XElement("ConnectionString", connectionString)));
        File.WriteAllText(ConfigPath, doc.ToString(), Encoding.UTF8);
    }

    public static bool Exists() => File.Exists(ConfigPath);
}
