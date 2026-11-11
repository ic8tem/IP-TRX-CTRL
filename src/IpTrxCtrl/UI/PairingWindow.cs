using System;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using IpTrxCtrl.Core;
using IpTrxCtrl.Net;

namespace IpTrxCtrl.UI
{
    /// <summary>
    /// Abbinamento tra i due PC con il registro del dialogo con i broker MQTT e con il server
    /// (▶ inviato, ◀ ricevuto, ✗ errore, ✓ completato).
    /// </summary>
    public sealed class PairingWindow : Window
    {
        private readonly Node _node;
        private readonly AppSettings _s;
        private readonly CancellationTokenSource _cts = new();
        private readonly StackPanel _log = new() { Spacing = 0 };
        private readonly ScrollViewer _logScroll;
        private readonly StringBuilder _logText = new();
        private readonly TextBlock _code, _result;
        private readonly TextBox _input = new() { FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"), FontSize = 20, Watermark = "es. 482 913 0571  oppure  4829 1305 7712", MaxLength = 24 };
        private readonly Button _pair, _copy, _retry;
        private bool _completed;

        public PairingWindow(Node node, AppSettings s)
        {
            _node = node;
            _s = s;
            Title = "Abbinamento";
            Width = 640;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            bool server = s.Role == NodeRole.Server;
            _code = Ui.Mono("Preparo il numero…", 30);
            _code.FontWeight = FontWeight.Bold;
            _code.Foreground = this.FindResource("B.Accent") as IBrush;
            _code.TextWrapping = TextWrapping.Wrap;
            _result = new TextBlock { FontSize = 14, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            _pair = Ui.Btn("Abbina", true);
            _copy = Ui.Btn("Copia numero");
            _copy.IsEnabled = false;
            _retry = Ui.Btn("Riprova");

            var root = new StackPanel { Margin = new Thickness(30), Spacing = 0 };
            root.Children.Add(Ui.H1("Abbinamento con l'altro PC"));
            var intro = Ui.Sub(server
                ? "Detta questo numero a chi usa il client. Vale 10 minuti e una sola volta; non contiene l'indirizzo del server, "
                  + "che il client riceve cifrato. Dopo l'abbinamento chiavi, porte e riconnessioni sono automatiche."
                : "Scrivi il numero di 10 cifre che compare sul server (vale 10 minuti) oppure il codice fisso del server (12 cifre). "
                  + "Ogni server abbinato resta nell'elenco del client: dopo si sceglie da lì, senza numero.", 14);
            intro.Margin = new Thickness(0, 6, 0, 20);
            root.Children.Add(intro);

            if (server)
            {
                root.Children.Add(Ui.Inset(Ui.Col(8, Ui.Lbl("Numero di abbinamento da dettare al client (10 minuti, una sola volta)"), _code, _copy)));
                _copy.Click += async (_, _) => await Dialogs.CopyText(this, _code.Text ?? "");
                if (node.FixedCodeEnabled && node.FixedCode.Length > 0)
                {
                    var fix = Ui.Sub($"Codice fisso del server, sempre valido: {node.FixedCode}  (si gestisce nelle Impostazioni)", 13);
                    fix.Margin = new Thickness(0, 10, 0, 0);
                    root.Children.Add(fix);
                }
            }
            else
            {
                root.Children.Add(Ui.Lbl("Numero di abbinamento (10 cifre) mostrato dal server, o codice fisso del server (12 cifre)"));
                root.Children.Add(_input);
                var row = Ui.Row(12, _pair, _result);
                row.Margin = new Thickness(0, 14, 0, 0);
                root.Children.Add(row);
                _pair.Click += async (_, _) => await PairAsync();
                _input.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) await PairAsync(); };
            }
            if (server)
            {
                _result.Margin = new Thickness(0, 12, 0, 0);
                root.Children.Add(_result);
            }

            // registro del dialogo
            _logScroll = new ScrollViewer { Height = 260, Content = _log, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            var logBox = new Border
            {
                Background = this.FindResource("B.LogBg") as IBrush,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8),
                Child = _logScroll
            };
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(Ui.H2("DIALOGO CON IL SERVER MQTT"));
            var legend = Ui.Sub("▶ inviato · ◀ ricevuto", 12);
            Grid.SetColumn(legend, 1);
            head.Children.Add(legend);
            var copyLog = Ui.Btn("Copia registro");
            copyLog.Click += async (_, _) => { string t; lock (_logText) t = _logText.ToString(); await Dialogs.CopyText(this, t); };
            _retry.Click += async (_, _) =>
            {
                if (server) await StartServerAsync();
                else await PairAsync();
            };
            var tools = Ui.Row(8, copyLog, _retry);
            tools.Margin = new Thickness(0, 10, 0, 0);
            var frame = new Border
            {
                BorderBrush = this.FindResource("B.Accent") as IBrush,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 22, 0, 0),
                Child = Ui.Col(0, head, logBox, tools, Ui.Sub("Il registro viene salvato anche nel log delle sessioni.", 12))
            };
            root.Children.Add(frame);

            var close = Ui.Btn("Chiudi");
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0, 18, 0, 0);
            close.Click += (_, _) => Close();
            root.Children.Add(close);
            Content = root;

            _node.PairingLog += OnLog;
            _node.PairingCompleted += OnPaired;
            _node.PairingRound += OnRound;
            _node.PairingExpired += OnExpired;
            Opened += async (_, _) => { if (server) await StartServerAsync(); };
            Closed += (_, _) =>
            {
                _node.PairingLog -= OnLog;
                _node.PairingCompleted -= OnPaired;
                _node.PairingRound -= OnRound;
                _node.PairingExpired -= OnExpired;
                _cts.Cancel();
                if (!_completed) _node.CancelPairing();   // il numero temporaneo non vale più; il codice fisso resta
            };
        }

        private async System.Threading.Tasks.Task StartServerAsync()
        {
            _copy.IsEnabled = false;
            _code.Text = "Preparo il numero…";
            try
            {
                string? code = await _node.BeginPairingServerAsync(_cts.Token);
                if (code == null)
                {
                    _code.Text = "—";
                    _result.Text = "Non riesco a preparare l'abbinamento: il motivo è nell'ultima riga del registro qui sotto. Poi premi Riprova.";
                    return;
                }
                _code.Text = code;
                _copy.IsEnabled = true;
                _result.Text = "In attesa del client…";
            }
            catch (OperationCanceledException) { }
        }

        private async System.Threading.Tasks.Task PairAsync()
        {
            if (!_pair.IsEnabled) return;
            _pair.IsEnabled = false;
            _result.Foreground = this.FindResource("B.Muted") as IBrush;
            _result.Text = "Abbinamento in corso…";
            string? err = await _node.PairClientAsync(_input.Text ?? "", _cts.Token);
            if (err != null)
            {
                _result.Foreground = this.FindResource("B.Tx") as IBrush;
                _result.Text = err;
                _pair.IsEnabled = true;
            }
        }

        /// <summary>Server: il numero è scaduto dopo 10 minuti.</summary>
        private void OnExpired() => Dispatcher.UIThread.Post(() =>
        {
            _code.Text = "scaduto";
            _copy.IsEnabled = false;
            _result.Text = "Il numero è scaduto (vale 10 minuti): premi Riprova per averne uno nuovo.";
        });

        /// <summary>Client: avanzamento del ciclo delle porte.</summary>
        private void OnRound(int round, int total) =>
            Dispatcher.UIThread.Post(() => _result.Text = $"Ciclo delle porte: giro {round} di {total} (una porta nuova per lato a ogni giro)…");

        private void OnLog(char sym, string text)
        {
            string time = DateTime.Now.ToString("HH:mm:ss.fff");
            lock (_logText) _logText.AppendLine($"{time}  {sym}  {text}");
            Dispatcher.UIThread.Post(() =>
            {
                string key = sym switch { '▶' => "B.LogSend", '◀' => "B.LogRecv", '✓' => "B.LogRecv", '✗' => "B.LogErr", _ => "B.LogMuted" };
                var color = this.FindResource(key) as IBrush;
                var tb = new TextBlock
                {
                    FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
                    FontSize = 12,
                    LineHeight = 20,
                    TextWrapping = TextWrapping.NoWrap
                };
                tb.Inlines = new InlineCollection
                {
                    new Run(sym == ' ' ? "            " : time) { Foreground = this.FindResource("B.LogMuted") as IBrush },
                    new Run("  " + (sym == ' ' ? " " : sym.ToString()) + "  ") { Foreground = color, FontWeight = FontWeight.Bold },
                    new Run(text) { Foreground = sym is '✗' or '✓' ? color : this.FindResource("B.LogText") as IBrush },
                };
                _log.Children.Add(tb);
                _logScroll.ScrollToEnd();
            });
        }

        private void OnPaired()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_completed) return;
                _completed = true;
                _result.Foreground = this.FindResource("B.Ok") as IBrush;
                _result.Text = _s.Role == NodeRole.Server
                    ? "Abbinamento completato: il client è stato aggiunto all'elenco. Da ora il collegamento è automatico."
                    : "Abbinamento completato. Da ora il collegamento è automatico.";
                _pair.IsEnabled = false;
            });
        }
    }
}
