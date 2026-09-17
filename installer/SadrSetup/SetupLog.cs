using System;
using System.IO;

namespace SadrSetup;

/// <summary>Appends setup progress to setup.log next to the installer for support/troubleshooting.</summary>
internal static class SetupLog
{
    private static readonly object Gate = new();

    public static string LogPath { get; } = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "setup.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // logging must never break setup
        }
    }
}
