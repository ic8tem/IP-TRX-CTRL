using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Open.Nat;

namespace IpTrxCtrl.Net
{
    /// <summary>
    /// Sceglie porte TCP casuali, le apre sul router via UPnP e scarta quelle rifiutate.
    /// Le porte cambiano a ogni avvio; nulla viene mostrato all'utente.
    /// </summary>
    public sealed class PortManager
    {
        private const int MinPort = 10000, MaxPort = 65000, MaxAttempts = 25;
        private static readonly HashSet<int> Excluded = new() { 50001, 50002, 50003 };

        private NatDevice? _device;
        private readonly List<Mapping> _mappings = new();
        private readonly Random _rnd = new();

        public IPAddress? ExternalIp { get; private set; }
        public bool CgnatSuspected { get; private set; }
        public bool UpnpAvailable => _device != null;
        public string LastError { get; private set; } = "";

        /// <summary>Ogni passaggio sulle porte viene scritto qui (registro delle sessioni).</summary>
        public Action<string>? Log;
        private void L(string text) => Log?.Invoke("PORTE " + text);

        public async Task<bool> DiscoverAsync(CancellationToken ct)
        {
            L("ricerca del router UPnP / NAT-PMP (3 tentativi da 10 s)…");
            try
            {
                for (int i = 1; ; i++)
                {
                    try
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        cts.CancelAfter(TimeSpan.FromSeconds(10));
                        _device = await new NatDiscoverer().DiscoverDeviceAsync(PortMapper.Upnp | PortMapper.Pmp, cts);
                        break;
                    }
                    catch (Exception) when (i < 3 && !ct.IsCancellationRequested)
                    {
                        L($"tentativo {i}/3: router non trovato, riprovo");
                    }
                }
                ExternalIp = await _device.GetExternalIPAsync();
                CgnatSuspected = IsPrivateOrCgnat(ExternalIp);
                L($"router UPnP trovato · IP esterno dichiarato dal router: {ExternalIp}");

                // Controllo aggiuntivo: l'IP che vede internet deve coincidere con quello del router
                IPAddress? pub = await GetPublicIpAsync(ct);
                if (pub != null)
                {
                    if (!pub.Equals(ExternalIp)) CgnatSuspected = true;
                    if (CgnatSuspected || ExternalIp == null || IsPrivateOrCgnat(ExternalIp)) ExternalIp = pub;
                    L($"IP pubblico visto da internet: {pub}" + (CgnatSuspected ? " · NON coincide con quello del router: probabile CGNAT o doppio NAT" : " · coincide"));
                }
                else L("IP pubblico non verificabile dai servizi esterni");
                return true;
            }
            catch (Exception ex)
            {
                _device = null;
                LastError = "UPnP non disponibile: " + ex.Message;
                L("router UPnP non trovato: " + ex.Message);
                ExternalIp = await GetPublicIpAsync(ct);
                return false;
            }
        }

        /// <summary>Apre una porta TCP libera; restituisce il numero o 0 se non riesce.</summary>
        public async Task<int> OpenRandomPortAsync(string description, CancellationToken ct)
        {
            if (_device == null) { L($"{description}: nessun router UPnP, porta non aperta"); return 0; }
            HashSet<int> used = new();
            try
            {
                foreach (var m in await _device.GetAllMappingsAsync()) used.Add(m.PublicPort);
            }
            catch { }

            for (int i = 0; i < MaxAttempts && !ct.IsCancellationRequested; i++)
            {
                int port = _rnd.Next(MinPort, MaxPort);
                if (Excluded.Contains(port) || used.Contains(port) || !IsLocalPortFree(port)) continue;
                try
                {
                    var map = new Mapping(Protocol.Tcp, port, port, 0, description);
                    await _device.CreatePortMapAsync(map);
                    lock (_mappings) _mappings.Add(map);
                    L($"{description}: porta TCP {port} aperta sul router (tentativo {i + 1})");
                    return port;
                }
                catch (Exception ex)
                {
                    LastError = $"Porta {port} rifiutata: {ex.Message}";
                    L($"{description}: porta {port} rifiutata dal router ({ex.Message})");
                    used.Add(port);
                }
            }
            L($"{description}: nessuna porta aperta dopo {MaxAttempts} tentativi");
            return 0;
        }

        /// <summary>Senza UPnP: usa comunque una porta locale libera (servirà un inoltro manuale o una rete aperta).</summary>
        /// <summary>
        /// Apre sul router una porta esterna SCELTA (per esempio 443) inoltrata verso una porta interna del PC.
        /// false se il router la rifiuta (già usata da un altro PC o dal router stesso).
        /// </summary>
        public async Task<bool> OpenFixedAsync(Protocol proto, int publicPort, int privatePort, string description)
        {
            if (_device == null) return false;
            try
            {
                var map = new Mapping(proto, privatePort, publicPort, 0, description);
                await _device.CreatePortMapAsync(map);
                lock (_mappings) _mappings.Add(map);
                L($"{description}: porta esterna {proto} {publicPort} aperta sul router verso la porta {privatePort} del PC");
                return true;
            }
            catch (Exception ex)
            {
                L($"{description}: porta esterna {proto} {publicPort} rifiutata dal router ({ex.Message})");
                return false;
            }
        }

        public int PickLocalPort()
        {
            for (int i = 0; i < 200; i++)
            {
                int port = _rnd.Next(MinPort, MaxPort);
                if (!Excluded.Contains(port) && IsLocalPortFree(port)) return port;
            }
            return 0;
        }

        /// <summary>Apre anche UDP sullo stesso numero di porta (per l'audio su UDP). false se il router rifiuta.</summary>
        public async Task<bool> OpenUdpAsync(int port, string description)
        {
            if (_device == null) return false;
            try
            {
                var map = new Mapping(Protocol.Udp, port, port, 0, description);
                await _device.CreatePortMapAsync(map);
                lock (_mappings) _mappings.Add(map);
                L($"{description}: porta UDP {port} aperta sul router");
                return true;
            }
            catch (Exception ex)
            {
                L($"{description}: porta UDP {port} rifiutata dal router ({ex.Message})");
                return false;
            }
        }

        public async Task ClosePortAsync(int port)
        {
            List<Mapping> ms;
            lock (_mappings) ms = _mappings.Where(x => x.PublicPort == port).ToList();
            if (ms.Count == 0 || _device == null) return;
            foreach (var m in ms)
            {
                try { await _device.DeletePortMapAsync(m); L($"porta {m.Protocol} {port} chiusa sul router"); }
                catch (Exception ex) { L($"porta {port}: chiusura non riuscita ({ex.Message})"); }
                lock (_mappings) _mappings.Remove(m);
            }
        }

        public async Task CloseAllAsync()
        {
            List<Mapping> copy;
            lock (_mappings) copy = _mappings.ToList();
            foreach (var m in copy)
            {
                try { if (_device != null) { await _device.DeletePortMapAsync(m); L($"porta {m.PublicPort} chiusa sul router"); } }
                catch (Exception ex) { L($"porta {m.PublicPort}: chiusura non riuscita ({ex.Message})"); }
            }
            lock (_mappings) _mappings.Clear();
        }

        public async Task<IPAddress?> RefreshExternalIpAsync(CancellationToken ct)
        {
            try
            {
                if (_device != null)
                {
                    var ip = await _device.GetExternalIPAsync();
                    if (!IsPrivateOrCgnat(ip)) { ExternalIp = ip; return ip; }
                }
            }
            catch { }
            var pub = await GetPublicIpAsync(ct);
            if (pub != null) ExternalIp = pub;
            return ExternalIp;
        }

        private static bool IsLocalPortFree(int port)
        {
            try
            {
                var l = new TcpListener(IPAddress.Any, port);
                l.Start(); l.Stop();
                return true;
            }
            catch { return false; }
        }

        public static bool IsPrivateOrCgnat(IPAddress? ip)
        {
            if (ip == null) return true;
            byte[] b = ip.MapToIPv4().GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || b[0] == 127 || b[0] == 0;
        }

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

        public static async Task<IPAddress?> GetPublicIpAsync(CancellationToken ct)
        {
            foreach (string url in new[] { "https://api.ipify.org", "https://ipv4.icanhazip.com", "https://checkip.amazonaws.com" })
            {
                try
                {
                    string s = (await Http.GetStringAsync(url, ct)).Trim();
                    if (IPAddress.TryParse(s, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork) return ip;
                }
                catch { }
            }
            return null;
        }
    }
}
