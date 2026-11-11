using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using IpTrxCtrl.Core;
using IpTrxCtrl.Net;

namespace IpTrxCtrl.UI
{
    /// <summary>
    /// Parti della finestra principale aggiunte nella 0.3.0, tutte facoltative (Impostazioni › Finestra principale):
    /// bande e passo di sintonia, comandi della radio, memorie, ALC, panadapter, stato della trasmissione,
    /// avviso di versione nuova.
    /// </summary>
    public partial class MainWindow
    {
        // ---------------------------------------------------------------- bande

        private sealed record Band(string Name, long Lo, long Hi, long DefHz, string DefMode);

        private static readonly Band[] Bands =
        {
            new("160", 1_810_000, 2_000_000, 1_840_000, "LSB"),
            new("80", 3_500_000, 4_000_000, 3_700_000, "LSB"),
            new("60", 5_250_000, 5_450_000, 5_354_000, "USB"),
            new("40", 7_000_000, 7_300_000, 7_100_000, "LSB"),
            new("30", 10_100_000, 10_150_000, 10_120_000, "CW"),
            new("20", 14_000_000, 14_350_000, 14_200_000, "USB"),
            new("17", 18_068_000, 18_168_000, 18_130_000, "USB"),
            new("15", 21_000_000, 21_450_000, 21_250_000, "USB"),
            new("12", 24_890_000, 24_990_000, 24_940_000, "USB"),
            new("10", 28_000_000, 29_700_000, 28_500_000, "USB"),
            new("6", 50_000_000, 54_000_000, 50_150_000, "USB"),
            new("2", 144_000_000, 148_000_000, 144_300_000, "USB"),
            new("70cm", 430_000_000, 440_000_000, 432_200_000, "USB"),
        };

        private static readonly int[] Steps = { 1, 10, 50, 100, 500, 1000, 2500, 5000, 9000, 10000, 12500, 25000 };
        private readonly Dictionary<string, Button> _bandButtons = new();

        private static Band? BandOf(long hz) => Bands.FirstOrDefault(b => hz >= b.Lo && hz <= b.Hi);

        private static string StepName(int hz) => hz < 1000 ? $"{hz} Hz" : (hz / 1000.0).ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',') + " kHz";

        // comandi della radio: dopo un clic il valore mostrato resta quello scelto finché il server non lo conferma
        private DateTime _ctlClickedAt = DateTime.MinValue;
        private double? _sqlPending;
        private DateTime _sqlPendingAt;

        private sealed class MemItem
        {
            public bool Server;
            public MemoryEntry E = new();
            public override string ToString() => $"{(Server ? "Server" : "PC")} · {E.Name} · {FormatQrgS(E.FreqHz)}{(E.Mode.Length > 0 ? " " + E.Mode : "")}";
        }

        private static string FormatQrgS(long hz) => $"{hz / 1_000_000}.{(hz / 1000) % 1000:D3}.{hz % 1000:D3}";

        // ================================================================ costruzione

        private void BuildExtras()
        {
            foreach (var b in Bands)
            {
                var btn = new Button { Content = b.Name };
                btn.Classes.Add("band");
                ToolTip.SetTip(btn, $"{b.Name}{(b.Name.EndsWith("cm") ? "" : " m")}: {b.Lo / 1000.0:0} – {b.Hi / 1000.0:0} kHz (torna all'ultima frequenza usata sulla banda)");
                var band = b;
                btn.Click += (_, _) => GoBand(band);
                _bandButtons[b.Name] = btn;
                WrapBands.Children.Add(btn);
            }
            CmbStep.ItemsSource = Steps.Select(StepName).ToList();
            CmbStep.SelectedIndex = Math.Max(0, Array.IndexOf(Steps, _s.TuneStepHz));
            CmbStep.SelectionChanged += (_, _) =>
            {
                if (_loading || CmbStep.SelectedIndex < 0) return;
                _s.TuneStepHz = Steps[CmbStep.SelectedIndex];
                _s.Save();
            };
            BtnStepDown.Click += (_, _) => StepTune(-1);
            BtnStepUp.Click += (_, _) => StepTune(+1);
            foreach (var c in new Control[] { BtnStepDown, BtnStepUp, CmbStep })
                c.AddHandler(PointerWheelChangedEvent, (_, e) => { StepTune(Math.Sign(e.Delta.Y)); e.Handled = true; }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

            // comandi della radio
            BtnNr.Click += (_, _) => CtlToggle("NR", BtnNr);
            BtnNb.Click += (_, _) => CtlToggle("NB", BtnNb);
            BtnAnf.Click += (_, _) => CtlToggle("ANF", BtnAnf);
            BtnComp.Click += (_, _) => CtlToggle("COMP", BtnComp);
            BtnAtt.Click += (_, _) => { _ctlClickedAt = DateTime.UtcNow; _node.SetRadioCtl("ATT", -1); };
            BtnPre.Click += (_, _) => { _ctlClickedAt = DateTime.UtcNow; _node.SetRadioCtl("PREAMP", -1); };
            BtnAgc.Click += (_, _) => { _ctlClickedAt = DateTime.UtcNow; _node.SetRadioCtl("AGC", -1); };
            SldSql.ValueChanged += (_, e) =>
            {
                if (_loading) return;
                _sqlPending = e.NewValue / 100.0;
                _sqlPendingAt = DateTime.UtcNow;
                TxtSql.Text = $"{e.NewValue:0}%";
            };

            // memorie
            BtnMemGo.Click += (_, _) =>
            {
                if (CmbMem.SelectedItem is MemItem m) _node.SetFreqMode(m.E.FreqHz, m.E.Mode);
            };
            CmbMem.DoubleTapped += (_, _) => { if (CmbMem.SelectedItem is MemItem m) _node.SetFreqMode(m.E.FreqHz, m.E.Mode); };
            BtnMemSave.Click += async (_, _) => await SaveMemoryAsync();
            BtnMemDel.Click += async (_, _) =>
            {
                if (CmbMem.SelectedItem is not MemItem m) return;
                if (!await Dialogs.YesNo(this, $"Elimino la memoria «{m.E.Name}»" + (m.Server && IsClient ? " dal server (sparisce per tutti i client)?" : "?"))) return;
                if (m.Server) _node.DeleteServerMemoryFromUi(m.E.Id);
                else { _s.Memories.RemoveAll(x => x.Id == m.E.Id); _s.Save(); FillMemories(); }
            };
            _node.MemoriesChanged += () => Dispatcher.UIThread.Post(FillMemories);
            FillMemories();

            // panadapter
            SldPanInt.Value = _s.PanIntensity;
            PanView.Intensity = _s.PanIntensity;
            SldPanInt.ValueChanged += (_, e) => { if (_loading) return; _s.PanIntensity = (int)e.NewValue; PanView.Intensity = e.NewValue; _s.Save(); };
            PanView.FrequencyClicked += hz =>
            {
                int r = Math.Clamp(_s.TuneStepHz, 10, 1000);
                TuneTo((long)Math.Round(hz / (double)r) * r);
            };
            PanView.WheelSteps += StepTune;
            _node.SpectrumReceived += l => { if (_panShown) PanView.Push(l); };
            _node.SpectrumResend += () => Dispatcher.UIThread.Post(() => { _panAsked = false; RefreshPanel(); });

            // stato della trasmissione: sul server un clic toglie il blocco per l'operatore alla radio
            TxtTxInfo.PointerPressed += async (_, _) =>
            {
                if (IsClient || _tel.LocalLockSec <= 0) return;
                if (await Dialogs.YesNo(this, "Tolgo il blocco della trasmissione remota? Il client potrà trasmettere subito.")) _node.ClearLocalLock();
            };

            // versione nuova
            BtnUpdateOpen.Click += (_, _) => OpenUrl(Support.ReleasesUrl);
            BtnUpdateClose.Click += (_, _) =>
            {
                BarUpdate.IsVisible = false;
                if (_updateVersion.Length > 0) { _s.UpdateSeen = _updateVersion; _s.Save(); }
            };
            ApplyOptionalParts();
        }

        /// <summary>Mostra o nasconde le parti facoltative secondo le Impostazioni.</summary>
        private void ApplyOptionalParts()
        {
            if (WrapBands == null) return;
            WrapBands.IsVisible = _s.ShowBands;
            PanelStep.IsVisible = _s.ShowBands;
            PanelRadioCtl.IsVisible = _s.ShowRadioControls;
            PanelMemories.IsVisible = _s.ShowMemories;
            TxtAlcLabel.IsVisible = PbAlc.IsVisible = TxtAlc.IsVisible = _s.ShowAlc;
            RefreshPanel();
        }

        // ================================================================ sintonia

        private void StepTune(int steps)
        {
            if (steps == 0) return;
            bool session = DateTime.UtcNow - _qrgWheelAtA < QrgHold && _qrgPendingA > 0;
            long cur = session ? _qrgPendingA : _tel.FreqHz;
            if (cur <= 0) return;
            long step = Math.Max(1, _s.TuneStepHz);
            // sulla griglia del passo: dal valore non allineato il primo passo porta al multiplo più vicino nella direzione
            long baseHz = steps > 0 ? (cur / step) * step : ((cur + step - 1) / step) * step;
            long nf = baseHz + steps * step;
            if (nf == cur) nf += steps * step;
            TuneTo(nf);
        }

        /// <summary>Sintonia del VFO A con lo stesso meccanismo della rotella sulle cifre (al massimo ogni 120 ms).</summary>
        private void TuneTo(long hz)
        {
            hz = Math.Clamp(hz, 1_000L, 99_999_999_999L);
            bool session = DateTime.UtcNow - _qrgWheelAtA < QrgHold;
            if (!session) _qrgSentA = 0;
            _qrgPendingA = hz;
            _qrgWheelAtA = DateTime.UtcNow;
            TxtQrg.Text = FormatQrgS(hz);
            if (!_qrgTimer.IsEnabled) { FlushQrg(); _qrgTimer.Start(); }
        }

        private void GoBand(Band b)
        {
            // la frequenza di adesso resta come «ultima» della sua banda
            if (BandOf(_tel.FreqHz) is { } cur)
            {
                _s.BandLast[cur.Name] = new BandSlot { FreqHz = _tel.FreqHz, Mode = _tel.Mode };
                if (cur.Name == b.Name) return;
            }
            long hz = b.DefHz; string mode = b.DefMode;
            if (_s.BandLast.TryGetValue(b.Name, out var last) && last.FreqHz >= b.Lo && last.FreqHz <= b.Hi) { hz = last.FreqHz; mode = last.Mode; }
            _s.Save();
            _node.SetFreqMode(hz, mode);
        }

        private void CtlToggle(string name, Avalonia.Controls.Primitives.ToggleButton b)
        {
            _ctlClickedAt = DateTime.UtcNow;
            _node.SetRadioCtl(name, b.IsChecked == true ? 1 : 0);
        }

        // ================================================================ memorie

        private void FillMemories()
        {
            var keep = (CmbMem.SelectedItem as MemItem)?.E.Id;
            var items = new List<MemItem>();
            if (IsClient) items.AddRange(_s.Memories.OrderBy(m => m.FreqHz).Select(m => new MemItem { Server = false, E = m }));
            items.AddRange(_node.ServerMemories.Select(m => new MemItem { Server = true, E = m }));
            CmbMem.ItemsSource = items;
            CmbMem.SelectedItem = items.FirstOrDefault(i => i.E.Id == keep);
        }

        private async Task SaveMemoryAsync()
        {
            long hz = _tel.FreqHz;
            if (hz <= 0) { await Dialogs.Info(this, "Frequenza della radio non disponibile: la memoria si salva con la radio collegata."); return; }
            var name = new TextBox { Text = $"{FormatQrgS(hz)} {_tel.Mode}".Trim(), MinWidth = 300 };
            var onPc = new RadioButton { Content = "In questo PC", GroupName = "dove", IsChecked = true };
            var onSrv = new RadioButton { Content = "Sul server (comune a tutti i client)", GroupName = "dove" };
            var ok = new Button { Content = "Salva", MinWidth = 100 };
            ok.Classes.Add("accent");
            var cancel = new Button { Content = "Annulla", MinWidth = 100 };
            var panel = new StackPanel { Margin = new Thickness(22), Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = $"Memoria: {FormatQrgS(hz)} {_tel.Mode}", FontSize = 16, FontWeight = FontWeight.Bold });
            panel.Children.Add(new TextBlock { Text = "Nome", FontSize = 12 });
            panel.Children.Add(name);
            if (IsClient) { panel.Children.Add(onPc); panel.Children.Add(onSrv); }
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } });
            var w = new Window { Title = "Salva memoria", SizeToContent = SizeToContent.WidthAndHeight, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
            bool saved = false;
            ok.Click += (_, _) => { saved = true; w.Close(); };
            cancel.Click += (_, _) => w.Close();
            name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { saved = true; w.Close(); } };
            await w.ShowDialog(this);
            if (!saved) return;
            string n = string.IsNullOrWhiteSpace(name.Text) ? FormatQrgS(hz) : name.Text.Trim();
            if (!IsClient || onSrv.IsChecked == true) { _node.SaveServerMemoryFromUi(n, hz, _tel.Mode); return; }
            var e2 = _s.Memories.FirstOrDefault(m => string.Equals(m.Name, n, StringComparison.OrdinalIgnoreCase));
            if (e2 == null) { e2 = new MemoryEntry { Id = Guid.NewGuid().ToString("N")[..8], Name = n }; _s.Memories.Add(e2); }
            e2.FreqHz = hz; e2.Mode = _tel.Mode;
            _s.Save();
            FillMemories();
            CmbMem.SelectedItem = (CmbMem.ItemsSource as List<MemItem>)?.FirstOrDefault(i => i.E.Id == e2.Id);
        }

        // ================================================================ panadapter

        private bool _panShown, _panAsked;

        /// <summary>Riquadro del panadapter: visibile se scelto nelle Impostazioni e se il server lo fornisce.</summary>
        private void RefreshPanel()
        {
            bool avail = IsClient ? _tel.PanAvailable && _node.Connected : _node.Pan.Available;
            bool show = _s.ShowPanadapter && avail;
            if (show != _panShown)
            {
                _panShown = show;
                PanelPan.IsVisible = show;
                if (!show) PanView.Clear();
            }
            if (IsClient && _panAsked != show && _node.Connected)
            {
                _panAsked = show;
                _node.SetSpectrumWanted(show);
            }
            TxtPanStatus.Text = IsClient ? _tel.PanStatus : _node.Pan.Status;
        }

        // ================================================================ aggiornamento

        /// <summary>Dalla telemetria: comandi della radio, ALC, stato della trasmissione, banda, panadapter.</summary>
        private void RefreshExtras(RigTelemetry t)
        {
            // banda in uso evidenziata
            var band = BandOf(t.FreqHz);
            foreach (var (name, b) in _bandButtons) b.Classes.Set("accent", band?.Name == name);

            // comandi della radio
            bool hold = DateTime.UtcNow - _ctlClickedAt < TimeSpan.FromSeconds(5);
            void Tog(Avalonia.Controls.Primitives.ToggleButton b, int v, string what)
            {
                b.IsEnabled = v >= 0 || !t.CatOk;
                if (!hold && v >= 0) b.IsChecked = v == 1;
                ToolTip.SetTip(b, v < 0 && t.CatOk ? $"{what}: non disponibile con questa radio (o non ancora letto)" : what);
            }
            Tog(BtnNr, t.Nr, "Riduzione del rumore (NR)");
            Tog(BtnNb, t.Nb, "Noise blanker (NB)");
            Tog(BtnAnf, t.Anf, "Notch automatico (ANF)");
            Tog(BtnComp, t.Comp, "Compressore del microfono della radio (COMP)");
            BtnAtt.Content = "ATT " + (t.Att < 0 ? "—" : t.Att == 0 ? "OFF" : t.Att.ToString(CultureInfo.InvariantCulture));
            BtnPre.Content = "PRE " + (t.Preamp < 0 ? "—" : t.Preamp == 0 ? "OFF" : t.Preamp.ToString(CultureInfo.InvariantCulture));
            BtnAgc.Content = "AGC " + RigTelemetry.AgcName(t.Agc);
            BtnAtt.Classes.Set("accent", t.Att > 0);
            BtnPre.Classes.Set("accent", t.Preamp > 0);
            if (_sqlPending == null && !float.IsNaN(t.Sql) && DateTime.UtcNow - _sqlPendingAt > TimeSpan.FromSeconds(2))
            {
                SldSql.Value = t.Sql * 100;
                TxtSql.Text = $"{t.Sql * 100:0}%";
            }
            SldSql.IsEnabled = !float.IsNaN(t.Sql) || !t.CatOk;

            // ALC (solo in trasmissione)
            if (!float.IsNaN(t.Alc)) { PbAlc.Value = Math.Clamp(t.Alc * 100, 0, 100); TxtAlc.Text = $"{t.Alc * 100:0} %"; }
            else { PbAlc.Value = 0; TxtAlc.Text = "—"; }

            // panadapter
            PanView.DialHz = t.FreqHz;
            PanView.PassbandHz = t.PassbandHz;
            PanView.Mode = t.Mode;
            RefreshPanel();
            RefreshTxInfo();
        }

        /// <summary>Riga sotto il PTT: tempo di trasmissione, tempo massimo, solo ascolto, operatore alla radio.</summary>
        private void RefreshTxInfo()
        {
            var t = _tel;
            string text = "";
            bool alert = false;
            static string Mmss(int s) => $"{s / 60}:{s % 60:D2}";
            if (IsClient && _node.Connected && !t.TxAllowed)
            {
                text = "Trasmissione non permessa: " + t.TxDenied;
                alert = true;
            }
            else if (t.TxTimedOut)
            {
                text = "Tempo massimo di trasmissione raggiunto: rilascia il PTT per trasmettere di nuovo.";
                alert = true;
            }
            else if (t.TxSeconds > 0)
            {
                text = t.TxLimit > 0 ? $"In trasmissione da {Mmss(t.TxSeconds)} · massimo {Mmss(t.TxLimit)}" : $"In trasmissione da {Mmss(t.TxSeconds)}";
                alert = t.TxLimit > 0 && t.TxLimit - t.TxSeconds <= 15;
            }
            else if (!IsClient && t.LocalLockSec > 0)
            {
                text = $"Trasmissione del client bloccata per {Mmss(t.LocalLockSec)}: c'è un operatore alla radio. Clic qui per togliere il blocco.";
            }
            TxtTxInfo.Text = text;
            TxtTxInfo.IsVisible = text.Length > 0;
            if (alert) TxtTxInfo.Foreground = this.FindResource("B.Tx") as IBrush;
            else TxtTxInfo.ClearValue(TextBlock.ForegroundProperty);
            TxtTxInfo.Cursor = !IsClient && t.LocalLockSec > 0 ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow);
            bool blocked = IsClient && _node.Connected && !t.TxAllowed;
            BtnPtt.Opacity = blocked ? 0.45 : 1.0;
            if (!_manualPtt && !_node.LocalPtt) TxtPtt.Text = blocked ? "PTT NON PERMESSO" : "PTT (PREMI E TIENI)";
        }

        /// <summary>Dal timer dell'interfaccia (100 ms).</summary>
        private void TickExtras()
        {
            if (_sqlPending is double v && DateTime.UtcNow - _sqlPendingAt > TimeSpan.FromMilliseconds(300))
            {
                _sqlPending = null;
                _sqlPendingAt = DateTime.UtcNow;
                _node.SetRadioCtl("SQL", (float)v);
            }
        }

        /// <summary>Client: trasmissione non permessa dal server (solo ascolto o operatore alla radio).</summary>
        private bool TxRefusedHere()
        {
            if (!IsClient || !_node.Connected || _tel.TxAllowed) return false;
            TxtNotice.Text = "Trasmissione non permessa: " + _tel.TxDenied + ".";
            TxtNotice.IsVisible = true;
            return true;
        }

        // ================================================================ versione nuova

        private string _updateVersion = "";

        private async void CheckUpdatesAsync()
        {
            if (!_s.CheckUpdates) return;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                string? v = await Support.CheckNewerAsync(cts.Token);
                if (v == null || v == _s.UpdateSeen) return;
                _updateVersion = v;
                SessionLog.Write($"AGGIORNAMENTI è disponibile la versione {v} (questa è la {AppVersion.Text})");
                TxtUpdate.Text = $"È disponibile IP-TRX-CTRL {v} (questa è la {AppVersion.Text}). Ricorda di aggiornare anche l'altro PC: guarda la tabella delle versioni compatibili.";
                BarUpdate.IsVisible = true;
            }
            catch { }
        }

        internal static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { SessionLog.Write("Pagina web non apribile: " + ex.Message); }
        }
    }
}
