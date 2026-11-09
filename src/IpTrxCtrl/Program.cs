using System;
using System.IO;
using Avalonia;
using IpTrxCtrl.Core;

namespace IpTrxCtrl
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) => LogError(e.ExceptionObject?.ToString());
            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                LogError(ex.ToString());
                throw;
            }
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        public static void LogError(string? text)
        {
            try { File.AppendAllText(Path.Combine(AppSettings.Folder, "errori.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}"); } catch { }
        }
    }
}
