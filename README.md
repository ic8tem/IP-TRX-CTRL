# IP-TRX-CTRL

Comando a distanza di una radio HF via internet: audio bidirezionale a 8 kHz cifrato AES-256, controllo CAT con Hamlib o TCI (anche l'audio della radio via TCI, senza schede né cavi), PTT (CAT, RTS, DTR, VOX, TCI), keyer con messaggi WAV in SSB e macro di testo in CW, cascata e decodificatore CW, VFO B con split, potenza TX, accesso anche dal browser di telefono, tablet e PC senza installare niente (dalla 0.3.1), protezione ROS (avviso e blocco della trasmissione), tempo massimo di trasmissione, precedenza a chi è alla radio, client in solo ascolto, comandi della radio (NR, NB, Notch, ATT, preamplificatore, AGC, compressore, squelch, ALC), bande, passo e memorie, panadapter della radio (Icom via CI-V, SDR via TCI), mute delle schede audio del server. Sul client: programmi digitali (WSJT-X, JTDX, fldigi…) con rigctld locale e cavi audio virtuali, pedale PTT e tasto CW meccanico su porta seriale. Porte aperte con UPnP e verificate dall'esterno, collegamento inverso e audio su UDP quando possibile.

Un unico programma per i due PC: **server** (la radio è collegata qui) e **client** (l'operatore). I ruoli si scelgono al primo avvio e si possono invertire.

Versioni: Windows 64 e 32 bit e ARM, Linux (Debian e derivate, Fedora, openSUSE, Arch, Slackware, Alpine, AppImage per tutte), Raspberry Pi 64 e 32 bit, macOS Apple Silicon e Intel.

## Scaricare

I file sono nella pagina **Releases**:

| Distribuzione | File da scaricare | Si aggiorna da solo | Note |
|---|---|---|---|
| **Windows 64 bit** | `IP-TRX-CTRL-…-win-x64-setup.exe` (portatile: `…-win-x64-portatile.zip`) | sì | Hamlib incluso; il setup propone anche il cavo audio virtuale VB-CABLE per i programmi digitali |
| **Windows 32 bit** | `IP-TRX-CTRL-…-win-x86-setup.exe` (portatile: `…-win-x86-portatile.zip`) | sì | Hamlib incluso |
| **Windows ARM** (Snapdragon) | `IP-TRX-CTRL-…-win-arm64-setup.exe` (portatile: `…-win-arm64-portatile.zip`) | sì | Hamlib incluso (rigctld per x64, eseguito dall'emulazione di Windows 11) |
| **Debian, Ubuntu, Mint** (x86_64) | `ip-trx-ctrl_…_amd64.deb` | no, avvisa | `sudo apt install ./<file>.deb` installa anche Hamlib |
| **Fedora, openSUSE, RHEL** | `ip-trx-ctrl-….x86_64.rpm` (o `.aarch64.rpm`, `.armv7hl.rpm`) | no, avvisa | `sudo dnf install ./<file>.rpm` o `sudo zypper install ./<file>.rpm`; Hamlib consigliato (`hamlib`) |
| **Arch, Manjaro, EndeavourOS, Arch Linux ARM** | `ip-trx-ctrl-…-x86_64.pkg.tar.zst` (o `-aarch64`) | no, avvisa | `sudo pacman -U ./<file>.pkg.tar.zst` installa anche Hamlib |
| **Slackware** (x86_64 e ARM64) | `ip-trx-ctrl-…-x86_64-1_ic8tem.txz` (o `-aarch64-`) | no, avvisa | da root `installpkg <file>.txz` (versioni successive: `upgradepkg`); Hamlib da SlackBuilds |
| **Qualsiasi Linux** (x86_64 e ARM64) | `IP-TRX-CTRL-…-x86_64.AppImage` (o `-aarch64`) | sì | un solo file: rendilo eseguibile e avvialo; Hamlib e ALSA quelli del sistema |
| **Linux x86_64 portatile** | `IP-TRX-CTRL-…-linux-x64-portatile.tar.gz` | sì | si scompatta e si avvia; Hamlib dal gestore di pacchetti |
| **Linux i686** (32 bit) | — | — | non disponibile: .NET 8, su cui è costruito il programma, non esiste per Linux a 32 bit su PC |
| **Alpine Linux** (musl, x86_64 e ARM64) | `IP-TRX-CTRL-…-linux-musl-x64-portatile.tar.gz` (o `-linux-musl-arm64-`) | sì | `apk add libstdc++ fontconfig libx11 libice libsm alsa-lib hamlib` |
| **Raspberry Pi (ARM)** | `ip-trx-ctrl_…_arm64.deb` (Raspberry Pi OS 64 bit) o `…_armhf.deb` (32 bit) | no, avvisa | `sudo apt install ./<file>.deb` installa anche Hamlib; per l'aggiornamento automatico c'è il portatile `…-linux-arm64-` / `…-linux-arm-portatile.tar.gz` |
| **Raspberry Pi Slackware ARM64** | `ip-trx-ctrl-…-aarch64-1_ic8tem.txz` o `IP-TRX-CTRL-…-linux-arm64-portatile.tar.gz` | il portatile sì | Hamlib da SlackBuilds (`hamlib`) |
| **Linux armv7l** | `ip-trx-ctrl_…_armhf.deb`, `ip-trx-ctrl-….armv7hl.rpm` o `IP-TRX-CTRL-…-linux-arm-portatile.tar.gz` | il portatile sì | ARM a 32 bit; Hamlib dal gestore di pacchetti |
| **FreeBSD amd64** | — | — | non disponibile: .NET 8 non ha una versione ufficiale per FreeBSD (solo prove della comunità, non adatte a un programma che comanda un trasmettitore) |
| **macOS** | `…-osx-arm64.dmg` (Apple Silicon) o `…-osx-x64.dmg` (Intel); app sola: `…-app.zip` | sì, se l'app è in Applicazioni o in un'altra cartella scrivibile | Hamlib con `brew install hamlib`; al primo avvio clic destro sull'app › Apri |

## Aggiornamenti automatici

Dalla **0.3.2** il programma si aggiorna da solo. All'avvio e poi ogni 6 ore guarda l'ultima versione pubblicata qui su GitHub; se è più nuova scarica il pacchetto adatto al PC, ne controlla dimensione e impronta SHA-256 e si riavvia nella versione nuova. Lo fa **solo quando non c'è una sessione in corso e non si trasmette**: se la sessione è aperta, la versione nuova resta pronta e si installa appena finisce. Un server lasciato acceso si aggiorna e riparte da solo, con le stesse impostazioni. Impostazioni, chiavi, abbinamenti e registri non si toccano.

Si aggiornano da soli: installatore e zip portatile di Windows, tar.gz portatili (Linux, Raspberry Pi, Alpine), AppImage, IP-TRX-CTRL.app sul Mac (spostata in Applicazioni o in un'altra cartella in cui l'utente può scrivere).

**Non** si aggiornano da soli i pacchetti dei gestori di sistema: **.deb** (Debian, Ubuntu, Mint, Raspberry Pi OS), **.rpm** (Fedora, openSUSE), **Arch** e **Slackware**. Sono installati da root in `/opt` e appartengono al gestore dei pacchetti: il programma mostra solo l'avviso della versione nuova, che si installa con lo stesso comando della prima volta. Chi li vuole automatici può usare l'AppImage o il tar.gz portatile.

Si spengono in **Impostazioni › Finestra principale** (casella «Aggiornamenti automatici»): resta l'avviso, con il pulsante «Aggiorna adesso». La 0.3.0 e la 0.3.1 non si aggiornano da sole: la 0.3.2 va installata a mano una volta, le successive arrivano da sole. Le versioni nuove dello stesso protocollo si parlano con le precedenti; se un giorno cambierà il protocollo, i due PC si allineano da soli al primo controllo (all'avvio o entro 6 ore).

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
| 0.3.0 – 0.3.2 | 3 | solo le 0.3.x (numero a 10 cifre, codice fisso del server, più client sul server, sicurezza in trasmissione, panadapter; dalla 0.3.1 accesso dal browser; dalla 0.3.2 aggiornamenti automatici) |

Dettagli e controllo automatico: [docs/Versioni-compatibili.md](docs/Versioni-compatibili.md).

## Sostieni il progetto

IP-TRX-CTRL è gratuito. Se ti è utile e vuoi sostenerne lo sviluppo: [ko-fi.com/ic8tem](https://ko-fi.com/ic8tem).

## Licenza

IP-TRX-CTRL è software libero, Copyright (C) 2026 IC8TEM, distribuito secondo la **GNU General Public License versione 3 o successiva** (file [`LICENSE`](LICENSE)), senza alcuna garanzia. Le licenze dei componenti di terze parti sono in `THIRD-PARTY-NOTICES.txt`.
