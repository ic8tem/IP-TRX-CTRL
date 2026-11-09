# Piattaforme e compilazione

IP-TRX-CTRL è scritto in **C# (.NET 8)** con interfaccia **Avalonia**, per avere un unico codice sorgente su tutti i sistemi.

## Eseguibili previsti

| Sistema | Identificativo .NET |
|---|---|
| Windows 64 bit | `win-x64` |
| Windows 32 bit | `win-x86` |
| macOS Apple Silicon | `osx-arm64` |
| macOS Intel | `osx-x64` |
| Linux 64 bit | `linux-x64` |
| Raspberry Pi 64 bit | `linux-arm64` |
| Raspberry Pi 32 bit | `linux-arm` |

Ogni eseguibile è autonomo (non serve installare .NET) e in un solo file.

## Componenti per sistema

| Funzione | Soluzione |
|---|---|
| Interfaccia | Avalonia |
| Audio | nativo, a 8 kHz: MME (winmm) su Windows, ALSA su Linux/Raspberry (con PipeWire/PulseAudio tramite il dispositivo «default»), AudioQueue su macOS |
| Chiavi salvate | Windows: DPAPI · macOS: Portachiavi · Linux/Raspberry: file cifrato leggibile solo dall'utente |
| Controllo radio | Hamlib `rigctld`: incluso per Windows 64 e 32 bit; su Linux/Raspberry pacchetto del sistema (`sudo apt install libhamlib-utils`); su macOS Homebrew (`brew install hamlib`) |
| Seriale RTS/DTR, MQTT, UPnP, cifratura | librerie .NET già multipiattaforma |

## Limiti noti

- **Raspberry Pi**: servono Pi 2, 3, 4 o 5; .NET non funziona su Pi Zero e Pi 1.
- **Linux con Wayland**: i tasti globali Ctrl+Alt+F1…F8 di solito non sono permessi dal sistema; funzionano con X11. Con Wayland restano F1…F8 a finestra in primo piano.
- **macOS**: al primo avvio il sistema chiede il permesso per il microfono e, per i tasti globali, quello di "Accessibilità". Per distribuire il programma senza avvisi va firmato e notarizzato, operazione che richiede un Mac.

## Compilazione automatica

Ogni push sul ramo `main` avvia la compilazione su GitHub Actions (file `.github/workflows/build.yml`)
per tutte e sette le versioni. I pacchetti si scaricano dalla scheda **Actions** del repository
(sezione *Artifacts*). Con un tag `v*` (per esempio `v0.1.0`) i pacchetti vengono pubblicati anche
nella pagina **Releases**.

| Pacchetto | Contenuto |
|---|---|
| `IP-TRX-CTRL-win-x64.zip`, `IP-TRX-CTRL-win-x86.zip` | `IP-TRX-CTRL.exe` con Hamlib incluso |
| `IP-TRX-CTRL-linux-x64.tar.gz`, `-linux-arm64`, `-linux-arm` | eseguibile `IP-TRX-CTRL` |
| `IP-TRX-CTRL-osx-arm64.zip`, `-osx-x64` | `IP-TRX-CTRL.app` (firma ad-hoc) |

Su macOS, al primo avvio: clic destro sull'app › Apri (l'app non è notarizzata).

## Compilare a mano

Serve il .NET SDK 8:

```
dotnet publish src/IpTrxCtrl/IpTrxCtrl.csproj -c Release -r <identificativo> -o dist/<identificativo>
```

Per includere Hamlib nella versione Windows, prima: `pwsh scripts/get_hamlib.ps1 -Arch w64` (oppure `w32`).
