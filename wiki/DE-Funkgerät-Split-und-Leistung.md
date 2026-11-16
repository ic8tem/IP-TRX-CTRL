# Funkgerät, Split und Leistung

Alle Befehle an das Funkgerät laufen über den Server, der **Hamlib** (`rigctld`) verwendet oder, seit 0.2.5, das **TCI** der SDR-Programme (siehe *TCI-Verbindung*). Beim Verbindungsaufbau erkennt der Server immer das Gerätemodell und welche Befehle es unterstützt.

## VFO A, VFO B und Split

| Befehl | Wirkung |
|---|---|
| **A/B** | tauscht die beiden VFOs |
| **A=B** | kopiert VFO A in VFO B |
| **SPLIT** | Empfang auf A, Senden auf B |

Split wird als **Ablage** von VFO B gegenüber VFO A eingestellt:

- Bereich: von **−50 kHz** bis **+50 kHz**;
- Schritt **1 kHz** in Fonie (SSB);
- Schritt **0,1 kHz** in CW;
- Tasten − und +, Wertfeld; das Mausrad verschiebt um einen Schritt (seit 0.3.0 gibt es keinen Schieberegler mehr, für ein kleineres Fenster).

## Sendeleistung

- Regler von **1 bis 100 W**.
- Unterstützt das Gerät, oder Hamlib für dieses Modell, die Umrechnung in Watt nicht, wechselt der Regler automatisch auf **1–100 %**.
- Der Befehl (Level `RFPOWER`) geht beim Loslassen des Reglers hinaus; nach jeder Änderung wird der Wert vom Gerät zurückgelesen und daneben angezeigt, zusammen mit Modell und Einheit (z. B. «IC-7300 · watt»).

## SWR-Schutz

Seit 0.2.4 überwacht der Server beim Senden das vom Gerät gelesene **SWR**. Der Schutz ist **ab Installation aktiv**.

| SWR | Was passiert |
|---|---|
| ab **1,9** | Warnung: der SWR-Balken blinkt rot, am Server und am Client |
| ab **2,5** bei zwei Messungen hintereinander (etwa eine Sekunde) | Sperre: der Server nimmt die PTT weg, stoppt WAV-Meldungen, CW-Text und -Taste sowie das Audio zum Gerät (auch bei VOX-PTT); am Client erscheint ein Fenster |

Das Senden bleibt gesperrt, bis der Operator im Fenster **«Sblocca la trasmissione»** (Senden freigeben) drückt. Hat er es mit «Lascia bloccata» (gesperrt lassen) geschlossen, öffnet ein Klick auf den SWR-Balken es wieder. Nach der Freigabe muss die PTT erneut gedrückt werden: auch wenn Pedal oder Taste noch gedrückt waren, geht das Gerät nicht von selbst wieder auf Senden.

Vor der Freigabe: Antenne, Kabel und Stecker, Tuner prüfen und ob die Frequenz im Band der Antenne liegt.

**Impostazioni › Radio (server)**:

- Feld **Protezione ROS** (SWR-Schutz) zum Ein- oder Ausschalten (beim Ausschalten fragt das Programm nach Bestätigung);
- **Avviso da ROS** (Warnung ab SWR, Standard 1,9) und **Blocco da ROS** (Sperre ab SWR, Standard 2,5); die Sperre reicht von 1,2 bis 10, die Warnung bleibt unter der Sperre.

Die Einstellungen gelten am Server, der das SWR misst. Vom Client aus lassen sie sich ändern, wenn er mit einem Server 0.2.4 oder neuer verbunden ist: sie werden beim Speichern an den Server geschickt. Jede Sperre, Freigabe und Einstellungsänderung steht in `sessioni.log` mit dem Präfix `ROS`.

Der Schutz funktioniert nur, wenn das Gerät Hamlib das SWR liefert (Level `SWR`); andernfalls bleibt der Balken auf «—» und `sessioni.log` meldet es einmal. Er ersetzt nicht die internen Schutzschaltungen von Gerät und Endstufe.

## Sicherheit beim Senden (seit 0.3.0)

Der Server entscheidet, für alle Clients. **Impostazioni › Sicurezza in trasmissione (server)** (Sicherheit beim Senden):

| Option | Standard | Was sie tut |
|---|---|---|
| **Tempo massimo di una trasmissione** (maximale Sendezeit) | an, 180 s | zählt die vom Programm gesteuerten Sendungen (PTT, WAV-Meldungen, CW-Taste auf serieller Leitung, Digitalprogramme). 15 Sekunden vor dem Ende wird die Zeile unter der PTT rot; bei Ablauf wird die PTT weggenommen und **bleibt weg, bis der Operator sie loslässt**. 30 bis 600 s |
| **Niente cambi di frequenza in trasmissione** (keine Frequenzwechsel beim Senden) | an | während das Gerät sendet, lehnt der Server Wechsel von Frequenz, Betriebsart, Filter, VFO B, Split und A/B ab; am Client erscheint der Hinweis |
| **Precedenza a chi è alla radio** (Vorrang für den Operator am Gerät) | an, 3 Minuten | sendet das Gerät, ohne dass das Programm es befohlen hat (Mikrofon oder PTT am Gerät), oder wird die PTT des Programms am Server gedrückt, kann der Client für die gewählten Minuten (1 bis 30) nicht senden. Die Sperre verlängert sich, solange die lokale Sendung dauert; am Server hebt ein Klick auf die Zeile unter der PTT sie früher auf |
| **Codice fisso in solo ascolto** (fester Code nur Empfang) | an | wer sich mit dem festen Code des Servers koppelt, kommt **nur mit Empfang** herein; der Server gibt ihn frei |

**Nur Empfang**: in **Impostazioni › Abbinamento e registri** wählt der Server für jeden Client **«Può trasmettere»** (darf senden) oder nicht. Ein Client nur mit Empfang hört das Gerät, wechselt die Frequenz und nutzt den Panadapter, sendet aber nicht (PTT, Meldungen, CW, Digitalprogramme), gibt den SWR-Schutz nicht frei, ändert die Speicher des Servers nicht und fordert keinen Rollentausch an.

Hinweise:

- Der Vorrang funktioniert mit Geräten, die per CAT melden, wann sie senden (Hamlib-Befehl `t`). An den Keyer des Geräts geschicktes CW zählt für die geschätzte Dauer des Textes als Sendung des Programms.
- Mit VOX-PTT nimmt die Maximalzeit die PTT des Programms weg, aber das Gerät entscheidet, wann es aufhört zu senden.
- Die Maximalzeit betrifft keine Sendungen, die das Gerät selbst macht (Operator am Gerät).
- Jedes Ereignis steht in `sessioni.log` (Zeilen `TEMPO MASSIMO`, `QSY rifiutato`, `PRECEDENZA`, `TRASMISSIONE remota rifiutata`).

## Gerätebedienung (seit 0.3.0)

Tastenreihe unter der Leistung (ausblendbar unter *Impostazioni › Finestra principale*). Es sind Standardbefehle von Hamlib, sie funktionieren also mit den Geräten, für die Hamlib sie unterstützt:

| Taste | Hamlib-Befehl | Funktion |
|---|---|---|
| **NR** | Funktion `NR` | Rauschminderung |
| **NB** | Funktion `NB` | Störaustaster |
| **NOTCH** | Funktion `ANF` | automatisches Notch |
| **COMP** | Funktion `COMP` | Mikrofonkompressor des Geräts |
| **ATT** | Level `ATT` | Abschwächer: jeder Klick geht zum nächsten Wert, den das Gerät annimmt, dann aus |
| **PRE** | Level `PREAMP` | Vorverstärker, wie der Abschwächer |
| **AGC** | Level `AGC` | schnell, mittel, langsam |
| **SQL** | Level `SQL` | Squelch, 0–100 % |

Eine graue Taste bedeutet, dass das Gerät (oder Hamlib für dieses Modell) den Befehl nicht anbietet. Der Server liest diese Werte alle 4 Sekunden und gleich nach jedem Befehl. Mit **CAT über TCI** funktionieren NR, NB, NOTCH und SQL (TCI-Befehle `rx_nr_enable`, `rx_nb_enable`, `rx_anf_enable`, `sql_enable` und `sql_level`).

## Bänder, Schritt und Speicher (seit 0.3.0)

- **Bänder** von 160 m bis 70 cm: ein Klick führt zur zuletzt auf diesem Band benutzten Frequenz und Betriebsart, beim ersten Mal zu einer typischen Frequenz (z. B. 14,200 MHz USB). Die letzten Frequenzen werden auf diesem PC gespeichert.
- **Abstimmschritt** von 1 Hz bis 25 kHz mit den Tasten ◀ ▶ und dem Mausrad über den Tasten; gilt auch für das Mausrad auf dem Panadapter.
- **Memorie** (Speicher): «Salva…» (speichern) fragt nach dem Namen und wo gespeichert wird: **in questo PC** (auf diesem PC, nur für dich) oder **sul server** (auf dem Server), für alle Clients gemeinsam. In der Liste sind die Speicher mit «PC» oder «Server» markiert; **Vai** (los, oder Doppelklick) ruft sie ab, **Elimina** (löschen) entfernt sie. Am Server werden die Server-Speicher direkt verwaltet. Nur ein Client, der senden darf, ändert die Speicher des Servers.

Quelle der Hamlib-Befehlsnamen: Handbuch von `rigctl`/`rigctld` (Funktionen `NR`, `NB`, `ANF`, `COMP`; Level `ATT`, `PREAMP`, `AGC`, `SQL`, `ALC`), https://hamlib.sourceforge.net/html/rigctl.1.html


---

[Inhalt](Deutsch) · Andere Sprachen: [Italiano](IT-Radio-split-e-potenza) · [English](EN-Radio-split-and-power) · [Español](ES-Radio-split-y-potencia) · [Français](FR-Radio-split-et-puissance)
