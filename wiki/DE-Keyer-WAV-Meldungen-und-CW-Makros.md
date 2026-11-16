# Keyer: WAV-Meldungen und CW-Makros

Der Keyer arbeitet **nur am Client**. Er übernimmt das Programm WavPTT, in C# innerhalb von IP-TRX-CTRL neu geschrieben. Dieselben Tasten F1…F8 wechseln ihre Funktion mit der Betriebsart des Geräts.

Gemeinsame Bedienung: **F1…F8** im Fenster, **Strg+Alt+F1…F8** auch aus anderen Programmen, **Esc** bricht ab.

## Fonie-Betriebsarten (SSB): WAV-Meldungen

- Jede Taste spielt eine WAV-Datei ab; das Audio geht über den Server zum entfernten Gerät, als würde man ins Mikrofon sprechen, und die PTT wird automatisch gesteuert.
- Die Dateien werden beim Laden auf 8 kHz umgewandelt.
- Audiokette: Umwandlung auf 8 kHz → **Normalisierung** (Spitze bei −1 dB unter Vollaussteuerung, damit alle Dateien vom gleichen Pegel starten) → Kompressor (aus oder 3 bis 13 dB) → Filter (keins, 300–2700, 500–2400, 500–2100 Hz) → Anhebung durch den VOLUME-Regler → Begrenzer.
- Anhebung des VOLUME-Reglers (wie WavPTT): Verstärkung = 1000^(Lautstärke/100).

| Regler | Verstärkung |
|---|---|
| 0 | Stille |
| 33 | ×10 (+20 dB) |
| 50 | ×31,6 (+30 dB) |
| 66 | ×100 (+40 dB) |
| 100 | ×1000 (+60 dB) |

- Das Mikrofon läuft nicht durch diese Kette: TX gain und «Boost oltre soglia» (Anhebung über der Schwelle) gelten nur für die Stimme.
- Mehrere nacheinander gedrückte Tasten werden eingereiht; die PTT wird am Ende der letzten Meldung gelöst.
- Die Taste **File WAV…** öffnet das Fenster **Memorie WAV (F1 – F8)** (WAV-Speicher), übernommen aus WavPTT:
  - **…** wählt die Datei für diese Taste;
  - **Play** / **Stop** Probehören am lokalen Audioausgang, ohne PTT;
  - **Salva** (speichern) kopiert die Dateien in den WAV-Ordner neben dem Programm; **Annulla** (abbrechen) schließt ohne zu speichern.

## CW-Betrieb: Textmakros

- Beim Drücken von F1…F8 ersetzt der Client die Variablen und **schickt den Makrotext als Zeichenkette an den Server**, zusammen mit der gewählten Geschwindigkeit.
- Der Server sendet ihn auf eine von zwei Arten, gewählt im PTT-Bereich des Servers unter **Invio CW** (CW-Ausgabe):
  - **CAT**: der Text geht an den internen Keyer des Geräts (Hamlib-Befehl `send_morse`, Geschwindigkeit mit `KEYSPD`);
  - **Tastung auf der DTR- oder RTS-Leitung** einer seriellen Schnittstelle: der Server setzt die PTT auf dem angegebenen Port, wartet die PTT-Verzögerung ab,
    tastet das CW auf der gewählten Leitung mit Morse-Zeiten bei der empfangenen Geschwindigkeit, wartet 3 Punkte und löst die PTT.
    Mehrere Makros hintereinander bleiben unter derselben PTT. Esc bricht sofort ab: Taste hoch und PTT gelöst.
- Die Taste **Macro…** öffnet das Fenster **Macro CW (F1 – F8)**: für jede Taste der angezeigte Name und der zu sendende Text. **Prova** (Probe) spielt den Text lokal ab, ohne zu senden.
- In den Texten verwendbare Variablen:

| Variable | Bedeutung |
|---|---|
| `{MYCALL}` | mein Rufzeichen |
| `{CALL}` | das Rufzeichen der Gegenstation |
| `{NR}` | laufende Nummer |
| `{RST}` | Rapport |

- Unter den Tasten stellt man Sendegeschwindigkeit, eigenes Rufzeichen, Rufzeichen der Gegenstation und Nummer ein.

### Serielle Schnittstellen für PTT und CW (Server)

| Einstellung | Wirkung |
|---|---|
| PTT auf RTS oder DTR, Port X | der Server steuert die Leitung von Port X |
| CW auf DTR oder RTS, Port Y (oder «gleicher Port wie die PTT») | der Server tastet CW auf der Leitung von Port Y |
| PTT und CW auf **demselben Port** | sie nutzen entgegengesetzte Leitungen: ist CW auf DTR, geht PTT auf RTS, und umgekehrt |
| PTT-Port gleich dem CAT-Port | die PTT steuert rigctld (der Port gehört ihm) |
| CW-Port gleich dem CAT-Port | dort kann kein CW getastet werden: CW geht über CAT. Die zweite serielle Schnittstelle des Geräts nutzen (etwa den «Standard»-Port der Yaesu) oder einen USB-Seriell-Adapter |

Die tatsächliche Wahl und eventuelle Leitungswechsel werden unter den Einstellungen angezeigt und in `sessioni.log` geschrieben (Zeilen `PTT/CW`).

## Am Server

Der Server hat keine F-Tasten und keine WAV-Dateien: er hält die PTT-Methode zum Gerät (RTS, DTR, CAT, VOX oder TCI), die PTT-Verzögerung und die CW-Ausgabe (CAT oder Tastung auf DTR/RTS).


---

[Inhalt](Deutsch) · Andere Sprachen: [Italiano](IT-Keyer-messaggi-WAV-e-macro-CW) · [English](EN-Keyer-WAV-messages-and-CW-macros) · [Español](ES-Keyer-mensajes-WAV-y-macros-CW) · [Français](FR-Keyer-messages-WAV-et-macros-CW)
