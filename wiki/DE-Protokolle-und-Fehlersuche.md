# Protokolle und Fehlersuche

## Wo die Dateien des Programms liegen

| System | Datenordner |
|---|---|
| Windows | `%APPDATA%\IP-TRX-CTRL` (also `C:\Users\<Benutzer>\AppData\Roaming\IP-TRX-CTRL`) |
| Linux und Raspberry Pi | `~/.config/IP-TRX-CTRL` |
| macOS | `~/.config/IP-TRX-CTRL` (versteckter Ordner: im Finder Cmd+Umschalt+G und den Pfad eingeben) |

Der Ordner lässt sich auch im Programm öffnen: **Impostazioni › Apri cartella dati** (Einstellungen › Datenordner öffnen).

Unter Windows: Win+R, `%APPDATA%\IP-TRX-CTRL` eingeben und Enter drücken.

## Was er enthält

| Datei | Inhalt |
|---|---|
| `errori.log` | unerwartete Fehler, mit Datum, Uhrzeit und Stelle im Programm. Die Datei erscheint erst nach dem ersten Fehler |
| `sessioni.log` | Verbindungsprotokoll: jeder Schritt an den Ports (Zeilen `PORTE`: Suche des UPnP-Routers, geöffnete oder abgelehnte Ports, öffentliche IP, Schließen), Netztyp (Zeilen `RETE`: IPv4 / IPv6), Kopplung mit MQTT-Dialog, Verbindungen, Rollenwechsel, Sicherheitsfreigaben der PTT. Uhrzeit in UTC |
| `settings.json` | Einstellungen |
| `pairing.key` | Kopplungsschlüssel, verschlüsselt (Windows: mit dem Windows-Benutzer; Linux/Raspberry: an Rechner und Benutzer gebunden). Unter macOS liegt der Schlüssel im Schlüsselbund, Eintrag «IP-TRX-CTRL» |
| `WAV/` | die Meldungsdateien des Keyers |

Unter Windows wird Hamlib nach `%LOCALAPPDATA%\IP-TRX-CTRL\hamlib\<Version>` entpackt.

## Wie man ein Problem meldet

Seit 0.3.0: **Impostazioni › Abbinamento e registri › «Rapporto per un problema…»** (Problembericht).

1. Schreib (wenn du möchtest), was passiert ist.
2. **Salva il rapporto…** (Bericht speichern): eine Textdatei mit Version, System, Rolle, Funkgerät, PTT- und CW-Methode, Sicherheitsoptionen, Panadapter, MQTT-Broker und den letzten Teilen von `errori.log` (200 kB) und `sessioni.log` (400 kB).
3. **Apri la pagina delle segnalazioni** (Meldeseite öffnen): die Seite **Issues** des Repositorys öffnet sich; die Datei anhängen.

Im Bericht **sind öffentliche IP-Adressen maskiert**: jede wird zu `[IP-1]`, `[IP-2]`… (dieselbe Adresse hat immer denselben Namen, damit das Protokoll lesbar bleibt). Sichtbar bleiben nur lokale Adressen (192.168.x.x, 10.x.x.x, 172.16–31.x.x, CGNAT 100.64–127.x.x, 127.x.x.x, 169.254.x.x), die aus dem Internet zu niemandem führen; IPv6-Adressen werden alle maskiert. Der Bericht enthält keine Schlüssel oder Kopplungscodes. Vor dem Anhängen kannst du ihn öffnen und lesen.

Ohne Bericht: Datenordner öffnen und `errori.log` und `sessioni.log` anhängen, mit Angabe von Betriebssystem, Version, Rolle und Funkgerät. `sessioni.log` enthält IP-Adressen und Rufzeichen. Nie die Schlüsseldateien (`pairing-….key`) anhängen.

## Neue Version und automatische Updates

Beim Start und dann alle 6 Stunden fragt das Programm bei GitHub nach der zuletzt veröffentlichten Version (`api.github.com/repos/ic8tem/IP-TRX-CTRL/releases/latest`, ohne Konto).

Seit **0.3.2** aktualisiert sich das Programm selbst, wenn sie neuer ist: es lädt das passende Paket für den PC, prüft Größe und SHA-256-Fingerabdruck (den GitHub für jede Datei veröffentlicht) und startet in der neuen Version neu, **nur wenn keine Sitzung läuft und nicht gesendet wird**. Bei offener Sitzung bleibt die neue Version bereit und wird installiert, sobald sie endet; die Zeile oben sagt es und bietet **Aggiorna adesso** (jetzt aktualisieren). In `sessioni.log` steht jeder Schritt mit dem Präfix `AGGIORNAMENTI`.

Selbst aktualisieren sich: Windows-Setup und portables Zip (seit 0.3.2 macht das Setup den Programmordner dafür beschreibbar), portable tar.gz (Linux, Raspberry Pi, Alpine), AppImage, IP-TRX-CTRL.app auf dem Mac in einem beschreibbaren Ordner. Die Systempakete **.deb, .rpm, Arch und Slackware** aktualisieren sich **nicht** selbst: es erscheint die Zeile der neuen Version mit **Apri la pagina** (Seite öffnen), und das neue Paket wird mit demselben Befehl wie beim ersten Mal installiert.

Automatische Updates schaltet man in **Impostazioni › Finestra principale** ab: die Zeile oben bleibt, mit **Aggiorna adesso**, **Apri la pagina** und **Chiudi** (schließen; für diese Version erscheint sie nicht mehr). Scheitert das Update (Netz, Ordner nicht beschreibbar), läuft das Programm mit der aktuellen Version weiter und versucht es bei der nächsten Prüfung erneut.

## Häufige Probleme

| Symptom | Was prüfen |
|---|---|
| Fenster «Io mi spengo perché non riesco a funzionare» (ich beende mich, weil ich nicht arbeiten kann) mit der Taste Exit (Server) | der Router hat die zwei Ports nicht per UPnP geöffnet: UPnP am Router einschalten und neu starten. Die `PORTE`-Zeilen in `sessioni.log` sagen, welcher Schritt gescheitert ist |
| Die Kopplung bleibt bei «la porta … non risponde» (Port … antwortet nicht) stehen | UPnP am Router des Servers aktiv; nutzt der Anbieter CGNAT, ist der Server aus dem Internet nicht erreichbar |
| «nessun broker disponibile» (kein Broker verfügbar) | Internetverbindung des PCs, Port 1883 nicht vom Netz gesperrt |
| «Hamlib non trovato» (Hamlib nicht gefunden) (Linux, Raspberry, macOS) | `sudo apt install libhamlib-utils` oder `brew install hamlib` |
| Das Gerät antwortet nicht auf CAT | Modell, serielle Schnittstelle und Geschwindigkeit in den Einstellungen; unter Linux/Raspberry muss der Benutzer in der Gruppe `dialout` sein |
| Kein Mikrofonaudio unter macOS | Systemeinstellungen › Datenschutz & Sicherheit › Mikrofon: IP-TRX-CTRL erlauben |
| Server-Audio stummgeschaltet | Bereich «Schede audio del server»: Stummschaltung aufheben oder «Togli il mute da solo» einschalten |


---

[Inhalt](Deutsch) · Andere Sprachen: [Italiano](IT-Registri-e-risoluzione-dei-problemi) · [English](EN-Logs-and-troubleshooting) · [Español](ES-Registros-y-solución-de-problemas) · [Français](FR-Journaux-et-résolution-des-problèmes)
