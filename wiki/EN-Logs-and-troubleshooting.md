# Logs and troubleshooting

## Where the program's files are

| System | Data folder |
|---|---|
| Windows | `%APPDATA%\IP-TRX-CTRL` (that is `C:\Users\<user>\AppData\Roaming\IP-TRX-CTRL`) |
| Linux and Raspberry Pi | `~/.config/IP-TRX-CTRL` |
| macOS | `~/.config/IP-TRX-CTRL` (hidden folder: in the Finder press Cmd+Shift+G and type the path) |

The folder can also be opened from the program: **Impostazioni › Apri cartella dati** (settings › open data folder).

On Windows: Win+R, type `%APPDATA%\IP-TRX-CTRL` and press Enter.

## What it contains

| File | Content |
|---|---|
| `errori.log` | unexpected errors, with date, time and the point of the program where they happened. The file appears only after the first error |
| `sessioni.log` | connection log: every step on the ports (`PORTE` lines: UPnP router search, ports opened or refused, public IP, closing), network type (`RETE` lines: IPv4 / IPv6), pairing with the MQTT dialogue, connections, role changes, safety PTT releases. Time in UTC |
| `settings.json` | settings |
| `pairing.key` | pairing key, encrypted (Windows: with the Windows user; Linux/Raspberry: tied to machine and user). On macOS the key is in the Keychain, entry «IP-TRX-CTRL» |
| `WAV/` | the keyer's message files |

On Windows, Hamlib is extracted into `%LOCALAPPDATA%\IP-TRX-CTRL\hamlib\<version>`.

## How to report a problem

Since 0.3.0: **Impostazioni › Abbinamento e registri › «Rapporto per un problema…»** (problem report).

1. Write (if you like) what happened.
2. **Salva il rapporto…** (save the report): a text file with version, system, role, radio, PTT and CW methods, safety options, panadapter, MQTT broker and the last parts of `errori.log` (200 kB) and `sessioni.log` (400 kB).
3. **Apri la pagina delle segnalazioni** (open the reports page): the repository's **Issues** page opens; attach the file.

In the report **public IP addresses are masked**: each becomes `[IP-1]`, `[IP-2]`… (the same address always gets the same name, so the log stays readable). Only local network addresses stay visible (192.168.x.x, 10.x.x.x, 172.16–31.x.x, CGNAT 100.64–127.x.x, 127.x.x.x, 169.254.x.x), which lead to nobody from the internet; IPv6 addresses are all masked. The report contains no keys or pairing codes. You can open and read it before attaching it.

Without the report: open the data folder and attach `errori.log` and `sessioni.log`, stating operating system, version, role and radio. `sessioni.log` contains IP addresses and callsigns. Never attach the key files (`pairing-….key`).

## New version and automatic updates

At start-up and then every 6 hours the program asks GitHub which is the latest published release (`api.github.com/repos/ic8tem/IP-TRX-CTRL/releases/latest`, no account).

Since **0.3.2**, if it is newer, the program updates itself: it downloads the package suited to the PC, checks its size and SHA-256 fingerprint (the one GitHub publishes for each file) and restarts in the new version, **only when no session is in progress and nothing is being transmitted**. With a session open, the new version stays ready and installs as soon as it ends; the line at the top says so and offers **Aggiorna adesso** (update now). In `sessioni.log` every step is written with the prefix `AGGIORNAMENTI`.

These update themselves: Windows Setup and portable zip (since 0.3.2 the Setup makes the program folder writable for this), portable tar.gz (Linux, Raspberry Pi, Alpine), AppImage, IP-TRX-CTRL.app on the Mac in a writable folder. System packages **.deb, .rpm, Arch and Slackware** do **not** update themselves: the new-version line appears with **Apri la pagina** (open the page), and the new package is installed with the same command as the first time.

Automatic updates are turned off in **Impostazioni › Finestra principale**: the line at the top stays, with **Aggiorna adesso**, **Apri la pagina** and **Chiudi** (close; it no longer appears for that version). If the update fails (network, folder not writable) the program carries on with the current version and tries again at the next check.

## Common problems

| Symptom | What to check |
|---|---|
| Pop-up «Io mi spengo perché non riesco a funzionare» (I am shutting down because I cannot work) with the Exit button (server) | the router did not open the two ports with UPnP: turn on UPnP on the router and restart. The `PORTE` lines of `sessioni.log` say which step failed |
| Pairing stops with «la porta … non risponde» (port … does not answer) | UPnP active on the server's router; if the provider uses CGNAT, the server cannot be reached from the internet |
| «nessun broker disponibile» (no broker available) | the PC's internet connection, port 1883 not blocked by the network |
| «Hamlib non trovato» (Hamlib not found) (Linux, Raspberry, macOS) | `sudo apt install libhamlib-utils` or `brew install hamlib` |
| The radio does not answer CAT | model, serial port and speed in Settings; on Linux/Raspberry the user must be in the `dialout` group |
| No microphone audio on macOS | System Settings › Privacy & Security › Microphone: enable IP-TRX-CTRL |
| Server audio muted | «Schede audio del server» box: unmute, or turn on «Togli il mute da solo» |


---

[Contents](English) · Other languages: [Italiano](IT-Registri-e-risoluzione-dei-problemi) · [Español](ES-Registros-y-solución-de-problemas) · [Français](FR-Journaux-et-résolution-des-problèmes) · [Deutsch](DE-Protokolle-und-Fehlersuche)
