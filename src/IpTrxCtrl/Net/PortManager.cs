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
        /// <summary>IP esterno dichiarato dal router (con CGNAT è privato, per esempio 10.x o 100.64.x).</summary>
        public IPAddress? RouterIp { get; private set; }

        /// <summary>Porte comuni che spesso passano i filtri delle reti (provate a giri alterni nell'abbinamento).</summary>
        public static readonly int[] CommonPorts = { 443, 80, 8080, 8443, 3478, 5222, 1194, 9000, 10443, 2222 };
        public bool UpnpAvailable => _device != null;
        /// <summary>Il router è stato trovato con NAT-PMP invece che con UPnP.</summary>
        public bool IsPmp => _isPmp;
        public string MapperName => _isPmp ? "NAT-PMP" : "UPnP";
        private bool _isPmp, _switched;
        // Open.NAT con NAT-PMP scrive le porte su 16 bit con segno: oltre 32767 va in errore («overflow»)
        private int MaxPortEff => _isPmp ? 32767 : MaxPort;

        private static async Task<NatDevice?> TryDiscoverAsync(PortMapper mapper, int seconds, CancellationToken ct)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(seconds));
                return await new NatDiscoverer().DiscoverDeviceAsync(mapper, cts);
            }
            catch { return null; }
        }

        /// <summary>Il router rifiuta le porte con il protocollo in uso: prova una volta l'altro (UPnP / NAT-PMP).</summary>
        private async Task<bool> SwitchMapperAsync(CancellationToken ct)
        {
            if (_switched || ct.IsCancellationRequested) return false;
            _switched = true;
            string other = _isPmp ? "UPnP" : "NAT-PMP";
            L($"il router rifiuta le porte con {MapperName}: provo con {other}");
            var d = await TryDiscoverAsync(_isPmp ? PortMapper.Upnp : PortMapper.Pmp, _isPmp ? 10 : 5, ct);
            if (d == null) { L($"{other}: il router non risponde"); return false; }
            _device = d;
            _isPmp = !_isPmp;
            L($"router trovato anche con {MapperName}: riprovo le porte");
            return true;
        }
        public string LastError { get; private set; } = "";

        /// <summary>Ogni passaggio sulle porte viene scritto qui (registro delle sessioni).</summary>
        public Action<string>? Log;
        private void L(string text) => Log?.Invoke("PORTE " + text);

        public async Task<bool> DiscoverAsync(CancellationToken ct)
        {
            // prima UPnP (il più diffuso e quello che funziona meglio); NAT-PMP solo se UPnP non risponde.
            // Prima della 0.2.8 vinceva il primo che rispondeva, e alcuni router rispondono a NAT-PMP senza aprire porte.
            L("ricerca del router: prima UPnP (2 tentativi da 8 s), poi NAT-PMP…");
            try
            {
                _device = null; _isPmp = false; _switched = false;
                for (int i = 1; i <= 2 && _device == null && !ct.IsCancellationRequested; i++)
                {
                    _device = await TryDiscoverAsync(PortMapper.Upnp, 8, ct);
                    if (_device == null && i < 2) L($"UPnP: tentativo {i}/2 senza risposta, riprovo");
                }
                if (_device == null && !ct.IsCancellationRequested)
                {
                    L("UPnP non risponde: provo NAT-PMP");
                    _device = await TryDiscoverAsync(PortMapper.Pmp, 5, ct);
                    _isPmp = _device != null;
                }
                if (_device == null) throw new Exception("nessun router risponde con UPnP né con NAT-PMP");
                ExternalIp = await _device.GetExternalIPAsync();
                RouterIp = ExternalIp;
                CgnatSuspected = IsPrivateOrCgnat(ExternalIp);
                L($"router trovato con {MapperName} · IP esterno dichiarato dal router: {ExternalIp}");

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

            int fails = 0;
            for (int i = 0; i < MaxAttempts && !ct.IsCancellationRequested; i++)
            {
                int port = _rnd.Next(MinPort, MaxPortEff);
                if (Excluded.Contains(port) || used.Contains(port) || !IsLocalPortFree(port)) continue;
                try
                {
                    var map = new Mapping(Protocol.Tcp, port, port, 0, description);
                    await _device!.CreatePortMapAsync(map);
                    lock (_mappings) _mappings.Add(map);
                    L($"{description}: porta TCP {port} aperta sul router con {MapperName} (tentativo {i + 1})");
                    return port;
                }
                catch (Exception ex)
                {
                    LastError = $"Porta {port} rifiutata: {ex.Message}";
                    L($"{description}: porta {port} rifiutata dal router ({ex.Message})");
                    used.Add(port);
                    if (++fails >= 5)
                    {
                        if (await SwitchMapperAsync(ct)) { fails = 0; continue; }
                        L($"{description}: il router rifiuta tutte le porte");
                        break;
                    }
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

        /// <summary>
        /// Abbinamento: apre una porta TCP esterna MAI provata prima (la aggiunge a «tried»). Prima le porte
        /// «preferred» (per esempio quelle che escono dalla rete dell'altro PC), poi, se «common», le porte
        /// comuni, altrimenti una a caso. Senza UPnP usa una porta locale nuova (serve DMZ o inoltro).
        /// Restituisce (porta esterna, porta del PC) oppure (0, 0).
        /// </summary>
        public async Task<(int ext, int inner)> OpenDistinctTcpAsync(ISet<int> tried, IEnumerable<int> preferred, bool common, string description, CancellationToken ct)
        {
            if (_device == null)
            {
                for (int i = 0; i < 200; i++)
                {
                    int p = PickLocalPort();
                    if (p == 0) break;
                    if (tried.Add(p)) { L($"{description}: router senza UPnP, uso la porta del PC {p} (serve DMZ o inoltro manuale)"); return (p, p); }
                }
                return (0, 0);
            }
            HashSet<int> used = new();
            try { foreach (var m in await _device.GetAllMappingsAsync()) used.Add(m.PublicPort); } catch { }
            var fixedList = preferred.Concat(common ? CommonPorts : Array.Empty<int>());
            foreach (int ext in fixedList)
            {
                if (ct.IsCancellationRequested) return (0, 0);
                if (tried.Contains(ext) || used.Contains(ext)) continue;
                tried.Add(ext);
                int inner = PickLocalPort();
                if (inner == 0) break;
                if (await OpenFixedAsync(Protocol.Tcp, ext, inner, description)) return (ext, inner);
            }
            int fails = 0;
            for (int i = 0; i < MaxAttempts && !ct.IsCancellationRequested; i++)
            {
                int port = _rnd.Next(MinPort, MaxPortEff);
                if (Excluded.Contains(port) || used.Contains(port) || tried.Contains(port) || !IsLocalPortFree(port)) continue;
                tried.Add(port);
                try
                {
                    var map = new Mapping(Protocol.Tcp, port, port, 0, description);
                    await _device!.CreatePortMapAsync(map);
                    lock (_mappings) _mappings.Add(map);
                    L($"{description}: porta TCP {port} aperta sul router con {MapperName}");
                    return (port, port);
                }
                catch (Exception ex)
                {
                    L($"{description}: porta {port} rifiutata dal router ({ex.Message})");
                    if (++fails >= 5)
                    {
                        if (await SwitchMapperAsync(ct)) { fails = 0; continue; }
                        break;
                    }
                }
            }
            // il router non apre porte: una porta del PC (rete locale, DMZ, inoltro manuale)
            for (int i = 0; i < 200 && !ct.IsCancellationRequested; i++)
            {
                int p = PickLocalPort();
                if (p == 0) break;
                if (tried.Add(p)) { L($"{description}: il router non apre porte, uso la porta del PC {p} (vale in rete locale, con DMZ o inoltro manuale)"); return (p, p); }
            }
            return (0, 0);
        }

        public int PickLocalPort()
        {
            for (int i = 0; i < 200; i++)
            {
                int port = _rnd.Next(MinPort, MaxPortEff);
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
