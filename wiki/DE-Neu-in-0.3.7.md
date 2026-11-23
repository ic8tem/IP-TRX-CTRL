# Neu in 0.3.7

0.3.7 übernimmt die besten Lösungen anderer Fernsteuerprogramme: remoteAudio (DH1TW), PiRO (VK3TR), RustyRig, RigControl (K6BP) und CAESAR (RA0SMS). Was IP-TRX-CTRL schon hatte (Opus, mehrere Zuhörer, STUN und UPnP, rigctl-Port, Panadapter, CW-Makros, Icom im Netzwerk), wurde nicht doppelt gebaut.

## VOX am Client (aus remoteAudio)

Schaltfläche **VOX** neben dem PTT des Clients: nur so lässt er sich einschalten, und bei jedem Start ist er aus. PTT schaltet von selbst, wenn du sprichst, und fällt nach der Haltezeit ab. Schwelle (dBFS) und Haltezeit (ms) in **Impostazioni › VOX, squelch vocale e registrazione**. Kopfhörer verwenden: Das Programm erkennt und ignoriert Funkgeräte-Audio, das ins Mikrofon zurückkommt, mit Lautsprecher bleibt VOX aber unzuverlässig. Es gelten die Sicherheiten des PTT (Höchstdauer, SWR, Vorrang für den Operator am Gerät).

## Opus-Bitrate (aus remoteAudio)

In den Einstellungen wählbar: 8, 12, **16** (voreingestellt), 24 oder 32 kbit/s.

## Notizen zwischen Operator und Zuhörern (aus PiRO und RustyRig)

Reiter **Note**: kurze Nachrichten („auf 40 m FT8 bis 16:30“, „kann ich nach der Runde übernehmen?“), auch von der Webseite. Der Server hebt sie eine Stunde auf und schickt sie an alle, die sich später verbinden.

## Zuhörer hören auch den Sendenden (aus PiRO)

Mit verbundenen Zuhörern erreicht sie auch die Stimme des sendenden Operators, wie in einer Runde. Nicht in Datenbetriebsarten und CW. Abschaltbar in **Impostazioni › Sicurezza in trasmissione (server)**.

## Neuronale Rauschunterdrückung (HamNoise, aus PiRO)

**Audio › NR neurale** (Client): das neuronale Netz [HamNoise](https://github.com/e04/HamNoise) von e04, auf HF-Signale trainiert, dasselbe wie PiROs Schaltfläche „HamNoise“. Ein Modell für Sprache (SSB, AM, FM) und eines für CW, je nach Betriebsart des Funkgeräts; in Datenbetriebsarten wirkt es nicht. Der Wert daneben (25-100 %) bestimmt, wie stark gefiltert wird: bei schwachen Signalen lässt ein niedrigerer Wert mehr Signal durch. Läuft am Client-PC, fügt etwa 60 ms Verzögerung und sehr wenig Last hinzu.

## Sprach-Squelch (aus PiRO)

**Audio › Squelch vocale** (Client): In SSB, AM und FM schaltet er das Rauschen zwischen den Durchgängen stumm. Er öffnet, wenn er die Periodizität einer Stimme erkennt, nicht bei Trägern, CW-Tönen oder Störungen: keine Schwelle einzustellen. Haltezeit einstellbar.

## Audioaufnahme (aus RustyRig)

**Audio › ● Registra**: eine WAV-Datei mit 8 kHz, Empfang und Senden, im Ordner `registrazioni` neben den Einstellungen (Schaltfläche „Cartella“). Mit **„Registra l'audio di ogni QSO“** startet die Aufnahme von selbst, wenn du im [Logbuch](DE-Neu-in-0.3.6) ein Rufzeichen tippst, und endet mit „Registra“, mit dem Rufzeichen im Dateinamen.

## Direkte IPv6-Verbindung mit PCP (aus RigControl)

IPv6 hat kein NAT und kein CGNAT: Haben Server und Client eine öffentliche IPv6-Adresse, lauscht der Server auch dort auf denselben Ports und der Client probiert sie zuerst. Der Server bittet den Router per **PCP** (RFC 6887), die beiden Ports durchzulassen. Ohne PCP im Router die Ports in der IPv6-Firewall des Routers für den Server-PC freigeben; antwortet IPv6 nicht, geht der Client zurück auf IPv4 und versucht es eine halbe Stunde lang nicht erneut. Abschaltbar in **Impostazioni › Porte aperte sul router**. Siehe auch [Ports und Verbindung](DE-Ports-und-Verbindung).

## Server ohne Fenster (aus CAESAR)

Für einen Raspberry Pi oder PC ohne Bildschirm neben dem Funkgerät: einmal alles mit Fenster einrichten (Rolle Server, Funkgerät, Audio, Kopplung oder fester Code), dann:

```
IP-TRX-CTRL --senza-finestra
```

Das `.deb`-Paket bringt auch einen systemd-Dienst mit, der beim Einschalten startet und sich selbst neu startet:

```
sudo systemctl enable --now ip-trx-ctrl-server@BENUTZER
journalctl -u ip-trx-ctrl-server@BENUTZER -f
```

## WinKeyer für Logprogramme (aus CAESAR)

**Impostazioni › WinKeyer per i programmi di log (client)**. N1MM, DXLog, TR4W, Win-Test, QLog und andere sehen einen WinKeyer: Das CW, das sie schicken, geht mit der gewünschten Geschwindigkeit vom Funkgerät aus, und die Esc-Taste des Programms stoppt es.

- **Windows**: Ein virtuelles Portpaar von com0com wird gebraucht (z. B. COM10 und COM11): eines in IP-TRX-CTRL, das andere im Logprogramm.
- **Linux und macOS**: Mit dem Port **„virtuale“** legt das Programm eine virtuelle serielle Schnittstelle an; im Logprogramm den Pfad `winkeyer` im Einstellungsordner angeben.

> Neue Funktionen, kompiliert, aber noch nicht mit einem echten Funkgerät getestet: bei Problemen den Problembericht anhängen.


---

[Inhalt](Deutsch) · Andere Sprachen: [Italiano](IT-Funzioni-nuove-della-0.3.7) · [English](EN-New-in-0.3.7) · [Español](ES-Novedades-de-la-0.3.7) · [Français](FR-Nouveautés-de-la-0.3.7)
