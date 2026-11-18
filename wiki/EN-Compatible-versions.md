# Compatible versions

Server and client must have versions of the same **protocol**. The protocol changes only when the two PCs must be updated together; within the same protocol, versions talk to each other.

| Versions | Protocol | Compatible with |
|---|---|---|
| 0.1.0 – 0.1.3 | 1 | only 0.1.x |
| 0.2.0 – 0.2.8 | 2 | all 0.2.x |
| 0.3.0 – 0.3.5 | 3 | only 0.3.x (10-digit number, the server's fixed code, several clients on the server, transmit safety, panadapter; since 0.3.1 browser access; since 0.3.2 automatic updates) |

A 0.1.x and a 0.2.x must **not** be used together, nor a 0.2.x and a 0.3.x: update both PCs. 0.3.0 also changes the telemetry format and adds new messages, which is why it does not talk to earlier versions. 0.3.0 changes pairing (10-digit number instead of the long code); keys already exchanged stay valid and move by themselves into the client's list of servers and the server's list of clients.

## Features that need a minimum version on both PCs

| Feature | Server and client at least |
|---|---|
| Digital programs, CW pedal/key, port cycle, UDP audio | 0.2.0 |
| Choosing the server's sound cards from the client | 0.2.1 |
| Server without UPnP, connection on the local network | 0.2.2 |
| Version exchange and automatic compatibility check | 0.2.3 |
| The client tells the server which ports get out of its network (filtered networks) | 0.2.4 |
| SWR protection: flashing bar, transmission block and window on the client | 0.2.4 |
| Server CAT and audio over TCI (ExpertSDR, Thetis and other SDRs) | 0.2.5 (on the server is enough) |
| Port cycle during pairing too (20 rounds, always new ports, reverse connection) | 0.2.7 on both PCs |
| 10-digit pairing number, list of servers on the client | 0.3.0 on both PCs |
| Several clients on the server (one connected at a time, «server busy»), the server's fixed code | 0.3.0 on both PCs |
| Transmit safety (maximum time, no QSY while transmitting, priority to whoever is at the radio, receive only), radio controls, server memories, panadapter | 0.3.0 on both PCs |
| Browser access (HTTPS web page, «Trova il server» page) | 0.3.1 on the server; a 0.3.0 client keeps working |
| Automatic updates | 0.3.2 on each PC: each one updates itself (not with the .deb, .rpm, Arch and Slackware packages) |

## Automatic updates (since 0.3.2)

Since 0.3.2 each PC updates itself to the latest release, at start-up or within 6 hours, and only when no session is in progress: if one day a version with a new protocol comes out, server and client line up by themselves without staying long on different protocols. The exception is the system packages (.deb, .rpm, Arch, Slackware), which must be updated by hand on both PCs. Details in the [README](https://github.com/ic8tem/IP-TRX-CTRL#aggiornamenti-automatici).

## Automatic check (since 0.2.3)

The two PCs exchange their versions at three moments:

- **pairing**: the server sends it, encrypted, on the MQTT broker together with address and ports; the client compares it before connecting. If the protocol is different, pairing stops and says so in the window's log;
- **address on the MQTT broker**: the server publishes it together with its address, at every update;
- **start of every connection**: in the greeting on the encrypted channel.

If the other PC has a different protocol, the window **«Versioni non compatibili»** (incompatible versions) appears; if it has an older version of the same protocol, a notice says which features are missing. Versions up to 0.2.2 do not send their version: they appear as «0.2.2 o precedente» (0.2.2 or earlier).

The other PC's version is shown in its network line in the main window; every comparison is written in `sessioni.log` with the prefix `VERSIONE`. This PC's version is in the «Informazioni e licenze» (about and licenses) window.


---

[Contents](English) · Other languages: [Italiano](IT-Versioni-compatibili) · [Español](ES-Versiones-compatibles) · [Français](FR-Versions-compatibles) · [Deutsch](DE-Kompatible-Versionen)
