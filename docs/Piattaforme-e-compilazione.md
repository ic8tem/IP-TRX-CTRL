# Piattaforme e compilazione

IP-TRX-CTRL è scritto in **C# (.NET 8)** con interfaccia **Avalonia**, per avere un unico codice sorgente su tutti i sistemi.

## Eseguibili previsti

| Sistema | Identificativo .NET |
|---|---|
| Windows 64 bit | `win-x64` |
| Windows 32 bit | `win-x86` |
| Windows ARM (Snapdragon) | `win-arm64` |
| macOS Apple Silicon | `osx-arm64` |
| macOS Intel | `osx-x64` |
| Linux 64 bit | `linux-x64` |
| Raspberry Pi 64 bit | `linux-arm64` |
| Raspberry Pi 32 bit | `linux-arm` |
| Alpine e altri Linux con musl, 64 bit | `linux-musl-x64` |
| Alpine e altri Linux con musl, ARM64 | `linux-musl-arm64` |

Ogni eseguibile è autonomo (non serve installare .NET) e in un solo file.

## Componenti per sistema

| Funzione | Soluzione |
|---|---|
| Interfaccia | Avalonia |
| Audio | nativo, a 8 kHz: MME (winmm) su Windows, ALSA su Linux/Raspberry (con PipeWire/PulseAudio tramite il dispositivo «default»), AudioQueue su macOS |
| Chiavi salvate | Windows: DPAPI · macOS: Portachiavi · Linux/Raspberry: file cifrato leggibile solo dall'utente |
| Controllo radio | Hamlib `rigctld`: incluso per Windows 64 e 32 bit e ARM (per ARM la versione x64, eseguita dall'emulazione di Windows 11); su Linux/Raspberry pacchetto del sistema (`sudo apt install libhamlib-utils`); su macOS Homebrew (`brew install hamlib`) |
| Seriale RTS/DTR, MQTT, UPnP, cifratura | librerie .NET già multipiattaforma |

## Limiti noti

- **Raspberry Pi**: servono Pi 2, 3, 4 o 5; .NET non funziona su Pi Zero e Pi 1.
- **Linux con Wayland**: i tasti globali Ctrl+Alt+F1…F8 di solito non sono permessi dal sistema; funzionano con X11. Con Wayland restano F1…F8 a finestra in primo piano.
- **macOS**: al primo avvio il sistema chiede il permesso per il microfono e, per i tasti globali, quello di "Accessibilità". Per distribuire il programma senza avvisi va firmato e notarizzato, operazione che richiede un Mac.

## Compilazione automatica

Ogni push sul ramo `main` avvia la compilazione su GitHub Actions (file `.github/workflows/build.yml`)
per tutte e undici le versioni. I pacchetti si scaricano dalla scheda **Actions** del repository
(sezione *Artifacts*) e vengono pubblicati nella pagina **Releases** con il numero di versione scritto
nel file `VERSION`: per una nuova versione basta cambiare quel numero.

| Sistema | Installatore o pacchetto | Versione portatile |
|---|---|---|
| Windows 64 bit | `IP-TRX-CTRL-<versione>-win-x64-setup.exe` | `…-win-x64-portatile.zip` |
| Windows 32 bit | `IP-TRX-CTRL-<versione>-win-x86-setup.exe` | `…-win-x86-portatile.zip` |
| Windows ARM | `IP-TRX-CTRL-<versione>-win-arm64-setup.exe` | `…-win-arm64-portatile.zip` |
| Linux 64 bit | `ip-trx-ctrl_<versione>_amd64.deb`, `ip-trx-ctrl-<versione>.x86_64.rpm`, `ip-trx-ctrl-<versione>-1-x86_64.pkg.tar.zst`, `ip-trx-ctrl-<versione>-x86_64-1_ic8tem.txz`, `IP-TRX-CTRL-<versione>-x86_64.AppImage` | `…-linux-x64-portatile.tar.gz` |
| Raspberry Pi / Linux ARM64 | `ip-trx-ctrl_<versione>_arm64.deb`, `….aarch64.rpm`, `…-aarch64.pkg.tar.zst`, `…-aarch64-1_ic8tem.txz`, `…-aarch64.AppImage` | `…-linux-arm64-portatile.tar.gz` |
| Raspberry Pi / Linux ARM 32 bit | `ip-trx-ctrl_<versione>_armhf.deb`, `….armv7hl.rpm` | `…-linux-arm-portatile.tar.gz` |
| Alpine (musl) | — | `…-linux-musl-x64-portatile.tar.gz`, `…-linux-musl-arm64-portatile.tar.gz` |
| macOS Apple Silicon | `IP-TRX-CTRL-<versione>-osx-arm64.dmg` | `…-osx-arm64-app.zip` |
| macOS Intel | `IP-TRX-CTRL-<versione>-osx-x64.dmg` | `…-osx-x64-app.zip` |

I pacchetti `.rpm`, Arch e Slackware si ricavano dal `.deb` (stessi file in `/opt/ip-trx-ctrl`); gli script sono in `packaging/linux`. Nei pacchetti di sistema il file `/opt/ip-trx-ctrl/PACCHETTO` dice al programma di non aggiornarsi da solo. Su Linux x86_64, ARM64 e Alpine la compilazione prova anche l'avvio dell'eseguibile (`IP-TRX-CTRL --versione`).

Il `.deb` si installa con `sudo apt install ./ip-trx-ctrl_<versione>_<arch>.deb`: installa anche Hamlib
(`libhamlib-utils`) e crea la voce nel menu delle applicazioni. Per usare la porta seriale della radio
aggiungi il tuo utente al gruppo `dialout` (`sudo usermod -aG dialout $USER`, poi esci e rientra).

Su macOS, al primo avvio: clic destro sull'app › Apri (l'app non è notarizzata).

## Compilare a mano

Serve il .NET SDK 8:

```
dotnet publish src/IpTrxCtrl/IpTrxCtrl.csproj -c Release -r <identificativo> -o dist/<identificativo>
```

Per includere Hamlib nella versione Windows, prima: `pwsh scripts/get_hamlib.ps1 -Arch w64` (oppure `w32`).
