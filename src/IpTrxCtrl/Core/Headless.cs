using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace IpTrxCtrl.Core
{
    /// <summary>
    /// Server senza finestra (dalla 0.3.7, idea di CAESAR di RA0SMS: server e client che partono da soli all'accensione
    /// e si ricollegano da soli). «IP-TRX-CTRL --senza-finestra» avvia il server con le impostazioni salvate e scrive
    /// lo stato sulla console; su Linux si usa con il servizio ip-trx-ctrl-server@utente di systemd.
    /// Le impostazioni, l'abbinamento e la scelta di radio e audio si fanno prima una volta con la finestra.
    /// </summary>
    public static class Headless
    {
        public static int Run()
        {
            var s = AppSettings.Load();
            Console.WriteLine($"IP-TRX-CTRL {AppVersion.Text} · server senza finestra · impostazioni in {AppSettings.Folder}");
            if (!s.FirstRunDone || s.Role != NodeRole.Server)
            {
                Console.Error.WriteLine("Questo PC non è impostato come server. Avvia una volta il programma con la finestra, scegli «Server», imposta radio e audio e abbina un client.");
                return 2;
            }
            SessionLog.Write("AVVIO server senza finestra");
            SessionLog.Echo = true;
            using var done = new ManualResetEventSlim();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
            PosixSignalRegistration? term = null;
            try { term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; done.Set(); }); } catch { }

            var node = new Node(s);
            node.Notice += m => Console.WriteLine("AVVISO " + m);
            node.PortsFailed += m => Console.WriteLine("PORTE " + m);
            node.PortsUnreachable += m => Console.WriteLine("PORTE " + m);
            node.Start();
            if (!node.IsPaired && !s.FixedCodeEnabled)
                Console.WriteLine("Nessun client abbinato e codice fisso spento: abbina un client avviando il programma con la finestra.");
            string last = "";
            while (!done.Wait(TimeSpan.FromSeconds(30)))
            {
                var st = node.Stats;
                string line = $"stato: {(node.Connected ? "operatore collegato" : "in attesa")} · radio {(node.LastTelemetry.CatOk ? "ok" : "non risponde")}"
                              + (st.Listeners.Length > 0 ? " · ascoltano " + st.Listeners : "")
                              + (node.Ipv6Status.Length > 0 ? " · IPv6 " + node.Ipv6Status : "");
                if (line != last) { Console.WriteLine(line); last = line; }
            }
            Console.WriteLine("Chiusura…");
            SessionLog.Write("CHIUSURA server senza finestra");
            try { node.Dispose(); } catch { }
            term?.Dispose();
            return 0;
        }
    }
}
