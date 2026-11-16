# Keyer: WAV messages and CW macros

The keyer works **on the client only**. It takes over the WavPTT program, rewritten in C# inside IP-TRX-CTRL. The same F1…F8 keys change function with the radio's mode.

Common commands: **F1…F8** in the window, **Ctrl+Alt+F1…F8** from other programs too, **Esc** stops.

## Phone modes (SSB): WAV messages

- Each key plays a WAV file; the audio goes to the remote radio through the server, as if you were speaking into the microphone, and PTT is handled automatically.
- Files are converted to 8 kHz when loaded.
- Audio chain: conversion to 8 kHz → **normalisation** (peak at −1 dB below full scale, so every file starts from the same level) → compressor (off or from 3 to 13 dB) → filter (none, 300–2700, 500–2400, 500–2100 Hz) → boost from the VOLUME slider → limiter.
- VOLUME slider boost (as in WavPTT): gain = 1000^(volume/100).

| Slider | Gain |
|---|---|
| 0 | silence |
| 33 | ×10 (+20 dB) |
| 50 | ×31.6 (+30 dB) |
| 66 | ×100 (+40 dB) |
| 100 | ×1000 (+60 dB) |

- The microphone does not go through this chain: TX gain and «Boost oltre soglia» (boost above threshold) apply only to the voice.
- Several keys pressed in sequence are queued; PTT is released at the end of the last message.
- The **File WAV…** button opens the **Memorie WAV (F1 – F8)** (WAV memories) window, taken from WavPTT:
  - **…** chooses the file for that key;
  - **Play** / **Stop** test listening on the local audio output, without PTT;
  - **Salva** (save) copies the files into the WAV folder next to the program; **Annulla** (cancel) closes without saving.

## CW mode: text macros

- When you press F1…F8 the client replaces the variables and **forwards the macro text to the server as a string**, together with the chosen speed.
- The server transmits it in one of two ways, chosen in the server's PTT box under **Invio CW** (CW sending):
  - **CAT**: the text goes to the radio's internal keyer (Hamlib command `send_morse`, speed with `KEYSPD`);
  - **key on the DTR or RTS line** of a serial port: the server raises PTT on the given port, waits for the PTT delay,
    keys the CW on the chosen line with Morse timing at the received speed, waits 3 dots and releases PTT.
    Several macros in a row stay under the same PTT. Esc stops at once: key up and PTT released.
- The **Macro…** button opens the **Macro CW (F1 – F8)** window: for each key, the name shown on the key and the text to transmit. **Prova** (test) plays the text locally, without transmitting.
- Variables usable in the texts:

| Variable | Meaning |
|---|---|
| `{MYCALL}` | my callsign |
| `{CALL}` | the other station's callsign |
| `{NR}` | serial number |
| `{RST}` | report |

- Under the keys you set the sending speed, my callsign, the other station's callsign and the number.

### Serial ports for PTT and CW (server)

| Setting | Effect |
|---|---|
| PTT on RTS or DTR, port X | the server drives the line of port X |
| CW on DTR or RTS, port Y (or «same port as PTT») | the server keys CW on the line of port Y |
| PTT and CW on the **same port** | they use opposite lines: if CW is on DTR, PTT goes on RTS, and vice versa |
| PTT port the same as the CAT port | PTT is driven by rigctld (the port is its own) |
| CW port the same as the CAT port | CW cannot be keyed there: CW goes via CAT. Use the radio's second serial port (for example the «Standard» port on Yaesu rigs) or a USB-serial adapter |

The actual choices and any line moves are shown under the settings and written in `sessioni.log` (`PTT/CW` lines).

## On the server

The server has no F keys or WAV files: it keeps the PTT method towards the radio (RTS, DTR, CAT, VOX or TCI), the PTT delay and the CW sending method (CAT or key on DTR/RTS).


---

[Contents](English) · Other languages: [Italiano](IT-Keyer-messaggi-WAV-e-macro-CW) · [Español](ES-Keyer-mensajes-WAV-y-macros-CW) · [Français](FR-Keyer-messages-WAV-et-macros-CW) · [Deutsch](DE-Keyer-WAV-Meldungen-und-CW-Makros)
