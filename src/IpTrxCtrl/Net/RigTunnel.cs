using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IpTrxCtrl.Core;

namespace IpTrxCtrl.Net
{
    /// <summary>
    /// Client: un «rigctld» locale (127.0.0.1:4532) per i programmi digitali (WSJT-X, JTDX, JS8Call, fldigi…),
    /// da usare con il modello Hamlib «NET rigctl» (2). Ogni comando viaggia cifrato fino al rigctld del server.
    /// I comandi di PTT non vengono inoltrati: diventano il PTT di IP-TRX-CTRL (stessa sicurezza e audio
    /// preso dal programma digitale).
    /// </summary>
    public sealed class RigTunnelClient : IDisposable
    {
        private static readonly Regex SetPtt = new(@"^[+;|,]?\s*(T|\\set_ptt)(\s+[A-Za-z]+[A-Za-z0-9]*)?\s+([0-9])\s*$", RegexOptions.Compiled);
        private static readonly Regex GetPtt = new(@"^[;|,]?\s*(t|\\get_ptt)(\s+[A-Za-z]+[A-Za-z0-9]*)?\s*$", RegexOptions.Compiled);

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<int, NetworkStream> _conns = new();
        private int _nextId;
        private readonly Action<byte[]> _send;           // verso il server (messaggio già costruito)
        private volatile bool _ptt;

        public event Action<bool>? PttRequest;
        public event Action<string>? Log;
        public int Port { get; }
        public int Connections => _conns.Count;

        public RigTunnelClient(int port, Action<byte[]> sendToServer)
        {
            _send = sendToServer;
            Port = port;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient tcp;
                try { tcp = await _listener.AcceptTcpClientAsync(_cts.Token); } catch { break; }
                int id = Interlocked.Increment(ref _nextId);
                tcp.NoDelay = true;
                var stream = tcp.GetStream();
                _conns[id] = stream;
                Log?.Invoke($"RIGCTL programma digitale collegato (#{id})");
                _send(new MsgWriter(Msg.RigTunnelOpen).I32(id).Build());
                _ = Task.Run(() => ReadLoopAsync(id, tcp, stream));
            }
        }

        private async Task ReadLoopAsync(int id, TcpClient tcp, NetworkStream stream)
        {
            var reader = new StreamReader(stream, Encoding.ASCII);
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(_cts.Token);
                    if (line == null) break;
                    var m = SetPtt.Match(line);
                    if (m.Success)
                    {
                        bool on = m.Groups[3].Value != "0";
                        _ptt = on;
                        PttRequest?.Invoke(on);
                        string reply = line.TrimStart().StartsWith("+") ? $"set_ptt: {(on ? 1 : 0)}\nRPRT 0\n" : "RPRT 0\n";
                        await WriteAsync(stream, Encoding.ASCII.GetBytes(reply));
                        continue;
                    }
                    if (GetPtt.IsMatch(line))
                    {
                        await WriteAsync(stream, Encoding.ASCII.GetBytes((_ptt ? "1" : "0") + "\n"));
                        continue;
                    }
                    if (line.Trim() == "q" || line.Trim() == "\\quit") break;
                    _send(new MsgWriter(Msg.RigTunnelData).I32(id).Bytes(Encoding.ASCII.GetBytes(line + "\n")).Build());
                }
            }
            catch { }
            finally
            {
                _conns.TryRemove(id, out _);
                try { tcp.Dispose(); } catch { }
                _send(new MsgWriter(Msg.RigTunnelClose).I32(id).Build());
                Log?.Invoke($"RIGCTL programma digitale scollegato (#{id})");
                if (_ptt && _conns.IsEmpty) { _ptt = false; PttRequest?.Invoke(false); }
            }
        }

        private static async Task WriteAsync(NetworkStream s, byte[] data)
        {
            try { await s.WriteAsync(data); } catch { }
        }

        /// <summary>Risposta del rigctld del server.</summary>
        public void OnData(int id, byte[] data)
        {
            if (_conns.TryGetValue(id, out var s)) _ = WriteAsync(s, data);
        }

        public void OnClose(int id)
        {
            if (_conns.TryRemove(id, out var s)) try { s.Dispose(); } catch { }
        }

        /// <summary>Il collegamento con il server è caduto: chiude i programmi collegati.</summary>
        public void DropAll()
        {
            foreach (var id in _conns.Keys) OnClose(id);
            if (_ptt) { _ptt = false; PttRequest?.Invoke(false); }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            DropAll();
        }
    }

    /// <summary>Server: per ogni programma del client apre un collegamento al proprio rigctld e passa i byte.</summary>
    public sealed class RigTunnelServer : IDisposable
    {
        private readonly ConcurrentDictionary<int, TcpClient> _conns = new();
        private readonly Func<(string host, int port)?> _endpoint;
        private readonly Action<byte[]> _send;

        public RigTunnelServer(Func<(string host, int port)?> rigctldEndpoint, Action<byte[]> sendToClient)
        {
            _endpoint = rigctldEndpoint;
            _send = sendToClient;
        }

        public async Task OpenAsync(int id)
        {
            var ep = _endpoint();
            if (ep == null) { _send(new MsgWriter(Msg.RigTunnelClose).I32(id).Build()); return; }
            try
            {
                var tcp = new TcpClient { NoDelay = true };
                await tcp.ConnectAsync(ep.Value.host, ep.Value.port);
                _conns[id] = tcp;
                SessionLog.Write($"RIGCTL programma digitale del client collegato al rigctld (#{id})");
                _ = Task.Run(async () =>
                {
                    var buf = new byte[4096];
                    try
                    {
                        var s = tcp.GetStream();
                        while (true)
                        {
                            int n = await s.ReadAsync(buf);
                            if (n <= 0) break;
                            _send(new MsgWriter(Msg.RigTunnelData).I32(id).Bytes(buf, 0, n).Build());
                        }
                    }
                    catch { }
                    if (_conns.TryRemove(id, out _)) _send(new MsgWriter(Msg.RigTunnelClose).I32(id).Build());
                });
            }
            catch { _send(new MsgWriter(Msg.RigTunnelClose).I32(id).Build()); }
        }

        private static readonly Regex RawPtt = new(@"^\s*[+;|,]?\s*(T|\\set_ptt)(\s+[A-Za-z]+[A-Za-z0-9]*)?\s+[0-9]\s*$", RegexOptions.Compiled);

        /// <summary>
        /// Comandi dal client verso il rigctld. Il client trasforma i comandi di PTT nel PTT del programma (con i
        /// suoi controlli: solo ascolto, tempo massimo, operatore alla radio): un comando di PTT che arriva grezzo
        /// non viene eseguito e riceve un errore.
        /// </summary>
        public async Task DataAsync(int id, byte[] data)
        {
            if (!_conns.TryGetValue(id, out var tcp)) return;
            string text = Encoding.ASCII.GetString(data);
            if (text.Contains('T') || text.Contains("set_ptt"))
            {
                var keep = new StringBuilder();
                string[] parts = text.Split('\n');
                for (int i = 0; i < parts.Length; i++)
                {
                    bool last = i == parts.Length - 1;   // pezzo senza a capo (vuoto se il testo finiva con l'a capo)
                    if (!last && RawPtt.IsMatch(parts[i].TrimEnd('\r')))
                    {
                        SessionLog.Write($"RIGCTL comando di PTT grezzo dal client scartato (#{id})");
                        _send(new MsgWriter(Msg.RigTunnelData).I32(id).Bytes(Encoding.ASCII.GetBytes("RPRT -1\n")).Build());
                        continue;
                    }
                    keep.Append(parts[i]);
                    if (!last) keep.Append('\n');
                }
                data = Encoding.ASCII.GetBytes(keep.ToString());
                if (data.Length == 0) return;
            }
            try { await tcp.GetStream().WriteAsync(data); } catch { Close(id); }
        }

        public void Close(int id)
        {
            if (_conns.TryRemove(id, out var tcp)) try { tcp.Dispose(); } catch { }
        }

        public void Dispose()
        {
            foreach (var id in _conns.Keys) Close(id);
        }
    }
}
