# TCI connection

Since 0.2.5 the server can control the radio with **TCI** (Transceiver Control Interface) instead of Hamlib. TCI is the protocol of Expert Electronics (ExpertSDR2, ExpertSDR3), also used by Thetis and other SDR programs: the radio's program acts as a WebSocket server, usually on the same PC, port **40001**.

## Settings

**Impostazioni › Radio (server) › Collegamento CAT** (settings › radio › CAT link) → **TCI (ExpertSDR, Thetis e altri SDR)**:

- **Indirizzo** (address) of the radio's program (default `127.0.0.1`) and **porta TCI** (TCI port, default `40001`);
- **Audio della radio via TCI** (radio audio over TCI): receive and transmit audio go through TCI, with no sound cards or virtual cables on the server.

In the radio's program, enable the TCI server (ExpertSDR: Options › TCI; Thetis: Setup › Network › TCI).

## What goes through TCI

| Function | TCI commands |
|---|---|
| VFO A and VFO B frequency | `vfo` |
| mode (USB, LSB, CW, CW-R, AM, FM, DIGU, DIGL…) | `modulation` |
| filter width (wide filter in CW) | `rx_filter_band` |
| split | `split_enable` |
| TX power in % | `drive` |
| PTT («CAT» or «TCI») | `trx` (with TCI audio: `trx:0,true,tci`) |
| S-meter | `rx_smeter` / `rx_channel_sensors` |
| output power and **SWR** (also for SWR protection) | `tx_sensors` |
| CW text and speed | `cw_macros`, `cw_macros_stop`, `cw_macros_speed` |
| audio | binary streams: receive, transmit on request (`TX_CHRONO`), at 8 kHz mono when the program accepts it, otherwise converted |

The server creates a small internal «rigctld» that translates Hamlib commands into TCI: so the rest of the program works as with Hamlib, including the client's **digital-mode programs** (WSJT-X, JTDX, fldigi…). For commands that do not concern the radio (`\dump_state`, `\chk_vfo`) the server starts a test `rigctld` (model 1, «Dummy»); if Hamlib is not installed TCI still works, but digital programs might not connect.

## Limits

- The **CW pitch** does not come through TCI: set it the same in the program and in the radio.
- Power is set as a **percentage** (TCI does not give the maximum power in watts).
- SWR and output power arrive only if the radio's program sends `tx_sensors` (ExpertSDR with TCI 1.9 or later); without them, SWR protection cannot act.
- With TCI audio, the server's cards are both «TCI · audio della radio»: you cannot have one direction on TCI and the other on a sound card.
- In the `sessioni.log` file, the `TCI` lines tell when the link opens, is ready or drops.

## Sources

There is no single public specification that can be consulted from here: the program follows three sources, compared with each other. Where they disagree, it reads format, channels and rate from the header of each audio block instead of assuming them.

| Source | What it confirms | Where it agrees | Where it differs |
|---|---|---|---|
| [Hamlib TCI Audio Sidechannel Protocol](https://raw.githubusercontent.com/jfrancis42/hamlib-tci-sidecar/HEAD/PROTOCOL.md) | 64-byte audio header (16 32-bit integers: receiver, rate, format, codec, crc, length, type, channels), formats 0–3, «length» in samples per channel, reply to `TX_CHRONO` with exactly those samples | stream types 1–3 the same as JTDX | it is the protocol of a helper program for Hamlib modelled on TCI, not TCI itself; it adds a type 4 (PTT state) that TCI does not have |
| [JTDX, TCI interface](https://deepwiki.com/jtdx-project/jtdx/4.1-tci-transceiver-interface) | stream types 0–3 (IQ, received audio, audio to transmit, `TX_CHRONO`), commands `vfo`, `modulation`, `trx`, `split_enable`, `drive`, `audio_start`, `audio_samplerate` | stream types the same as the first source; commands the same as the third | a summary generated from JTDX's code, without the order of the header fields; it gives typical audio as 48 kHz stereo float32 |
| [PE5JW, TCI protocol overview](https://github.com/pe5jw/tci-protocol-overview) | syntax of the audio commands (`audio_stream_sample_type`, `audio_stream_channels`), `trx` with `tci` source, sensors `rx_smeter`, `tx_sensors`, aliases used by Thetis and others | commands and modes the same as JTDX; SWR as the 5th field of `tx_sensors` | describes a different header (it warns that it varies between programs); for `tx_sensors` it gives forward and reflected power where other descriptions give average and peak power |

Consequences in the program: of the `tx_sensors` line it uses only the 3rd field (output power) and the 5th (SWR), on which the sources agree; format, channels and rate of the audio are read from each block.


---

[Contents](English) · Other languages: [Italiano](IT-Collegamento-TCI) · [Español](ES-Conexión-TCI) · [Français](FR-Connexion-TCI) · [Deutsch](DE-TCI-Verbindung)
