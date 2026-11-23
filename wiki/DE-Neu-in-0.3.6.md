# Neu in 0.3.6

## Opus-Audio

Zwischen zwei Programmen ab 0.3.6 wird das Audio mit **Opus** (16 kbit/s, Sprache mit 8 kHz) statt PCM komprimiert: weniger Bandbreite, Fehlerkorrektur (FEC) und Lückenfüllung, wenn ein Paket verloren geht. Mit älteren Versionen und mit der Webseite bleibt das Audio 8-kHz-PCM. Nichts einzustellen.

## Mehrere Zuhörer am selben Server

**Impostazioni › Sicurezza in trasmissione (server) › „Altri client e pagine web possono collegarsi in solo ascolto“** (Einstellungen › Sendesicherheit (Server) › „Andere Clients und Webseiten dürfen nur zum Hören verbinden“; standardmäßig an). Während ein Operator das Funkgerät benutzt, können sich bis zu **8** weitere gekoppelte Clients oder Webseiten verbinden: Sie hören das Funkgerät und sehen Frequenz und Instrumente, können aber weder senden noch etwas ändern. Ihr Fenster zeigt „tu sei in solo ascolto: trasmette X“ (nur Hören: X sendet); auf dem Server zeigt das Feld des anderen PCs, wie viele zuhören. Trennt sich der Operator, werden die Zuhörer getrennt, und wer sich zuerst wieder verbindet, wird Operator.

## CW-Taste über USB und MIDI

**Impostazioni › Tasto CW USB e MIDI** (Einstellungen › CW-Taste USB und MIDI, Client):

- **Tastaturmodus** (VBand, Vail und ähnliche): linke Strg-Taste = Punkt, rechte Strg-Taste = Strich.
- **Iambic-Keyer Modus A oder B** oder **Handtaste**; Tempo 5–60 WPM; Punkt/Strich tauschen; Mithörton am PC auf dem CW-Pitch.
- **MIDI** (Vail im MIDI-Modus, TinyMIDI): C = Handtaste, Cis = Punkt, D = Strich. Windows und Linux (Benutzer in der Gruppe `audio`), nicht am Mac.

Der Keyer läuft am Client und schickt die Tastzeiten an den Server, der sie mit der festen Verzögerung der CW-Taste wiederholt (wie die mechanische Taste an der seriellen Schnittstelle, siehe [Digitalbetrieb und Pedal](DE-Digitalbetrieb-und-Pedal)).

Auch die **Webseite** hat die Taste: Einstellungen › Tasto CW, mit Tastaturmodus (Strg oder `[` und `]`) und MIDI in Chrome oder Edge.

## QSO-Logbuch

Schaltfläche **„Registro“** (Logbuch) oben im Hauptfenster: Rufzeichen, RST gegeben und erhalten, Locator, Name, QTH und Notizen. Frequenz, Band, Betriebsart und Zeiten kommen vom Funkgerät.

- **Registra** (Eintragen) speichert das QSO in `registro.adi` im Einstellungsordner und schickt es, wenn aktiv, mit dem **UDP-Protokoll von WSJT-X** an Logprogramme („QSO Logged“ und „Logged ADIF“, Port 2237 voreingestellt): GridTracker, JTAlert, Log4OM, N1MM, HRD und andere tragen es wie ein WSJT-X-QSO ein.
- **Esporta ADIF…** (ADIF exportieren) speichert eine Datei für **LoTW** (TQSL), QRZ und Club Log.
- **Impostazioni › Registro dei QSO**: Adresse, Port und dein Locator. Läuft WSJT-X auf demselben PC, im Logprogramm einen zweiten Port anlegen oder die Multicast-Weiterleitung nutzen.

## Icom-Funkgeräte im Netzwerk

Für IC-7610, IC-9700, IC-705, IC-7300MK2, IC-905 und andere Icom mit Netzwerk: Der Server spricht mit dem Funkgerät wie RS-BA1 und wfview, **ohne USB-Kabel und ohne PC neben dem Funkgerät**.

1. Am Funkgerät: Set › Network › **Network Control ON** und ein Benutzer in Network User1 (Name und Passwort, höchstens 16 Zeichen).
2. **Impostazioni › Radio Icom in rete (server)**: Adresse des Funkgeräts, Port 50001, Benutzer und Passwort (bleibt verschlüsselt auf diesem PC).
3. In **Radio (server)**: das Hamlib-Modell des Funkgeräts und PTT „CAT“.
4. Speichern, Einstellungen erneut öffnen und **„Icom LAN · audio della radio“** als Soundkarte des Servers wählen (Empfang und Senden mit 8 kHz).

CAT läuft über rigctld, verbunden mit einer internen Brücke zum CI-V-Strom des Funkgeräts. Das Protokoll ist in C# nach [wfview](https://gitlab.com/eliggett/wfview) (GPL 3) und der Dokumentation von [kappanhang](https://github.com/nonoo/kappanhang) neu geschrieben; siehe [Lizenzen](DE-Lizenzen).

> Neue Funktion, bisher ohne echtes Funkgerät getestet: Bei Problemen den Problembericht anhängen (ICOM-Zeilen in `sessioni.log`).


---

[Inhalt](Deutsch) · Andere Sprachen: [Italiano](IT-Funzioni-nuove-della-0.3.6) · [English](EN-New-in-0.3.6) · [Español](ES-Novedades-de-la-0.3.6) · [Français](FR-Nouveautés-de-la-0.3.6)
