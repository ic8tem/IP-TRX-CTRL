# Hauptfenster

Seit 0.3.0 ist das Fenster kleiner: es öffnet mit **1080 × 760** Punkten (vorher 1240 × 816) und lässt sich bis 900 × 520 verkleinern; passt der Inhalt nicht hinein, erscheint eine Bildlaufleiste. Es ist so aufgebaut:

1. **Kopfzeile** in einer Zeile: Programmname, Verbindungsstatus, UPnP-LED, Rolle (CLIENT oder SERVER), Farben, **Impostazioni** (Einstellungen), **Inverti ruoli** (Rollen tauschen), **Help**. Darunter, wenn vorhanden, die Zeile der **neuen Version** mit **Aggiorna adesso** (jetzt aktualisieren) (siehe *Protokolle und Fehlersuche*).
2. **Drei Spalten**: Funkgerät · Instrumente und PTT · der andere PC.
3. **Panadapter des Funkgeräts**, falls aktiv (siehe *Panadapter*).
4. **Reiter** unten: **Keyer**, **CW** (nur in CW), **Audio**, **Collegamento** (Verbindung).
5. **Hinweiszeile** (Server belegt, Senden nicht erlaubt, Maximalzeit…).

Die neueren Teile lassen sich unter **Impostazioni › Finestra principale** (Einstellungen › Hauptfenster) ausblenden: Bandtasten und Schrittweite, Gerätebedienung, Speicher, ALC-Anzeige, Panadapter, Prüfung auf neue Version und automatische Updates. So bleibt das Fenster noch niedriger.

## Funkgerät

- **VFO A** groß, mit Betriebsart. **Mausrad über den Ziffern**: mit der Maus über einer Ziffer dreht das Rad sie hoch oder runter, mit Übertrag (auch beim VFO B).
- Feld **Nuova QRG** (neue Frequenz) in Hz, Betriebsart, **Imposta** (setzen).
- **Abstimmschritt** (1 Hz bis 25 kHz) mit den Tasten ◀ ▶ (auch mit dem Mausrad über den Tasten).
- **Bänder** von 160 m bis 70 cm: ein Klick führt zur zuletzt auf diesem Band benutzten Frequenz und Betriebsart (beim ersten Mal zu einer typischen Frequenz). Das aktuelle Band ist hervorgehoben.
- **VFO B** in einer Zeile mit **A/B**, **A=B**, **SPLIT** und der Ablage (−, Wert, +). Split wird im Tooltip der Taste erklärt.
- Sende-**Potenza** (Leistung).
- **Gerätebedienung** (siehe *Funkgerät, Split und Leistung*): NR, NB, NOTCH, COMP, ATT, PRE, AGC, SQL.
- **Memorie** (Speicher) (siehe *Funkgerät, Split und Leistung*).

## Instrumente und PTT

- **S**, **Po** (Ausgangsleistung), **ROS** (SWR), **ALC** (beim Senden), **Gain** (Sendeverstärkung des Geräts). Der SWR-Balken blinkt beim SWR-Schutz.
- **RX/TX**-Anzeige.
- **PTT** «drücken und halten», auch mit der Leertaste. Unter der PTT sagt eine Zeile, wie lange die Sendung schon dauert und die **Maximalzeit**, oder warum Senden **nicht erlaubt** ist (nur Empfang, Operator am Gerät). Ist es nicht erlaubt, ist die Taste blass und zeigt «PTT NON PERMESSO».
- Am Client: am Server eingestellte PTT-Methode und Verzögerung.

## Der andere PC

- Client: Liste der **gekoppelten Server** und **Abbina…** (koppeln).
- Rufzeichen, IP, Verbindungszeit, Latenz, Pegel des empfangenen Audios.
- Einklappbare Bereiche: **Reti dei due PC** (Netze der beiden PCs: IPv4/IPv6, Audioweg, Version des anderen PCs), **Metodo PTT e CW** (PTT- und CW-Methode, nur Server), **Schede audio del server** (Soundkarten des Servers).

## Reiter

- **Keyer**: F1…F8 (WAV in Fonie, Makros in CW). Siehe *Keyer, WAV-Meldungen und CW-Makros*.
- **CW**: erscheint in CW und öffnet sich von selbst; RX-Audio in einer Zeile, Pitch, Breite, Geschwindigkeit, Schwelle, Gerätefilter, Wasserfall und Decoder. F1…F8 funktionieren auch hier. Siehe *CW-Betrieb*.
- **Audio**: Mikrofon und Lautsprecher, Lautstärken, Anhebung und Pegel.
- **Collegamento** (Verbindung): Status, Latenz, Jitter, verlorene Pakete, Puffer, MQTT-Broker und, am Server, der Ersatzcode.


---

[Inhalt](Deutsch) · Andere Sprachen: [Italiano](IT-Finestra-principale) · [English](EN-Main-window) · [Español](ES-Ventana-principal) · [Français](FR-Fenêtre-principale)
