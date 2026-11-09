# IP-TRX-CTRL

Comando a distanza di una radio HF via internet: audio bidirezionale a 8 kHz cifrato AES-256, controllo CAT con Hamlib, PTT (CAT, RTS, DTR, VOX, TCI), keyer con messaggi WAV in SSB e macro di testo in CW, cascata e decodificatore CW, VFO B con split, potenza TX, mute delle schede audio del server.

Un unico programma per i due PC: **server** (la radio è collegata qui) e **client** (l'operatore). I ruoli si scelgono al primo avvio e si possono invertire.

Versioni: Windows 64 e 32 bit, Linux 64 bit, Raspberry Pi 64 e 32 bit, macOS Apple Silicon e Intel.

## Scaricare

I pacchetti vengono compilati a ogni aggiornamento dalla scheda **Actions** (sezione *Artifacts*) e, per le versioni con tag, pubblicati in **Releases**.

- Windows: estrai lo zip e avvia `IP-TRX-CTRL.exe` (Hamlib è incluso).
- Linux / Raspberry Pi: estrai il `.tar.gz`, installa Hamlib con `sudo apt install libhamlib-utils` e avvia `./IP-TRX-CTRL`.
- macOS: estrai lo zip, installa Hamlib con `brew install hamlib`, poi clic destro su `IP-TRX-CTRL.app` › Apri.

La documentazione è nella cartella `docs`. Le note sulle licenze dei componenti di terze parti sono in `THIRD-PARTY-NOTICES.txt` e nella finestra «Informazioni e licenze» del programma.

Uso dichiarato: attività radioamatoriale.
