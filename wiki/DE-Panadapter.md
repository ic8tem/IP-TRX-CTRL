# Panadapter des Funkgeräts

Seit 0.3.0 kann das Fenster das **echte Spektrum des Funkgeräts** zeigen (Panadapter), nicht das des Audios wie der CW-Wasserfall: oben das Spektrum, darunter der Wasserfall, unten die kHz-Skala. Die **rote Linie** ist die Empfangsfrequenz, das **helle Band** das Filter.

- **Klick** auf einen Punkt: dort abstimmen (gerundet auf den Abstimmschritt, höchstens 1 kHz).
- **Mausrad**: ein Abstimmschritt.
- **Intensità** (Intensität): Kontrast des Wasserfalls.

## Einschalten

Er wird **wahlweise** an zwei Stellen eingeschaltet:

1. **Am Server**: *Impostazioni › Panadapter della radio (server)* › Feld **«Panadapter della radio per i client e per questa finestra»** (Panadapter für die Clients und für dieses Fenster; nach der Installation aus).
2. **An jedem PC**: *Impostazioni › Finestra principale* › **«Panadapter della radio»** (standardmäßig an: der Bereich erscheint nur, wenn der Server ihn liefert).

Der Server schickt das Spektrum nur, solange der Client es anzeigt: höchstens 10 Zeilen pro Sekunde, etwa 5 kB/s.

## Woher das Spektrum kommt

| Funkgerät | Quelle | Was nötig ist |
|---|---|---|
| **SDR mit CAT über TCI** (ExpertSDR, Thetis…) | IQ-Strom von Empfänger 0 (`iq_start`, 48 000 Samples/s), mit einer 2048-Punkte-FFT um die Panoramamitte (`dds`) in ein Spektrum umgewandelt | nichts: CAT über TCI genügt |
| **Icom** mit Scope (IC-7300, IC-705, IC-9700, IC-7610…) | Wasserfalldaten des Geräts, CI-V-Befehl `27 00` | ein CI-V-Port, **den Hamlib nicht benutzt**: etwa der zweite USB-Port der Geräte, die ihn als CI-V anbieten (im Gerätemenü einstellen), oder ein separates CI-V-Interface. In den Einstellungen: Port, Geschwindigkeit und **CI-V-Adresse** des Geräts hexadezimal (IC-7300 `94`, IC-705 `A4`, IC-9700 `A2`, IC-7610 `98`: im Gerätemenü prüfen) |

Mit dem Icom-Port schaltet das Programm das Scope (`27 10 01`) und die Ausgabe der Wasserfalldaten (`27 11 01`) ein und beim Beenden wieder aus. Ein Scope im Modus **Mitte** zeigt Mitte ± Spanne, im Modus **fest** die beiden Ränder. Liegt die Frequenz außerhalb des festen Bereichs, sagt es der Bereich.

Mit anderen Marken (Yaesu, Kenwood, Elecraft…) gibt es den Panadapter vorerst nur mit TCI.

**Neue Funktion, noch nicht mit allen Geräten erprobt**: bleibt der Bereich auf «In attesa dei dati» (warte auf Daten), sagen die Statuszeile neben dem Titel und `sessioni.log` (Zeilen `PANADAPTER`), was fehlt (Port, Geschwindigkeit, Adresse).

## Quellen

- Icom, *CI-V Reference Guide* IC-9700 und IC-7300MK2: Befehl `27 00` (Wasserfalldaten: Haupt- oder Neben-Scope, Teilung und Zahl der Teilungen, Modus Mitte oder fest, Frequenzen, außerhalb des Bereichs, Pegel 0 bis 160), `27 10` (Scope ein/aus), `27 11` (Datenausgabe). Miteinander verglichen: sie stimmen im Format überein; laut Handbuch kommen die Daten über USB in 11 Teilen, über LAN auf einmal.
  https://www.icomfrance.com/uploads/files/produit/not-IC-9700_ENG_CI-V_1-en.pdf
  https://icomuk.co.uk/files/icom/PDF/productAdditionalFile/IC-7300MK2_ENG_CI-V_0.pdf
- TCI-Bibliothek `github.com/ftl/tci` (Go): Befehle `iq_start`, `iq_stop`, `iq_samplerate` (48, 96, 192 kHz), `dds`, binärer Strom vom Typ 0 = IQ.
  https://pkg.go.dev/github.com/ftl/tci/client


---

[Inhalt](Deutsch) · Andere Sprachen: [Italiano](IT-Panadapter) · [English](EN-Panadapter) · [Español](ES-Panadaptador) · [Français](FR-Panadapter)
