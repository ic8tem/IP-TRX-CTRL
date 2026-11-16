# TCI-Verbindung

Seit 0.2.5 kann der Server das Funkgerät über **TCI** (Transceiver Control Interface) statt über Hamlib steuern. TCI ist das Protokoll von Expert Electronics (ExpertSDR2, ExpertSDR3), das auch Thetis und andere SDR-Programme verwenden: das Programm des Funkgeräts ist WebSocket-Server, meist auf demselben PC, Port **40001**.

## Einstellung

**Impostazioni › Radio (server) › Collegamento CAT** (Einstellungen › Funkgerät › CAT-Verbindung) → **TCI (ExpertSDR, Thetis e altri SDR)**:

- **Indirizzo** (Adresse) des Geräteprogramms (Standard `127.0.0.1`) und **porta TCI** (TCI-Port, Standard `40001`);
- **Audio della radio via TCI** (Geräte-Audio über TCI): Empfangs- und Sendeaudio laufen über TCI, ohne Soundkarten oder virtuelle Kabel am Server.

Im Programm des Funkgeräts den TCI-Server einschalten (ExpertSDR: Options › TCI; Thetis: Setup › Network › TCI).

## Was über TCI läuft

| Funktion | TCI-Befehle |
|---|---|
| Frequenz VFO A und VFO B | `vfo` |
| Betriebsart (USB, LSB, CW, CW-R, AM, FM, DIGU, DIGL…) | `modulation` |
| Filterbreite (breites Filter in CW) | `rx_filter_band` |
| Split | `split_enable` |
| Sendeleistung in % | `drive` |
| PTT («CAT» oder «TCI») | `trx` (mit TCI-Audio: `trx:0,true,tci`) |
| S-Meter | `rx_smeter` / `rx_channel_sensors` |
| Ausgangsleistung und **SWR** (auch für den SWR-Schutz) | `tx_sensors` |
| CW-Text und Geschwindigkeit | `cw_macros`, `cw_macros_stop`, `cw_macros_speed` |
| Audio | Binärströme: Empfang, Senden auf Anforderung (`TX_CHRONO`), mit 8 kHz mono, wenn das Programm es annimmt, sonst umgewandelt |

Der Server erzeugt einen kleinen internen «rigctld», der Hamlib-Befehle in TCI übersetzt: so funktioniert der Rest des Programms wie mit Hamlib, einschließlich der **Digitalprogramme** des Clients (WSJT-X, JTDX, fldigi…). Für Befehle, die das Gerät nicht betreffen (`\dump_state`, `\chk_vfo`), startet der Server einen Test-`rigctld` (Modell 1, «Dummy»); ist Hamlib nicht installiert, funktioniert TCI trotzdem, aber die Digitalprogramme verbinden sich eventuell nicht.

## Grenzen

- Der **CW-Pitch** kommt nicht über TCI: im Programm und im Gerät gleich einstellen.
- Die Leistung wird in **Prozent** eingestellt (TCI liefert die Maximalleistung nicht in Watt).
- SWR und Ausgangsleistung kommen nur, wenn das Geräteprogramm `tx_sensors` sendet (ExpertSDR mit TCI 1.9 oder neuer); ohne sie kann der SWR-Schutz nicht eingreifen.
- Mit TCI-Audio sind beide Karten des Servers «TCI · audio della radio»: man kann nicht eine Richtung über TCI und die andere über eine Karte führen.
- In der Datei `sessioni.log` zeigen die `TCI`-Zeilen, wann die Verbindung aufgeht, bereit ist oder abbricht.

## Quellen

Es gibt keine einheitliche, von hier aus einsehbare öffentliche Spezifikation: das Programm folgt drei Quellen, die miteinander verglichen wurden. Wo sie sich widersprechen, liest es Format, Kanäle und Rate aus dem Kopf jedes Audioblocks, statt sie vorauszusetzen.

| Quelle | Was sie bestätigt | Wo sie übereinstimmt | Wo sie abweicht |
|---|---|---|---|
| [Hamlib TCI Audio Sidechannel Protocol](https://raw.githubusercontent.com/jfrancis42/hamlib-tci-sidecar/HEAD/PROTOCOL.md) | 64-Byte-Audiokopf (16 32-Bit-Ganzzahlen: Empfänger, Rate, Format, Codec, crc, Länge, Typ, Kanäle), Formate 0–3, «Länge» in Samples pro Kanal, Antwort auf `TX_CHRONO` mit genau diesen Samples | Stromtypen 1–3 wie bei JTDX | Protokoll eines an TCI angelehnten Hilfsprogramms für Hamlib, nicht TCI selbst; fügt einen Typ 4 (PTT-Zustand) hinzu, den TCI nicht hat |
| [JTDX, TCI-Schnittstelle](https://deepwiki.com/jtdx-project/jtdx/4.1-tci-transceiver-interface) | Stromtypen 0–3 (IQ, empfangenes Audio, zu sendendes Audio, `TX_CHRONO`), Befehle `vfo`, `modulation`, `trx`, `split_enable`, `drive`, `audio_start`, `audio_samplerate` | Stromtypen wie bei der ersten Quelle; Befehle wie bei der dritten | aus dem JTDX-Code erzeugte Zusammenfassung, ohne Reihenfolge der Kopffelder; nennt typisches Audio mit 48 kHz Stereo float32 |
| [PE5JW, Überblick über das TCI-Protokoll](https://github.com/pe5jw/tci-protocol-overview) | Syntax der Audiobefehle (`audio_stream_sample_type`, `audio_stream_channels`), `trx` mit Quelle `tci`, Sensoren `rx_smeter`, `tx_sensors`, von Thetis und anderen verwendete Aliasse | Befehle und Betriebsarten wie bei JTDX; SWR als 5. Feld von `tx_sensors` | beschreibt einen anderen Kopf (warnt, dass er je nach Programm variiert); für `tx_sensors` nennt sie Vor- und Rücklaufleistung, wo andere Beschreibungen Effektiv- und Spitzenleistung nennen |

Folgen im Programm: von der Zeile `tx_sensors` verwendet es nur das 3. Feld (Ausgangsleistung) und das 5. (SWR), bei denen die Quellen übereinstimmen; Format, Kanäle und Rate des Audios werden aus jedem Block gelesen.


---

[Inhalt](Deutsch) · Andere Sprachen: [Italiano](IT-Collegamento-TCI) · [English](EN-TCI-connection) · [Español](ES-Conexión-TCI) · [Français](FR-Connexion-TCI)
