# IP-TRX-CTRL

Comando a distanza di una radio HF via internet: audio bidirezionale a 8 kHz cifrato AES-256, controllo CAT con Hamlib, PTT (CAT, RTS, DTR, VOX, TCI), keyer con messaggi WAV in SSB e macro di testo in CW, cascata e decodificatore CW, VFO B con split, potenza TX, mute delle schede audio del server.

Un unico programma per i due PC: **server** (la radio è collegata qui) e **client** (l'operatore). I ruoli si scelgono al primo avvio e si possono invertire.

Versioni: Windows 64 e 32 bit, Linux 64 bit, Raspberry Pi 64 e 32 bit, macOS Apple Silicon e Intel.

## Scaricare

Gli installatori sono nella pagina **Releases**:

- Windows 64 / 32 bit: `…-win-x64-setup.exe` / `…-win-x86-setup.exe` (Hamlib incluso).
- Linux 64 bit e Raspberry Pi: `ip-trx-ctrl_…_amd64.deb`, `…_arm64.deb` (Raspberry Pi OS 64 bit), `…_armhf.deb` (32 bit); si installano con `sudo apt install ./<file>.deb`, che installa anche Hamlib.
- macOS: `…-osx-arm64.dmg` (Apple Silicon) o `…-osx-x64.dmg` (Intel); installa Hamlib con `brew install hamlib`; al primo avvio clic destro sull'app › Apri.

Per ogni sistema c'è anche una versione portatile, senza installazione.

La documentazione è nella cartella `docs`. Le note sulle licenze dei componenti di terze parti sono in `THIRD-PARTY-NOTICES.txt` e nella finestra «Informazioni e licenze» del programma.

Uso dichiarato: attività radioamatoriale.
