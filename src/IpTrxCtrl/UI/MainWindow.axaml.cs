using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using IpTrxCtrl.Audio;
using IpTrxCtrl.Core;
using IpTrxCtrl.Net;
using IpTrxCtrl.Platform;
using IpTrxCtrl.Rig;

namespace IpTrxCtrl.UI
{
    public partial class MainWindow : Window
    {
        private static readonly string[] Modes = { "USB", "LSB", "CW", "CWR", "FM", "AM", "PKTUSB", "PKTLSB", "PKTFM", "RTTY" };
        private static readonly int[] CwWidths = { 50, 75, 100, 150, 200 };
        private static readonly int[] CwFilters = { 1200, 2400, 2700, 3000, 3600 };

        private readonly AppSettings _s;
        private readonly Node _node;
        private readonly GlobalHotkeys _hotkeys = new();
        private IpTrxCtrl.Platform.SerialInput? _serialIn;
        private readonly DispatcherTimer _uiTimer;
        private RigTelemetry _tel = new();
        private bool _loading = true;
        private bool _manualPtt;
        private int _blinkTick;
        private bool _cwLayout;
        private volatile bool _cwActive;

        // tasti F
        private readonly Button[] _fButtons = new Button[8];
        private readonly TextBlock[] _fNames = new TextBlock[8];
        private readonly Dictionary<int, (string key, short[] clip)> _clips = new();

        // split e potenza (modifiche in attesa di invio)
        private double _offsetKhz;
        private DateTime _offsetEditedAt = DateTime.MinValue;
        private double? _powerPending;
        private DateTime _powerPendingAt, _powerSentAt = DateTime.MinValue;
        private float? _devVolPendingIn, _devVolPendingOut;
        private DateTime _devVolAt;
        private DateTime _devUserAt = DateTime.MinValue;

        // CW
        private readonly Spectrum _spectrum = new();
        private readonly CwDecoder _decoder;
        private readonly StringBuilder _decoded = new();
        private readonly object _decLock = new();
        private readonly List<CwDecoder> _extraDecoders = new();
        private readonly List<DecodeWindow> _decodeWindows = new();
        private bool _pitchFromRadioDone;

        public MainWindow() : this(AppSettings.Load()) { }

        public MainWindow(AppSettings s)
        {
            InitializeComponent();
            _s = s;
            _node = new Node(s);
            _decoder = new CwDecoder(s.CwPitch, s.CwDecodeWidth) { ThresholdFactor = ThresholdOf(s.CwThreshold), FixedWpm = s.CwRxWpm };
            _decoder.Text += t => { lock (_decLock) { _decoded.Append(t); if (_decoded.Length > 400) _decoded.Remove(0, _decoded.Length - 400); } };
            _spectrum.Row += row => Wf.Push(row);

            BuildCombos();
            BuildFKeys();
            LoadControls();
            HookEvents();

            _node.StatsChanged += () => Dispatcher.UIThread.Post(RefreshStats);
            _node.TelemetryChanged += t => Dispatcher.UIThread.Post(() => { _tel = t; RefreshRadio(); });
            _node.DeviceStateChanged += d => Dispatcher.UIThread.Post(() => RefreshDevices(d));
            _node.FallbackCodeChanged += code => Dispatcher.UIThread.Post(() => ShowServerFallback(code));
            _node.NeedFallbackCode += need => Dispatcher.UIThread.Post(() => PanelFallbackClient.IsVisible = need && _s.Role == NodeRole.Client);
            _node.Notice += msg => Dispatcher.UIThread.Post(() => { TxtNotice.Text = msg; TxtNotice.IsVisible = true; });
            _node.SwapRequested += answer => Dispatcher.UIThread.Post(() => AskSwap(answer));
            _node.RoleChanged += () => Dispatcher.UIThread.Post(OnRoleChanged);
            _node.PortsFailed += reason => Dispatcher.UIThread.Post(() => ShowPortsFailed(reason));
            _node.LocalPttChanged += on => Dispatcher.UIThread.Post(() => ShowPttState(on));
            _node.Audio.Keyer.Changed += () => Dispatcher.UIThread.Post(RefreshKeyerStatus);
            _node.Audio.RxSamples += OnRxSamples;
            _hotkeys.Pressed += i => Dispatcher.UIThread.Post(() => FKey(i));

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _uiTimer.Tick += (_, _) => OnUiTick();
            _uiTimer.Start();

            Opened += OnOpened;
            Closing += (_, _) => OnClosingWindow();
            Deactivated += (_, _) => { if (_manualPtt) SetManualPtt(false); };
            AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
            AddHandler(KeyUpEvent, OnKeyUpTunnel, RoutingStrategies.Tunnel);
        }

        private static double ThresholdOf(int v) => 0.25 + Math.Clamp(v, 0, 100) / 200.0;

        private void OnOpened(object? sender, EventArgs e)
        {
            _node.Start();
            if (_s.Role == NodeRole.Client) _hotkeys.Start();
            StartSerialInput();
            ApplyRoleTexts();
            RefreshStats();
            RefreshRadio();
            if (!string.IsNullOrEmpty(_node.Audio.LastError)) { TxtNotice.Text = _node.Audio.LastError; TxtNotice.IsVisible = true; }
            if (!_node.IsPaired) ShowPairing();
        }

        private void OnClosingWindow()
        {
            _uiTimer.Stop();
            foreach (var w in _decodeWindows.ToList()) w.Close();
            _hotkeys.Dispose();
            _serialIn?.Dispose();
            _node.Dispose();
            _s.Save();
        }

        private async void ShowPairing()
        {
            var w = new PairingWindow(_node, _s);
            await w.ShowDialog(this);
            ApplyRoleTexts();
        }

        // ================================================================= costruzione

        private void BuildCombos()
        {
            CmbTheme.ItemsSource = Themes.All.Select(t => t.Name).ToList();
            CmbTheme.SelectedIndex = Math.Max(0, Themes.All.FindIndex(t => t.Key == _s.Theme));
            CmbMode.ItemsSource = Modes;
            CmbPttMethod.ItemsSource = new[] { "CAT via rigctld", "Porta seriale – RTS", "Porta seriale – DTR", "VOX (nessun comando)", "TCI (WebSocket)" };
            CmbPttCom.ItemsSource = new[] { "(stessa porta del CAT)" }.Concat(Hamlib.SerialPorts()).ToList();
            CmbCwMethod.ItemsSource = new[] { "CAT (keyer della radio, send_morse)", "Tasto su linea DTR", "Tasto su linea RTS" };
            CmbCwCom.ItemsSource = new[] { "(stessa porta del PTT)" }.Concat(Hamlib.SerialPorts()).ToList();
            CmbFilter.ItemsSource = Dsp.FilterNames;
            CmbComp.ItemsSource = new[] { "Compressore off" }.Concat(Enumerable.Range(3, 11).Select(d => $"Compressore {d} dB")).ToList();
            CmbCwWidth.ItemsSource = CwWidths.Select(w => $"{w} Hz").ToList();
            CmbCwRxSpeed.ItemsSource = new[] { "Auto" }.Concat(Enumerable.Range(1, 12).Select(i => $"{i * 5} WPM")).ToList();
            CmbCwTxSpeed.ItemsSource = Enumerable.Range(5, 56).Select(w => $"{w} WPM").ToList();
            CmbCwFilter.ItemsSource = CwFilters.Select(w => $"{w} Hz").ToList();
        }

        private void BuildFKeys()
        {
            for (int i = 0; i < 8; i++)
            {
                int idx = i;
                var name = new TextBlock { FontSize = 11, Opacity = 0.85, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                var b = new Button
                {
                    Height = 58,
                    Margin = new Thickness(0, 0, 6, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Content = new StackPanel
                    {
                        Spacing = 2,
                        Children = { new TextBlock { Text = $"F{i + 1}", FontSize = 16, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center }, name }
                    }
                };
                b.Click += (_, _) => FKey(idx);
                _fButtons[i] = b;
                _fNames[i] = name;
                PanelFKeys.Children.Add(b);
            }
        }

        private void LoadControls()
        {
            _loading = true;
            SldRx.Value = _s.RxVolume; SldRxCompact.Value = _s.RxVolume;
            SldTx.Value = _s.TxVolume;
            TxtRxBoost.Text = _s.RxBoost.ToString(CultureInfo.InvariantCulture);
            TxtRxBoostCompact.Text = TxtRxBoost.Text;
            TxtTxBoost.Text = _s.TxBoost.ToString(CultureInfo.InvariantCulture);
            ChkSpace.IsChecked = _s.PttWithSpace;
            CmbPttMethod.SelectedIndex = (int)_s.PttMethod;
            CmbPttCom.SelectedItem = string.IsNullOrEmpty(_s.PttCom) ? "(stessa porta del CAT)" : _s.PttCom;
            if (CmbPttCom.SelectedIndex < 0) CmbPttCom.SelectedIndex = 0;
            TxtTciHost.Text = _s.TciHost;
            TxtTciPort.Text = _s.TciPort.ToString(CultureInfo.InvariantCulture);
            TxtPttDelay.Text = _s.PttDelayMs.ToString(CultureInfo.InvariantCulture);
            CmbCwMethod.SelectedIndex = (int)_s.CwMethod;
            CmbCwCom.SelectedItem = string.IsNullOrEmpty(_s.CwCom) ? "(stessa porta del PTT)" : _s.CwCom;
            if (CmbCwCom.SelectedIndex < 0) CmbCwCom.SelectedIndex = 0;
            SldKeyerVol.Value = _s.KeyerVolume;
            CmbFilter.SelectedIndex = Math.Clamp(_s.KeyerFilter, 0, 3);
            CmbComp.SelectedIndex = _s.KeyerCompDb <= 0 ? 0 : Math.Clamp(_s.KeyerCompDb - 2, 1, 11);
            TxtPitch.Text = _s.CwPitch.ToString(CultureInfo.InvariantCulture);
            CmbCwWidth.SelectedIndex = Math.Max(0, Array.IndexOf(CwWidths, _s.CwDecodeWidth));
            CmbCwRxSpeed.SelectedIndex = _s.CwRxWpm <= 0 ? 0 : Math.Clamp(_s.CwRxWpm / 5, 1, 12);
            CmbCwTxSpeed.SelectedIndex = Math.Clamp(_s.CwSpeedWpm - 5, 0, 55);
            SldThreshold.Value = _s.CwThreshold;
            ChkWideFilter.IsChecked = _s.CwWideFilter;
            CmbCwFilter.SelectedIndex = Math.Max(0, Array.IndexOf(CwFilters, _s.CwFilterHz));
            SldWfIntensity.Value = _s.WfIntensity;
            Wf.Intensity = _s.WfIntensity;
            TxtMyCall.Text = _s.Callsign;
            TxtCwCall.Text = _s.CwCall;
            TxtCwNr.Text = _s.CwNr.ToString("D3");
            ChkAutoUnmute.IsChecked = _s.AutoUnmute;
            Wf.PitchHz = _s.CwPitch;
            FillAudioDevices();
            UpdateVolumeLabels();
            UpdatePttMethodPanels();
            RefreshFKeyNames();
            _loading = false;
        }

        private void FillAudioDevices()
        {
            var ins = _node.Audio.Inputs();
            var outs = _node.Audio.Outputs();
            CmbIn.ItemsSource = ins;
            CmbOut.ItemsSource = outs;
            CmbOutCompact.ItemsSource = outs;
            CmbIn.SelectedItem = ins.Contains(_s.InputDevice) ? _s.InputDevice : null;
            CmbOut.SelectedItem = outs.Contains(_s.OutputDevice) ? _s.OutputDevice : null;
            if (CmbIn.SelectedIndex < 0 && ins.Count > 0) CmbIn.SelectedIndex = 0;
            if (CmbOut.SelectedIndex < 0 && outs.Count > 0) CmbOut.SelectedIndex = 0;
            CmbOutCompact.SelectedIndex = CmbOut.SelectedIndex;
            _s.InputDevice = CmbIn.SelectedItem as string ?? "";
            _s.OutputDevice = CmbOut.SelectedItem as string ?? "";
        }

        private void HookEvents()
        {
            CmbDevIn.SelectionChanged += (_, _) => ServerDeviceChosen();
            CmbDevOut.SelectionChanged += (_, _) => ServerDeviceChosen();
            _node.ServerDevicesChanged += () => Dispatcher.UIThread.Post(() =>
            {
                _loading = true;
                FillAudioDevices();
                _loading = false;
            });
            CmbTheme.SelectionChanged += (_, _) =>
            {
                if (_loading || CmbTheme.SelectedIndex < 0) return;
                _s.Theme = Themes.All[CmbTheme.SelectedIndex].Key;
                _s.Save();
                Themes.Apply(_s.Theme);
                InvalidateVisual();
            };
            BtnSettings.Click += async (_, _) => await OpenSettings();
            BtnSwap.Click += async (_, _) =>
            {
                if (await Dialogs.YesNo(this, "Vuoi chiedere al peer di invertire i ruoli? Il server diventerà client e viceversa."))
                    _node.RequestSwap();
            };
            BtnHelp.Click += async (_, _) => await new HelpWindow().ShowDialog(this);

            BtnSetQrg.Click += (_, _) => SendQrg();
            TxtNewQrg.KeyDown += (_, e) => { if (e.Key == Key.Enter) SendQrg(); };

            // VFO B e split
            BtnAB.Click += (_, _) => _node.VfoOp("XCHG");
            BtnAeqB.Click += (_, _) => _node.VfoOp("CPY");
            BtnSplit.Click += (_, _) =>
            {
                bool on = BtnSplit.IsChecked == true;
                if (on && _tel.FreqHz > 0) _node.SetVfoB(_tel.FreqHz + (long)Math.Round(_offsetKhz * 1000));
                _node.SetSplit(on);
                UpdateSplitNote(on);
            };
            BtnOffMinus.Click += (_, _) => SetOffset(_offsetKhz - OffsetStep);
            BtnOffPlus.Click += (_, _) => SetOffset(_offsetKhz + OffsetStep);
            SldOffset.ValueChanged += (_, e) => { if (!_loading && Math.Abs(e.NewValue - _offsetKhz) >= OffsetStep / 2) SetOffset(e.NewValue); };
            SldOffset.PointerWheelChanged += (_, e) => { SetOffset(_offsetKhz + Math.Sign(e.Delta.Y) * OffsetStep); e.Handled = true; };
            TxtOffset.KeyDown += (_, e) => { if (e.Key == Key.Enter) ParseOffsetBox(); };
            TxtOffset.LostFocus += (_, _) => ParseOffsetBox();
            TxtOffset.PointerWheelChanged += (_, e) => { SetOffset(_offsetKhz + Math.Sign(e.Delta.Y) * OffsetStep); e.Handled = true; };

            // potenza
            SldPower.ValueChanged += (_, e) =>
            {
                if (_loading) return;
                _powerPending = e.NewValue;
                _powerPendingAt = DateTime.UtcNow;
                TxtPower.Text = FormatPower(e.NewValue);
            };
            SldPower.PointerWheelChanged += (_, e) =>
            {
                double step = _tel.PowerInWatts ? 5 : 5;
                SldPower.Value = Math.Clamp(SldPower.Value + Math.Sign(e.Delta.Y) * step, SldPower.Minimum, SldPower.Maximum);
                e.Handled = true;
            };

            // audio
            SldRx.ValueChanged += (_, _) => { if (!_loading) { _loading = true; SldRxCompact.Value = SldRx.Value; _loading = false; SaveVolumes(); } };
            SldRxCompact.ValueChanged += (_, _) => { if (!_loading) { _loading = true; SldRx.Value = SldRxCompact.Value; _loading = false; SaveVolumes(); } };
            SldTx.ValueChanged += (_, _) => { if (!_loading) SaveVolumes(); };
            foreach (var tb in new[] { TxtRxBoost, TxtTxBoost, TxtRxBoostCompact })
            {
                tb.LostFocus += (_, _) => SaveBoosts(tb);
                tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) SaveBoosts(tb); };
            }
            CmbIn.SelectionChanged += (_, _) => AudioDeviceChanged(CmbIn);
            CmbOut.SelectionChanged += (_, _) => AudioDeviceChanged(CmbOut);
            CmbOutCompact.SelectionChanged += (_, _) => AudioDeviceChanged(CmbOutCompact);

            // PTT
            BtnPtt.PointerPressed += (_, e) => { e.Pointer.Capture(BtnPtt); SetManualPtt(true); e.Handled = true; };
            BtnPtt.PointerReleased += (_, e) => { e.Pointer.Capture(null); SetManualPtt(false); e.Handled = true; };
            BtnPtt.PointerCaptureLost += (_, _) => { if (_manualPtt) SetManualPtt(false); };
            ChkSpace.IsCheckedChanged += (_, _) => { if (!_loading) { _s.PttWithSpace = ChkSpace.IsChecked == true; _s.Save(); } };
            CmbPttMethod.SelectionChanged += (_, _) => PttMethodChanged();
            CmbPttCom.SelectionChanged += (_, _) => PttMethodChanged();
            CmbCwMethod.SelectionChanged += (_, _) => PttMethodChanged();
            CmbCwCom.SelectionChanged += (_, _) => PttMethodChanged();
            TxtTciHost.LostFocus += (_, _) => PttMethodChanged();
            TxtTciPort.LostFocus += (_, _) => PttMethodChanged();
            TxtPttDelay.LostFocus += (_, _) =>
            {
                if (int.TryParse(TxtPttDelay.Text, out int d)) { _s.PttDelayMs = Math.Clamp(d, 0, 2000); _s.Save(); }
                TxtPttDelay.Text = _s.PttDelayMs.ToString(CultureInfo.InvariantCulture);
            };

            // schede audio del server
            BtnDevIn.Click += (_, _) => ToggleDeviceMute(true);
            BtnDevOut.Click += (_, _) => ToggleDeviceMute(false);
            SldDevIn.ValueChanged += (_, e) => { if (!_loading) { _devVolPendingIn = (float)(e.NewValue / 100); _devVolAt = DateTime.UtcNow; _devUserAt = DateTime.UtcNow; } };
            SldDevOut.ValueChanged += (_, e) => { if (!_loading) { _devVolPendingOut = (float)(e.NewValue / 100); _devVolAt = DateTime.UtcNow; _devUserAt = DateTime.UtcNow; } };
            ChkAutoUnmute.IsCheckedChanged += (_, _) =>
            {
                if (_loading) return;
                bool on = ChkAutoUnmute.IsChecked == true;
                _devUserAt = DateTime.UtcNow;
                if (_s.Role == NodeRole.Server) { _s.AutoUnmute = on; _s.Save(); _node.RefreshDeviceState(); }
                else _node.SetRemoteAutoUnmute(on);
            };

            // keyer
            BtnMacros.Click += async (_, _) => await OpenMacros();
            SldKeyerVol.ValueChanged += (_, _) => { if (!_loading) { _s.KeyerVolume = (int)SldKeyerVol.Value; _s.Save(); _clips.Clear(); } };
            CmbFilter.SelectionChanged += (_, _) => { if (!_loading && CmbFilter.SelectedIndex >= 0) { _s.KeyerFilter = CmbFilter.SelectedIndex; _s.Save(); _clips.Clear(); } };
            CmbComp.SelectionChanged += (_, _) => { if (!_loading && CmbComp.SelectedIndex >= 0) { _s.KeyerCompDb = CmbComp.SelectedIndex == 0 ? 0 : CmbComp.SelectedIndex + 2; _s.Save(); _clips.Clear(); } };
            CmbCwTxSpeed.SelectionChanged += (_, _) =>
            {
                if (_loading || CmbCwTxSpeed.SelectedIndex < 0) return;
                _s.CwSpeedWpm = CmbCwTxSpeed.SelectedIndex + 5;
                _s.Save();
                _node.SetKeySpeed(_s.CwSpeedWpm);
            };
            TxtCwCall.LostFocus += (_, _) => { _s.CwCall = (TxtCwCall.Text ?? "").Trim().ToUpperInvariant(); TxtCwCall.Text = _s.CwCall; _s.Save(); };
            TxtCwNr.LostFocus += (_, _) =>
            {
                if (int.TryParse(TxtCwNr.Text, out int n)) _s.CwNr = Math.Clamp(n, 0, 99999);
                TxtCwNr.Text = _s.CwNr.ToString("D3");
                _s.Save();
            };

            // CW
            TxtPitch.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyPitchFromBox(); };
            TxtPitch.LostFocus += (_, _) => ApplyPitchFromBox();
            BtnReadPitch.Click += (_, _) => { if (_tel.CwPitch >= 200) SetPitch(_tel.CwPitch, false); };
            CmbCwWidth.SelectionChanged += (_, _) =>
            {
                if (_loading || CmbCwWidth.SelectedIndex < 0) return;
                _s.CwDecodeWidth = CwWidths[CmbCwWidth.SelectedIndex];
                _s.Save();
                lock (_decLock) _decoder.Configure(_s.CwPitch, _s.CwDecodeWidth);
            };
            CmbCwRxSpeed.SelectionChanged += (_, _) =>
            {
                if (_loading || CmbCwRxSpeed.SelectedIndex < 0) return;
                _s.CwRxWpm = CmbCwRxSpeed.SelectedIndex * 5;
                _s.Save();
                _decoder.FixedWpm = _s.CwRxWpm;
            };
            SldThreshold.ValueChanged += (_, _) =>
            {
                if (_loading) return;
                _s.CwThreshold = (int)SldThreshold.Value;
                _decoder.ThresholdFactor = ThresholdOf(_s.CwThreshold);
                _s.Save();
            };
            Wf.FrequencyClicked += OnWaterfallClick;
            SldWfIntensity.ValueChanged += (_, e) => { if (!_loading) { _s.WfIntensity = (int)e.NewValue; Wf.Intensity = e.NewValue; _s.Save(); } };
            ChkWideFilter.IsCheckedChanged += (_, _) => { if (_loading) return; _s.CwWideFilter = ChkWideFilter.IsChecked == true; _s.Save(); ApplyCwFilter(); };
            CmbCwFilter.SelectionChanged += (_, _) =>
            {
                if (_loading || CmbCwFilter.SelectedIndex < 0) return;
                _s.CwFilterHz = CwFilters[CmbCwFilter.SelectedIndex];
                _s.Save();
                ApplyCwFilter();
            };

            // collegamento
            BtnCopyFallback.Click += async (_, _) => await Dialogs.CopyText(this, TxtFallbackCode.Text ?? "");
            BtnFallbackConnect.Click += async (_, _) =>
            {
                if (_node.UseFallbackCode(TxtFallbackInput.Text ?? "")) { TxtNotice.Text = "Codice accettato, mi collego…"; TxtNotice.IsVisible = true; }
                else await Dialogs.Info(this, "Codice non valido: controlla di averlo scritto bene.");
            };
        }

        // ================================================================= ruoli

        private bool IsClient => _s.Role == NodeRole.Client;

        private void ApplyRoleTexts()
        {
            bool server = !IsClient;
            TxtRoleBadge.Text = server ? "SERVER" : "CLIENT";
            TxtRoleLine.Text = server
                ? $"{_s.Callsign} · Server: la radio è collegata a questo PC · audio 8 kHz cifrato AES-256"
                : $"Client collegato al server di {(_s.PeerCallsign.Length > 0 ? _s.PeerCallsign : "—")} · audio 8 kHz cifrato AES-256";
            TxtRadioTitle.Text = server
                ? "RADIO · IN LOCALE · " + (_s.UseExternalRigctld ? $"rigctld {_s.ExternalRigctldHost}:{_s.ExternalRigctldPort}" : "HAMLIB")
                : "RADIO REMOTA" + (_tel.RigName.Length > 0 ? " · " + _tel.RigName.ToUpperInvariant() : "");
            TxtPeerTitle.Text = server ? "CLIENT (OPERATORE REMOTO)" : "SERVER (STAZIONE RADIO)";
            TxtClientIpLabel.Text = server ? "IP client" : "Mio IP (client)";
            TxtNetLocalLabel.Text = server ? "Rete server" : "Rete client";
            TxtNetPeerLabel.Text = server ? "Rete client" : "Rete server";
            TxtPeerLevelLabel.Text = server ? "Livello audio ricevuto dal client" : "Livello audio ricevuto dal server";
            TxtInLabel.Text = server ? "Ingresso: audio RX della radio (va al client)" : "Microfono (verso la radio)";
            TxtOutLabel.Text = server ? "Uscita: verso l'ingresso TX della radio" : "Altoparlante (dalla radio)";
            TxtRxTitle.Text = server ? "Audio dal client · verso la radio" : "RX · audio ricevuto";
            TxtTxTitle.Text = server ? "Audio della radio · verso il client" : "TX · audio inviato";
            PanelPttMethod.IsVisible = server;
            PanelPttInfo.IsVisible = !server;
            PanelKeyer.IsVisible = !server;
            PanelFallbackServer.IsVisible = false;
            PanelFallbackClient.IsVisible = false;
            TxtRadioModel.Text = "Radio: " + (server ? (string.IsNullOrEmpty(_s.RigModelName) ? "non configurata" : $"{_s.RigModelName} su {_s.RigCom}") : (_tel.RigName.Length > 0 ? _tel.RigName : "del server"));
            TxtMyCall.Text = _s.Callsign;
            UpdateModeLayout();
        }

        private async void OnRoleChanged()
        {
            _node.Audio.Keyer.Stop();
            ApplyRoleTexts();
            _loading = true;
            FillAudioDevices();
            _loading = false;
            _node.RestartAudio();
            _tel = new RigTelemetry();
            RefreshRadio();
            if (IsClient) _hotkeys.Start(); else _hotkeys.Dispose();
            await Dialogs.Info(this, IsClient ? "Ruoli invertiti: questo PC ora è il CLIENT." : "Ruoli invertiti: questo PC ora è il SERVER.");
        }

        private async void AskSwap(Action<bool> answer)
        {
            bool ok = await Dialogs.YesNo(this, $"{(_s.PeerCallsign.Length > 0 ? _s.PeerCallsign : "Il peer")} chiede di invertire i ruoli. Accetti?");
            answer(ok);
        }

        private async System.Threading.Tasks.Task OpenSettings()
        {
            var w = new SettingsWindow(_s, _node);
            await w.ShowDialog(this);
            if (w.Saved || w.PairingReset)
            {
                ApplyRoleTexts();
                _loading = true;
                FillAudioDevices();
                _loading = false;
                _node.ApplyAudioSettings();
                if (w.RigChanged && _s.Role == NodeRole.Server) _node.Rig.Start();
                if (w.DigitalChanged) _node.StartDigital();
                if (w.SerialInputChanged) StartSerialInput();
                if (w.PairingReset) ShowPairing();
            }
        }

        // ================================================================= modo CW / fonia

        /// <summary>In CW apre il filtro della radio alla larghezza scelta, così la cascata 0-3000 Hz mostra i segnali.</summary>
        private void ApplyCwFilter()
        {
            if (!IsClient || !RigTelemetry.IsCw(_tel.Mode) || !_s.CwWideFilter) return;
            _node.SetPassband(_tel.Mode, _s.CwFilterHz);
        }

        private void UpdateModeLayout()
        {
            bool cw = IsClient && RigTelemetry.IsCw(_tel.Mode);
            _cwActive = cw;
            if (cw && !_cwLayout) ApplyCwFilter();     // appena si entra in CW
            if (cw == _cwLayout && PanelCw.IsVisible == cw) { RefreshFKeyNames(); return; }
            _cwLayout = cw;
            PanelCw.IsVisible = cw;
            PanelAudio.IsVisible = !cw;
            PanelRxCompact.IsVisible = cw;
            RowWav.IsVisible = !cw;
            RowCw.IsVisible = cw;
            TxtKeyerTitle.Text = cw ? "CW KEYER · 8 macro di testo" : "VOICE KEYER · 8 messaggi WAV";
            TxtKeyerDest.Text = cw ? "In SSB gli stessi tasti tornano messaggi WAV" : "Destinazione: radio remota, tramite il server (come in locale)";
            BtnMacros.Content = cw ? "Macro…" : "File WAV…";
            BtnMacros.Classes.Set("accent", cw);
            if (cw) { _node.Audio.Keyer.Stop(); }
            RefreshFKeyNames();
            RefreshKeyerStatus();
            UpdateSplitNote(BtnSplit.IsChecked == true);
            SetOffsetDisplay(_offsetKhz);
        }

        private void RefreshFKeyNames()
        {
            bool cw = _cwLayout;
            for (int i = 0; i < 8; i++)
            {
                string name = cw
                    ? _s.CwMacros[i].Name
                    : (string.IsNullOrEmpty(_s.WavMemories[i]) ? "—" : Path.GetFileNameWithoutExtension(_s.WavMemories[i]));
                _fNames[i].Text = string.IsNullOrWhiteSpace(name) ? "—" : name;
            }
        }

        // ================================================================= tasti F e keyer

        private void FKey(int i)
        {
            if (!IsClient || i < 0 || i > 7) return;
            if (!_node.Connected) { TxtKeyerStatus.Text = "Non collegato al server"; return; }
            if (_cwLayout)
            {
                string text = _s.ExpandMacro(_s.CwMacros[i].Text);
                if (text.Trim().Length == 0) { TxtKeyerStatus.Text = $"F{i + 1}: macro vuota (usa il pulsante Macro…)"; return; }
                _node.SendMorse(text);
                TxtKeyerStatus.Text = $"TX CW  F{i + 1}  «{text}»  · Esc interrompe";
                return;
            }
            var clip = GetClip(i, out string? err);
            if (clip == null) { TxtKeyerStatus.Text = err ?? $"F{i + 1}: nessun file assegnato (usa il pulsante File WAV…)"; return; }
            _node.Audio.Keyer.Enqueue(i, clip);
        }

        private short[]? GetClip(int i, out string? error)
        {
            error = null;
            string rel = _s.WavMemories[i];
            if (string.IsNullOrEmpty(rel)) return null;
            string path = Path.IsPathRooted(rel) ? rel : Path.Combine(AppSettings.WavFolder, rel);
            string key = $"{path}|{_s.KeyerVolume}|{_s.KeyerFilter}|{_s.KeyerCompDb}";
            if (_clips.TryGetValue(i, out var c) && c.key == key) return c.clip;
            try
            {
                var clip = Dsp.PrepareClip(path, _s.KeyerVolume, _s.KeyerFilter, _s.KeyerCompDb);
                _clips[i] = (key, clip);
                return clip;
            }
            catch (Exception ex)
            {
                error = $"F{i + 1}: {ex.Message}";
                return null;
            }
        }

        private void StopKeyer()
        {
            _node.Audio.Keyer.Stop();
            if (_cwLayout) _node.StopMorse();
            TxtKeyerStatus.Text = "Interrotto";
        }

        private void RefreshKeyerStatus()
        {
            var k = _node.Audio.Keyer;
            for (int i = 0; i < 8; i++) _fButtons[i].Classes.Set("accent", k.Active && k.CurrentIndex == i);
            if (_cwLayout)
            {
                if (!TxtKeyerStatus.Text!.StartsWith("TX CW"))
                    TxtKeyerStatus.Text = "Pronto · F1..F8 mandano il testo della macro al server, che lo trasmette in CW (" + (_tel.PttInfo.Contains("CW via CAT") ? "keyer della radio via CAT" : "tasto su porta seriale") + ") · Ctrl+Alt+F1..F8 da altri programmi · Esc interrompe";
                return;
            }
            if (k.Active && k.CurrentIndex >= 0)
                TxtKeyerStatus.Text = $"TX  F{k.CurrentIndex + 1}  {_fNames[k.CurrentIndex].Text}  · in coda: {k.Queued} · Esc interrompe";
            else if (k.Active)
                TxtKeyerStatus.Text = $"PTT attivo, attendo {k.DelayMs} ms…";
            else
                TxtKeyerStatus.Text = "Pronto · F1..F8 (Ctrl+Alt+F1..F8 da altri programmi), Esc interrompe · il microfono è escluso mentre parte un messaggio";
        }

        private async System.Threading.Tasks.Task OpenMacros()
        {
            if (_cwLayout)
            {
                var w = new MacroCwWindow(_s, _node);
                await w.ShowDialog(this);
            }
            else
            {
                var w = new WavMemoriesWindow(_s, _node);
                await w.ShowDialog(this);
                _clips.Clear();
            }
            RefreshFKeyNames();
        }

        // ================================================================= tastiera

        private bool FocusInTextBox() => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;

        private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
        {
            if (FocusInTextBox() && e.Key != Key.Escape) return;
            if (e.Key == Key.Space && _s.PttWithSpace)
            {
                if (!_manualPtt) SetManualPtt(true);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape) { StopKeyer(); e.Handled = true; return; }
            if (e.Key >= Key.F1 && e.Key <= Key.F8 && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0)
            {
                FKey(e.Key - Key.F1);
                e.Handled = true;
            }
        }

        private void OnKeyUpTunnel(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && _s.PttWithSpace && !FocusInTextBox())
            {
                if (_manualPtt) SetManualPtt(false);
                e.Handled = true;
            }
        }

        // ================================================================= PTT

        /// <summary>Client: pedale PTT e tasto CW meccanico (o porta virtuale di un altro programma) sulla seriale.</summary>
        private void StartSerialInput()
        {
            _serialIn?.Dispose();
            _serialIn = null;
            if (_s.Role != NodeRole.Client || string.IsNullOrEmpty(_s.InputCom)) return;
            try
            {
                var si = new IpTrxCtrl.Platform.SerialInput(_s.InputCom, _s.InputPttPin, _s.InputCwPin);
                si.PttChanged += on => Dispatcher.UIThread.Post(() => SetManualPtt(on));
                si.CwKeyChanged += (down, ms) => _node.SendCwKey(down, ms);
                _serialIn = si;
                SessionLog.Write($"INGRESSO seriale {_s.InputCom}: PTT su {_s.InputPttPin}, tasto CW su {_s.InputCwPin}");
            }
            catch (Exception ex)
            {
                SessionLog.Write($"INGRESSO seriale {_s.InputCom} non apribile: {ex.Message}");
                TxtNotice.Text = $"Porta seriale {_s.InputCom} (pedale / tasto) non apribile: {ex.Message}";
                TxtNotice.IsVisible = true;
            }
        }

        private void SetManualPtt(bool on)
        {
            _manualPtt = on;
            if (!on && _node.Audio.Keyer.Active) return;   // il keyer gestisce il suo PTT
            _node.SetPttLocal(on);
            ShowPttState(on);
        }

        private void ShowPttState(bool on)
        {
            if (on)
            {
                BtnPtt.Background = this.FindResource("B.Tx") as IBrush;
                BtnPtt.BorderBrush = this.FindResource("B.Tx") as IBrush;
                TxtPtt.Foreground = Brushes.White;
                TxtPtt.Text = _node.Audio.Keyer.Active ? "TX · MESSAGGIO" : "IN TRASMISSIONE";
            }
            else
            {
                BtnPtt.Background = this.FindResource("B.Inset") as IBrush;
                BtnPtt.BorderBrush = this.FindResource("B.Border") as IBrush;
                TxtPtt.Foreground = this.FindResource("B.Text") as IBrush;
                TxtPtt.Text = "PTT (PREMI E TIENI)";
            }
        }

        private void PttMethodChanged()
        {
            if (_loading) return;
            _s.PttMethod = (PttMethod)Math.Max(0, CmbPttMethod.SelectedIndex);
            string com = CmbPttCom.SelectedItem as string ?? "";
            _s.PttCom = com.StartsWith("(") ? "" : com;
            _s.TciHost = string.IsNullOrWhiteSpace(TxtTciHost.Text) ? "127.0.0.1" : TxtTciHost.Text.Trim();
            if (int.TryParse(TxtTciPort.Text, out int p) && p > 0 && p < 65536) _s.TciPort = p;
            _s.CwMethod = (CwMethod)Math.Max(0, CmbCwMethod.SelectedIndex);
            string cwCom = CmbCwCom.SelectedItem as string ?? "";
            _s.CwCom = cwCom.StartsWith("(") ? "" : cwCom;
            _s.Save();
            UpdatePttMethodPanels();
            if (_s.Role == NodeRole.Server) _node.Rig.Start();
        }

        private void UpdatePttMethodPanels()
        {
            PanelPttCom.IsVisible = _s.PttMethod == PttMethod.Rts || _s.PttMethod == PttMethod.Dtr;
            PanelTci.IsVisible = _s.PttMethod == PttMethod.Tci;
            PanelCwCom.IsVisible = _s.CwMethod != CwMethod.Cat;
            var plan = KeyingPlan.Resolve(_s);
            TxtKeyingNote.Text = plan.Note;
            TxtKeyingNote.IsVisible = plan.Note.Length > 0;
        }

        // ================================================================= frequenza, split, potenza

        private void SendQrg()
        {
            string txt = (TxtNewQrg.Text ?? "").Trim().Replace(".", "").Replace(",", "").Replace(" ", "");
            long hz = 0;
            if (txt.Length > 0 && !long.TryParse(txt, out hz))
            {
                _ = Dialogs.Info(this, "Scrivi la frequenza in Hz, per esempio 14025000.");
                return;
            }
            _node.SetFreqMode(hz, CmbMode.SelectedItem as string ?? "");
        }

        private double OffsetStep => _cwLayout || RigTelemetry.IsCw(_tel.Mode) ? 0.1 : 1.0;

        private void SetOffset(double khz)
        {
            double step = OffsetStep;
            khz = Math.Clamp(Math.Round(khz / step) * step, -50, 50);
            _offsetKhz = khz;
            _offsetEditedAt = DateTime.UtcNow;
            SetOffsetDisplay(khz);
            if (_tel.FreqHz > 0) _node.SetVfoB(_tel.FreqHz + (long)Math.Round(khz * 1000));
        }

        private void SetOffsetDisplay(double khz)
        {
            bool old = _loading;
            _loading = true;
            SldOffset.Value = khz;
            if (!TxtOffset.IsFocused)
                TxtOffset.Text = (khz >= 0 ? "+" : "−") + Math.Abs(khz).ToString(OffsetStep < 1 ? "0.0" : "0", CultureInfo.InvariantCulture).Replace('.', ',') + " kHz";
            _loading = old;
        }

        private void ParseOffsetBox()
        {
            string t = (TxtOffset.Text ?? "").Replace("kHz", "").Replace("−", "-").Replace(",", ".").Replace("+", "").Trim();
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) SetOffset(v);
            else SetOffsetDisplay(_offsetKhz);
        }

        private void UpdateSplitNote(bool split)
        {
            string step = OffsetStep < 1 ? "0,1 kHz in CW" : "1 kHz in fonia";
            TxtSplitNote.Text = (split ? "SPLIT attivo · RX su VFO A, TX su VFO B" : "TX e RX su VFO A")
                                + $" · passo {step} · campo da −50 a +50 kHz · rotella del mouse = un passo";
            TxtVfoALabel.Text = split ? "VFO A · RX" : "VFO A";
            TxtVfoBLabel.Text = split ? "VFO B · TX" : "VFO B";
        }

        private string FormatPower(double v) =>
            _tel.PowerInWatts ? $"{v:F0} W" : $"{v:F0} %";

        // ================================================================= aggiornamento indicatori

        private void OnUiTick()
        {
            var st = _node.Stats;
            PbRx.Value = st.RxPeak * 100;
            PbRxCompact.Value = st.RxPeak * 100;
            PbPeerLevel.Value = _s.Role == NodeRole.Client ? st.RxPeak * 100 : st.RxPeak * 100;
            PbTx.Value = (IsClient && !_node.LocalPtt) ? 0 : st.TxPeak * 100;

            // LED di trasmissione lampeggiante
            bool tx = IsClient ? (_tel.Transmitting || st.PeerTx) : (_tel.Transmitting || _node.LocalPtt || st.PeerTx);
            _blinkTick++;
            bool on = tx && (_blinkTick / 4) % 2 == 0;
            LedTx.Fill = this.FindResource(tx ? (on ? "B.Tx" : "B.TxDim") : "B.Track") as IBrush;
            TxtLed.Text = tx ? "TX" : "RX";
            TxtLed.Foreground = this.FindResource(tx ? "B.Tx" : "B.Muted") as IBrush;

            // potenza: invio al rilascio (350 ms di quiete)
            if (_powerPending is double pv && DateTime.UtcNow - _powerPendingAt > TimeSpan.FromMilliseconds(350))
            {
                _powerPending = null;
                _powerSentAt = DateTime.UtcNow;
                _node.SetPower((float)(_tel.PowerInWatts ? pv : pv / 100.0), _tel.PowerInWatts);
            }
            // volume schede audio del server
            if ((_devVolPendingIn != null || _devVolPendingOut != null) && DateTime.UtcNow - _devVolAt > TimeSpan.FromMilliseconds(350))
            {
                if (_devVolPendingIn is float vi) SetDeviceVolume(true, vi);
                if (_devVolPendingOut is float vo) SetDeviceVolume(false, vo);
                _devVolPendingIn = null; _devVolPendingOut = null;
            }

            if (_cwActive)
            {
                Wf.Flush();
                string txt;
                lock (_decLock) txt = _decoded.ToString();
                if (txt.Length > 90) txt = txt[^90..];
                TxtDecoded.Text = $"{_s.CwPitch} » " + txt + "▌";
                foreach (var w in _decodeWindows) w.Tick();
            }
        }

        private void RefreshStats()
        {
            var st = _node.Stats;
            TxtConnChip.Text = st.Connected ? "Collegato" : st.Status;
            DotConn.Fill = this.FindResource(st.Connected ? "B.Bar" : "B.Track") as IBrush;
            TxtState.Text = st.Status;
            TxtLatency.Text = double.IsNaN(st.LatencyMs) ? "—" : $"{st.LatencyMs:F0} ms";
            TxtJitter.Text = st.Connected ? $"{st.JitterMs:F1} ms" : "—";
            TxtLoss.Text = st.Connected ? $"{st.LossPercent:F1} %" : "—";
            TxtBuffer.Text = $"{st.BufferMs} ms";
            TxtMqtt.Text = st.MqttOk < 0 ? "—" : st.MqttOk == 0 ? "nessun broker" : $"{st.MqttOk} broker ok";
            TxtPeerCall.Text = string.IsNullOrEmpty(_s.PeerCallsign) ? "—" : _s.PeerCallsign;
            TxtPeerSince.Text = st.Connected ? $"{st.ConnectedSince:HH:mm:ss} ({DateTime.Now - st.ConnectedSince:hh\\:mm\\:ss})" : "—";
            TxtPeerLatency.Text = TxtLatency.Text;
            TxtServerIp.Text = string.IsNullOrEmpty(st.ServerEndpoint) ? "—" : st.ServerEndpoint;
            TxtClientIp.Text = string.IsNullOrEmpty(st.ClientEndpoint) ? (IsClient ? "— · LAN " + Node.LanIp() : "—") : st.ClientEndpoint;
            TxtNetLocal.Text = st.LocalNet.Length > 0 ? st.LocalNet : "verifico IPv4 / IPv6…";
            TxtNetPeer.Text = st.Connected && st.PeerNet.Length > 0 ? st.PeerNet : "—";
            if (st.Connected && st.AudioPath.Length > 0) TxtNetPeer.Text += " · audio " + st.AudioPath;
            if (st.Connected && IsClient) PanelFallbackClient.IsVisible = false;
            if (IsClient && _s.PeerCallsign.Length > 0 && !TxtRoleLine.Text!.Contains(_s.PeerCallsign)) ApplyRoleTexts();
        }

        private static string FormatQrg(long hz) => $"{hz / 1_000_000}.{(hz / 1000) % 1000:D3}.{hz % 1000:D3}";

        private void RefreshRadio()
        {
            var t = _tel;
            bool server = !IsClient;
            _loading = true;
            try
            {
                TxtCatStatus.Text = t.CatOk ? "CAT ok · rigctld" : (server ? _node.Rig.Status : (_node.Stats.Connected ? (t.Status.Length > 0 ? t.Status : "CAT del server non disponibile") : "—"));
                TxtCatStatus.Foreground = this.FindResource(t.CatOk ? "B.Ok" : "B.Muted") as IBrush;
                TxtQrg.Text = t.FreqHz > 0 ? FormatQrg(t.FreqHz) : "--.---.---";
                TxtMode.Text = "MHz · " + (string.IsNullOrEmpty(t.Mode) ? "—" : t.Mode);
                TxtQrgB.Text = t.FreqBHz > 0 ? FormatQrg(t.FreqBHz) : "--.---.---";
                TxtModeB.Text = "MHz" + (string.IsNullOrEmpty(t.Mode) ? "" : " · " + t.Mode);
                BtnSplit.IsChecked = t.Split;
                UpdateSplitNote(t.Split);
                if (t.FreqBHz > 0 && t.FreqHz > 0 && DateTime.UtcNow - _offsetEditedAt > TimeSpan.FromSeconds(3))
                {
                    _offsetKhz = Math.Clamp((t.FreqBHz - t.FreqHz) / 1000.0, -50, 50);
                    SetOffsetDisplay(_offsetKhz);
                }

                PbS.Value = RigTelemetry.SMeterPercent(t.StrengthDb);
                TxtS.Text = RigTelemetry.SMeterText(t.StrengthDb);
                if (!float.IsNaN(t.PowerW)) { PbRf.Value = Math.Clamp(t.PowerW, 0, 100); TxtRf.Text = $"{t.PowerW:F0} W"; }
                else if (!float.IsNaN(t.PowerRel)) { PbRf.Value = t.PowerRel * 100; TxtRf.Text = $"{t.PowerRel * 100:F0} %"; }
                else { PbRf.Value = 0; TxtRf.Text = "—"; }
                if (!float.IsNaN(t.Swr)) { PbSwr.Value = Math.Clamp((t.Swr - 1) / 2 * 100, 0, 100); TxtSwr.Text = $"{t.Swr:F1}"; }
                else { PbSwr.Value = 0; TxtSwr.Text = "—"; }
                if (!float.IsNaN(t.TxGain)) { PbGain.Value = t.TxGain * 100; TxtGain.Text = $"{t.TxGain * 100:F0} %"; }
                else { PbGain.Value = 0; TxtGain.Text = "—"; }
                TxtMute.Text = "Mute / inibizione: " + (t.Mute < 0 ? "—" : t.Mute == 1 ? "sì" : "no");

                // potenza: 1-100 W se la radio converte in watt, altrimenti 1-100 %
                bool watts = t.PowerInWatts;
                double max = watts ? Math.Max(1, Math.Min(100, Math.Round(t.PowerMaxW))) : 100;
                SldPower.Minimum = 1;
                SldPower.Maximum = max;
                TxtPowMin.Text = watts ? "1 W" : "1 %";
                TxtPowMax.Text = watts ? $"{max:F0} W" : "100 %";
                string rig = t.RigName.Length > 0 ? t.RigName : (server ? _s.RigModelName : "radio");
                TxtPowerUnit.Text = float.IsNaN(t.PowerSetRel) ? "—" : $"{rig} · {(watts ? "watt" : "percentuale")}";
                if (_powerPending == null && DateTime.UtcNow - _powerSentAt > TimeSpan.FromSeconds(2))
                {
                    double v = watts ? t.PowerSetW : t.PowerSetRel * 100;
                    if (!double.IsNaN(v)) { SldPower.Value = Math.Clamp(Math.Round(v), 1, max); TxtPower.Text = FormatPower(SldPower.Value); }
                    else TxtPower.Text = "—";
                }

                // pitch CW letto dalla radio
                TxtPassband.Text = t.PassbandHz > 0 ? $"Filtro radio: {t.PassbandHz} Hz" : "Filtro radio: —";
                TxtRadioPitch.Text = t.CwPitch > 0 ? $"Radio: {t.CwPitch} Hz (CAT CWPITCH)" : "Radio: — (pitch non letto)";
                if (t.CwPitch >= 200 && t.CwPitch <= 1000 && !_pitchFromRadioDone)
                {
                    _pitchFromRadioDone = true;
                    SetPitch(t.CwPitch, false);
                }
                if (t.KeySpeed >= 5 && t.KeySpeed <= 60) CmbCwTxSpeed.SelectedIndex = t.KeySpeed - 5;

                if (!server)
                {
                    TxtPttInfo.Text = t.PttInfo.Length > 0 ? t.PttInfo : "—";
                    TxtPttDelayInfo.Text = $"ritardo {t.PttDelayMs} ms";
                    TxtRadioModel.Text = "Radio: " + (t.RigName.Length > 0 ? t.RigName : "del server");
                    TxtRadioTitle.Text = "RADIO REMOTA" + (t.RigName.Length > 0 ? " · " + t.RigName.ToUpperInvariant() : "");
                }
            }
            finally { _loading = false; }
            UpdateModeLayout();
        }

        // ================================================================= schede audio del server

        private void RefreshDevices(DeviceStateInfo d)
        {
            if (DateTime.UtcNow - _devUserAt < TimeSpan.FromSeconds(2)) return;
            _loading = true;
            try
            {
                FillDeviceCombo(CmbDevIn, d.Inputs, d.InName);
                FillDeviceCombo(CmbDevOut, d.Outputs, d.OutName);
                TxtDevInState.Text = MuteText(d.InMute, d.InVol);
                TxtDevOutState.Text = MuteText(d.OutMute, d.OutVol);
                ChipDevIn.Background = this.FindResource(d.InMute == 1 ? "B.TxDim" : "B.Inset") as IBrush;
                ChipDevOut.Background = this.FindResource(d.OutMute == 1 ? "B.TxDim" : "B.Inset") as IBrush;
                BtnDevIn.Content = d.InMute == 1 ? "Togli mute" : "Metti mute";
                BtnDevOut.Content = d.OutMute == 1 ? "Togli mute" : "Metti mute";
                BtnDevIn.IsEnabled = d.InMute >= 0;
                BtnDevOut.IsEnabled = d.OutMute >= 0;
                if (!float.IsNaN(d.InVol)) SldDevIn.Value = d.InVol * 100;
                if (!float.IsNaN(d.OutVol)) SldDevOut.Value = d.OutVol * 100;
                ChkAutoUnmute.IsChecked = d.AutoUnmute;
                TxtDevNote.Text = d.Note;
                TxtDevNote.IsVisible = d.Note.Length > 0;
            }
            finally { _loading = false; }
        }

        private static void FillDeviceCombo(ComboBox cmb, List<string> list, string current)
        {
            var items = list.Count > 0 ? new List<string>(list) : new List<string>();
            if (current.Length > 0 && !items.Contains(current)) items.Insert(0, current);
            var old = cmb.ItemsSource as List<string>;
            if (old == null || !old.SequenceEqual(items)) cmb.ItemsSource = items;
            cmb.SelectedItem = items.Contains(current) ? current : null;
        }

        /// <summary>Scelta delle schede audio del server dal riquadro «Schede audio del server» (anche dal client).</summary>
        private void ServerDeviceChosen()
        {
            if (_loading) return;
            string din = CmbDevIn.SelectedItem as string ?? "";
            string dout = CmbDevOut.SelectedItem as string ?? "";
            var d = _node.DeviceState;
            if (din.Length == 0 || dout.Length == 0 || (din == d.InName && dout == d.OutName)) return;
            _devUserAt = DateTime.MinValue;
            _node.SetServerDevices(din, dout);
            TxtDevInState.Text = "cambio scheda…";
            TxtDevOutState.Text = "cambio scheda…";
        }

        private static string MuteText(int mute, float vol) =>
            (mute < 0 ? "stato sconosciuto" : mute == 1 ? "MUTE" : "attivo") + (float.IsNaN(vol) ? "" : $" · {vol * 100:F0} %");

        private void ToggleDeviceMute(bool input)
        {
            var d = _node.DeviceState;
            bool mute = (input ? d.InMute : d.OutMute) != 1;
            _devUserAt = DateTime.MinValue;
            if (_s.Role == NodeRole.Server)
            {
                DeviceMute.SetMute(input ? _s.InputDevice : _s.OutputDevice, input, mute);
                _node.RefreshDeviceState();
            }
            else _node.SetRemoteDeviceMute(input, mute);
        }

        private void SetDeviceVolume(bool input, float v)
        {
            if (_s.Role == NodeRole.Server)
            {
                DeviceMute.SetVolume(input ? _s.InputDevice : _s.OutputDevice, input, v);
                _node.RefreshDeviceState();
            }
            else _node.SetRemoteDeviceVolume(input, v);
        }

        // ================================================================= audio locale

        private void SaveVolumes()
        {
            _s.RxVolume = (int)SldRx.Value;
            _s.TxVolume = (int)SldTx.Value;
            UpdateVolumeLabels();
            _node.ApplyAudioSettings();
            _s.Save();
        }

        private void UpdateVolumeLabels()
        {
            TxtRxVol.Text = $"Volume {(int)SldRx.Value} %";
            TxtTxVol.Text = $"Volume {(int)SldTx.Value} %";
        }

        private void SaveBoosts(TextBox source)
        {
            if (_loading) return;
            int Parse(TextBox tb)
            {
                string t = (tb.Text ?? "").Replace("%", "").Trim();
                return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ? Math.Clamp(x, 0, 1000) : 0;
            }
            if (source == TxtRxBoostCompact) TxtRxBoost.Text = TxtRxBoostCompact.Text;
            _s.RxBoost = Parse(TxtRxBoost);
            _s.TxBoost = Parse(TxtTxBoost);
            TxtRxBoost.Text = _s.RxBoost.ToString(CultureInfo.InvariantCulture);
            TxtRxBoostCompact.Text = TxtRxBoost.Text;
            TxtTxBoost.Text = _s.TxBoost.ToString(CultureInfo.InvariantCulture);
            _node.ApplyAudioSettings();
            _s.Save();
        }

        private void AudioDeviceChanged(ComboBox source)
        {
            if (_loading) return;
            _loading = true;
            if (source == CmbOutCompact) CmbOut.SelectedIndex = CmbOutCompact.SelectedIndex;
            else if (source == CmbOut) CmbOutCompact.SelectedIndex = CmbOut.SelectedIndex;
            _loading = false;
            _s.InputDevice = CmbIn.SelectedItem as string ?? "";
            _s.OutputDevice = CmbOut.SelectedItem as string ?? "";
            _s.Save();
            _node.RestartAudio();
            if (_s.Role == NodeRole.Server) _node.RefreshDeviceState();
        }

        // ================================================================= CW: pitch, cascata, decodifica

        private void OnRxSamples(short[] samples, int count)
        {
            if (!_cwActive) return;
            _spectrum.Process(samples, count);
            lock (_decLock)
            {
                _decoder.Process(samples, count);
                foreach (var d in _extraDecoders) d.Process(samples, count);
            }
        }

        private void ApplyPitchFromBox()
        {
            if (int.TryParse(TxtPitch.Text, out int p)) SetPitch(p, true);
            else TxtPitch.Text = _s.CwPitch.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Imposta il pitch (200-1000 Hz) sul decodificatore e, se richiesto, sulla radio.</summary>
        private void SetPitch(int hz, bool sendToRadio)
        {
            hz = Math.Clamp(hz, 200, 1000);
            _s.CwPitch = hz;
            _s.Save();
            TxtPitch.Text = hz.ToString(CultureInfo.InvariantCulture);
            Wf.PitchHz = hz;
            lock (_decLock) _decoder.Configure(hz, _s.CwDecodeWidth);
            if (sendToRadio) _node.SetPitch(hz);
            Wf.InvalidateVisual();
        }

        private void OnWaterfallClick(double hz)
        {
            if (hz >= 200 && hz <= 1000) { SetPitch((int)hz, true); return; }
            var existing = _decodeWindows.FirstOrDefault(w => Math.Abs(w.FrequencyHz - hz) < 40);
            if (existing != null) { existing.Activate(); return; }
            var win = new DecodeWindow(this, _node, _s, hz);
            _decodeWindows.Add(win);
            win.Closed += (_, _) => _decodeWindows.Remove(win);
            win.Show(this);
        }

        internal void AddDecoder(CwDecoder d, double hz)
        {
            lock (_decLock) _extraDecoders.Add(d);
            Wf.Markers.Add((hz, new SolidColorBrush(Color.FromRgb(255, 179, 71))));
        }

        internal void RemoveDecoder(CwDecoder d)
        {
            lock (_decLock) _extraDecoders.Remove(d);
            Wf.Markers.RemoveAll(m => Math.Abs(m.hz - d.FrequencyHz) < 1);
        }

        internal void ReconfigureDecoder(CwDecoder d, double hz, double width) { lock (_decLock) d.Configure(hz, width); }

        internal int CurrentPitch => _s.CwPitch;
        internal RigTelemetry Telemetry => _tel;

        // ================================================================= porte non aperte: il programma si spegne

        private bool _portsFailedShown;

        private async void ShowPortsFailed(string reason)
        {
            if (_portsFailedShown) return;
            _portsFailedShown = true;
            Program.LogError("SPEGNIMENTO: nessuna porta apribile dopo 20 giri · " + reason);
            var exit = new Button { Content = "Exit", MinWidth = 110, HorizontalAlignment = HorizontalAlignment.Right };
            exit.Classes.Add("accent");
            var w = new Window
            {
                Title = "IP-TRX-CTRL",
                Width = 520,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(26),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = "Nessuna porta è apribile: io mi spengo perché non riesco a funzionare.", FontSize = 18, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = "Per 20 giri il server e il client si sono scambiati sul broker MQTT le porte aperte con UPnP e hanno provato a collegarsi in tutti e due i versi, senza riuscirci.", FontSize = 14, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = "Motivo: " + reason, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = this.FindResource("B.Muted") as IBrush },
                        new TextBlock { Text = "Attiva UPnP almeno su uno dei due router (meglio su tutti e due), controlla che il server sia acceso, poi riavvia il programma. Ogni giro è scritto nel registro sessioni.log (Impostazioni › Apri cartella dati). Guida: Aiuto › Porte e collegamento.", FontSize = 13, TextWrapping = TextWrapping.Wrap },
                        exit
                    }
                }
            };
            exit.Click += (_, _) => w.Close();
            await w.ShowDialog(this);
            Close();
        }

        // ================================================================= varie

        private void ShowServerFallback(string? code)
        {
            if (_s.Role != NodeRole.Server || code == null) { PanelFallbackServer.IsVisible = false; return; }
            TxtFallbackCode.Text = code;
            PanelFallbackServer.IsVisible = true;
        }
    }
}
