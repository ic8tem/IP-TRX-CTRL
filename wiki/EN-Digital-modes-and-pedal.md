# Digital modes, PTT pedal and CW key

These functions concern the **client** (the operator's PC).

## Digital-mode programs (WSJT-X, JTDX, JS8Call, fldigi…)

The client offers digital-mode programs two things:

- **a local rigctld** on `127.0.0.1:4532`: every CAT command travels encrypted to the server's rigctld. PTT commands are not forwarded: they become IP-TRX-CTRL's PTT, with the same safety as the PTT button;
- **two audio paths** on virtual cables: the radio's receive audio goes to the program, and while the program asks to transmit, the program's audio goes to the radio instead of the microphone.

Enable it from Impostazioni › **Programmi digitali** (digital programs) › «Usa programmi digitali» (use digital programs).

### Settings in the digital-mode program

| Item | Value |
|---|---|
| Radio | Hamlib **NET rigctl** |
| Address | `127.0.0.1:4532` (the port can be changed in IP-TRX-CTRL's settings) |
| PTT | CAT |
| Audio input | the cable IP-TRX-CTRL uses as «verso il programma» (towards the program) |
| Audio output | the cable IP-TRX-CTRL uses as «dal programma» (from the program) |

### Virtual audio cables per system

| System | Cable | Notes |
|---|---|---|
| Windows 64-bit | **VB-CABLE** (VB-Audio, donationware) | The installer offers it as an optional task, downloads it from the maker's website and skips it if it is already installed. A restart is needed after installing. |
| Windows 32-bit | — | Not available. |
| macOS | **BlackHole 2ch** (MIT) | `brew install blackhole-2ch`, then restart IP-TRX-CTRL. |
| Linux / Raspberry Pi | created by the program | Two «null sinks» `IPTRX_RX` and `IPTRX_TX` with `pactl` (PipeWire or PulseAudio, package `pulseaudio-utils`). In the digital program: input «Monitor of IP-TRX-CTRL_RX», output «IP-TRX-CTRL_TX». |

**One cable** (free VB-CABLE): IP-TRX-CTRL writes receive audio to «CABLE Input» and reads from «CABLE Output»; the digital program does the same. It works **half-duplex**: while the program transmits, IP-TRX-CTRL stops sending it receive audio. **Two cables** (for example VB-CABLE A+B): one per direction; the «Scegli i cavi in automatico» (choose cables automatically) button assigns them.

## PTT pedal and mechanical CW key

Impostazioni › **Pedale PTT e tasto CW** (PTT pedal and CW key). A serial port of the client (a USB-serial adapter too) is used as input:

| Contact | Wiring | Input to choose |
|---|---|---|
| PTT pedal | between **RTS** (pin 7 of the DB9) and **CTS** (pin 8) | CTS |
| Straight key | between **DTR** (pin 4) and **DSR** (pin 6) | DSR |
| alternatively | between DTR (pin 4) and **DCD** (pin 1) | DCD |

The program raises RTS and DTR and reads the inputs every millisecond. The pedal acts like the PTT button. The key sends the server each press and release with its time; the server repeats them on the radio's CW port with a **fixed delay** (250 ms by default, adjustable) that absorbs the internet's delay variations, so the lengths of dots and dashes stay exact. The radio's PTT goes on before the first element and is released after 800 ms without elements; a key held down for more than 10 seconds is released for safety.

**Another program's virtual port.** Instead of hardware you can use the virtual port of a program that drives PTT or CW with RTS and DTR. On Windows you need a **com0com** pair of virtual ports: the program uses one port of the pair, IP-TRX-CTRL the other (RTS arrives on CTS, DTR on DSR). On Linux and macOS pseudo-ports (pty) have no control lines: a real adapter is needed.


---

[Contents](English) · Other languages: [Italiano](IT-Programmi-digitali-e-pedale) · [Español](ES-Modos-digitales-y-pedal) · [Français](FR-Modes-numériques-et-pédale) · [Deutsch](DE-Digitalbetrieb-und-Pedal)
