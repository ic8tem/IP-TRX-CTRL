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

        public void Save()
        {
            var m = Selected;
            if (m != null) { _s.RigModel = m.Id; _s.RigModelName = m.ToString(); }
            _s.RigCom = Com.SelectedItem as string ?? _s.RigCom;
            if (Baud.SelectedItem is int b) _s.RigBaud = b;
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
            return Ui.Col(6, g, Info);
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
        public bool PairingReset { get; private set; }

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
            var prefill = new Slider { Minimum = 40, Maximum = 600, Value = s.PrefillMs };
            var prefillTxt = Ui.Sub($"Jitter buffer (pre-riempimento): {s.PrefillMs} ms", 13);
            prefill.ValueChanged += (_, e) => prefillTxt.Text = $"Jitter buffer (pre-riempimento): {(int)e.NewValue} ms";
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
                int oldModel = s.RigModel, oldBaud = s.RigBaud, oldPort = s.ExternalRigctldPort;
                string oldCom = s.RigCom, oldHost = s.ExternalRigctldHost, oldPath = s.RigctldPath;
                bool oldExt = s.UseExternalRigctld;
                s.Callsign = call.Text.Trim().ToUpperInvariant();
                radio.Save();
                s.RigctldPath = (rigPath.Text ?? "").Trim();
                s.TxGainLevel = string.IsNullOrWhiteSpace(gain.Text) ? "MICGAIN" : gain.Text.Trim();
                s.UseExternalRigctld = ext.IsChecked == true;
                s.ExternalRigctldHost = (extHost.Text ?? "").Trim();
                if (int.TryParse(extPort.Text, out int p)) s.ExternalRigctldPort = p;
                s.PrefillMs = (int)prefill.Value;
                var list = (brokers.Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (list.Count > 0) s.MqttBrokers = list;
                RigChanged = oldModel != s.RigModel || oldBaud != s.RigBaud || oldCom != s.RigCom || oldExt != s.UseExternalRigctld
                             || oldHost != s.ExternalRigctldHost || oldPort != s.ExternalRigctldPort || oldPath != s.RigctldPath;
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
                        ext, Ui.Col(0, Ui.Lbl("rigctld esterno: indirizzo e porta"), extRow))),
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
            sb.AppendLine("Sorgenti: https://github.com/ic8tem/IP-TRX-CTRL");
            string hv = Hamlib.Version(s);
            sb.AppendLine("Hamlib: " + (hv.Length > 0 ? hv : "non trovato") + (OperatingSystem.IsWindows() && Hamlib.IsEmbedded ? " (incluso nel programma)" : " (installato nel sistema)"));
            sb.AppendLine();
            sb.AppendLine(notices);
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
Server = il PC collegato alla radio (audio, CAT con Hamlib, PTT).
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
Cascata 0-3000 Hz e decodificatore sul pitch (200-1000 Hz, letto dalla radio). Clic su un segnale nella fascia
del pitch per spostare il pitch; clic fuori dalla fascia per aprire una finestra di decodifica in più.

SPLIT
VFO B = VFO A + offset, da −50 a +50 kHz; passo 1 kHz in fonia, 0,1 kHz in CW.

POTENZA
Cursore da 1 a 100 W se la radio converte in watt, altrimenti da 1 a 100 %.

SCHEDE AUDIO DEL SERVER
Il client vede se le schede del server sono silenziate dal sistema operativo e può togliere il mute.

AUDIO
Sempre 8 kHz, 16 bit mono, cifrato AES-256-GCM.

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
