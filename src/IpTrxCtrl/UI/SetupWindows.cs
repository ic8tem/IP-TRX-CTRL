using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using IpTrxCtrl.Core;
using IpTrxCtrl.Rig;

namespace IpTrxCtrl.UI
{
    /// <summary>Campi della radio, comuni a prima configurazione e Impostazioni.</summary>
    internal sealed class RadioFields
    {
        public static readonly int[] Bauds = { 4800, 9600, 19200, 38400, 57600, 115200 };
        public readonly ComboBox Model = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
        public readonly ComboBox Com = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
        public readonly ComboBox Baud = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
        public readonly Button Detect = Ui.Btn("Rileva");
        public readonly TextBlock Info = Ui.Sub("", 12);
        // collegamento CAT: Hamlib (seriale/rete) oppure TCI (ExpertSDR, Thetis…), anche per l'audio
        public readonly ComboBox Cat = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
        public readonly TextBox TciHost = new();
        public readonly TextBox TciPort = new() { Width = 110 };
        public readonly CheckBox TciAudio = new() { Content = "Audio della radio via TCI (niente schede audio né cavi virtuali sul server)" };
        private Control? _hamlibPanel, _tciPanel;
        private List<RigModelInfo> _models = new();
        private readonly AppSettings _s;

        public RadioFields(AppSettings s)
        {
            _s = s;
            Com.ItemsSource = Hamlib.SerialPorts().ToList();
            Com.SelectedItem = s.RigCom;
            if (Com.SelectedIndex < 0 && Com.ItemCount > 0) Com.SelectedIndex = 0;
            Baud.ItemsSource = Bauds;
            Baud.SelectedItem = s.RigBaud;
            if (Baud.SelectedIndex < 0) Baud.SelectedItem = 38400;
            Detect.Click += async (_, _) => await DetectAsync();
            Cat.ItemsSource = new[] { "Hamlib (porta seriale o rete)", "TCI (ExpertSDR, Thetis e altri SDR)" };
            Cat.SelectedIndex = s.CatViaTci ? 1 : 0;
            Cat.SelectionChanged += (_, _) => UpdCat();
            TciHost.Text = s.TciHost;
            TciPort.Text = s.TciPort.ToString(CultureInfo.InvariantCulture);
            TciAudio.IsChecked = s.InputDeviceServer == IpTrxCtrl.Audio.TciAudioBackend.DeviceName || s.OutputDeviceServer == IpTrxCtrl.Audio.TciAudioBackend.DeviceName;
            _ = LoadModelsAsync();
        }

        public async Task LoadModelsAsync()
        {
            Info.Text = "Leggo l'elenco delle radio da Hamlib…";
            _models = await Hamlib.ListModelsAsync(_s);
            if (_models.Count == 0)
            {
                Info.Text = Hamlib.InstallHint;
                Model.ItemsSource = new[] { _s.RigModel > 0 ? $"{_s.RigModelName} ({_s.RigModel})" : "(nessuna radio)" };
                Model.SelectedIndex = 0;
                return;
            }
            Model.ItemsSource = _models.Select(m => $"{m} ({m.Id})").ToList();
            int idx = _models.FindIndex(m => m.Id == _s.RigModel);
            Model.SelectedIndex = idx >= 0 ? idx : 0;
            Info.Text = $"Hamlib {Hamlib.Version(_s)} · {_models.Count} radio";
        }

        public RigModelInfo? Selected => Model.SelectedIndex >= 0 && Model.SelectedIndex < _models.Count ? _models[Model.SelectedIndex] : null;

        private async Task DetectAsync()
        {
            var m = Selected;
            if (m == null) { Info.Text = "Scegli prima il modello di radio."; return; }
            Detect.IsEnabled = false;
            var res = await Hamlib.DetectAsync(m.Id, _s, new Progress<string>(t => Info.Text = t));
            Detect.IsEnabled = true;
            if (res == null) { Info.Text = "La radio non risponde su nessuna porta: controlla cavo, alimentazione e impostazioni CI-V/CAT."; return; }
            Com.SelectedItem = res.Value.com;
            Baud.SelectedItem = res.Value.baud;
            Info.Text = $"Trovata su {res.Value.com} a {res.Value.baud} baud.";
        }

        /// <summary>Salva i campi; restituisce true se sono cambiate le schede audio del server (audio TCI).</summary>
        public bool Save()
        {
            bool tci = Cat.SelectedIndex == 1;
            _s.CatViaTci = tci;
            if (tci)
            {
                _s.TciHost = string.IsNullOrWhiteSpace(TciHost.Text) ? "127.0.0.1" : TciHost.Text.Trim();
                if (int.TryParse(TciPort.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tp) && tp > 0 && tp < 65536) _s.TciPort = tp;
            }
            else
            {
                var m = Selected;
                if (m != null) { _s.RigModel = m.Id; _s.RigModelName = m.ToString(); }
                _s.RigCom = Com.SelectedItem as string ?? _s.RigCom;
                if (Baud.SelectedItem is int b) _s.RigBaud = b;
            }
            // audio TCI: le schede del server diventano «TCI»; togliendolo tornano a quelle predefinite
            string dev = IpTrxCtrl.Audio.TciAudioBackend.DeviceName;
            string oldIn = _s.InputDeviceServer, oldOut = _s.OutputDeviceServer;
            if (tci && TciAudio.IsChecked == true) { _s.InputDeviceServer = dev; _s.OutputDeviceServer = dev; }
            else
            {
                if (_s.InputDeviceServer == dev) _s.InputDeviceServer = "";
                if (_s.OutputDeviceServer == dev) _s.OutputDeviceServer = "";
            }
            return oldIn != _s.InputDeviceServer || oldOut != _s.OutputDeviceServer;
        }

        private void UpdCat()
        {
            bool tci = Cat.SelectedIndex == 1;
            if (_hamlibPanel != null) _hamlibPanel.IsVisible = !tci;
            if (_tciPanel != null) _tciPanel.IsVisible = tci;
        }

        public Control Build()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*,10,110,10,Auto") };
            void A(Control c, int col) { Grid.SetColumn(c, col); g.Children.Add(c); }
            A(Ui.Col(0, Ui.Lbl("Modello (Hamlib)"), Model), 0);
            A(Ui.Col(0, Ui.Lbl("Porta seriale"), Com), 2);
            A(Ui.Col(0, Ui.Lbl("Velocità"), Baud), 4);
            Detect.VerticalAlignment = VerticalAlignment.Bottom;
            A(Detect, 6);
            _hamlibPanel = Ui.Col(6, g, Info);

            var t = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,110") };
            var hostCol = Ui.Col(0, Ui.Lbl("Indirizzo del programma della radio (TCI)"), TciHost);
            var portCol = Ui.Col(0, Ui.Lbl("Porta TCI"), TciPort);
            Grid.SetColumn(portCol, 2);
            t.Children.Add(hostCol); t.Children.Add(portCol);
            _tciPanel = Ui.Col(6, t, TciAudio,
                Ui.Sub("Attiva il TCI nel programma della radio (ExpertSDR: Opzioni › TCI, porta 40001 di serie; Thetis: Setup › Network › TCI). "
                     + "Dal TCI arrivano frequenze, modo, split, potenza, S-meter, potenza in uscita e ROS; il PTT «CAT» diventa il PTT TCI. "
                     + "Con l'audio TCI l'audio di ricezione e trasmissione passa dal WebSocket, senza schede né cavi.", 12));
            UpdCat();
            return Ui.Col(8, Ui.Col(0, Ui.Lbl("Collegamento CAT"), Cat), _hamlibPanel, _tciPanel);
        }
    }

    /// <summary>Prima configurazione: ruolo, nominativo, radio e dichiarazione d'uso.</summary>
    public sealed class FirstRunWindow : Window
    {
        public bool Completed { get; private set; }

        public FirstRunWindow(AppSettings s)
        {
            Title = "IP-TRX-CTRL · prima configurazione";
            Width = 720;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var role = s.Role;
            var bServer = Ui.Btn("SERVER\nla radio è collegata a questo PC");
            var bClient = Ui.Btn("CLIENT\ncomando la radio da remoto");
            foreach (var b in new[] { bServer, bClient }) { b.Height = 80; b.HorizontalAlignment = HorizontalAlignment.Stretch; }
            var roles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
            Grid.SetColumn(bClient, 2);
            roles.Children.Add(bServer); roles.Children.Add(bClient);
            var call = new TextBox { Text = s.Callsign };
            var radio = new RadioFields(s);
            var radioPanel = Ui.Col(6, Ui.H2("RADIO (SOLO SERVER)"), radio.Build());
            var decl = new CheckBox { Content = "Dichiaro che userò il programma per attività radioamatoriale, nel rispetto della mia licenza." };
            var lic = Ui.Btn("Informazioni e licenze");
            lic.Click += async (_, _) => await new LicensesWindow(s).ShowDialog(this);
            var go = Ui.Btn("Continua", true);
            var msg = Ui.Sub("", 13);

            void Upd()
            {
                bServer.Classes.Set("accent", role == NodeRole.Server);
                bClient.Classes.Set("accent", role == NodeRole.Client);
                radioPanel.IsVisible = role == NodeRole.Server;
            }
            bServer.Click += (_, _) => { role = NodeRole.Server; Upd(); };
            bClient.Click += (_, _) => { role = NodeRole.Client; Upd(); };
            Upd();

            go.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(call.Text)) { msg.Text = "Inserisci il nominativo."; return; }
                if (decl.IsChecked != true) { msg.Text = "Per usare il programma serve la dichiarazione d'uso radioamatoriale."; return; }
                s.Role = role;
                s.Callsign = call.Text.Trim().ToUpperInvariant();
                s.DeclarationAccepted = true;
                if (role == NodeRole.Server) radio.Save();
                s.FirstRunDone = true;
                s.Save();
                SessionLog.Write($"PRIMA CONFIGURAZIONE ruolo={role} nominativo={s.Callsign} dichiarazione uso radioamatoriale accettata");
                Completed = true;
                Close();
            };

            var buttons = Ui.Row(8, lic, go);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            Content = new StackPanel
            {
                Margin = new Thickness(28),
                Spacing = 14,
                Children =
                {
                    Ui.H1("Benvenuto in IP-TRX-CTRL"),
                    Ui.Sub("Scegli il ruolo di questo PC. I ruoli si possono invertire in seguito con «Inverti ruoli».", 14),
                    roles,
                    Ui.Col(0, Ui.Lbl("Nominativo"), call),
                    radioPanel,
                    decl,
                    msg,
                    buttons
                }
            };
        }
    }

    /// <summary>Impostazioni: ruolo, nominativo, radio, Hamlib, rete, abbinamento, chiavi per le autorità.</summary>
    public sealed class SettingsWindow : Window
    {
        public bool Saved { get; private set; }
        public bool RigChanged { get; private set; }
        public bool AudioChanged { get; private set; }
        public bool PairingReset { get; private set; }
        public bool DigitalChanged { get; private set; }
        public bool SerialInputChanged { get; private set; }

        public SettingsWindow(AppSettings s, Node node)
        {
            Title = "Impostazioni";
            Width = 760;
            Height = 820;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var call = new TextBox { Text = s.Callsign };
            var role = new ComboBox { ItemsSource = new[] { "Server (la radio è collegata qui)", "Client (operatore remoto)" }, SelectedIndex = s.Role == NodeRole.Server ? 0 : 1 };
            var radio = new RadioFields(s);
            var rigPath = new TextBox { Text = s.RigctldPath, Watermark = "automatico" };
            var browse = Ui.Btn("…");
            browse.Click += async (_, _) =>
            {
                var f = (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Percorso di rigctld", AllowMultiple = false })).FirstOrDefault();
                string? p = f?.TryGetLocalPath();
                if (p != null) { rigPath.Text = p; s.RigctldPath = p; await radio.LoadModelsAsync(); }
            };
            var gain = new TextBox { Text = s.TxGainLevel };
            var ext = new CheckBox { Content = "Usa un rigctld esterno già avviato", IsChecked = s.UseExternalRigctld };
            var extHost = new TextBox { Text = s.ExternalRigctldHost };
            var extPort = new TextBox { Text = s.ExternalRigctldPort.ToString(CultureInfo.InvariantCulture) };
            // ---- protezione ROS: decide il server; dal client si cambia a distanza quando è collegato
            bool swrServer = s.Role == NodeRole.Server;
            var tel = node.LastTelemetry;
            bool swrRemoteOk = !swrServer && node.Connected && tel.CatOk && tel.SwrKnown;
            bool swrOnNow = swrServer ? s.SwrProtection : tel.SwrProtect;
            double swrWarnNow = swrServer ? s.SwrWarn : tel.SwrWarnAt, swrTripNow = swrServer ? s.SwrTrip : tel.SwrTripAt;
            var swrOn = new CheckBox { Content = "Protezione ROS (consigliata, attiva dall'installazione)", IsChecked = swrOnNow };
            var swrWarn = new TextBox { Text = swrWarnNow.ToString("0.0", CultureInfo.InvariantCulture), Width = 90 };
            var swrTrip = new TextBox { Text = swrTripNow.ToString("0.0", CultureInfo.InvariantCulture), Width = 90 };
            var swrNote = Ui.Sub(swrServer
                ? "Sopra la soglia di avviso la barra del ROS lampeggia; sopra la soglia di blocco, per due letture di fila, il PTT si toglie, i messaggi e il CW si fermano e la trasmissione resta bloccata finché l'operatore non la sblocca dalla finestra che compare sul client (e qui)."
                : swrRemoteOk
                    ? "Le impostazioni valgono sul server (è lui che misura il ROS e blocca la trasmissione): vengono mandate al server al salvataggio."
                    : "Le impostazioni valgono sul server. Per cambiarle da qui collega il client a un server 0.2.4 o successivo con la radio in funzione.", 13);
            bool swrEditable = swrServer || swrRemoteOk;
            swrOn.IsEnabled = swrEditable; swrWarn.IsEnabled = swrEditable; swrTrip.IsEnabled = swrEditable;
            var prefill = new Slider { Minimum = 40, Maximum = 600, Value = s.PrefillMs };
            var prefillTxt = Ui.Sub($"Jitter buffer (pre-riempimento): {s.PrefillMs} ms", 13);
            prefill.ValueChanged += (_, e) => prefillTxt.Text = $"Jitter buffer (pre-riempimento): {(int)e.NewValue} ms";
            // ---- client: porta seriale d'ingresso (pedale PTT, tasto CW meccanico, porta virtuale)
            var inPorts = new List<string> { "(nessuna)" };
            inPorts.AddRange(Hamlib.SerialPorts());
            if (s.InputCom.Length > 0 && !inPorts.Contains(s.InputCom)) inPorts.Add(s.InputCom);
            var inCom = new ComboBox { ItemsSource = inPorts, SelectedIndex = Math.Max(0, inPorts.IndexOf(s.InputCom)), MinWidth = 220 };
            string[] pins = { "nessuno", "CTS (contatto con RTS)", "DSR (contatto con DTR)", "DCD (contatto con DTR)" };
            var inPtt = new ComboBox { ItemsSource = pins, SelectedIndex = (int)s.InputPttPin, MinWidth = 220 };
            var inCw = new ComboBox { ItemsSource = pins, SelectedIndex = (int)s.InputCwPin, MinWidth = 220 };
            var keyDelay = new TextBox { Text = s.CwKeyDelayMs.ToString(CultureInfo.InvariantCulture), Width = 90 };

            // ---- client: programmi digitali (cavi audio virtuali + rigctld locale)
            bool digSupported = IpTrxCtrl.Audio.VirtualCables.Supported;
            var outs = node.Audio.Outputs();
            var ins = node.Audio.Inputs();
            var digEnable = new CheckBox { Content = "Usa programmi digitali (WSJT-X, JTDX, JS8Call, fldigi…) con questo client", IsChecked = s.DigitalEnabled };
            var digTo = new ComboBox { ItemsSource = outs, SelectedItem = outs.Contains(s.DigitalToProgram) ? s.DigitalToProgram : null, MinWidth = 300 };
            var digFrom = new ComboBox { ItemsSource = ins, SelectedItem = ins.Contains(s.DigitalFromProgram) ? s.DigitalFromProgram : null, MinWidth = 300 };
            var rigctlPort = new TextBox { Text = s.RigctlPort.ToString(CultureInfo.InvariantCulture), Width = 90 };
            var digNote = Ui.Sub(IpTrxCtrl.Audio.VirtualCables.Describe(ins, outs), 13);
            digNote.TextWrapping = TextWrapping.Wrap;
            var digAuto = Ui.Btn("Scegli i cavi in automatico");
            digAuto.Click += (_, _) =>
            {
                var (to, from) = IpTrxCtrl.Audio.VirtualCables.Suggest(ins, outs);
                if (outs.Contains(to)) digTo.SelectedItem = to;
                if (ins.Contains(from)) digFrom.SelectedItem = from;
            };
            var digState = Ui.Sub("Stato: " + node.DigitalStatus, 13);
            digState.TextWrapping = TextWrapping.Wrap;
            var digDevices = Ui.Col(10,
                Ui.Col(0, Ui.Lbl("Audio verso il programma (il programma lo usa come INGRESSO)"), digTo),
                Ui.Col(0, Ui.Lbl("Audio dal programma (il programma lo usa come USCITA)"), digFrom),
                digAuto);
            digDevices.IsVisible = !IpTrxCtrl.Audio.VirtualCables.IsLinux;

            var brokers = new TextBox { Text = string.Join(Environment.NewLine, s.MqttBrokers), AcceptsReturn = true, Height = 90 };

            var newPair = Ui.Btn("Nuovo abbinamento");
            newPair.Click += async (_, _) =>
            {
                if (!await Dialogs.YesNo(this, "Cancello l'abbinamento attuale? Dovrai abbinare di nuovo i due PC con un codice.")) return;
                node.ForgetPairing();
                PairingReset = true;
            };
            var export = Ui.Btn("Esporta chiavi per le autorità");
            export.Click += async (_, _) => await ExportAsync(s);
            var lic = Ui.Btn("Informazioni e licenze");
            lic.Click += async (_, _) => await new LicensesWindow(s).ShowDialog(this);
            var folder = Ui.Btn("Apri cartella dati");
            folder.Click += (_, _) => OpenFolder(AppSettings.Folder);

            var hamlibRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto") };
            Grid.SetColumn(browse, 2);
            hamlibRow.Children.Add(rigPath); hamlibRow.Children.Add(browse);
            var extRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,100") };
            Grid.SetColumn(extPort, 2);
            extRow.Children.Add(extHost); extRow.Children.Add(extPort);

            var save = Ui.Btn("Salva", true);
            var cancel = Ui.Btn("Annulla");
            cancel.Click += (_, _) => Close();
            save.Click += async (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(call.Text)) { await Dialogs.Info(this, "Inserisci il nominativo."); return; }
                if (swrEditable)
                {
                    static double Num(string? txt) =>
                        double.TryParse((txt ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
                    var (swrW, swrT) = Node.SwrLimits(Num(swrWarn.Text), Num(swrTrip.Text));
                    bool swrNew = swrOn.IsChecked == true;
                    if (!swrNew && swrOnNow && !await Dialogs.YesNo(this, "Disattivi la protezione ROS? Con il ROS alto la radio continuerà a trasmettere e lo stadio finale potrebbe danneggiarsi.")) return;
                    if (swrNew != swrOnNow || Math.Abs(swrW - swrWarnNow) > 0.001 || Math.Abs(swrT - swrTripNow) > 0.001)
                        node.SetSwrProtection(swrNew, swrW, swrT);
                }
                int oldModel = s.RigModel, oldBaud = s.RigBaud, oldPort = s.ExternalRigctldPort;
                string oldCom = s.RigCom, oldHost = s.ExternalRigctldHost, oldPath = s.RigctldPath;
                bool oldExt = s.UseExternalRigctld, oldTci = s.CatViaTci;
                string oldTciHost = s.TciHost; int oldTciPort = s.TciPort;
                s.Callsign = call.Text.Trim().ToUpperInvariant();
                AudioChanged = radio.Save();
                s.RigctldPath = (rigPath.Text ?? "").Trim();
                s.TxGainLevel = string.IsNullOrWhiteSpace(gain.Text) ? "MICGAIN" : gain.Text.Trim();
                s.UseExternalRigctld = ext.IsChecked == true;
                s.ExternalRigctldHost = (extHost.Text ?? "").Trim();
                if (int.TryParse(extPort.Text, out int p)) s.ExternalRigctldPort = p;
                s.PrefillMs = (int)prefill.Value;

                string newCom = inCom.SelectedIndex <= 0 ? "" : inCom.SelectedItem as string ?? "";
                var newPtt = (InputPin)Math.Max(0, inPtt.SelectedIndex);
                var newCw = (InputPin)Math.Max(0, inCw.SelectedIndex);
                SerialInputChanged = newCom != s.InputCom || newPtt != s.InputPttPin || newCw != s.InputCwPin;
                s.InputCom = newCom; s.InputPttPin = newPtt; s.InputCwPin = newCw;
                if (int.TryParse(keyDelay.Text, out int kd)) s.CwKeyDelayMs = Math.Clamp(kd, 50, 2000);

                bool newDig = digSupported && digEnable.IsChecked == true;
                string newTo = digTo.SelectedItem as string ?? "", newFrom = digFrom.SelectedItem as string ?? "";
                int newRp = int.TryParse(rigctlPort.Text, out int rp) && rp > 1023 && rp < 65536 ? rp : s.RigctlPort;
                DigitalChanged = newDig != s.DigitalEnabled || newTo != s.DigitalToProgram || newFrom != s.DigitalFromProgram || newRp != s.RigctlPort;
                s.DigitalEnabled = newDig; s.DigitalToProgram = newTo; s.DigitalFromProgram = newFrom; s.RigctlPort = newRp;
                var list = (brokers.Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (list.Count > 0) s.MqttBrokers = list;
                RigChanged = oldModel != s.RigModel || oldBaud != s.RigBaud || oldCom != s.RigCom || oldExt != s.UseExternalRigctld
                             || oldHost != s.ExternalRigctldHost || oldPort != s.ExternalRigctldPort || oldPath != s.RigctldPath
                             || oldTci != s.CatViaTci || oldTciHost != s.TciHost || oldTciPort != s.TciPort;
                var newRole = role.SelectedIndex == 0 ? NodeRole.Server : NodeRole.Client;
                bool roleChanged = newRole != s.Role;
                s.Role = newRole;
                s.Save();
                if (roleChanged)
                {
                    SessionLog.Write($"RUOLO cambiato a mano: ora {newRole}");
                    await Dialogs.Info(this, "Il programma si riavvia con il nuovo ruolo.");
                    string? exe = Environment.ProcessPath;
                    if (exe != null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
                    Environment.Exit(0);
                }
                Saved = true;
                Close();
            };
            var bottom = Ui.Row(8, save, cancel);
            bottom.HorizontalAlignment = HorizontalAlignment.Right;

            var body = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 16,
                Children =
                {
                    Ui.H1("Impostazioni"),
                    Ui.Panel(Ui.Col(10, Ui.H2("STAZIONE"), Ui.Col(0, Ui.Lbl("Nominativo"), call), Ui.Col(0, Ui.Lbl("Ruolo di questo PC"), role))),
                    Ui.Panel(Ui.Col(10, Ui.H2("RADIO (SERVER)"), radio.Build(),
                        Ui.Col(0, Ui.Lbl("Percorso di rigctld (vuoto = automatico)"), hamlibRow),
                        Ui.Col(0, Ui.Lbl("Livello Hamlib per il guadagno TX (es. MICGAIN)"), gain),
                        ext, Ui.Col(0, Ui.Lbl("rigctld esterno: indirizzo e porta"), extRow),
                        swrOn,
                        Ui.Row(16, Ui.Col(0, Ui.Lbl("Avviso da ROS"), swrWarn), Ui.Col(0, Ui.Lbl("Blocco da ROS"), swrTrip)),
                        swrNote)),
                    Ui.Panel(Ui.Col(10, Ui.H2("PEDALE PTT E TASTO CW (CLIENT)"),
                        Ui.Sub("Porta seriale del client letta come ingresso: un pedale tra RTS e CTS, un tasto verticale tra DTR e DSR. Va bene anche la porta virtuale di un altro programma (su Windows una coppia com0com).", 13),
                        Ui.Col(0, Ui.Lbl("Porta seriale"), inCom),
                        Ui.Row(16, Ui.Col(0, Ui.Lbl("Ingresso del PTT"), inPtt), Ui.Col(0, Ui.Lbl("Ingresso del tasto CW"), inCw)),
                        Ui.Col(0, Ui.Lbl("Ritardo fisso del tasto sul server (ms): assorbe i ritardi di internet"), keyDelay))),
                    digSupported
                        ? Ui.Panel(Ui.Col(10, Ui.H2("PROGRAMMI DIGITALI (CLIENT)"), digEnable, digNote, digDevices,
                            Ui.Col(0, Ui.Lbl("Porta del rigctld locale: nel programma scegli la radio Hamlib «NET rigctl» su 127.0.0.1 e questa porta"), rigctlPort),
                            digState))
                        : Ui.Panel(Ui.Col(10, Ui.H2("PROGRAMMI DIGITALI (CLIENT)"), Ui.Sub("Non disponibili nella versione per Windows 32 bit.", 13))),
                    Ui.Panel(Ui.Col(10, Ui.H2("RETE E AUDIO"), prefillTxt, prefill,
                        Ui.Col(0, Ui.Lbl("Broker MQTT (uno per riga, host:porta)"), brokers))),
                    Ui.Panel(Ui.Col(10, Ui.H2("ABBINAMENTO E REGISTRI"),
                        Ui.Sub("Impronta dell'abbinamento: " + (node.IsPaired ? node.Fingerprint : "nessun abbinamento"), 13),
                        Ui.Row(8, newPair, export, folder, lic))),
                    bottom
                }
            };
            Content = new ScrollViewer { Content = body };
        }

        private async Task ExportAsync(AppSettings s)
        {
            byte[]? master = KeyStore.LoadMaster();
            if (master == null) { await Dialogs.Info(this, "Non c'è ancora un abbinamento da esportare."); return; }
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Esporta chiavi e registro per le autorità",
                SuggestedFileName = $"IP-TRX-CTRL_{s.Callsign}_{DateTime.Now:yyyyMMdd_HHmm}.txt",
                DefaultExtension = "txt"
            });
            string? path = file?.TryGetLocalPath();
            if (path == null) return;
            var sb = new StringBuilder();
            sb.AppendLine("IP-TRX-CTRL – CHIAVI E REGISTRO DELLE SESSIONI");
            sb.AppendLine("Uso dichiarato: attività radioamatoriale.");
            sb.AppendLine($"Stazione: {s.Callsign}   Peer: {s.PeerCallsign}   Ruolo attuale: {s.Role}");
            sb.AppendLine($"Esportato il: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (ora locale)");
            sb.AppendLine();
            sb.AppendLine("CHIAVE MASTER DELL'ABBINAMENTO (256 bit, esadecimale):");
            sb.AppendLine(Convert.ToHexString(master));
            sb.AppendLine("Impronta: " + Crypto.Fingerprint(master));
            sb.AppendLine();
            sb.AppendLine("COME DECIFRARE IL TRAFFICO REGISTRATO");
            sb.AppendLine("- Due connessioni TCP: canale 1 = controllo/CAT, canale 2 = audio.");
            sb.AppendLine("- Per ogni connessione il registro riporta nC (client) e nS (server).");
            sb.AppendLine("- Chiave di sessione = HKDF-SHA256(IKM = chiave master, salt = nC||nS, info = \"aal-session-<canale>\"), 32 byte.");
            sb.AppendLine("- Ogni frame: lunghezza (4 byte, big endian) | nonce (12) | tag (16) | testo cifrato; AES-256-GCM.");
            sb.AppendLine("- Testo in chiaro: 1° byte = tipo. 0x01 = audio: numero sequenza (4 byte LE) + PCM 16 bit LE mono 8 kHz.");
            sb.AppendLine();
            sb.AppendLine("REGISTRO DELLE SESSIONI (UTC)");
            sb.AppendLine(SessionLog.ReadAll());
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            SessionLog.Write("ESPORTAZIONE chiavi e registro su file");
            await Dialogs.Info(this, "File esportato. Conservalo in un luogo sicuro: chi lo possiede può decifrare il traffico.");
        }

        private static void OpenFolder(string path)
        {
            try
            {
                if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                else if (OperatingSystem.IsMacOS()) Process.Start("open", $"\"{path}\"");
                else Process.Start("xdg-open", $"\"{path}\"");
            }
            catch { }
        }
    }

    /// <summary>Informazioni e licenze dei componenti di terze parti.</summary>
    public sealed class LicensesWindow : Window
    {
        public LicensesWindow(AppSettings s)
        {
            Title = "Informazioni e licenze";
            Width = 780;
            Height = 680;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var asm = Assembly.GetExecutingAssembly();
            string notices;
            using (var st = asm.GetManifestResourceStream("notices/THIRD-PARTY-NOTICES.txt"))
                notices = st == null ? "" : new StreamReader(st).ReadToEnd();
            var sb = new StringBuilder();
            sb.AppendLine($"IP-TRX-CTRL {asm.GetName().Version}");
            sb.AppendLine("Copyright (C) 2026 IC8TEM");
            sb.AppendLine("Questo programma è software libero: puoi ridistribuirlo e/o modificarlo secondo i termini della");
            sb.AppendLine("GNU General Public License, versione 3 o (a tua scelta) successiva, pubblicata dalla Free Software");
            sb.AppendLine("Foundation. È distribuito nella speranza che sia utile, ma SENZA ALCUNA GARANZIA; senza neppure la");
            sb.AppendLine("garanzia implicita di COMMERCIABILITÀ o IDONEITÀ PER UN PARTICOLARE SCOPO. Il testo completo è in fondo.");
            sb.AppendLine("Sorgenti: https://github.com/ic8tem/IP-TRX-CTRL");
            string hv = Hamlib.Version(s);
            sb.AppendLine("Hamlib: " + (hv.Length > 0 ? hv : "non trovato") + (OperatingSystem.IsWindows() && Hamlib.IsEmbedded ? " (incluso nel programma)" : " (installato nel sistema)"));
            sb.AppendLine();
            sb.AppendLine(notices);
            using (var lic = asm.GetManifestResourceStream("notices/LICENSE"))
                if (lic != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("==================== IP-TRX-CTRL – GNU GPL 3.0 ====================");
                    sb.AppendLine(new StreamReader(lic).ReadToEnd());
                }
            foreach (string f in new[] { "COPYING", "COPYING.LIB" })
            {
                string? t = Hamlib.ReadText(f);
                if (t == null) continue;
                sb.AppendLine();
                sb.AppendLine($"==================== Hamlib – {f} ====================");
                sb.AppendLine(t);
            }
            var text = new SelectableTextBlock
            {
                Text = sb.ToString(),
                FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            Content = new ScrollViewer { Content = new Border { Padding = new Thickness(20), Child = text } };
        }
    }

    /// <summary>Guida rapida.</summary>
    public sealed class HelpWindow : Window
    {
        private const string Text =
@"IP-TRX-CTRL – GUIDA RAPIDA

RUOLI
Server = il PC collegato alla radio (audio, CAT con Hamlib o TCI, PTT).
Client = il PC dell'operatore. «Inverti ruoli» scambia i ruoli tra i due PC collegati.

ABBINAMENTO (una sola volta)
Il server mostra un codice, il client lo scrive e preme «Abbina». La finestra mostra il dialogo con i broker MQTT
e con il server: ▶ inviato, ◀ ricevuto, ✗ errore, ✓ completato. «Copia registro» mette il testo negli appunti.

PTT
Pulsante «premi e tieni» o barra spaziatrice. Il server manda in trasmissione la radio con il metodo scelto:
CAT, RTS, DTR, VOX o TCI. Per sicurezza il PTT si rilascia se il collegamento cade o la finestra perde il fuoco.
La spia in alto a destra del riquadro del server lampeggia di rosso quando la radio trasmette.

KEYER (solo client)
SSB: F1..F8 trasmettono messaggi WAV (pulsante «File WAV…»). Filtro, compressore e volume come in WavPTT.
CW: F1..F8 trasmettono testi in CW con il keyer interno della radio (pulsante «Macro…»).
Variabili: {MYCALL} {CALL} {NR} {RST}. Ctrl+Alt+F1..F8 funzionano anche da altri programmi. Esc interrompe.

MODO CW
Cascata 0-2000 Hz e decodificatore sul pitch (200-1000 Hz, letto dalla radio). Clic su un segnale nella fascia
del pitch per spostare il pitch; clic fuori dalla fascia per aprire una finestra di decodifica in più.

CAT VIA TCI (ExpertSDR, Thetis e altri SDR)
Impostazioni › Radio (server) › Collegamento CAT: «TCI», con indirizzo e porta del programma della radio (di serie
127.0.0.1 e 40001). Frequenze, modo, split, potenza, S-meter, potenza in uscita e ROS arrivano dal TCI; il PTT
«CAT» diventa il PTT TCI e il testo CW parte con il keyer del programma della radio. Con «Audio della radio via
TCI» anche l'audio di ricezione e trasmissione passa dal TCI: niente schede audio né cavi virtuali sul server.
I programmi digitali del client funzionano come con Hamlib. Il pitch CW non arriva dal TCI: va regolato uguale
nel programma e nella radio.
Fonti usate per il protocollo (confrontate tra loro; dove non concordano il programma legge i dati di ogni blocco):
- Hamlib TCI Audio Sidechannel Protocol: intestazione audio di 64 byte, formati, TX_CHRONO
  https://raw.githubusercontent.com/jfrancis42/hamlib-tci-sidecar/HEAD/PROTOCOL.md
- JTDX, interfaccia TCI: tipi di flusso e comandi vfo, modulation, trx, split_enable, drive
  https://deepwiki.com/jtdx-project/jtdx/4.1-tci-transceiver-interface
- PE5JW, panoramica del protocollo TCI: comandi audio, sensori (rx_smeter, tx_sensors) e differenze tra programmi
  https://github.com/pe5jw/tci-protocol-overview

SINTONIA CON LA ROTELLA
Mouse su una cifra della QRG (VFO A o VFO B) e rotella: quella cifra sale o scende di uno, con riporto sulle
cifre a sinistra (sulle centinaia di Hz un passo = 100 Hz, sui kHz = 1 kHz…). La radio riceve la frequenza
mentre giri; il numero mostrato è quello scelto finché la radio non conferma.

SPLIT
VFO B = VFO A + offset, da −50 a +50 kHz; passo 1 kHz in fonia, 0,1 kHz in CW.

POTENZA
Cursore da 1 a 100 W se la radio converte in watt, altrimenti da 1 a 100 %.

SCHEDE AUDIO DEL SERVER
Il client vede se le schede del server sono silenziate dal sistema operativo e può togliere il mute.
Dal riquadro «Schede audio del server» il client sceglie anche quali schede usa il server (ingresso = audio della
radio, uscita = audio verso la radio): il server riavvia l'audio sulle schede nuove e, se non si aprono, torna a
quelle di prima e lo segnala. La scelta resta salvata sul server.

AUDIO
Sempre 8 kHz, 16 bit mono, cifrato AES-256-GCM.

MODO CW: FILTRO E CASCATA
Entrando in CW il programma chiede alla radio il filtro largo (3000 Hz di serie, scelta 1200-3600 Hz) così la
cascata mostra tutta la banda 0-2000 Hz; togli «Filtro largo» per tornare al filtro stretto. Il cursore
«Intensità» abbassa il colore del rumore e fa risaltare i segnali. Dalla 0.2.5 la cascata scorre 3 volte più
veloce (una colonna ogni 21 ms circa, circa 14 secondi visibili). A destra lo scostamento dal pitch (000 sul
pitch), a sinistra la QRG reale dei segnali; col mouse sulla cascata si legge la QRG precisa del punto.

PEDALE PTT E TASTO CW (client)
Impostazioni › «Pedale PTT e tasto CW». Collega un pedale tra RTS e CTS e un tasto verticale tra DTR e DSR di una
porta seriale (anche adattatore USB): il programma alza RTS e DTR e legge CTS/DSR/DCD ogni millisecondo.
Il tasto viene ripetuto sul server con un ritardo fisso (250 ms di serie) che assorbe i ritardi di internet.
Va bene anche la porta virtuale di un altro programma: su Windows una coppia com0com (RTS→CTS, DTR→DSR).

PROGRAMMI DIGITALI (client, non su Windows 32 bit)
Impostazioni › «Programmi digitali». Nel programma (WSJT-X, JTDX, JS8Call, fldigi…) scegli la radio Hamlib
«NET rigctl», indirizzo 127.0.0.1:4532, PTT «CAT». Audio:
- Windows 64 bit: VB-CABLE (lo propone l'installatore). Con un cavo il programma usa «CABLE Output» come ingresso
  e «CABLE Input» come uscita; IP-TRX-CTRL fa il contrario e lavora in semi-duplex. Con due cavi uno per direzione.
- macOS: BlackHole 2ch (brew install blackhole-2ch).
- Linux / Raspberry: il programma crea da solo i cavi IPTRX_RX e IPTRX_TX con pactl. Nel programma digitale:
  ingresso «Monitor of IP-TRX-CTRL_RX», uscita «IP-TRX-CTRL_TX».
Quando il programma chiede il PTT, IP-TRX-CTRL trasmette con l'audio del programma invece del microfono.

VERSIONI COMPATIBILI
Server e client devono avere versioni dello stesso «protocollo». La versione di questo PC è nella finestra «Informazioni e licenze».
  Versioni          Protocollo   Compatibili con
  0.1.0 – 0.1.3     1            solo 0.1.x
  0.2.0 – 0.2.6     2            tutte le 0.2.x
Dentro la serie 0.2 alcune funzioni servono su tutti e due i PC:
  scelta delle schede audio del server dal client ......... 0.2.1 o successiva
  server senza UPnP e collegamento sulla rete locale ...... 0.2.2 o successiva
  scambio della versione e controllo automatico ........... 0.2.3 o successiva
  porte in uscita del client comunicate al server ......... 0.2.4 o successiva
  protezione ROS (avviso e blocco della trasmissione) ..... 0.2.4 o successiva
  CAT e audio del server via TCI ......................... 0.2.5 sul server
Dalla 0.2.3 i due PC si scambiano la versione (sul broker MQTT durante l'abbinamento e all'inizio di ogni
collegamento). Se il protocollo è diverso l'abbinamento si ferma e compare una finestra; se l'altro PC è
più vecchio compare un avviso. La versione dell'altro PC si vede nella riga della sua rete.

PROTEZIONE ROS
Il server legge il ROS (SWR) della radio mentre trasmette. Con ROS da 1.9 in su la barra del ROS lampeggia in
rosso (avviso). Con ROS da 2.5 in su per due letture di fila (circa un secondo) il server toglie il PTT, ferma
messaggi WAV, CW e audio verso la radio e blocca la trasmissione; sul client (e sul server) compare una finestra.
La trasmissione resta bloccata finché non premi «Sblocca la trasmissione»: se hai chiuso la finestra, clic sulla
barra del ROS per riaprirla. Controlla antenna, cavi, accordatore e banda prima di sbloccare.
La protezione è attiva dall'installazione; si disattiva o si cambiano le soglie in Impostazioni › Radio (server),
anche dal client quando è collegato. Funziona se la radio fornisce il ROS a Hamlib (livello SWR).

UPNP E LED
All'avvio (server e client) una finestra dice se il router ha UPnP attivo e, se manca, la causa probabile e cosa
fare. Il LED «UPnP» in alto è verde lampeggiante finché il router risponde (controllo ogni 60 s), rosso se manca,
grigio durante la ricerca. Clic sul LED per riaprire la finestra.

PORTE E COLLEGAMENTO
Il server apre due porte con UPnP (controllo TCP, audio TCP+UDP) e chiede a un server esterno se sono davvero
raggiungibili da internet; le prime due porte utili vanno sul broker MQTT. Il client le prova; se non riesce apre
le sue porte, le verifica e le manda al server sul broker MQTT: il server si collega al client (collegamento
inverso) oppure cambia le sue porte. Il ciclo si ripete per 20 giri; dopo il ventesimo compare «Nessuna porta è
apribile» e il client si chiude con «Exit». Attiva UPnP su almeno uno dei due router.
Router senza UPnP: il server usa comunque due porte locali (vanno bene con DMZ o inoltro manuale, e sempre dalla
stessa rete locale). Se i due PC sono nella stessa casa il client prova anche l'indirizzo locale del server.
Se le porte del server non sono raggiungibili da internet compare un avviso con le porte da inoltrare.
A collegamento fatto l'audio prova anche UDP (STUN e perforazione del NAT); se UDP non passa resta su TCP.
Il percorso in uso compare nella riga della rete dell'altro PC («audio UDP» o «audio TCP»).
RETI FILTRATE (dalla 0.2.4): all'avvio il client prova quali porte escono dalla sua rete (443, 80, 8443, 8080…).
Se la rete le filtra (aziende, alberghi, Wi-Fi pubblici) manda l'elenco al server sul broker MQTT e il server
chiede al router proprio quelle come porte esterne, inoltrate verso porte libere del PC.

ERRORI E REGISTRI
Impostazioni › «Apri cartella dati»: errori.log (errori imprevisti) e sessioni.log (collegamenti e abbinamento).
Windows: %APPDATA%\IP-TRX-CTRL · Linux, Raspberry Pi e macOS: ~/.config/IP-TRX-CTRL";

        public HelpWindow()
        {
            Title = "Help";
            Width = 760;
            Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Content = new ScrollViewer
            {
                Content = new SelectableTextBlock { Text = Text, Margin = new Thickness(24), FontSize = 14, TextWrapping = TextWrapping.Wrap }
            };
        }
    }
}
