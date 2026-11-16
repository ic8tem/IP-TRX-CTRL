# Digitalbetrieb, PTT-Pedal und CW-Taste

Diese Funktionen betreffen den **Client** (den PC des Operators).

## Digitalprogramme (WSJT-X, JTDX, JS8Call, fldigi…)

Der Client bietet den Digitalprogrammen zwei Dinge:

- **einen lokalen rigctld** auf `127.0.0.1:4532`: jeder CAT-Befehl reist verschlüsselt bis zum rigctld des Servers. PTT-Befehle werden nicht weitergeleitet: sie werden zur PTT von IP-TRX-CTRL, mit denselben Sicherheiten wie die PTT-Taste;
- **zwei Audiowege** über virtuelle Kabel: der Empfang des Geräts geht zum Programm, und während der vom Programm angeforderten Sendung geht das Audio des Programms statt des Mikrofons zum Gerät.

Einschalten unter Impostazioni › **Programmi digitali** (Digitalprogramme) › «Usa programmi digitali» (Digitalprogramme verwenden).

### Einstellungen im Digitalprogramm

| Eintrag | Wert |
|---|---|
| Funkgerät | Hamlib **NET rigctl** |
| Adresse | `127.0.0.1:4532` (der Port lässt sich in den Einstellungen von IP-TRX-CTRL ändern) |
| PTT | CAT |
| Audioeingang | das Kabel, das IP-TRX-CTRL als «verso il programma» (zum Programm) nutzt |
| Audioausgang | das Kabel, das IP-TRX-CTRL als «dal programma» (vom Programm) nutzt |

### Virtuelle Audiokabel je System

| System | Kabel | Hinweise |
|---|---|---|
| Windows 64 Bit | **VB-CABLE** (VB-Audio, Donationware) | Der Installer bietet es als optionale Aufgabe an, lädt es von der Herstellerseite und überspringt es, wenn es schon installiert ist. Nach der Installation ist ein Neustart nötig. |
| Windows 32 Bit | — | Nicht verfügbar. |
| macOS | **BlackHole 2ch** (MIT) | `brew install blackhole-2ch`, dann IP-TRX-CTRL neu starten. |
| Linux / Raspberry Pi | vom Programm erzeugt | Zwei «Null-Sinks» `IPTRX_RX` und `IPTRX_TX` mit `pactl` (PipeWire oder PulseAudio, Paket `pulseaudio-utils`). Im Digitalprogramm: Eingang «Monitor of IP-TRX-CTRL_RX», Ausgang «IP-TRX-CTRL_TX». |

**Ein Kabel** (kostenloses VB-CABLE): IP-TRX-CTRL schreibt den Empfang auf «CABLE Input» und liest von «CABLE Output»; das Digitalprogramm macht dasselbe. Es arbeitet **halbduplex**: während das Programm sendet, schickt IP-TRX-CTRL ihm keinen Empfang. **Zwei Kabel** (zum Beispiel VB-CABLE A+B): eines pro Richtung; die Taste «Scegli i cavi in automatico» (Kabel automatisch wählen) weist sie zu.

## PTT-Pedal und mechanische CW-Taste

Impostazioni › **Pedale PTT e tasto CW** (PTT-Pedal und CW-Taste). Eine serielle Schnittstelle des Clients (auch ein USB-Seriell-Adapter) dient als Eingang:

| Kontakt | Verdrahtung | Zu wählender Eingang |
|---|---|---|
| PTT-Pedal | zwischen **RTS** (Pin 7 der DB9) und **CTS** (Pin 8) | CTS |
| Handtaste | zwischen **DTR** (Pin 4) und **DSR** (Pin 6) | DSR |
| alternativ | zwischen DTR (Pin 4) und **DCD** (Pin 1) | DCD |

Das Programm setzt RTS und DTR und liest die Eingänge jede Millisekunde. Das Pedal wirkt wie die PTT-Taste. Die Taste schickt dem Server jedes Drücken und Loslassen mit Zeitstempel; der Server wiederholt sie am CW-Port des Geräts mit einer **festen Verzögerung** (standardmäßig 250 ms, einstellbar), die die Laufzeitschwankungen des Internets auffängt, so bleiben die Längen von Punkten und Strichen exakt. Die PTT des Geräts geht vor dem ersten Zeichen an und wird nach 800 ms ohne Zeichen gelöst; eine länger als 10 Sekunden gedrückte Taste wird sicherheitshalber gelöst.

**Virtueller Port eines anderen Programms.** Statt Hardware kann man den virtuellen Port eines Programms nutzen, das PTT oder CW über RTS und DTR steuert. Unter Windows braucht man ein Paar virtueller Ports **com0com**: das Programm nutzt einen Port des Paars, IP-TRX-CTRL den anderen (RTS kommt auf CTS an, DTR auf DSR). Unter Linux und macOS haben Pseudo-Ports (pty) keine Steuerleitungen: ein echter Adapter ist nötig.


---

[Inhalt](Deutsch) · Andere Sprachen: [Italiano](IT-Programmi-digitali-e-pedale) · [English](EN-Digital-modes-and-pedal) · [Español](ES-Modos-digitales-y-pedal) · [Français](FR-Modes-numériques-et-pédale)
