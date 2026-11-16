# Main window

Since 0.3.0 the window is smaller: it opens at **1080 × 760** points (previously 1240 × 816) and can be shrunk down to 900 × 520; if the content does not fit, a scroll bar appears. It is organised like this:

1. **Header** on one line: program name, connection state, UPnP LED, role (CLIENT or SERVER), colours, **Impostazioni** (settings), **Inverti ruoli** (swap roles), **Help**. Below it, when there is one, the **new version** line with **Aggiorna adesso** (update now) (see *Logs and troubleshooting*).
2. **Three columns**: radio · meters and PTT · the other PC.
3. **Radio panadapter**, if enabled (see *Panadapter*).
4. **Tabs** at the bottom: **Keyer**, **CW** (only in CW mode), **Audio**, **Collegamento** (connection).
5. **Notice** line (server busy, transmission not allowed, maximum time…).

The newer parts can be hidden from **Impostazioni › Finestra principale** (settings › main window): band buttons and step, radio controls, memories, ALC meter, panadapter, new-version check and automatic updates. This keeps the window even shorter.

## Radio

- **VFO A** in large digits, with the mode. **Wheel over the digits**: with the mouse over a digit, the wheel moves it up or down, with carry (VFO B too).
- **Nuova QRG** (new frequency) box in Hz, mode, **Imposta** (set).
- **Tuning step** (1 Hz to 25 kHz) with the ◀ ▶ buttons (the wheel over the buttons works too).
- **Bands** from 160 m to 70 cm: one click goes to the last frequency and mode used on that band (the first time, to a typical frequency). The band in use is highlighted.
- **VFO B** on one line with **A/B**, **A=B**, **SPLIT** and the offset (−, value, +). Split is explained in the button's tooltip.
- TX **Potenza** (power).
- **Radio controls** (see *Radio, split and power*): NR, NB, NOTCH, COMP, ATT, PRE, AGC, SQL.
- **Memorie** (memories) (see *Radio, split and power*).

## Meters and PTT

- **S**, **Po** (output power), **ROS** (SWR), **ALC** (in transmit), **Gain** (the radio's TX gain). The SWR bar flashes with SWR protection.
- **RX/TX** indicator.
- **Press-and-hold PTT**, with the space bar too. Under the PTT a line says how long the transmission has lasted and the **maximum time**, or why transmission is **not allowed** (receive only, operator at the radio). When it is not allowed the button is faded and reads «PTT NON PERMESSO».
- On the client: PTT method and delay set on the server.

## The other PC

- Client: list of **paired servers** and **Abbina…** (pair).
- Callsign, IP, connection time, latency, level of the received audio.
- Collapsible boxes: **Reti dei due PC** (networks of the two PCs: IPv4/IPv6, audio path, version of the other PC), **Metodo PTT e CW** (PTT and CW method, server only), **Schede audio del server** (server's sound cards).

## Tabs

- **Keyer**: F1…F8 (WAV in phone, macros in CW). See *Keyer, WAV messages and CW macros*.
- **CW**: appears in CW mode and opens by itself; RX audio on one line, pitch, width, speed, threshold, radio filter, waterfall and decoder. F1…F8 work from here too. See *CW mode*.
- **Audio**: microphone and speaker, volumes, boost and levels.
- **Collegamento** (connection): state, latency, jitter, lost packets, buffer, MQTT broker and, on the server, the backup code.


---

[Contents](English) · Other languages: [Italiano](IT-Finestra-principale) · [Español](ES-Ventana-principal) · [Français](FR-Fenêtre-principale) · [Deutsch](DE-Hauptfenster)
