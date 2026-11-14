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
        /// <summary>Windows: regola del firewall per questo eseguibile (con conferma di amministratore).</summary>
        internal static string AllowInWindowsFirewall()
        {
            string? exe = Environment.ProcessPath;
            if (exe == null) return "Percorso del programma non disponibile.";
            string cmd = $"/c netsh advfirewall firewall delete rule name=all program=\"{exe}\" >nul 2>&1 & "
                       + $"netsh advfirewall firewall add rule name=\"IP-TRX-CTRL\" dir=in action=allow program=\"{exe}\" enable=yes profile=any";
            try
            {
                var p = Process.Start(new ProcessStartInfo("cmd.exe", cmd) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
                if (p == null) return "Il comando non è partito.";
                p.WaitForExit(20000);
                bool ok = p.HasExited && p.ExitCode == 0;
                SessionLog.Write(ok ? $"FIREWALL regola aggiunta per {exe}" : $"FIREWALL regola non aggiunta (codice {(p.HasExited ? p.ExitCode : -1)})");
                return ok ? "Fatto: il firewall di Windows ora consente le connessioni in arrivo a IP-TRX-CTRL su tutte le reti."
                          : "Il firewall non ha accettato la regola: prova da Sicurezza di Windows › Firewall e protezione rete › Consenti app attraverso il firewall.";
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return "Conferma di amministratore annullata: la regola del firewall non è stata aggiunta.";
            }
            catch (Exception ex) { return "Firewall: " + ex.Message; }
        }

        public bool Saved { get; private set; }
        public bool RigChanged { get; private set; }
        public bool AudioChanged { get; private set; }
        public bool PairingReset { get; private set; }
        public bool DigitalChanged { get; private set; }
        public bool SerialInputChanged { get; private set; }
        public bool PanChanged { get; private set; }
        public bool WebChanged { get; private set; }

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
            bool swrRemoteOk = !swrServer && node.Connected && tel.CatOk;
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
            // firewall di Windows: serve soprattutto sul server e con le versioni portatili (l'installatore lo fa da solo)
            var fwBtn = Ui.Btn("Consenti nel firewall di Windows");
            var fwNote = Ui.Sub("Toglie i blocchi del firewall di Windows per questo programma (anche quelli creati premendo «Annulla» alla prima "
                              + "richiesta di Windows) e consente le connessioni in arrivo su tutte le reti. Chiede la conferma di amministratore.", 12);
            fwBtn.IsVisible = fwNote.IsVisible = OperatingSystem.IsWindows();
            fwBtn.Click += async (_, _) => await Dialogs.Info(this, AllowInWindowsFirewall());
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

            bool clientRole = s.Role == NodeRole.Client;
            // client: elenco dei server; server: elenco dei client (un abbinamento nuovo non toglie gli altri)
            var newPair = Ui.Btn(clientRole ? "Abbina un altro server" : "Abbina un nuovo client");
            newPair.Click += async (_, _) =>
            {
                PairingReset = true;   // la finestra di abbinamento si apre chiudendo le Impostazioni
                await Dialogs.Info(this, clientRole ? "Chiudi le Impostazioni: si apre la finestra per il numero del nuovo server."
                    : "Chiudi le Impostazioni: si apre la finestra con il numero di abbinamento per il nuovo client. I client già abbinati restano.");
            };

            // ---- server: client abbinati
            var cliList = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            var cliName = new TextBox { Watermark = "nome del client" };
            var cliRename = Ui.Btn("Rinomina");
            var cliDelete = Ui.Btn("Togli accesso");
            var cliTx = new CheckBox { Content = "Può trasmettere (altrimenti solo ascolto)" };
            var cliInfo = Ui.Sub("", 12);
            bool cliFilling = false;
            List<ClientEntry> CliItems() => node.Clients.OrderByDescending(x => x.LastUsed).ToList();
            void ShowCli()
            {
                cliFilling = true;
                cliTx.IsChecked = (cliList.SelectedItem as ClientEntry)?.CanTransmit ?? true;
                cliTx.IsEnabled = cliList.SelectedItem is ClientEntry;
                cliFilling = false;
                if (cliList.SelectedItem is ClientEntry e)
                {
                    cliName.Text = e.Name;
                    cliInfo.Text = $"Nominativo: {(e.Callsign.Length > 0 ? e.Callsign : "—")} · abbinato il {e.PairedAt:dd/MM/yyyy}"
                                 + (e.How.Length > 0 ? $" con {e.How}" : "") + $" · ultimo uso {e.LastUsed:dd/MM/yyyy HH:mm}"
                                 + (e.CanTransmit ? "" : " · SOLO ASCOLTO")
                                 + (e.Id == node.SessionClientId ? " · COLLEGATO ORA" : "");
                }
                else { cliName.Text = ""; cliInfo.Text = "Nessun client abbinato."; }
            }
            void FillCli(string? keep)
            {
                var items = CliItems();
                cliList.ItemsSource = items;
                cliList.SelectedItem = items.FirstOrDefault(x => x.Id == keep) ?? items.FirstOrDefault();
                ShowCli();
            }
            cliList.SelectionChanged += (_, _) => ShowCli();
            FillCli(node.SessionClientId);
            cliRename.Click += (_, _) =>
            {
                if (cliList.SelectedItem is not ClientEntry e || string.IsNullOrWhiteSpace(cliName.Text)) return;
                node.RenameClient(e.Id, cliName.Text);
                FillCli(e.Id);
            };
            cliDelete.Click += async (_, _) =>
            {
                if (cliList.SelectedItem is not ClientEntry e) return;
                if (!await Dialogs.YesNo(this, $"Tolgo l'accesso a «{e.Name}»? Cancello la sua chiave: non potrà più collegarsi"
                        + (e.Id == node.SessionClientId ? " e viene scollegato subito" : "") + ". Per farlo rientrare servirà un nuovo abbinamento.")) return;
                node.RemoveClient(e.Id);
                FillCli(null);
            };
            var cliNameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto,8,Auto") };
            Grid.SetColumn(cliRename, 2); Grid.SetColumn(cliDelete, 4);
            cliNameRow.Children.Add(cliName); cliNameRow.Children.Add(cliRename); cliNameRow.Children.Add(cliDelete);
            cliTx.IsCheckedChanged += (_, _) =>
            {
                if (cliFilling || cliList.SelectedItem is not ClientEntry e) return;
                node.SetClientCanTransmit(e.Id, cliTx.IsChecked == true);
                ShowCli();
            };
            var cliPanel = Ui.Col(6, Ui.Lbl("Client abbinati (ognuno con la sua chiave; si collega uno per volta, gli altri trovano il server occupato)"), cliList, cliNameRow, cliTx, cliInfo);

            // ---- server: codice fisso
            var fixOn = new CheckBox { Content = "Accetta nuovi client con il codice fisso del server", IsChecked = node.FixedCodeEnabled };
            var fixCode = Ui.Mono(node.FixedCode.Length > 0 ? node.FixedCode : "—", 22);
            fixCode.FontWeight = FontWeight.Bold;
            var fixCopy = Ui.Btn("Copia codice");
            var fixNew = Ui.Btn("Cambia codice");
            var fixNote = Ui.Sub("Il codice fisso (12 cifre) vale finché non lo cambi o non disattivi l'opzione: chi lo riceve si abbina quando vuole, "
                + "anche dopo giorni, e poi entra nell'elenco dei client. È comodo ma meno sicuro del numero temporaneo: chi lo scopre può abbinarsi. "
                + "Dallo solo a persone fidate, cambialo se è circolato troppo e togli dall'elenco i client che non devono più entrare. "
                + "Cambiare il codice non toglie l'accesso ai client già abbinati.", 12);
            fixNote.TextWrapping = TextWrapping.Wrap;
            void ShowFix()
            {
                bool on = node.FixedCodeEnabled;
                fixCode.Text = on && node.FixedCode.Length > 0 ? node.FixedCode : "disattivato";
                fixCopy.IsEnabled = on; fixNew.IsEnabled = on;
            }
            ShowFix();
            fixOn.IsCheckedChanged += (_, _) => { node.SetFixedCodeEnabled(fixOn.IsChecked == true); ShowFix(); };
            fixCopy.Click += async (_, _) => await Dialogs.CopyText(this, node.FixedCode);
            fixNew.Click += async (_, _) =>
            {
                if (!await Dialogs.YesNo(this, "Genero un codice fisso nuovo? Quello attuale smette subito di valere; i client già abbinati restano.")) return;
                node.RegenerateFixedCode();
                ShowFix();
            };
            var fixPanel = Ui.Inset(Ui.Col(8, fixOn, Ui.Row(12, fixCode, fixCopy, fixNew), fixNote));
            var srvSidePanel = Ui.Col(12, cliPanel, fixPanel);
            srvSidePanel.IsVisible = !clientRole;
            var srvList = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = node.Servers.OrderByDescending(x => x.LastUsed).ToList() };
            var srvName = new TextBox { Watermark = "nome del server" };
            var srvRename = Ui.Btn("Rinomina");
            var srvDelete = Ui.Btn("Togli dall'elenco");
            var srvInfo = Ui.Sub("", 12);
            void ShowSrv()
            {
                if (srvList.SelectedItem is ServerEntry e)
                {
                    srvName.Text = e.Name;
                    srvInfo.Text = $"Nominativo: {(e.Callsign.Length > 0 ? e.Callsign : "—")} · abbinato il {e.PairedAt:dd/MM/yyyy} · ultimo uso {e.LastUsed:dd/MM/yyyy HH:mm}"
                                 + (e.Id == node.ActiveServer?.Id ? " · IN USO" : "");
                }
                else { srvName.Text = ""; srvInfo.Text = "Nessun server abbinato."; }
            }
            srvList.SelectionChanged += (_, _) => ShowSrv();
            srvList.SelectedItem = node.ActiveServer is { } act ? ((List<ServerEntry>)srvList.ItemsSource!).FirstOrDefault(x => x.Id == act.Id) : null;
            ShowSrv();
            srvRename.Click += (_, _) =>
            {
                if (srvList.SelectedItem is not ServerEntry e || string.IsNullOrWhiteSpace(srvName.Text)) return;
                node.RenameServer(e.Id, srvName.Text);
                var keep = e.Id;
                srvList.ItemsSource = node.Servers.OrderByDescending(x => x.LastUsed).ToList();
                srvList.SelectedItem = ((List<ServerEntry>)srvList.ItemsSource).FirstOrDefault(x => x.Id == keep);
            };
            srvDelete.Click += async (_, _) =>
            {
                if (srvList.SelectedItem is not ServerEntry e) return;
                if (!await Dialogs.YesNo(this, $"Tolgo «{e.Name}» dall'elenco e cancello la sua chiave? Per usarlo di nuovo servirà un nuovo numero di abbinamento.")) return;
                node.DeleteServer(e.Id);
                srvList.ItemsSource = node.Servers.OrderByDescending(x => x.LastUsed).ToList();
                srvList.SelectedItem = node.ActiveServer is { } a2 ? ((List<ServerEntry>)srvList.ItemsSource).FirstOrDefault(x => x.Id == a2.Id) : null;
                ShowSrv();
                if (!node.IsPaired) PairingReset = true;
            };
            var srvNameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto,8,Auto") };
            Grid.SetColumn(srvRename, 2); Grid.SetColumn(srvDelete, 4);
            srvNameRow.Children.Add(srvName); srvNameRow.Children.Add(srvRename); srvNameRow.Children.Add(srvDelete);
            var srvPanel = Ui.Col(6, Ui.Lbl("Server abbinati (il client si collega a uno per volta: si sceglie nella finestra principale)"), srvList, srvNameRow, srvInfo);
            srvPanel.IsVisible = clientRole;
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

            // ---- finestra principale: parti facoltative
            var optBands = new CheckBox { Content = "Pulsanti delle bande e passo di sintonia", IsChecked = s.ShowBands };
            var optCtl = new CheckBox { Content = "Comandi della radio (NR, NB, Notch, ATT, preamplificatore, AGC, compressore, squelch)", IsChecked = s.ShowRadioControls };
            var optMem = new CheckBox { Content = "Memorie (di questo PC e del server)", IsChecked = s.ShowMemories };
            var optAlc = new CheckBox { Content = "Strumento ALC", IsChecked = s.ShowAlc };
            var optPan = new CheckBox { Content = "Panadapter della radio (se il server lo fornisce)", IsChecked = s.ShowPanadapter };
            var optUpd = new CheckBox { Content = "Controlla se su GitHub c'è una versione nuova (all'avvio e ogni 6 ore)", IsChecked = s.CheckUpdates };
            bool selfUpd = Updater.CanSelfUpdate(out string selfWhy);
            var optAuto = new CheckBox
            {
                Content = new TextBlock { Text = "Aggiornamenti automatici: scarica e installa da solo la versione nuova e riavvia il programma, solo quando non c'è una sessione in corso e non si trasmette", TextWrapping = TextWrapping.Wrap },
                IsChecked = s.AutoUpdate && selfUpd, IsEnabled = selfUpd && s.CheckUpdates
            };
            optUpd.IsCheckedChanged += (_, _) => optAuto.IsEnabled = selfUpd && optUpd.IsChecked == true;
            var optUpdNote = Ui.Sub((selfUpd ? "" : "Aggiornamenti automatici non disponibili su questo PC: " + selfWhy + ". ")
                + "Il controllo legge la pagina pubblica delle release (api.github.com) e scarica il pacchetto da GitHub, senza account: GitHub vede l'indirizzo IP di chi chiede, come per qualunque pagina web. Nient'altro viene mandato. Impostazioni, chiavi e abbinamenti non si toccano.", 12);

            // ---- sicurezza in trasmissione (server)
            bool srvRole = s.Role == NodeRole.Server;
            var totOn = new CheckBox { Content = "Tempo massimo di una trasmissione", IsChecked = s.TxTimeoutEnabled };
            var totSec = new TextBox { Text = s.TxTimeoutSec.ToString(CultureInfo.InvariantCulture), Width = 80 };
            var qsyLock = new CheckBox { Content = "Niente cambi di frequenza, modo, filtro e split durante la trasmissione", IsChecked = s.QsyLockInTx };
            var locOn = new CheckBox { Content = "Precedenza a chi è alla radio: dopo una trasmissione locale il client non trasmette per", IsChecked = s.LocalPriority };
            var locMin = new TextBox { Text = s.LocalPriorityMin.ToString(CultureInfo.InvariantCulture), Width = 60 };
            var fixRx = new CheckBox { Content = "I client che si abbinano con il codice fisso partono in solo ascolto", IsChecked = s.FixedCodeRxOnly };
            var safeNote = Ui.Sub("Tempo massimo (da 30 a 600 s): conta le trasmissioni comandate dal programma (PTT, messaggi, tasto CW su linea seriale, programmi digitali); allo scadere il PTT si toglie e resta tolto finché l'operatore non lo rilascia. "
                + "Precedenza: se la radio trasmette senza che il programma l'abbia comandata (microfono o PTT della radio), o se si preme il PTT di questo programma sul server, la trasmissione del client si blocca per i minuti scelti (da 1 a 30); funziona con le radio che dicono via CAT quando sono in trasmissione. "
                + "Il blocco si toglie prima con un clic sulla riga sotto il PTT.", 12);

            // ---- panadapter (server)
            var panOn = new CheckBox { Content = "Panadapter della radio per i client e per questa finestra", IsChecked = s.PanEnabled };
            var panCom = new ComboBox { ItemsSource = new[] { "(nessuna)" }.Concat(Hamlib.SerialPorts()).ToList(), MinWidth = 180 };
            panCom.SelectedItem = s.PanCom.Length > 0 && ((List<string>)panCom.ItemsSource!).Contains(s.PanCom) ? s.PanCom : "(nessuna)";
            var panBauds = new[] { 9600, 19200, 38400, 57600, 115200 };
            var panBaud = new ComboBox { ItemsSource = panBauds.Select(b => b.ToString(CultureInfo.InvariantCulture)).ToList(), Width = 110 };
            panBaud.SelectedIndex = Math.Max(0, Array.IndexOf(panBauds, s.PanBaud));
            var panAddr = new TextBox { Text = s.PanCivAddr, Width = 70 };
            var panNote = Ui.Sub(s.CatViaTci
                ? "Il CAT passa dal TCI: il panadapter usa il flusso IQ del programma della radio (ExpertSDR, Thetis…), senza altre impostazioni."
                : "Radio Icom: serve una porta CI-V che Hamlib non usa, per esempio la seconda porta USB delle radio che la offrono come CI-V, impostata nel menu della radio. "
                  + "Indirizzo CI-V della radio in esadecimale (IC-7300 94, IC-705 A4, IC-9700 A2, IC-7610 98: controlla nel menu della radio). "
                  + "Il programma accende lo scopio e l'uscita dei dati della cascata (comandi CI-V 27 10 e 27 11). Con altre marche, per ora, il panadapter c'è solo via TCI.", 12);
            var panRows = Ui.Row(12, Ui.Col(0, Ui.Lbl("Porta CI-V"), panCom), Ui.Col(0, Ui.Lbl("Velocità"), panBaud), Ui.Col(0, Ui.Lbl("Indirizzo"), panAddr));
            panRows.IsVisible = !s.CatViaTci;

            // ---- accesso dal browser (server) e pagina «Trova il server» (tutti)
            var webOn = new CheckBox { Content = "Accesso dal browser: pagina web HTTPS della stazione (si entra con il codice fisso)", IsChecked = s.WebEnabled };
            var webPort = new TextBox { Text = s.WebPort.ToString(CultureInfo.InvariantCulture), Width = 80 };
            var webRouter = new CheckBox { Content = "Apri la porta anche sul router (UPnP): accesso da internet senza VPN", IsChecked = s.WebOpenRouter };
            var webRx = new CheckBox { Content = "Il browser parte in solo ascolto (poi si abilita nell'elenco dei client)", IsChecked = s.WebRxOnly };
            var webInfo = Ui.Sub(node.WebInfo() + (node.FixedCode.Length > 0 ? $" · Codice di accesso: {node.FixedCode}" : ""), 12);
            webInfo.TextWrapping = TextWrapping.Wrap;
            var webNote = Ui.Sub("Dal browser di telefono, tablet o PC, senza installare niente. La pagina «Trova il server» (si salva qui sotto e si tiene come file) "
                + "chiede l'indirizzo ai broker MQTT con il codice fisso: arriva cifrato. Da fuori casa il browser raggiunge il server con una VPN (Tailscale, WireGuard…) "
                + "o con la porta aperta sul router; dietro CGNAT solo con la VPN. La prima volta il browser avvisa del certificato: si confronta l'impronta e si accetta. "
                + "Sulla rete di casa la pagina si scarica anche da https://<indirizzo del server>:<porta>/trova-server.html.", 12);
            webNote.TextWrapping = TextWrapping.Wrap;
            var finder = Ui.Btn("Salva la pagina «Trova il server»…");
            finder.Click += async (_, _) =>
            {
                var page = Node.FinderPage();
                if (page == null) return;
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Salva la pagina «Trova il server»", SuggestedFileName = "trova-server.html", DefaultExtension = "html" });
                string? path = file?.TryGetLocalPath();
                if (path == null) return;
                try { File.WriteAllBytes(path, page); await Dialogs.Info(this, "Salvata. Copiala sul telefono o sul PC e aprila con il browser: chiede il codice fisso del server e trova l'indirizzo."); }
                catch (Exception ex) { await Dialogs.Info(this, "Non riesco a salvarla: " + ex.Message); }
            };

            // ---- rapporto per segnalare un problema
            var report = Ui.Btn("Rapporto per un problema…");
            report.Click += async (_, _) => await ReportAsync(s);

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
                // finestra principale
                s.ShowBands = optBands.IsChecked == true; s.ShowRadioControls = optCtl.IsChecked == true; s.ShowMemories = optMem.IsChecked == true;
                s.ShowAlc = optAlc.IsChecked == true; s.ShowPanadapter = optPan.IsChecked == true; s.CheckUpdates = optUpd.IsChecked == true; if (selfUpd) s.AutoUpdate = optAuto.IsChecked == true;
                if (srvRole)
                {
                    // sicurezza in trasmissione
                    bool totNew = totOn.IsChecked == true;
                    int sec = int.TryParse(totSec.Text, out int ts) ? Math.Clamp(ts, 30, 600) : s.TxTimeoutSec;
                    int min = int.TryParse(locMin.Text, out int lm) ? Math.Clamp(lm, 1, 30) : s.LocalPriorityMin;
                    if (totNew != s.TxTimeoutEnabled || sec != s.TxTimeoutSec || (qsyLock.IsChecked == true) != s.QsyLockInTx
                        || (locOn.IsChecked == true) != s.LocalPriority || min != s.LocalPriorityMin || (fixRx.IsChecked == true) != s.FixedCodeRxOnly)
                        SessionLog.Write($"SICUREZZA TX: tempo massimo {(totNew ? sec + " s" : "spento")} · blocco QSY {(qsyLock.IsChecked == true ? "sì" : "no")} · precedenza locale {(locOn.IsChecked == true ? min + " min" : "no")} · codice fisso in solo ascolto {(fixRx.IsChecked == true ? "sì" : "no")}");
                    s.TxTimeoutEnabled = totNew; s.TxTimeoutSec = sec; s.QsyLockInTx = qsyLock.IsChecked == true;
                    s.LocalPriority = locOn.IsChecked == true; s.LocalPriorityMin = min; s.FixedCodeRxOnly = fixRx.IsChecked == true;
                    // panadapter
                    string pc = panCom.SelectedItem as string ?? "";
                    if (pc.StartsWith("(")) pc = "";
                    int pb = panBaud.SelectedIndex >= 0 ? panBauds[panBaud.SelectedIndex] : s.PanBaud;
                    string pa = (panAddr.Text ?? "").Trim().ToUpperInvariant();
                    if (!byte.TryParse(pa.Replace("0X", "").Replace("H", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) pa = s.PanCivAddr;
                    PanChanged = (panOn.IsChecked == true) != s.PanEnabled || pc != s.PanCom || pb != s.PanBaud || pa != s.PanCivAddr;
                    s.PanEnabled = panOn.IsChecked == true; s.PanCom = pc; s.PanBaud = pb; s.PanCivAddr = pa;
                    // accesso dal browser
                    int wp = int.TryParse(webPort.Text, out int wpp) ? Math.Clamp(wpp, 1024, 65535) : s.WebPort;
                    WebChanged = (webOn.IsChecked == true) != s.WebEnabled || wp != s.WebPort || (webRouter.IsChecked == true) != s.WebOpenRouter;
                    s.WebEnabled = webOn.IsChecked == true; s.WebPort = wp; s.WebOpenRouter = webRouter.IsChecked == true; s.WebRxOnly = webRx.IsChecked == true;
                    node.Rig.PushTelemetry();
                }
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
                    Ui.Panel(Ui.Col(8, Ui.H2("FINESTRA PRINCIPALE"),
                        Ui.Sub("Le parti nuove si possono nascondere per tenere la finestra più piccola.", 12),
                        optBands, optCtl, optMem, optAlc, optPan, optUpd, optAuto, optUpdNote)),
                    srvRole
                        ? Ui.Panel(Ui.Col(8, Ui.H2("SICUREZZA IN TRASMISSIONE (SERVER)"),
                            Ui.Row(8, totOn, totSec, Ui.Sub("secondi", 13)), qsyLock, Ui.Row(8, locOn, locMin, Ui.Sub("minuti", 13)), fixRx, safeNote))
                        : Ui.Panel(Ui.Col(8, Ui.H2("SICUREZZA IN TRASMISSIONE"),
                            Ui.Sub("Tempo massimo di trasmissione, blocco dei cambi di frequenza in trasmissione, precedenza a chi è alla radio e solo ascolto si impostano sul server: valgono per tutti i client.", 13))),
                    srvRole
                        ? Ui.Panel(Ui.Col(8, Ui.H2("PANADAPTER DELLA RADIO (SERVER)"), panOn, panRows, panNote))
                        : new Control(),
                    srvRole
                        ? Ui.Panel(Ui.Col(8, Ui.H2("ACCESSO DAL BROWSER (SERVER)"), webOn, Ui.Row(8, Ui.Sub("Porta HTTPS", 13), webPort), webRouter, webRx, webInfo, webNote, finder))
                        : Ui.Panel(Ui.Col(8, Ui.H2("ACCESSO DAL BROWSER"),
                            Ui.Sub("Si attiva sul server. Da qui puoi salvare la pagina «Trova il server» da tenere sul telefono o su un altro PC.", 13), finder)),
                    Ui.Panel(Ui.Col(10, Ui.H2("RETE E AUDIO"), prefillTxt, prefill,
                        Ui.Col(0, Ui.Lbl("Broker MQTT (uno per riga, host:porta)"), brokers),
                        fwBtn, fwNote)),
                    Ui.Panel(Ui.Col(10, Ui.H2("ABBINAMENTO E REGISTRI"),
                        Ui.Sub("Impronta dell'abbinamento" + (clientRole ? " in uso" : "") + ": " + (node.IsPaired ? node.Fingerprint : "nessun abbinamento"), 13),
                        srvPanel, srvSidePanel,
                        Ui.Row(8, newPair, export, folder),
                        Ui.Row(8, report, lic))),
                    bottom
                }
            };
            Content = new ScrollViewer { Content = body };
        }

        /// <summary>Rapporto per segnalare un problema: registri con gli indirizzi IP pubblici mascherati.</summary>
        private async Task ReportAsync(AppSettings s)
        {
            var desc = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 110, Watermark = "Cosa è successo, quando, cosa ti aspettavi (facoltativo)" };
            var saveBtn = Ui.Btn("Salva il rapporto…", true);
            var issue = Ui.Btn("Apri la pagina delle segnalazioni");
            var close = Ui.Btn("Chiudi");
            var result = Ui.Sub("", 12);
            var w = new Window
            {
                Title = "Rapporto per segnalare un problema",
                Width = 600,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = Ui.Col(10,
                    Ui.H1("Rapporto per segnalare un problema"),
                    Ui.Sub("Il rapporto raccoglie versione, sistema, impostazioni principali (senza chiavi né codici) e gli ultimi registri errori.log e sessioni.log. "
                         + "Gli indirizzi IP pubblici diventano [IP-1], [IP-2]…; restano solo gli indirizzi di rete locale. Prima di allegarlo puoi aprirlo e leggerlo.", 13),
                    desc, Ui.Row(8, saveBtn, issue, close), result)
            };
            if (w.Content is Control c) c.Margin = new Thickness(24);
            saveBtn.Click += async (_, _) =>
            {
                var file = await w.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Salva il rapporto",
                    SuggestedFileName = $"IP-TRX-CTRL_rapporto_{s.Callsign}_{DateTime.Now:yyyyMMdd_HHmm}.txt",
                    DefaultExtension = "txt"
                });
                string? path = file?.TryGetLocalPath();
                if (path == null) return;
                try
                {
                    File.WriteAllText(path, Support.BuildReport(s, desc.Text ?? ""), Encoding.UTF8);
                    SessionLog.Write("RAPPORTO per un problema salvato su file");
                    result.Text = "Salvato. Apri una segnalazione (Issue) su GitHub e allega il file.";
                }
                catch (Exception ex) { result.Text = "Non riesco a salvare: " + ex.Message; }
            };
            issue.Click += (_, _) => MainWindow.OpenUrl(Support.IssuesUrl);
            close.Click += (_, _) => w.Close();
            await w.ShowDialog(this);
        }

        private async Task ExportAsync(AppSettings s)
        {
            // client: le chiavi di tutti i server abbinati; server: quelle di tutti i client abbinati
            var keys = new List<(string label, byte[] key)>();
            if (s.Role == NodeRole.Client)
            {
                foreach (var e in s.Servers)
                    if (KeyStore.LoadMaster(e.Id) is { } k) keys.Add(($"{e.Name}" + (e.Callsign.Length > 0 ? $" ({e.Callsign})" : "") + $" · id {e.Id}", k));
            }
            else
            {
                foreach (var e in s.Clients)
                    if (KeyStore.LoadMaster("cli-" + e.Id) is { } k) keys.Add(($"client {e.Name}" + (e.Callsign.Length > 0 && e.Callsign != e.Name ? $" ({e.Callsign})" : "") + $" · id {e.Id}", k));
            }
            if (keys.Count == 0) { await Dialogs.Info(this, "Non c'è ancora un abbinamento da esportare."); return; }
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
            foreach (var (label, key) in keys)
            {
                sb.AppendLine($"CHIAVE MASTER DELL'ABBINAMENTO · {label} (256 bit, esadecimale):");
                sb.AppendLine(Convert.ToHexString(key));
                sb.AppendLine("Impronta: " + Crypto.Fingerprint(key));
                sb.AppendLine();
            }
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

ABBINAMENTO (una sola volta per server)
Il server mostra un NUMERO DI 10 CIFRE (es. 482 913 0571), valido 10 minuti e una sola volta: il client lo scrive
e preme «Abbina». Il numero non contiene l'indirizzo del server: il client lo riceve cifrato (AES-256-GCM) dal
broker MQTT, quindi per abbinare serve almeno un broker raggiungibile. Il calcolo della chiave dal numero è
volutamente lento (qualche secondo sul client) e cambia ogni 10 minuti: provare tutti i numeri non serve.
La finestra mostra il dialogo con i broker MQTT e con il server: ▶ inviato, ◀ ricevuto, ✗ errore, ✓ completato.
«Copia registro» mette il testo negli appunti.
SERVER ABBINATI (client): ogni server abbinato resta in un elenco con la sua chiave. Nel riquadro del server della
finestra principale si sceglie a quale collegarsi (uno solo per volta: scegliendone un altro il collegamento in
corso si chiude); «Abbina nuovo…» aggiunge un server. In Impostazioni › Abbinamento si rinomina o si toglie un
server.
CLIENT ABBINATI (server): anche il server tiene un elenco di client, ognuno con la sua chiave; un abbinamento nuovo
non toglie gli altri. Si collega un client per volta: un secondo client trova «server occupato» con il nominativo
di chi opera e riprova da solo ogni 30 secondi. In Impostazioni › Abbinamento si rinomina un client o gli si
toglie l'accesso (chiave cancellata, scollegato subito se è collegato).
CODICE FISSO DEL SERVER: opzione delle Impostazioni (disattivata all'installazione). Un codice di 12 cifre che vale
finché non lo cambi: chi lo riceve si abbina quando vuole, scrivendolo al posto del numero. Comodo ma meno sicuro
del numero temporaneo: dallo solo a persone fidate e cambialo se è circolato troppo. Cambiarlo non toglie
l'accesso ai client già abbinati.
INDIRIZZI CIFRATI: l'indirizzo IP del server passa solo cifrato sui broker MQTT (abbinamento e riconnessioni) e
nei codici di riserva; i nomi dei canali MQTT sono impronte che non rivelano nulla.

FINESTRA PRINCIPALE (dalla 0.3.0)
Più piccola: tre colonne in alto (radio · strumenti e PTT · l'altro PC), panadapter se attivo, schede in basso
(Keyer, CW in modo CW, Audio, Collegamento). Reti, metodo PTT e schede audio del server sono riquadri richiudibili.
Bande, passo, comandi della radio, memorie, ALC, panadapter e controllo della versione nuova si nascondono da
Impostazioni › Finestra principale.

PTT
Pulsante «premi e tieni» o barra spaziatrice. Il server manda in trasmissione la radio con il metodo scelto:
CAT, RTS, DTR, VOX o TCI. Per sicurezza il PTT si rilascia se il collegamento cade o la finestra perde il fuoco.
La spia RX/TX lampeggia di rosso quando la radio trasmette. Sotto il PTT una riga dice da quanto dura la
trasmissione e il tempo massimo, oppure perché la trasmissione non è permessa.

SICUREZZA IN TRASMISSIONE (server, dalla 0.3.0)
Impostazioni › Sicurezza in trasmissione (server), valgono per tutti i client:
- Tempo massimo di una trasmissione (attivo, 180 s; da 30 a 600): allo scadere il PTT si toglie e resta tolto
  finché non lo rilasci. 15 secondi prima la riga sotto il PTT diventa rossa.
- Niente cambi di frequenza, modo, filtro, VFO B e split mentre la radio trasmette (attivo).
- Precedenza a chi è alla radio (attiva, 3 minuti; da 1 a 30): se la radio trasmette senza che il programma l'abbia
  comandata, o se si preme il PTT del programma sul server, il client non trasmette per quei minuti. Sul server un
  clic sulla riga sotto il PTT toglie il blocco. Serve una radio che dica via CAT quando trasmette.
- Solo ascolto: in Impostazioni › Abbinamento il server sceglie per ogni client «Può trasmettere» o no. Chi si
  abbina con il codice fisso parte in solo ascolto (opzione attiva di serie).

COMANDI DELLA RADIO (dalla 0.3.0)
NR, NB, NOTCH (notch automatico ANF), COMP (compressore della radio): acceso/spento. ATT e PRE: ogni clic passa al
valore successivo che la radio accetta, poi spento. AGC: veloce, medio, lento. SQL: squelch 0-100 %. Strumenti
ALC e Po accanto al ROS. Sono comandi standard di Hamlib: un pulsante grigio = la radio non lo fornisce.
Con il CAT via TCI funzionano NR, NB, NOTCH e SQL.

BANDE, PASSO E MEMORIE (dalla 0.3.0)
Bande da 160 m a 70 cm: un clic porta all'ultima frequenza usata su quella banda. Passo di sintonia da 1 Hz a
25 kHz con ◀ ▶ (anche la rotella sopra i pulsanti). Memorie: «Salva…» le mette in questo PC oppure sul server,
comuni a tutti i client; «Vai» (o doppio clic) le richiama.

PANADAPTER DELLA RADIO (dalla 0.3.0)
Lo spettro della radio, a scelta: sul server Impostazioni › Panadapter (spento all'installazione), su ogni PC
Impostazioni › Finestra principale. SDR con CAT via TCI: flusso IQ, niente altro da impostare. Icom con scopio:
una porta CI-V che Hamlib non usa (per esempio la seconda porta USB impostata come CI-V), velocità e indirizzo
CI-V della radio. Clic sullo spettro = sintonia lì, rotella = un passo. Funzione nuova: se resta in attesa dei
dati, la riga di stato e sessioni.log (righe PANADAPTER) dicono cosa manca.
Fonti: Icom CI-V Reference Guide IC-9700 e IC-7300MK2 (comandi 27 00, 27 10, 27 11)
  https://www.icomfrance.com/uploads/files/produit/not-IC-9700_ENG_CI-V_1-en.pdf
  https://icomuk.co.uk/files/icom/PDF/productAdditionalFile/IC-7300MK2_ENG_CI-V_0.pdf
  Libreria TCI github.com/ftl/tci (iq_start, iq_samplerate, dds, rx_nr_enable, rx_nb_enable, rx_anf_enable, sql_enable)
  https://pkg.go.dev/github.com/ftl/tci/client
  Manuale di rigctl (funzioni NR, NB, ANF, COMP; livelli ATT, PREAMP, AGC, SQL, ALC)
  https://hamlib.sourceforge.net/html/rigctl.1.html

ACCESSO DAL BROWSER (server, dalla 0.3.1)
Impostazioni › Accesso dal browser: il server serve una pagina HTTPS (porta 8443) per telefono, tablet e PC, senza
installare niente. Si entra con il codice fisso del server: il codice non passa in rete (sfida e risposta).
La pagina «Trova il server» (Impostazioni › «Salva la pagina…», da tenere come file sul telefono o sul PC) chiede
l'indirizzo ai broker MQTT con il codice fisso: arriva cifrato insieme all'impronta del certificato. Prova prima
la VPN, poi la rete di casa, poi l'IP pubblico. Da fuori casa serve una VPN (Tailscale, WireGuard…) o la porta
aperta sul router; dietro CGNAT solo la VPN. La prima volta il browser avvisa del certificato fatto dal server:
si confronta l'impronta e si accetta (iPhone: si installa il certificato come profilo). Il browser compare
nell'elenco dei client come «Browser»: lì si sceglie se può trasmettere. Un operatore per volta, come sempre.

RAPPORTO PER UN PROBLEMA E VERSIONE NUOVA (dalla 0.3.0)
Impostazioni › Abbinamento e registri › «Rapporto per un problema…»: salva un file con versione, sistema,
impostazioni principali e gli ultimi registri, con gli indirizzi IP pubblici sostituiti da [IP-1], [IP-2]…; poi
«Apri la pagina delle segnalazioni» apre le Issue su GitHub, dove si allega il file.
All'avvio e ogni 6 ore il programma chiede a GitHub l'ultima release (api.github.com, senza account).

AGGIORNAMENTI AUTOMATICI (dalla 0.3.2)
Se la release è più nuova, il programma scarica il pacchetto adatto a questo PC, ne controlla dimensione e impronta
SHA-256 e si riavvia nella versione nuova, solo quando non c'è una sessione in corso e non si trasmette: con la
sessione aperta la versione nuova resta pronta e si installa appena finisce. Il server riparte da solo con le stesse
impostazioni; impostazioni, chiavi, abbinamenti e registri non si toccano.
Si aggiornano da soli: Setup e zip di Windows, tar.gz portatili (Linux, Raspberry Pi, Alpine), AppImage,
IP-TRX-CTRL.app sul Mac (in Applicazioni o in un'altra cartella scrivibile).
NON si aggiornano da soli i pacchetti di sistema .deb, .rpm, Arch e Slackware (installati da root in /opt): compare
solo la riga della versione nuova; si installa il pacchetto nuovo con lo stesso comando della prima volta.
Con gli aggiornamenti automatici spenti (Impostazioni › Finestra principale) resta la riga in alto con «Aggiorna
adesso». La 0.3.0 e la 0.3.1 non si aggiornano da sole: la 0.3.2 si installa a mano una volta.

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
  0.2.0 – 0.2.8     2            tutte le 0.2.x
  0.3.0 - 0.3.2     3            solo 0.3.x (numero a 10 cifre, codice fisso, più client, sicurezza TX, panadapter; 0.3.1 browser;
                                 0.3.2 aggiornamenti automatici)
Dentro la serie 0.2 alcune funzioni servono su tutti e due i PC:
  scelta delle schede audio del server dal client ......... 0.2.1 o successiva
  server senza UPnP e collegamento sulla rete locale ...... 0.2.2 o successiva
  scambio della versione e controllo automatico ........... 0.2.3 o successiva
  porte in uscita del client comunicate al server ......... 0.2.4 o successiva
  protezione ROS (avviso e blocco della trasmissione) ..... 0.2.4 o successiva
  CAT e audio del server via TCI ......................... 0.2.5 sul server
  ciclo delle porte anche nell'abbinamento ............... 0.2.7 su entrambi i PC
  numero a 10 cifre ed elenco dei server sul client ....... 0.3.0 su entrambi i PC
  più client sul server e codice fisso del server ......... 0.3.0 su entrambi i PC
  sicurezza in trasmissione, comandi radio, panadapter .... 0.3.0 su entrambi i PC
  accesso dal browser (pagina web HTTPS) .................. 0.3.1 sul server
  aggiornamenti automatici ............................... 0.3.2 su ciascun PC (ognuno si aggiorna per sé)
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
ABBINAMENTO CON IL CICLO DELLE PORTE (dalla 0.2.7): il server dice al client se le sue porte sono raggiungibili da
internet (e se è dietro CGNAT). Se i due PC escono con lo stesso IP pubblico il client prova prima la rete locale.
Se il collegamento diretto non riesce partono fino a 20 giri: in ognuno il client apre una porta NUOVA (a giri
alterni una comune come 443, 80, 8080 o una a caso), la verifica e la propone al server sul broker; il server
prova a collegarsi a lei (collegamento inverso), poi apre anche lui una porta nuova, la verifica e la comunica;
il client la prova subito. Nessuna porta viene provata due volte. Una porta del server raggiungibile da internet
diventa la sua porta di controllo. Tutto lo scambio compare riga per riga nella finestra di abbinamento.
ROUTER (dalla 0.2.8): il router si cerca prima con UPnP e solo se non risponde con NAT-PMP. Se rifiuta le porte
il programma prova l'altro protocollo; se le rifiuta comunque usa una porta del PC (rete locale, DMZ, inoltro
manuale) e l'abbinamento continua con il ciclo delle porte e il collegamento inverso.
FIREWALL DI WINDOWS: l'installatore consente il programma su tutte le reti; con le versioni portatili usa
Impostazioni › Rete e audio › «Consenti nel firewall di Windows» (serve soprattutto sul server).

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
