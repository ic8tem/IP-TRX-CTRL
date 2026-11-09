using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using IpTrxCtrl.Core;
using IpTrxCtrl.UI;

namespace IpTrxCtrl
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            Resources["MonoFont"] = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, Liberation Mono, monospace");
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // un errore in un pulsante o nel disegno non deve chiudere il programma: lo registra e prosegue
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                Program.LogError(e.Exception.ToString());
                e.Handled = true;
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Program.LogError(e.Exception.ToString());
                e.SetObserved();
            };
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var s = AppSettings.Load();
                Themes.Apply(s.Theme);
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
                if (!s.FirstRunDone || !s.DeclarationAccepted)
                {
                    var first = new FirstRunWindow(s);
                    first.Closed += (_, _) =>
                    {
                        if (first.Completed)
                        {
                            var main = new MainWindow(s);
                            main.Closed += (_, _) => desktop.Shutdown();
                            desktop.MainWindow = main;
                            main.Show();
                        }
                        else desktop.Shutdown();
                    };
                    desktop.MainWindow = first;
                }
                else
                {
                    var main = new MainWindow(s);
                    main.Closed += (_, _) => desktop.Shutdown();
                    desktop.MainWindow = main;
                }
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
