# New in 0.3.7

0.3.7 takes the best solutions of other remote-control programs: remoteAudio (DH1TW), PiRO (VK3TR), RustyRig, RigControl (K6BP) and CAESAR (RA0SMS). What IP-TRX-CTRL already had (Opus, several listeners, STUN and UPnP, rigctl port, panadapter, CW macros, networked Icom) was not duplicated.

## Client VOX (from remoteAudio)

**VOX** button next to the client's PTT: it is the only way to turn it on, and it is off at every start. PTT goes on by itself when you speak and drops after the hold time. Threshold (dBFS) and hold (ms) in **Impostazioni › VOX, squelch vocale e registrazione**. Use headphones: the program recognises and ignores radio audio coming back into the microphone, but with a loudspeaker VOX stays unreliable. The usual PTT safety applies (time limit, SWR, priority to whoever is at the radio).

## Opus bitrate (from remoteAudio)

Settings let you choose the compressed audio bandwidth: 8, 12, **16** (default), 24 or 32 kbit/s.

## Notes between operator and listeners (from PiRO and RustyRig)

**Note** tab: short messages ("on 40 m FT8 until 16:30", "can I have it after the net?"), also from the web page. The server keeps them for an hour and sends them to whoever connects later.

## Listeners also hear the operator (from PiRO)

With listeners connected, the transmitting operator's voice reaches them too, as in a net. Not in data modes or CW. Turn it off in **Impostazioni › Sicurezza in trasmissione (server)**.

## Neural noise reduction (HamNoise, from PiRO)

**Audio › NR neurale** (client): the [HamNoise](https://github.com/e04/HamNoise) neural network by e04, trained on HF signals, the same as PiRO's "HamNoise" button. One model for voice (SSB, AM, FM) and one for CW, chosen from the radio's mode; it does nothing in data modes. The value next to it (25-100 %) sets how hard to filter: on weak signals a lower value lets more signal through. It runs on the client PC, adds about 60 ms of delay and very little load.

## Voice squelch (from PiRO)

**Audio › Squelch vocale** (client): in SSB, AM and FM it mutes the hiss between overs. It opens when it recognises the periodicity of a voice, not on carriers, CW tones or static crashes, so there is no threshold to set. Adjustable hold.

## Audio recording (from RustyRig)

**Audio › ● Registra**: an 8 kHz WAV file with receive and transmit audio, in the `registrazioni` folder next to the settings ("Cartella" button). With **"Registra l'audio di ogni QSO"** recording starts by itself when you type a call in the [log](EN-New-in-0.3.6) and stops on "Registra", with the call in the file name.

## Direct IPv6 connection with PCP (from RigControl)

IPv6 has no NAT and no CGNAT: when both server and client have a public IPv6 address, the server also listens there on the same ports and the client tries it first. The server asks the router to let the two ports through with **PCP** (RFC 6887). Without PCP on the router, allow the ports in the router's IPv6 firewall for the server PC; if IPv6 does not answer, the client goes back to IPv4 and does not retry for half an hour. Turn it off in **Impostazioni › Porte aperte sul router**. See also [Ports and connection](EN-Ports-and-connection).

## Server without a window (from CAESAR)

For a Raspberry Pi or a PC without a screen next to the radio: set everything up once with the window (server role, radio, audio, pairing or fixed code), then:

```
IP-TRX-CTRL --senza-finestra
```

The `.deb` package also installs a systemd service that starts at boot and restarts by itself:

```
sudo systemctl enable --now ip-trx-ctrl-server@USERNAME
journalctl -u ip-trx-ctrl-server@USERNAME -f
```

## WinKeyer for logging programs (from CAESAR)

**Impostazioni › WinKeyer per i programmi di log (client)**. N1MM, DXLog, TR4W, Win-Test, QLog and others see a WinKeyer: the CW they send goes out from the radio at the speed they ask for, and the program's Esc key stops it.

- **Windows**: a com0com virtual port pair is needed (e.g. COM10 and COM11): one in IP-TRX-CTRL, the other in the logging program.
- **Linux and macOS**: with the **"virtuale"** port the program creates a virtual serial port; in the logging program use the `winkeyer` path in the settings folder.

> New features, built but not yet tried with a real radio: if something goes wrong, attach the problem report.


---

[Contents](English) · Other languages: [Italiano](IT-Funzioni-nuove-della-0.3.7) · [Español](ES-Novedades-de-la-0.3.7) · [Français](FR-Nouveautés-de-la-0.3.7) · [Deutsch](DE-Neu-in-0.3.7)
