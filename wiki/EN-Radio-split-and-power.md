# Radio, split and power

Every command to the radio goes through the server, which uses **Hamlib** (`rigctld`) or, since 0.2.5, the **TCI** of SDR programs (see *TCI connection*). When the link starts, the server always recognises the radio model and which commands it supports.

## VFO A, VFO B and split

| Command | Effect |
|---|---|
| **A/B** | swaps the two VFOs |
| **A=B** | copies VFO A into VFO B |
| **SPLIT** | receive on A, transmit on B |

Split is set as the **offset** of VFO B from VFO A:

- range: from **−50 kHz** to **+50 kHz**;
- **1 kHz** step in phone modes (SSB);
- **0.1 kHz** step in CW;
- − and + buttons, value box; the mouse wheel moves one step (since 0.3.0 there is no slider, for a smaller window).

## TX power

- Slider from **1 to 100 W**.
- If the radio, or Hamlib for that model, does not support conversion to watts, the slider switches automatically to **1–100 %**.
- The command (level `RFPOWER`) is sent when the slider is released; after each change the value is read back from the radio and shown next to it, together with the model and the unit used (e.g. «IC-7300 · watt»).

## SWR protection

Since 0.2.4 the server checks the **SWR** read from the radio while transmitting. The protection is **on from installation**.

| SWR | What happens |
|---|---|
| from **1.9** | warning: the SWR bar flashes red, on the server and on the client |
| from **2.5** for two readings in a row (about one second) | trip: the server drops PTT, stops WAV messages, CW text and key and the audio towards the radio (with VOX PTT too); a window appears on the client |

Transmission stays blocked until the operator presses **«Sblocca la trasmissione»** (unlock transmission) in the window. If they closed it with «Lascia bloccata» (leave blocked), a click on the SWR bar reopens it. After unlocking, PTT must be pressed again: even if the pedal or button were still held, the radio does not go back to transmit by itself.

Before unlocking: check antenna, cable and connectors, tuner, and that the frequency is within the antenna's band.

**Impostazioni › Radio (server)**:

- **Protezione ROS** (SWR protection) box to turn it on or off (when turning it off the program asks for confirmation);
- **Avviso da ROS** (warning from SWR, default 1.9) and **Blocco da ROS** (trip from SWR, default 2.5); the trip ranges from 1.2 to 10, the warning stays below the trip.

The settings apply on the server, which measures SWR. From the client they can be changed when connected to a server 0.2.4 or later: they are sent to the server when saved. Every trip, unlock and settings change is written in `sessioni.log` with the prefix `ROS`.

The protection works only if the radio provides SWR to Hamlib (level `SWR`); if it does not, the bar stays on «—» and `sessioni.log` reports it once. It does not replace the internal protections of the radio and the amplifier.

## Transmit safety (since 0.3.0)

The server decides, for all clients. **Impostazioni › Sicurezza in trasmissione (server)** (transmit safety):

| Option | Default | What it does |
|---|---|---|
| **Tempo massimo di una trasmissione** (maximum transmission time) | on, 180 s | counts transmissions driven by the program (PTT, WAV messages, CW key on a serial line, digital programs). 15 seconds before the end the line under the PTT turns red; when time runs out PTT is dropped and **stays dropped until the operator releases it**. From 30 to 600 s |
| **Niente cambi di frequenza in trasmissione** (no frequency changes while transmitting) | on | while the radio transmits, the server refuses changes of frequency, mode, filter, VFO B, split and A/B; a notice appears on the client |
| **Precedenza a chi è alla radio** (priority to whoever is at the radio) | on, 3 minutes | if the radio transmits without the program having commanded it (microphone or the radio's PTT), or if the program's PTT is pressed on the server, the client cannot transmit for the chosen minutes (1 to 30). The block lengthens while the local transmission continues; on the server a click on the line under the PTT removes it earlier |
| **Codice fisso in solo ascolto** (fixed code receive only) | on | whoever pairs with the server's fixed code enters as **receive only**; the server enables them |

**Receive only**: in **Impostazioni › Abbinamento e registri** the server chooses for each client **«Può trasmettere»** (may transmit) or not. A receive-only client hears the radio, changes frequency and uses the panadapter, but does not transmit (PTT, messages, CW, digital programs), cannot unlock the SWR protection, cannot change the server's memories and cannot ask to swap roles.

Notes:

- Priority works with radios that report over CAT when they are transmitting (Hamlib command `t`). CW sent to the radio's keyer counts as a program transmission for the estimated duration of the text.
- With VOX PTT the maximum time drops the program's PTT, but the radio decides when to stop transmitting.
- The maximum time does not affect transmissions made by the radio itself (operator at the radio).
- Every event is written in `sessioni.log` (lines `TEMPO MASSIMO`, `QSY rifiutato`, `PRECEDENZA`, `TRASMISSIONE remota rifiutata`).

## Radio controls (since 0.3.0)

A row of buttons under the power (it can be hidden from *Impostazioni › Finestra principale*). They are standard Hamlib commands, so they work with the radios for which Hamlib supports them:

| Button | Hamlib command | Function |
|---|---|---|
| **NR** | function `NR` | noise reduction |
| **NB** | function `NB` | noise blanker |
| **NOTCH** | function `ANF` | automatic notch |
| **COMP** | function `COMP` | the radio's microphone compressor |
| **ATT** | level `ATT` | attenuator: each click moves to the next value the radio accepts, then off |
| **PRE** | level `PREAMP` | preamplifier, like the attenuator |
| **AGC** | level `AGC` | fast, medium, slow |
| **SQL** | level `SQL` | squelch, 0–100 % |

A grey button means the radio (or Hamlib for that model) does not provide that command. The server reads these values every 4 seconds and right after each command. With **CAT over TCI**, NR, NB, NOTCH and SQL work (TCI commands `rx_nr_enable`, `rx_nb_enable`, `rx_anf_enable`, `sql_enable` and `sql_level`).

## Bands, step and memories (since 0.3.0)

- **Bands** from 160 m to 70 cm: one click goes to the last frequency and mode used on that band, the first time to a typical frequency (e.g. 14.200 MHz USB). The last frequencies are saved on this PC.
- **Tuning step** from 1 Hz to 25 kHz with the ◀ ▶ buttons and the wheel over the buttons; it also applies to the wheel on the panadapter.
- **Memorie** (memories): «Salva…» (save) asks for the name and where to save it: **in questo PC** (on this PC, only for you) or **sul server** (on the server), shared by all clients. In the list the memories are marked «PC» or «Server»; **Vai** (go, or double-click) recalls them, **Elimina** (delete) removes them. On the server, the server's memories are managed directly. Only a client that may transmit can change the server's memories.

Source of the Hamlib command names: the `rigctl`/`rigctld` manual (functions `NR`, `NB`, `ANF`, `COMP`; levels `ATT`, `PREAMP`, `AGC`, `SQL`, `ALC`), https://hamlib.sourceforge.net/html/rigctl.1.html


---

[Contents](English) · Other languages: [Italiano](IT-Radio-split-e-potenza) · [Español](ES-Radio-split-y-potencia) · [Français](FR-Radio-split-et-puissance) · [Deutsch](DE-Funkgerät-Split-und-Leistung)
