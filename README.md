# IP-TRX-CTRL

Comando a distanza di una radio HF via internet: audio bidirezionale a 8 kHz cifrato AES-256, controllo CAT con Hamlib o TCI (anche l'audio della radio via TCI, senza schede né cavi), PTT (CAT, RTS, DTR, VOX, TCI), keyer con messaggi WAV in SSB e macro di testo in CW, cascata e decodificatore CW, VFO B con split, potenza TX, accesso anche dal browser di telefono, tablet e PC senza installare niente (dalla 0.3.1), protezione ROS (avviso e blocco della trasmissione), tempo massimo di trasmissione, precedenza a chi è alla radio, client in solo ascolto, comandi della radio (NR, NB, Notch, ATT, preamplificatore, AGC, compressore, squelch, ALC), bande, passo e memorie, panadapter della radio (Icom via CI-V, SDR via TCI), mute delle schede audio del server. Sul client: programmi digitali (WSJT-X, JTDX, fldigi…) con rigctld locale e cavi audio virtuali, pedale PTT e tasto CW meccanico su porta seriale. Porte aperte con UPnP e verificate dall'esterno, collegamento inverso e audio su UDP quando possibile.

Un unico programma per i due PC: **server** (la radio è collegata qui) e **client** (l'operatore). I ruoli si scelgono al primo avvio e si possono invertire.

Versioni: Windows 64 e 32 bit, Linux 64 bit, Raspberry Pi 64 e 32 bit, macOS Apple Silicon e Intel.

## Scaricare

I file sono nella pagina **Releases**:

| Distribuzione | File da scaricare | Note |
|---|---|---|
| **Windows 64 bit** | `IP-TRX-CTRL-…-win-x64-setup.exe` (portatile: `…-win-x64-portatile.zip`) | Hamlib incluso; il setup propone anche il cavo audio virtuale VB-CABLE per i programmi digitali |
| **Windows 32 bit** | `IP-TRX-CTRL-…-win-x86-setup.exe` (portatile: `…-win-x86-portatile.zip`) | Hamlib incluso |
| **Linux x86_64** (64 bit) | `ip-trx-ctrl_…_amd64.deb` (portatile: `…-linux-x64-portatile.tar.gz`) | `sudo apt install ./<file>.deb` installa anche Hamlib; con altre distribuzioni il portatile e Hamlib dal proprio gestore di pacchetti |
| **Linux i686** (32 bit) | — | non disponibile: .NET 8, su cui è costruito il programma, non esiste per Linux a 32 bit su PC |
| **Raspberry Pi (ARM)** | `ip-trx-ctrl_…_arm64.deb` (Raspberry Pi OS 64 bit) o `…_armhf.deb` (32 bit) | `sudo apt install ./<file>.deb` installa anche Hamlib |
| **Raspberry Pi Slackware ARM64** | `IP-TRX-CTRL-…-linux-arm64-portatile.tar.gz` | portatile: si scompatta e si avvia; Hamlib da SlackBuilds (`hamlib`) |
| **Linux armv7l** | `ip-trx-ctrl_…_armhf.deb` o `IP-TRX-CTRL-…-linux-arm-portatile.tar.gz` | ARM a 32 bit; Hamlib dal gestore di pacchetti |
| **FreeBSD amd64** | — | non disponibile: .NET 8 non ha una versione ufficiale per FreeBSD (solo prove della comunità, non adatte a un programma che comanda un trasmettitore) |
| **macOS** | `…-osx-arm64.dmg` (Apple Silicon) o `…-osx-x64.dmg` (Intel) | Hamlib con `brew install hamlib`; al primo avvio clic destro sull'app › Apri |

Senza installare niente si può usare anche il **browser** (dalla 0.3.1): vedi [docs/Accesso-dal-browser.md](docs/Accesso-dal-browser.md).

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
| 0.2.0 – 0.2.8 | 2 | tutte le 0.2.x (alcune funzioni richiedono una versione minima su entrambi i PC) |
| 0.3.0 – 0.3.1 | 3 | solo le 0.3.x (numero a 10 cifre, codice fisso del server, più client sul server, sicurezza in trasmissione, panadapter; dalla 0.3.1 accesso dal browser) |

Dettagli e controllo automatico: [docs/Versioni-compatibili.md](docs/Versioni-compatibili.md).

## Sostieni il progetto

IP-TRX-CTRL è gratuito. Se ti è utile e vuoi sostenerne lo sviluppo: [ko-fi.com/ic8tem](https://ko-fi.com/ic8tem).

## Licenza

IP-TRX-CTRL è software libero, Copyright (C) 2026 IC8TEM, distribuito secondo la **GNU General Public License versione 3 o successiva** (file [`LICENSE`](LICENSE)), senza alcuna garanzia. Le licenze dei componenti di terze parti sono in `THIRD-PARTY-NOTICES.txt`.
