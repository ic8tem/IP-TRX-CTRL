using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Open.Nat;

namespace IpTrxCtrl.Net
{
    /// <summary>
    /// Dalla 0.3.9: ricerca del router UPnP / NAT-PMP senza consumo di CPU. La ricerca di Open.NAT gira a vuoto
    /// finché non risponde un router: misurato su GitHub, due o tre core al 100 % per tutto il tempo della
    /// ricerca (fino a 30 s ogni 90 s quando il router non ha UPnP: doppio NAT, CGNAT, UPnP spento).
    /// Qui si manda prima la stessa domanda (SSDP M-SEARCH, richiesta NAT-PMP) e si aspetta la risposta senza
    /// girare a vuoto; solo se il router risponde si chiede a Open.NAT di prepararlo, e risponde subito.
    /// Il risultato è lo stesso di prima, con lo stesso router e gli stessi tempi massimi.
    /// </summary>
    public static class NatProbe
    {
        private static readonly IPEndPoint Ssdp = new(IPAddress.Parse("239.255.255.250"), 1900);
        private static readonly string[] Targets =
        {
            "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
            "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANPPPConnection:1",
        };

        private static List<(IPAddress local, IPAddress? gw)> Ipv4Interfaces()
        {
            var l = new List<(IPAddress, IPAddress?)>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var p = ni.GetIPProperties();
                    var gw = p.GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
                    foreach (var ua in p.UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                            l.Add((ua.Address, gw));
                }
            }
            catch { }
            return l;
        }

        /// <summary>Un router UPnP (IGD) risponde a M-SEARCH entro il tempo dato.</summary>
        public static async Task<bool> UpnpAnswersAsync(TimeSpan timeout, CancellationToken ct)
        {
            var socks = new List<UdpClient>();
            try
            {
                foreach (var (local, _) in Ipv4Interfaces())
                {
                    try { socks.Add(new UdpClient(new IPEndPoint(local, 0))); } catch { }
                }
                if (socks.Count == 0) return false;
                using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                t.CancelAfter(timeout);
                var found = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                foreach (var s in socks)
                {
                    var sock = s;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            while (!t.IsCancellationRequested)
                            {
                                var r = await sock.ReceiveAsync(t.Token);
                                string txt = Encoding.ASCII.GetString(r.Buffer);
                                if (txt.StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase)
                                    && (txt.Contains("InternetGatewayDevice") || txt.Contains("WANIPConnection") || txt.Contains("WANPPPConnection")))
                                { found.TrySetResult(true); return; }
                            }
                        }
                        catch { }
                    });
                }
                // la domanda si ripete ogni secondo (i pacchetti multicast si perdono), come fa Open.NAT
                while (!t.IsCancellationRequested && !found.Task.IsCompleted)
                {
                    foreach (var s in socks)
                        foreach (var st in Targets)
                        {
                            byte[] q = Encoding.ASCII.GetBytes($"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nST: {st}\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\n\r\n");
                            try { await s.SendAsync(q, q.Length, Ssdp); } catch { }
                        }
                    await Task.WhenAny(found.Task, Task.Delay(1000, t.Token).ContinueWith(_ => { }));
                }
                return found.Task.IsCompleted;
            }
            catch { return false; }
            finally { foreach (var s in socks) s.Dispose(); }
        }

        /// <summary>Il router (gateway) risponde a NAT-PMP (richiesta dell'indirizzo esterno, RFC 6886) entro il tempo dato.</summary>
        public static async Task<bool> PmpAnswersAsync(TimeSpan timeout, CancellationToken ct)
        {
            var gws = Ipv4Interfaces().Select(x => x.gw).Where(g => g != null).Distinct().ToList();
            if (gws.Count == 0) return false;
            try
            {
                using var udp = new UdpClient(AddressFamily.InterNetwork);
                using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                t.CancelAfter(timeout);
                int wait = 250;
                while (!t.IsCancellationRequested)
                {
                    foreach (var g in gws) { try { await udp.SendAsync(new byte[] { 0, 0 }, 2, new IPEndPoint(g!, 5351)); } catch { } }
                    using var w = CancellationTokenSource.CreateLinkedTokenSource(t.Token);
                    w.CancelAfter(wait);
                    try
                    {
                        while (true)
                        {
                            var r = await udp.ReceiveAsync(w.Token);
                            if (r.Buffer.Length >= 12 && r.Buffer[0] == 0 && r.Buffer[1] == 128) return true;
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (SocketException) { return false; }   // porta 5351 chiusa sul router: niente NAT-PMP
                    wait = Math.Min(wait * 2, 2000);
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Come NatDiscoverer.DiscoverDeviceAsync, ma Open.NAT parte solo dopo che il router ha risposto alla domanda
        /// leggera: niente CPU sprecata quando il router non c'è. Se il router non risponde: NatDeviceNotFoundException.
        /// </summary>
        public static async Task<NatDevice> DiscoverDeviceAsync(PortMapper mapper, TimeSpan timeout, CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task<bool> tu = mapper.HasFlag(PortMapper.Upnp) ? UpnpAnswersAsync(timeout, probe.Token) : Task.FromResult(false);
            Task<bool> tp = mapper.HasFlag(PortMapper.Pmp) ? PmpAnswersAsync(timeout, probe.Token) : Task.FromResult(false);
            // basta il primo che risponde: l'altra domanda si ferma (come la ricerca di Open.NAT con entrambi)
            var first = await Task.WhenAny(tu, tp);
            if (!first.Result) await Task.WhenAll(tu, tp);
            probe.Cancel();
            bool upnp = tu.IsCompletedSuccessfully && tu.Result, pmp = tp.IsCompletedSuccessfully && tp.Result;
            if (!upnp && !pmp) throw new NatDeviceNotFoundException();
            var use = (upnp ? PortMapper.Upnp : 0) | (pmp ? PortMapper.Pmp : 0);
            var left = timeout - sw.Elapsed;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(left > TimeSpan.FromSeconds(3) ? left : TimeSpan.FromSeconds(3));
            return await new NatDiscoverer().DiscoverDeviceAsync(use, cts);
        }

        /// <summary>Come NatDiscoverer.DiscoverDevicesAsync (tutti i router UPnP), dopo la domanda leggera.</summary>
        public static async Task<IEnumerable<NatDevice>> DiscoverDevicesAsync(PortMapper mapper, TimeSpan timeout, CancellationToken ct)
        {
            if (!await UpnpAnswersAsync(TimeSpan.FromSeconds(Math.Min(3, timeout.TotalSeconds)), ct)) return Array.Empty<NatDevice>();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            return await new NatDiscoverer().DiscoverDevicesAsync(mapper, cts);
        }
    }
}
