# New in 0.3.6

## Opus audio

Between two programs at 0.3.6 or later the audio is compressed with **Opus** (16 kbit/s, voice at 8 kHz) instead of PCM: less bandwidth, forward error correction (FEC) and gap filling when a packet is lost. With older versions and with the web page the audio stays 8 kHz PCM. Nothing to set.

## More listeners on the same server

**Impostazioni › Sicurezza in trasmissione (server) › «Altri client e pagine web possono collegarsi in solo ascolto»** (Settings › Transmit safety (server) › "Other clients and web pages may connect listen-only"; on by default). While one operator uses the radio, up to **8** other paired clients or web pages can still connect: they hear the radio and see frequency and meters, but cannot transmit or change anything. Their window shows "tu sei in solo ascolto: trasmette X" (you are listening only: X is transmitting); on the server the other-PC panel shows how many are listening. When the operator disconnects, the listeners are disconnected and the first one to reconnect becomes the operator.

## USB and MIDI CW key

**Impostazioni › Tasto CW USB e MIDI** (Settings › USB and MIDI CW key, client):

- **Keyboard mode** (VBand, Vail and similar): left Ctrl = dit, right Ctrl = dah.
- **Iambic keyer mode A or B**, or **straight key**; speed 5–60 WPM; swap dit/dah; sidetone on the PC at the CW pitch.
- **MIDI** (Vail in MIDI mode, TinyMIDI): C = straight key, C# = dit, D = dah. Windows and Linux (the user must be in the `audio` group), not on the Mac.

The keyer runs on the client and sends the key timing to the server, which repeats it with the fixed CW key delay (like the mechanical key on a serial port, see [Digital modes and pedal](EN-Digital-modes-and-pedal)).

The **web page** has the key too: Settings › Tasto CW, with keyboard mode (Ctrl or `[` and `]`) and MIDI in Chrome or Edge.

## QSO log

**«Registro»** (Log) button at the top of the main window: call, RST sent and received, locator, name, QTH and notes. Frequency, band, mode and times come from the radio.

- **Registra** (Log it) saves the QSO in `registro.adi`, in the settings folder, and if enabled sends it to logging programs with the **WSJT-X UDP protocol** ("QSO Logged" and "Logged ADIF" messages, port 2237 by default): GridTracker, JTAlert, Log4OM, N1MM, HRD and others log it as a WSJT-X QSO.
- **Esporta ADIF…** (Export ADIF) saves a file for **LoTW** (TQSL), QRZ and Club Log.
- **Impostazioni › Registro dei QSO** (Settings › QSO log): address, port and your locator. If WSJT-X runs on the same PC, add a second port in the logging program or use multicast forwarding.

## Icom radios over the network

For the IC-7610, IC-9700, IC-705, IC-7300MK2, IC-905 and other networked Icoms: the server talks to the radio like RS-BA1 and wfview, **with no USB cables and no PC next to the radio**.

1. On the radio: Set › Network › **Network Control ON** and a user in Network User1 (name and password, up to 16 characters).
2. **Impostazioni › Radio Icom in rete (server)** (Settings › Networked Icom radio): radio address, port 50001, user and password (kept encrypted on that PC).
3. In **Radio (server)**: the radio's Hamlib model and PTT "CAT".
4. Save, reopen Settings and pick **"Icom LAN · audio della radio"** as the server sound card (receive and transmit at 8 kHz).

CAT goes through rigctld, connected to an internal bridge to the radio's CI-V stream. The protocol is rewritten in C# following [wfview](https://gitlab.com/eliggett/wfview) (GPL 3) and the [kappanhang](https://github.com/nonoo/kappanhang) documentation; see [Licenses](EN-Licenses).

> New feature, so far tested without a real radio: if something goes wrong, attach the problem report (ICOM lines in `sessioni.log`).


---

[Contents](English) · Other languages: [Italiano](IT-Funzioni-nuove-della-0.3.6) · [Español](ES-Novedades-de-la-0.3.6) · [Français](FR-Nouveautés-de-la-0.3.6) · [Deutsch](DE-Neu-in-0.3.6)
