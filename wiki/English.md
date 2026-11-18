# IP-TRX-CTRL

IP-TRX-CTRL lets you use an HF radio remotely, over the internet, as if it were on your desk. A single program runs on two PCs:

- **server**: the PC connected to the radio (audio, CAT through Hamlib/rigctld, PTT);
- **client**: the operator's PC, which receives the radio's audio, transmits with its own microphone and controls the radio.

The roles are chosen at the first start and can be swapped at any time.

> **Current version: 0.3.5.** The pictures are real screenshots of the program (a client connected to a server with a radio simulated by Hamlib). The program's interface is in Italian: these pages give the English meaning of each label.

## In short

- Two-way audio always at **8 kHz, 16-bit mono**, encrypted with AES-256.
- Ports, keys and reconnections handled automatically; the server is found again even when its IP address changes, through a public MQTT broker.
- CAT control of the radio: frequency, mode, VFO A/B with split, TX power, S-meter, power and SWR readings, SWR protection that blocks transmission.
- A **keyer** built into the client (derived from the WavPTT program): in SSB eight WAV messages on F1…F8, in CW eight text macros sent by the radio.
- In CW mode: a 0–2000 Hz waterfall with the real frequency of the signals, a decoder on the pitch and extra decoding windows for other signals.
- Digital-mode programs on the client (WSJT-X, JTDX, fldigi…) through a local rigctld and virtual audio cables: see [Digital modes, PTT pedal and CW key](EN-Digital-modes-and-pedal).
- PTT pedal and mechanical CW key on a serial port of the client.
- Port cycle between server and client (UPnP checked from outside, reverse connection, 20 rounds) and audio over UDP when possible: see [Ports and connection](EN-Ports-and-connection).
- Since 0.3.0: transmit safety (maximum time, no frequency changes while transmitting, priority to whoever is at the radio, receive-only clients), radio controls (NR, NB, Notch, ATT, preamp, AGC, compressor, squelch, ALC), bands, step and memories, the [radio panadapter](EN-Panadapter), a problem report and new-version notice. See [Radio, split and power](EN-Radio-split-and-power) and [Main window](EN-Main-window).
- Since 0.3.1: [browser access](EN-Browser-access) from phones, tablets and PCs, with the «Trova il server» (find the server) page.
- Since 0.3.2: automatic updates (not for the .deb, .rpm, Arch and Slackware packages) and packages for Windows ARM, Fedora/openSUSE, Arch, Slackware, AppImage and Alpine: see [Logs and troubleshooting](EN-Logs-and-troubleshooting).
- Since 0.3.5: packages for Windows 7 SP1, 8 and 8.1 (`-win7-x64-`, `-win7-x86-`, with .NET 6); at the top of every release, the table with a link to each package.
- Since 0.3.4: ports you choose in Settings (with the list of the two ports to open), double NAT and UPnP from a device that is not the router recognised.
- Since 0.3.3: on Linux, Raspberry Pi and Mac the first-start window, Settings and licenses no longer freeze; SWR and Gain labels visible.
- Six colour themes: Chiaro, Notte, Nero, Blu, Viola, Verde (light, night, black, blue, violet, green): see [Colour themes](EN-Colour-themes).
- A single source code for **Windows 64-bit, 32-bit and ARM, macOS, Linux (Alpine too) and Raspberry Pi**.

Before updating one of the two PCs, check the [Compatible versions](EN-Compatible-versions).

![Client connected, Chiaro theme](https://raw.githubusercontent.com/ic8tem/IP-TRX-CTRL/video-corso/wiki-images/main.png)

## Pages

- [Video course](EN-Video-course)
- [Main window](EN-Main-window)
- [Pairing and MQTT](EN-Pairing-and-MQTT)
- [Ports and connection](EN-Ports-and-connection)
- [Radio, split and power](EN-Radio-split-and-power)
- [Audio and server sound cards](EN-Audio-and-server-sound-cards)
- [Keyer, WAV messages and CW macros](EN-Keyer-WAV-messages-and-CW-macros)
- [CW mode](EN-CW-mode)
- [Panadapter](EN-Panadapter)
- [Digital modes and pedal](EN-Digital-modes-and-pedal)
- [TCI connection](EN-TCI-connection)
- [Browser access](EN-Browser-access)
- [Colour themes](EN-Colour-themes)
- [Logs and troubleshooting](EN-Logs-and-troubleshooting)
- [Compatible versions](EN-Compatible-versions)
- [Platforms and building](EN-Platforms-and-building)
- [Licenses](EN-Licenses)


---

[Contents](English) · Other languages: [Italiano](Italiano) · [Español](Español) · [Français](Français) · [Deutsch](Deutsch)
