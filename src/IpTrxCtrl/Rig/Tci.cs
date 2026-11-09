using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IpTrxCtrl.Rig
{
    /// <summary>
    /// PTT tramite protocollo TCI (Expert Electronics): WebSocket in chiaro, comandi testuali "trx:0,true;".
    /// La connessione resta aperta e viene ristabilita quando cade.
    /// </summary>
    public sealed class TciClient : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private ClientWebSocket? _ws;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private CancellationTokenSource? _readCts;

        public string Status { get; private set; } = "TCI non collegato";

        public TciClient(string host, int port) { _host = host; _port = port; }

        private async Task EnsureAsync(CancellationToken ct)
        {
            if (_ws != null && _ws.State == WebSocketState.Open) return;
            _readCts?.Cancel();
            _ws?.Dispose();
            _ws = new ClientWebSocket();
            using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                t.CancelAfter(4000);
                await _ws.ConnectAsync(new Uri($"ws://{_host}:{_port}"), t.Token);
            }
            Status = $"TCI {_host}:{_port} collegato";
            _readCts = new CancellationTokenSource();
            var ws = _ws;
            var rct = _readCts.Token;
            _ = Task.Run(async () =>
            {
                var buf = new byte[16384];
                try
                {
                    while (!rct.IsCancellationRequested && ws.State == WebSocketState.Open)
                    {
                        var r = await ws.ReceiveAsync(buf, rct);
                        if (r.MessageType == WebSocketMessageType.Close) break;
                    }
                }
                catch { }
            });
        }

        public async Task SendAsync(string command, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        await EnsureAsync(ct);
                        byte[] b = Encoding.ASCII.GetBytes(command);
                        await _ws!.SendAsync(b, WebSocketMessageType.Text, true, ct);
                        return;
                    }
                    catch (Exception ex)
                    {
                        Status = "TCI: " + ex.Message;
                        _ws?.Dispose();
                        _ws = null;
                        if (attempt == 1) throw;
                    }
                }
            }
            finally { _lock.Release(); }
        }

        public Task SetPttAsync(bool on) => SendAsync(on ? "trx:0,true;" : "trx:0,false;");

        public void Dispose()
        {
            _readCts?.Cancel();
            try { _ws?.Abort(); } catch { }
            _ws?.Dispose();
        }
    }
}
