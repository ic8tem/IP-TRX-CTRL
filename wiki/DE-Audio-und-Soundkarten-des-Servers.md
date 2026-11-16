# Audio und Soundkarten des Servers

## Audio immer mit 8 kHz

Das gesamte Audio wird mit **8 kHz, 16 Bit mono** verarbeitet:

- das Betriebssystem öffnet Mikrofon und Lautsprecher im gemeinsamen Modus mit der Rate der Karte; das Programm wandelt sofort nach Eintreffen auf 8 kHz und nur für die Ausgabe zurück auf die Rate der Karte;
- Filter, Kompressor, Mischung Mikrofon/Meldungen, Netz und CW-Decoder arbeiten mit 8 kHz;
- die nutzbare Bandbreite reicht bis etwa 4 kHz, genug für SSB und den CW-Wasserfall 0–2000 Hz.

## Soundkarten des Servers vom Client aus wählen

Im Bereich **Schede audio del server** (Soundkarten des Servers) sind die Zeilen *Ingresso* (Eingang) und *Uscita* (Ausgang) zwei Listen der am Server-PC vorhandenen Karten:

- **Ingresso**: die Karte, von der der Server das Audio des Funkgeräts nimmt (Empfang);
- **Uscita**: die Karte, über die der Server Audio zum Funkgerät schickt (Senden).

Wählt man am Client eine Karte, startet der Server sein Audio auf der neuen Karte neu und speichert die Wahl. Lässt sich die Karte nicht öffnen (abgesteckt, belegt, Zugriff verweigert), kehrt der Server sofort zu den vorherigen Karten zurück und der Client sieht einen Hinweis; im `sessioni.log` des Servers bleibt eine `AUDIO`-Zeile. Die Kartenliste des Servers aktualisiert sich alle 30 Sekunden selbst, etwa nach dem Anstecken eines USB-Interfaces.

Dieselbe Liste funktioniert auch am Server: die Karte dort zu ändern ist dasselbe wie im Bereich des lokalen Audios.

Mit CAT über TCI (seit 0.2.5) erscheint in den Listen auch **«TCI · audio della radio»** (TCI · Audio des Funkgeräts): als Ein- und Ausgang gewählt, läuft das Audio über das TCI des Geräteprogramms, ohne Karten oder virtuelle Kabel (siehe *TCI-Verbindung*).

## Stummschaltung der Soundkarten des Servers

Der Client sieht, ob die beiden mit dem Funkgerät verbundenen Soundkarten des Servers (Empfang und Senden) vom Betriebssystem stummgeschaltet sind, samt Systemlautstärke, und kann:

- die Stummschaltung aus der Ferne aufheben;
- die Systemlautstärke einstellen;
- **Togli il mute da solo** (Stummschaltung automatisch aufheben) einschalten: der Server hebt sie auf, sobald jemand sie aktiviert, und meldet es dem Client.

So geschieht es auf jedem System:

| System | Schnittstelle |
|---|---|
| Windows | Core Audio |
| macOS | CoreAudio |
| Linux / Raspberry Pi | PipeWire oder PulseAudio; ALSA, falls sie fehlen |

Grenzen:

- das Serverprogramm muss in der Sitzung des Benutzers laufen, nicht als Dienst;
- verweigert das System den Mikrofonzugriff aus Datenschutzgründen, meldet das Programm es, ändert es aber nicht: einmal am Server von Hand freigeben;
- eine Stummschaltung im USB-Treiber des Funkgeräts ist für das Betriebssystem nicht sichtbar.


---

[Inhalt](Deutsch) · Andere Sprachen: [Italiano](IT-Audio-e-schede-audio-del-server) · [English](EN-Audio-and-server-sound-cards) · [Español](ES-Audio-y-tarjetas-de-sonido-del-servidor) · [Français](FR-Audio-et-cartes-son-du-serveur)
