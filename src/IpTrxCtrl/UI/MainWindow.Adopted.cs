using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using IpTrxCtrl.Audio;
using IpTrxCtrl.Core;

namespace IpTrxCtrl.UI
{
    /// <summary>
    /// Funzioni della 0.3.7 prese da altri progetti: VOX (remoteAudio), squelch vocale e note (PiRO),
    /// registrazione dell'audio (rustyrig), emulazione WinKeyer (CAESAR).
    /// </summary>
    public partial class MainWindow
    {
        private int _unreadNotes;
        private IpTrxCtrl.Rig.WinKeyerEmu? _winkeyer;

        private void InitAdopted()
        {
            // VOX: solo con il pulsante accanto al PTT del client; all'avvio è sempre spento e la scelta non si salva
            _s.VoxEnabled = false;
            BtnVox.IsChecked = false;
            ChkSquelch.IsChecked = _s.VoiceSquelch;
            // riduzione del rumore neurale (HamNoise)
            int[] nrLevels = { 25, 50, 75, 100 };
            CmbNr.ItemsSource = nrLevels.Select(v => v + " %").ToList();
            int nrIx = Array.IndexOf(nrLevels, _s.NeuralNrPercent);
            CmbNr.SelectedIndex = nrIx >= 0 ? nrIx : 3;
            ChkNr.IsChecked = _s.NeuralNr;
            ChkNr.IsCheckedChanged += (_, _) => { _s.NeuralNr = ChkNr.IsChecked == true; _node.ApplyAudioSettings(); _s.Save(); };
            CmbNr.SelectionChanged += (_, _) =>
            {
                if (CmbNr.SelectedIndex < 0) return;
                _s.NeuralNrPercent = nrLevels[CmbNr.SelectedIndex]; _node.ApplyAudioSettings(); _s.Save();
            };
            BtnVox.IsCheckedChanged += (_, _) =>
            {
                _s.VoxEnabled = BtnVox.IsChecked == true && IsClient;
                if (!_s.VoxEnabled) _node.VoxRelease();
                BtnVox.Content = _s.VoxEnabled ? "VOX ●" : "VOX";
                SessionLog.Write(_s.VoxEnabled ? "VOX attivato dal pulsante" : "VOX spento dal pulsante");
            };
            ChkSquelch.IsCheckedChanged += (_, _) =>
            {
                _s.VoiceSquelch = ChkSquelch.IsChecked == true;
                _node.ApplyAudioSettings();
                _s.Save();
            };
            BtnRec.Click += (_, _) =>
            {
                if (_node.Recorder.Active) _node.Recorder.Stop();
                else
                {
                    try { _node.Recorder.Start(_tel.FreqHz, ""); }
                    catch (Exception ex) { TxtNotice.Text = "Registrazione non avviata: " + ex.Message; TxtNotice.IsVisible = true; }
                }
                RefreshRec();
            };
            BtnRecFolder.Click += (_, _) => OpenFolderOf(Recorder.FolderPath);
            _node.Recorder.Changed += () => Dispatcher.UIThread.Post(RefreshRec);

            foreach (var n in _node.Notes) AppendNote(n);
            _node.NoteAdded += n => Dispatcher.UIThread.Post(() =>
            {
                AppendNote(n);
                if (!ReferenceEquals(Tabs.SelectedItem, TabNotes)) { _unreadNotes++; TabNotes.Header = $"Note ({_unreadNotes})"; }
            });
            Tabs.SelectionChanged += (_, _) =>
            {
                if (ReferenceEquals(Tabs.SelectedItem, TabNotes)) { _unreadNotes = 0; TabNotes.Header = "Note"; }
            };
            void Send()
            {
                string t = (TxtNoteIn.Text ?? "").Trim();
                if (t.Length == 0) return;
                if (!_node.Connected && IsClient) { TxtNotice.Text = "Per mandare una nota il client dev'essere collegato al server."; TxtNotice.IsVisible = true; return; }
                _node.SendNote(t);
                TxtNoteIn.Text = "";
            }
            BtnNoteSend.Click += (_, _) => Send();
            TxtNoteIn.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Send(); e.Handled = true; } };
            RefreshRec();
            ApplyAdoptedRole();
        }

        private void ApplyAdoptedRole()
        {
            BtnVox.IsVisible = IsClient;
            if (!IsClient && BtnVox.IsChecked == true) BtnVox.IsChecked = false;
            ChkSquelch.IsVisible = IsClient;
            ChkNr.IsVisible = IsClient; CmbNr.IsVisible = IsClient;
        }

        private void AppendNote(Node.NoteItem n)
        {
            var sb = new StringBuilder(TxtNotes.Text ?? "");
            if (sb.Length > 0) sb.Append('\n');
            sb.Append($"{n.Utc.ToLocalTime():HH:mm}  {n.From}: {n.Text}");
            var lines = sb.ToString().Split('\n');
            TxtNotes.Text = string.Join('\n', lines.Skip(Math.Max(0, lines.Length - 40)));
            SvNotes.ScrollToEnd();
        }

        private void RefreshRec()
        {
            bool on = _node.Recorder.Active;
            BtnRec.Content = on ? "■ Ferma" : "● Registra";
            TxtRec.Text = on ? $"registro… {_node.Recorder.Length:mm\\:ss} · {Path.GetFileName(_node.Recorder.CurrentFile)}" : "registrazioni WAV nella cartella «registrazioni»";
        }

        /// <summary>Ogni 100 ms: durata della registrazione e stato del VOX.</summary>
        private void TickAdopted()
        {
            if (_node.Recorder.Active) RefreshRec();
            if (_node.Denoiser.LastError.Length > 0 && ChkNr.IsChecked == true)
            {
                TxtNotice.Text = "NR neurale spento: " + _node.Denoiser.LastError; TxtNotice.IsVisible = true;
                ChkNr.IsChecked = false;
            }
        }

        // ---- emulazione WinKeyer (client)
        private void StartWinKeyer()
        {
            _winkeyer?.Dispose();
            _winkeyer = null;
            if (!IsClient || !_s.WinKeyerEnabled) return;
            try
            {
                _winkeyer = new IpTrxCtrl.Rig.WinKeyerEmu(_node, _s);
                _winkeyer.Start();
            }
            catch (Exception ex)
            {
                SessionLog.Write("WINKEYER non avviato: " + ex.Message);
                TxtNotice.Text = "WinKeyer non avviato: " + ex.Message; TxtNotice.IsVisible = true;
            }
        }

        private static void OpenFolderOf(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                else if (OperatingSystem.IsMacOS()) Process.Start("open", $"\"{path}\"");
                else Process.Start("xdg-open", $"\"{path}\"");
            }
            catch { }
        }
    }
}
