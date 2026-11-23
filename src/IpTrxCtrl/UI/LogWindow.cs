using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IpTrxCtrl.Core;

namespace IpTrxCtrl.UI
{
    /// <summary>
    /// Registro dei QSO (0.3.6): frequenza, modo e orari presi dalla radio, salvataggio in ADIF e invio ai programmi
    /// che ascoltano il protocollo UDP di WSJT-X (GridTracker, JTAlert, Log4OM…).
    /// </summary>
    public sealed class LogWindow : Window
    {
        private readonly Node _node;
        private readonly AppSettings _s;
        private readonly TextBox _call = new() { Width = 150, Watermark = "nominativo" };
        private readonly TextBox _sent = new() { Width = 70 }, _rcvd = new() { Width = 70 };
        private readonly TextBox _name = new() { Width = 150 }, _qth = new() { Width = 150 }, _grid = new() { Width = 90 };
        private readonly TextBox _comment = new() { MinWidth = 300 };
        private readonly TextBlock _radio = Ui.Sub("", 13);
        private readonly ListBox _list = new() { Height = 240, FontFamily = new FontFamily("JetBrains Mono,DejaVu Sans Mono,Consolas,monospace"), FontSize = 12 };
        private DateTime _start = DateTime.MinValue;
        private bool _autoRec;   // 0.3.7: registrazione automatica del QSO aperta da questa finestra

        private void StopAutoRec(string call)
        {
            if (!_autoRec) return;
            _autoRec = false;
            _node.Recorder.StopAs(call);
        }
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

        public LogWindow(Node node, AppSettings s)
        {
            _node = node; _s = s;
            Title = "Registro QSO";
            Width = 760;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _call.TextChanged += (_, _) =>
            {
                string t = _call.Text ?? "";
                if (t.Length > 0 && _start == DateTime.MinValue)
                {
                    _start = DateTime.UtcNow;   // inizio del QSO: quando si scrive il nominativo
                    if (_s.RecordEveryQso && !_node.Recorder.Active)
                    {
                        try { _node.Recorder.Start(_node.LastTelemetry.FreqHz, ""); _autoRec = true; } catch { }
                    }
                }
                if (t.Length == 0) { _start = DateTime.MinValue; StopAutoRec(""); }
                string up = t.ToUpperInvariant().Replace(" ", "");
                if (up != t) { int c = _call.CaretIndex; _call.Text = up; _call.CaretIndex = Math.Min(c, up.Length); }
            };
            _call.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };
            _comment.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };
            ResetReports();

            var save = Ui.Btn("Registra", true);
            save.Click += (_, _) => Save();
            var clear = Ui.Btn("Pulisci");
            clear.Click += (_, _) => ClearForm();
            var export = Ui.Btn("Esporta ADIF…");
            export.Click += async (_, _) =>
            {
                var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Esporta il registro (ADIF)", SuggestedFileName = $"IP-TRX-CTRL-{DateTime.UtcNow:yyyyMMdd}.adi", DefaultExtension = "adi"
                });
                string? path = f?.TryGetLocalPath();
                if (path == null) return;
                try { QsoLog.Export(path); await Dialogs.Info(this, $"Registro esportato: {QsoLog.All().Count} QSO. Il file ADIF va bene per LoTW (TQSL), QRZ, Club Log e gli altri programmi di log."); }
                catch (Exception ex) { await Dialogs.Info(this, "Non riesco a salvarlo: " + ex.Message); }
            };
            var udpNote = Ui.Sub(s.QsoUdpEnabled
                ? $"Ogni QSO va anche a {s.QsoUdpHost}:{s.QsoUdpPort} con il protocollo UDP di WSJT-X (GridTracker, JTAlert, Log4OM, N1MM…). Si cambia in Impostazioni › REGISTRO DEI QSO."
                : "Invio ai programmi di log spento: si accende in Impostazioni › REGISTRO DEI QSO.", 12);
            udpNote.TextWrapping = TextWrapping.Wrap;

            Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 12,
                    Children =
                    {
                        Ui.H1("Registro QSO"),
                        _radio,
                        Ui.Panel(Ui.Col(10,
                            Ui.Row(10, Ui.Col(0, Ui.Lbl("Nominativo"), _call), Ui.Col(0, Ui.Lbl("RST dato"), _sent), Ui.Col(0, Ui.Lbl("RST ricevuto"), _rcvd), Ui.Col(0, Ui.Lbl("Locatore"), _grid)),
                            Ui.Row(10, Ui.Col(0, Ui.Lbl("Nome"), _name), Ui.Col(0, Ui.Lbl("QTH"), _qth), Ui.Col(0, Ui.Lbl("Note"), _comment)),
                            Ui.Row(8, save, clear))),
                        Ui.Row(8, Ui.H2("ULTIMI QSO"), export),
                        _list,
                        udpNote,
                        Ui.Sub("Il registro è il file registro.adi nella cartella dei dati del programma.", 12),
                    }
                }
            };
            Refresh();
            QsoLog.Changed += OnChanged;
            _timer.Tick += (_, _) => UpdateRadio();
            _timer.Start();
            UpdateRadio();
            Closed += (_, _) => { _timer.Stop(); QsoLog.Changed -= OnChanged; StopAutoRec(""); };
            Opened += (_, _) => _call.Focus();
        }

        private void OnChanged() => Dispatcher.UIThread.Post(Refresh);

        private void ResetReports()
        {
            bool cw = (_node.LastTelemetry.Mode ?? "").StartsWith("CW", StringComparison.OrdinalIgnoreCase);
            _sent.Text = cw ? "599" : "59";
            _rcvd.Text = cw ? "599" : "59";
        }

        private void UpdateRadio()
        {
            var t = _node.LastTelemetry;
            string band = QsoLog.BandOf(t.FreqHz);
            _radio.Text = t.FreqHz > 0
                ? $"Radio: {t.FreqHz / 1e6:F4} MHz {(band.Length > 0 ? "(" + band + ") " : "")}{t.Mode} · {DateTime.UtcNow:HH:mm:ss} UTC"
                  + (_start != DateTime.MinValue ? $" · QSO iniziato alle {_start:HH:mm:ss}" : "")
                : "Radio: frequenza non ancora nota (la prende dal CAT)";
        }

        private void Refresh()
        {
            var all = QsoLog.All();
            _list.ItemsSource = all.Reverse().Take(200).Select(q => q.Line).ToList();
        }

        private void ClearForm()
        {
            _call.Text = ""; _name.Text = ""; _qth.Text = ""; _grid.Text = ""; _comment.Text = "";
            _start = DateTime.MinValue;
            StopAutoRec("");
            ResetReports();
            _call.Focus();
        }

        private async void Save()
        {
            string call = (_call.Text ?? "").Trim().ToUpperInvariant();
            if (call.Length < 3) { await Dialogs.Info(this, "Scrivi il nominativo del corrispondente."); return; }
            var t = _node.LastTelemetry;
            var now = DateTime.UtcNow;
            var q = new Qso
            {
                Call = call,
                OnUtc = _start == DateTime.MinValue ? now : _start,
                OffUtc = now,
                FreqHz = t.FreqHz,
                Band = QsoLog.BandOf(t.FreqHz),
                Mode = t.Mode ?? "",
                RstSent = (_sent.Text ?? "").Trim(),
                RstRcvd = (_rcvd.Text ?? "").Trim(),
                Name = (_name.Text ?? "").Trim(),
                Qth = (_qth.Text ?? "").Trim(),
                Grid = (_grid.Text ?? "").Trim().ToUpperInvariant(),
                Comment = (_comment.Text ?? "").Trim(),
                Operator = _s.Callsign,
                StationCall = _node.Role == NodeRole.Client && _s.PeerCallsign.Length > 0 ? _s.PeerCallsign : _s.Callsign,
                MyGrid = _s.MyGrid,
            };
            try { QsoLog.Add(q, _s); }
            catch (Exception ex) { await Dialogs.Info(this, "Non riesco a scrivere il registro: " + ex.Message); return; }
            StopAutoRec(call);
            ClearForm();
        }
    }
}
