using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using IpTrxCtrl.Audio;
using IpTrxCtrl.Core;

namespace IpTrxCtrl.UI
{
    /// <summary>Macro CW (F1 – F8): nome sul tasto e testo da trasmettere, con variabili.</summary>
    public sealed class MacroCwWindow : Window
    {
        public MacroCwWindow(AppSettings s, Node node)
        {
            Title = "Macro CW (F1 – F8)";
            Width = 680;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var names = new TextBox[8];
            var texts = new TextBox[8];
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("32,150,*,Auto") };
            void Add(Control c, int row, int col) { Grid.SetRow(c, row); Grid.SetColumn(c, col); grid.Children.Add(c); }
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Add(Ui.Lbl("Nome sul tasto"), 0, 1);
            Add(Ui.Lbl("Testo da trasmettere"), 0, 2);
            for (int i = 0; i < 8; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                int idx = i;
                var k = new TextBlock { Text = $"F{i + 1}", FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
                names[i] = new TextBox { Text = s.CwMacros[i].Name, Margin = new Thickness(0, 4, 8, 4) };
                texts[i] = new TextBox { Text = s.CwMacros[i].Text, Margin = new Thickness(0, 4, 8, 4), FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace") };
                var test = Ui.Btn("Prova");
                test.Margin = new Thickness(0, 4, 0, 4);
                test.Click += (_, _) =>
                {
                    string t = s.ExpandMacro(texts[idx].Text ?? "");
                    node.Audio.PlayLocal(AudioEngine.Sidetone(t, s.CwSpeedWpm, s.CwPitch));
                };
                Add(k, i + 1, 0); Add(names[i], i + 1, 1); Add(texts[i], i + 1, 2); Add(test, i + 1, 3);
            }

            var save = Ui.Btn("Salva", true);
            var cancel = Ui.Btn("Annulla");
            save.Click += (_, _) =>
            {
                for (int i = 0; i < 8; i++)
                {
                    s.CwMacros[i].Name = (names[i].Text ?? "").Trim();
                    s.CwMacros[i].Text = (texts[i].Text ?? "").Trim().ToUpperInvariant();
                }
                s.Save();
                node.Audio.StopLocal();
                Close();
            };
            cancel.Click += (_, _) => { node.Audio.StopLocal(); Close(); };
            var buttons = Ui.Row(8, save, cancel);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;

            Content = new StackPanel
            {
                Margin = new Thickness(22),
                Spacing = 10,
                Children =
                {
                    grid,
                    Ui.Sub("Variabili: {MYCALL} mio nominativo · {CALL} corrispondente · {NR} numero progressivo · {RST} rapporto. " +
                           "Prova = ascolto in sidetone locale, senza trasmettere. In trasmissione il testo è inviato con il keyer interno della radio (send_morse).", 12),
                    buttons
                }
            };
        }
    }

    /// <summary>Memorie WAV (F1 – F8), come in WavPTT: scelta del file, ascolto di prova, salvataggio nella cartella WAV.</summary>
    public sealed class WavMemoriesWindow : Window
    {
        private readonly string[] _tmp = new string[8];
        private readonly TextBox[] _fields = new TextBox[8];
        private readonly TextBlock _hint;

        public WavMemoriesWindow(AppSettings s, Node node)
        {
            Title = "Memorie WAV (F1 – F8)";
            Width = 620;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            for (int i = 0; i < 8; i++)
            {
                string m = s.WavMemories[i];
                _tmp[i] = string.IsNullOrEmpty(m) ? "" : (Path.IsPathRooted(m) ? m : Path.Combine(AppSettings.WavFolder, m));
            }
            _hint = Ui.Sub("Play: ascolto sull'uscita audio locale, senza PTT.", 12);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*,Auto,Auto,Auto") };
            void Add(Control c, int row, int col) { Grid.SetRow(c, row); Grid.SetColumn(c, col); grid.Children.Add(c); }
            for (int i = 0; i < 8; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                int idx = i;
                _fields[i] = new TextBox { IsReadOnly = true, Text = _tmp[i].Length == 0 ? "—" : Path.GetFileName(_tmp[i]), Margin = new Thickness(0, 4, 8, 4) };
                var browse = Ui.Btn("…");
                browse.Width = 44;
                browse.Margin = new Thickness(0, 4, 6, 4);
                browse.Click += async (_, _) =>
                {
                    var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = $"Scegli il file WAV per F{idx + 1}",
                        AllowMultiple = false,
                        FileTypeFilter = new[] { new FilePickerFileType("File WAV") { Patterns = new[] { "*.wav", "*.WAV" } }, FilePickerFileTypes.All }
                    });
                    var f = files.FirstOrDefault();
                    string? p = f?.TryGetLocalPath();
                    if (p != null) { _tmp[idx] = p; _fields[idx].Text = Path.GetFileName(p); }
                };
                var play = Ui.Btn("Play");
                play.Margin = new Thickness(0, 4, 6, 4);
                play.Click += (_, _) =>
                {
                    if (_tmp[idx].Length == 0) return;
                    try
                    {
                        node.Audio.PlayLocal(Dsp.PrepareClip(_tmp[idx], s.KeyerVolume, s.KeyerFilter, s.KeyerCompDb));
                        _hint.Text = $"Ascolto F{idx + 1}: {Path.GetFileName(_tmp[idx])}";
                    }
                    catch (Exception ex) { _hint.Text = ex.Message; }
                };
                var stop = Ui.Btn("Stop");
                stop.Margin = new Thickness(0, 4, 0, 4);
                stop.Click += (_, _) => { node.Audio.StopLocal(); _hint.Text = "Play: ascolto sull'uscita audio locale, senza PTT."; };
                Add(new TextBlock { Text = $"F{i + 1}", FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center }, i, 0);
                Add(_fields[i], i, 1); Add(browse, i, 2); Add(play, i, 3); Add(stop, i, 4);
            }

            var save = Ui.Btn("Salva", true);
            var cancel = Ui.Btn("Annulla");
            save.Click += (_, _) =>
            {
                for (int i = 0; i < 8; i++)
                {
                    string p = _tmp[i];
                    if (p.Length == 0) { s.WavMemories[i] = ""; continue; }
                    try
                    {
                        string name = Path.GetFileName(p);
                        string target = Path.Combine(AppSettings.WavFolder, name);
                        if (!string.Equals(Path.GetFullPath(p), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(p, target, true);
                        s.WavMemories[i] = name;
                    }
                    catch { s.WavMemories[i] = p; }
                }
                s.Save();
                node.Audio.StopLocal();
                Close();
            };
            cancel.Click += (_, _) => { node.Audio.StopLocal(); Close(); };
            var buttons = Ui.Row(8, save, cancel);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;

            Content = new StackPanel
            {
                Margin = new Thickness(22),
                Spacing = 10,
                Children = { grid, _hint, Ui.Sub("Salva copia i file scelti nella cartella WAV del programma.", 12), buttons }
            };
        }
    }

    /// <summary>Finestra di decodifica di un segnale fuori dal pitch (una per segnale).</summary>
    public sealed class DecodeWindow : Window
    {
        private readonly MainWindow _main;
        private readonly Node _node;
        private readonly CwDecoder _decoder;
        private readonly object _lock = new();
        private readonly System.Text.StringBuilder _text = new();
        private readonly TextBlock _out, _wpm;

        public double FrequencyHz { get; }

        public DecodeWindow(MainWindow main, Node node, AppSettings s, double hz)
        {
            _main = main;
            _node = node;
            FrequencyHz = hz;
            Title = $"Decodifica · {hz:F0} Hz";
            Width = 600;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _decoder = new CwDecoder(hz, s.CwDecodeWidth) { FixedWpm = s.CwRxWpm };
            _decoder.Text += t => { lock (_lock) { _text.Append(t); if (_text.Length > 2000) _text.Remove(0, 1000); } };
            _main.AddDecoder(_decoder, hz);

            var freq = Ui.Mono($"{hz:F0} Hz", 22);
            freq.FontWeight = FontWeight.Bold;
            freq.Foreground = new SolidColorBrush(Color.FromRgb(224, 123, 0));
            _wpm = Ui.Mono("— WPM", 13);
            var width = new ComboBox { Width = 100, ItemsSource = new[] { "50 Hz", "75 Hz", "100 Hz", "150 Hz", "200 Hz" } };
            int[] widths = { 50, 75, 100, 150, 200 };
            width.SelectedIndex = Math.Max(0, Array.IndexOf(widths, s.CwDecodeWidth));
            width.SelectionChanged += (_, _) => { if (width.SelectedIndex >= 0) _main.ReconfigureDecoder(_decoder, hz, widths[width.SelectedIndex]); };
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto") };
            void H(Control c, int col) { Grid.SetColumn(c, col); head.Children.Add(c); }
            H(freq, 0);
            var chip = new Border { Child = _wpm, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            chip.Classes.Add("chip");
            H(chip, 1);
            var wl = Ui.Sub("Larghezza", 12);
            wl.VerticalAlignment = VerticalAlignment.Center;
            wl.Margin = new Thickness(0, 0, 8, 0);
            H(wl, 3); H(width, 4);

            _out = new TextBlock
            {
                FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 24
            };
            var box = new Border
            {
                Height = 170,
                Padding = new Thickness(12, 10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Background = this.FindResource("B.Field") as IBrush,
                BorderBrush = this.FindResource("B.Border") as IBrush,
                Child = new ScrollViewer { Content = _out }
            };

            var qsy = Ui.Btn("Porta al pitch (QSY)", true);
            qsy.Click += (_, _) =>
            {
                var t = _main.Telemetry;
                if (t.FreqHz <= 0) return;
                double delta = FrequencyHz - _main.CurrentPitch;
                bool reverse = (t.Mode ?? "").ToUpperInvariant().Contains("CWR") || (t.Mode ?? "").ToUpperInvariant().Contains("-L");
                long target = t.FreqHz + (long)Math.Round(reverse ? -delta : delta);
                _node.SetFreqMode(target, "");
            };
            var copyCall = Ui.Btn("Copia nominativo");
            copyCall.Click += async (_, _) =>
            {
                string txt; lock (_lock) txt = _text.ToString();
                var call = System.Text.RegularExpressions.Regex.Matches(txt, @"\b[A-Z0-9]{1,3}[0-9][A-Z0-9]{0,3}[A-Z]\b").LastOrDefault()?.Value;
                if (call != null) { s.CwCall = call; s.Save(); await Dialogs.CopyText(this, call); }
            };
            var clear = Ui.Btn("Pulisci");
            clear.Click += (_, _) => { lock (_lock) _text.Clear(); };
            var close = Ui.Btn("Chiudi");
            close.Click += (_, _) => Close();
            var tools = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto") };
            void T(Control c, int col) { Grid.SetColumn(c, col); c.Margin = new Thickness(0, 0, 8, 0); tools.Children.Add(c); }
            T(qsy, 0); T(copyCall, 1); T(clear, 2); T(close, 4);

            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    head, box, tools,
                    Ui.Sub("«Porta al pitch» sposta il VFO A perché questo segnale cada sul pitch del decodificatore principale.", 12)
                }
            };
            Closed += (_, _) => _main.RemoveDecoder(_decoder);
        }

        /// <summary>Chiamato dal timer della finestra principale.</summary>
        public void Tick()
        {
            string t;
            lock (_lock) t = _text.ToString();
            if (_out.Text != t) _out.Text = t;
            _wpm.Text = $"{_decoder.Wpm:F0} WPM";
        }
    }
}
