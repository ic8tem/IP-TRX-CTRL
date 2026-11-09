using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Open.Nat;

namespace IpTrxCtrl.Net
{
    public enum UpnpState { Searching, Ok, Missing }

    /// <summary>
    /// Controllo di UPnP sul router, uguale per server e client: ricerca con più tentativi (UPnP e NAT-PMP),
    /// diagnosi delle cause più comuni quando il router non risponde, verifica periodica che resti attivo.
    /// </summary>
    public sealed class UpnpCheck
    {
        public UpnpState State { get; private set; } = UpnpState.Searching;
        public string Method { get; private set; } = "";
        public string ExternalIp { get; private set; } = "";
        public string Diagnosis { get; private set; } = "";
        public DateTime LastOk { get; private set; } = DateTime.MinValue;
        public event Action? Changed;
        public Action<string>? Log;

        private NatDevice? _device;

        private void L(string s) => Log?.Invoke("UPNP " + s);

        /// <summary>Ciclo: prima ricerca (3 tentativi), poi controllo ogni 60 s; se il router non risponde più si ricomincia.</summary>
        public async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (_device == null) await DiscoverAsync(ct);
                else
                {
                    bool alive = false;
                    try
                    {
                        var t = _device.GetExternalIPAsync();
                        alive = await Task.WhenAny(t, Task.Delay(8000, ct)) == t && t.Result != null;
                        if (alive) ExternalIp = t.Result!.ToString();
                    }
                    catch { }
                    if (alive) { LastOk = DateTime.UtcNow; Set(UpnpState.Ok); }
                    else
                    {
                        L("il router non risponde più a UPnP: ricomincio la ricerca");
                        _device = null;
                        continue;
                    }
                }
                try { await Task.Delay(TimeSpan.FromSeconds(60), ct); } catch { break; }
            }
        }

        private async Task DiscoverAsync(CancellationToken ct)
        {
            for (int i = 1; i <= 3 && !ct.IsCancellationRequested; i++)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(10));
                    var dev = await new NatDiscoverer().DiscoverDeviceAsync(PortMapper.Upnp | PortMapper.Pmp, cts);
                    var ip = await dev.GetExternalIPAsync();
                    _device = dev;
                    Method = dev.GetType().Name.Contains("Pmp", StringComparison.OrdinalIgnoreCase) ? "NAT-PMP" : "UPnP";
                    ExternalIp = ip?.ToString() ?? "";
                    Diagnosis = "";
                    LastOk = DateTime.UtcNow;
                    L($"router trovato ({Method}) al tentativo {i} · IP esterno {ExternalIp}");
                    Set(UpnpState.Ok);
                    return;
                }
                catch (Exception ex)
                {
                    L($"tentativo {i}/3: router non trovato ({ex.GetType().Name})");
                }
            }
            Diagnosis = Diagnose();
            L("UPnP non disponibile · " + Diagnosis.Replace("\n", " · "));
            Set(UpnpState.Missing);
        }

        private void Set(UpnpState s)
        {
            bool changed = s != State;
            State = s;
            if (changed || s == UpnpState.Missing) Changed?.Invoke();
        }

        /// <summary>Cause possibili, dalla più probabile; sempre con le istruzioni per il router.</summary>
        private static string Diagnose()
        {
            var lines = new List<string>();
            if (OperatingSystem.IsWindows())
            {
                string cat = WindowsNetworkCategory();
                if (cat.Contains("Public", StringComparison.OrdinalIgnoreCase))
                    lines.Add("La rete di Windows è impostata come «Pubblica»: Windows blocca le risposte del router. " +
                              "Impostazioni › Rete e Internet › proprietà della rete › scegli «Privata», poi riavvia il programma.");
            }
            var extra = ExtraAdapters();
            if (extra.Count > 0)
                lines.Add("Ci sono altre schede di rete attive (" + string.Join(", ", extra) + "): se è una VPN, spegnila e riavvia il programma.");
            lines.Add("Se non è questo, UPnP è spento sul router: entra nella pagina del router (di solito 192.168.1.1 o 192.168.0.1), " +
                      "cerca la voce «UPnP» (spesso in Rete, NAT, Avanzate o Servizi) e attivala. " +
                      "Sul FRITZ!Box va abilitata anche per il singolo PC (Internet › Abilitazioni, «abilitazione porte automatica»).");
            return string.Join("\n\n", lines);
        }

        private static string WindowsNetworkCategory()
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -Command \"(Get-NetConnectionProfile | Select-Object -ExpandProperty NetworkCategory) -join ','\"")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                if (p == null) return "";
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return o.Trim();
            }
            catch { return ""; }
        }

        private static List<string> ExtraAdapters()
        {
            var list = new List<string>();
            try
            {
                string[] marks = { "VPN", "Virtual", "Hyper-V", "VirtualBox", "VMware", "TAP", "WireGuard", "Tailscale", "ZeroTier", "Hamachi" };
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    string d = ni.Description + " " + ni.Name;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Ppp || marks.Any(m => d.Contains(m, StringComparison.OrdinalIgnoreCase)))
                        list.Add(ni.Name);
                }
            }
            catch { }
            return list.Distinct().Take(4).ToList();
        }
    }
}
