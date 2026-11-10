# IP-TRX-CTRL

Comando a distanza di una radio HF via internet: audio bidirezionale a 8 kHz cifrato AES-256, controllo CAT con Hamlib, PTT (CAT, RTS, DTR, VOX, TCI), keyer con messaggi WAV in SSB e macro di testo in CW, cascata e decodificatore CW, VFO B con split, potenza TX, mute delle schede audio del server. Sul client: programmi digitali (WSJT-X, JTDX, fldigi…) con rigctld locale e cavi audio virtuali, pedale PTT e tasto CW meccanico su porta seriale. Porte aperte con UPnP e verificate dall'esterno, collegamento inverso e audio su UDP quando possibile.

Un unico programma per i due PC: **server** (la radio è collegata qui) e **client** (l'operatore). I ruoli si scelgono al primo avvio e si possono invertire.

Versioni: Windows 64 e 32 bit, Linux 64 bit, Raspberry Pi 64 e 32 bit, macOS Apple Silicon e Intel.

## Scaricare

Gli installatori sono nella pagina **Releases**:

- Windows 64 / 32 bit: `…-win-x64-setup.exe` / `…-win-x86-setup.exe` (Hamlib incluso). Il setup a 64 bit propone anche di scaricare e installare il cavo audio virtuale VB-CABLE per i programmi digitali (saltato se è già presente).
- Linux 64 bit e Raspberry Pi: `ip-trx-ctrl_…_amd64.deb`, `…_arm64.deb` (Raspberry Pi OS 64 bit), `…_armhf.deb` (32 bit); si installano con `sudo apt install ./<file>.deb`, che installa anche Hamlib.
- macOS: `…-osx-arm64.dmg` (Apple Silicon) o `…-osx-x64.dmg` (Intel); installa Hamlib con `brew install hamlib`; al primo avvio clic destro sull'app › Apri.

Per ogni sistema c'è anche una versione portatile, senza installazione.

## Errori e registri

Il programma scrive gli errori in `errori.log` e il registro dei collegamenti in `sessioni.log`, nella cartella dei dati:

- Windows: `%APPDATA%\IP-TRX-CTRL` (Win+R, scrivi il percorso e premi Invio)
- Linux e Raspberry Pi: `~/.config/IP-TRX-CTRL`
- macOS: `~/.config/IP-TRX-CTRL`

Dal programma: **Impostazioni › Apri cartella dati**. Per segnalare un problema apri una **Issue** e allega `errori.log` (e `sessioni.log` per i problemi di collegamento). Dettagli in `docs/Registri-e-risoluzione-problemi.md`.

La documentazione è nella cartella `docs`. Le note sulle licenze dei componenti di terze parti sono in `THIRD-PARTY-NOTICES.txt` e nella finestra «Informazioni e licenze» del programma.

Uso dichiarato: attività radioamatoriale.

## Versioni compatibili

Server e client devono avere versioni dello stesso protocollo:

| Versioni | Protocollo | Compatibili con |
|---|---|---|
| 0.1.0 – 0.1.3 | 1 | solo le 0.1.x |
| 0.2.0 – 0.2.4 | 2 | tutte le 0.2.x (alcune funzioni richiedono una versione minima su entrambi i PC) |

Dettagli e controllo automatico: [docs/Versioni-compatibili.md](docs/Versioni-compatibili.md).

## Licenza

IP-TRX-CTRL è software libero, Copyright (C) 2026 IC8TEM, distribuito secondo la **GNU General Public License versione 3 o successiva** (file [`LICENSE`](LICENSE)), senza alcuna garanzia. Le licenze dei componenti di terze parti sono in `THIRD-PARTY-NOTICES.txt`.
