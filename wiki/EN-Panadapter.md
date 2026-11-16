# Radio panadapter

Since 0.3.0 the window can show the **radio's real spectrum** (panadapter), not the audio spectrum like the CW waterfall: the spectrum at the top, the waterfall below, the kHz scale at the bottom. The **red line** is the receive frequency, the **light band** is the filter.

- **Click** on a point: tune there (rounded to the tuning step, at most 1 kHz).
- **Wheel**: one tuning step.
- **Intensità** (intensity): waterfall contrast.

## Enabling it

It is enabled **optionally**, in two places:

1. **On the server**: *Impostazioni › Panadapter della radio (server)* › the box **«Panadapter della radio per i client e per questa finestra»** (radio panadapter for the clients and for this window; off after installation).
2. **On each PC**: *Impostazioni › Finestra principale* › **«Panadapter della radio»** (on by default: the box appears only if the server provides it).

The server sends the spectrum to the client only while the client shows it: at most 10 lines per second, about 5 kB/s.

## Where the spectrum comes from

| Radio | Source | What you need |
|---|---|---|
| **SDR with CAT over TCI** (ExpertSDR, Thetis…) | IQ stream of receiver 0 (`iq_start`, 48,000 samples/s), turned into a spectrum with a 2048-point FFT around the panorama centre (`dds`) | nothing: CAT over TCI is enough |
| **Icom** with a scope (IC-7300, IC-705, IC-9700, IC-7610…) | the radio's waterfall data, CI-V command `27 00` | a CI-V port **that Hamlib does not use**: for example the second USB port of radios that offer it as CI-V (set it in the radio's menu), or a separate CI-V interface. In Settings: port, speed and the radio's **CI-V address** in hexadecimal (IC-7300 `94`, IC-705 `A4`, IC-9700 `A2`, IC-7610 `98`: check in the radio's menu) |

With the Icom port the program turns on the scope (`27 10 01`) and the waterfall data output (`27 11 01`), and turns them off on exit. A scope in **centre** mode shows centre ± span; in **fixed** mode the two edges. If the frequency is outside the fixed scope's range, the box says so.

With other makes (Yaesu, Kenwood, Elecraft…) the panadapter is available, for now, only with TCI.

**New feature, not yet tested with all radios**: if the box stays on «In attesa dei dati» (waiting for data), the status line next to the title and `sessioni.log` (`PANADAPTER` lines) say what is missing (port, speed, address).

## Sources

- Icom, *CI-V Reference Guide* IC-9700 and IC-7300MK2: command `27 00` (waterfall data: main or sub scope, division and number of divisions, centre or fixed mode, frequencies, out of range, levels 0 to 160), `27 10` (scope on/off), `27 11` (data output). Compared with each other: they agree on the format; the guide says that over USB the data arrive split into 11 parts and over LAN all at once.
  https://www.icomfrance.com/uploads/files/produit/not-IC-9700_ENG_CI-V_1-en.pdf
  https://icomuk.co.uk/files/icom/PDF/productAdditionalFile/IC-7300MK2_ENG_CI-V_0.pdf
- TCI library `github.com/ftl/tci` (Go): commands `iq_start`, `iq_stop`, `iq_samplerate` (48, 96, 192 kHz), `dds`, binary stream type 0 = IQ.
  https://pkg.go.dev/github.com/ftl/tci/client


---

[Contents](English) · Other languages: [Italiano](IT-Panadapter) · [Español](ES-Panadaptador) · [Français](FR-Panadapter) · [Deutsch](DE-Panadapter)
