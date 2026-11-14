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
            if (Array.IndexOf(args, "--versione") >= 0) { Console.WriteLine($"IP-TRX-CTRL {AppVersion.Text} ({Updater.Rid})"); return; }
            // dopo un aggiornamento automatico: aspetta l'uscita della versione vecchia (porte, radio) e toglie i suoi resti
            Updater.AfterRestart(args);
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
