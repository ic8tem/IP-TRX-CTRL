# Plattformen und Kompilierung

IP-TRX-CTRL ist in **C# (.NET 8)** mit der Oberfläche **Avalonia** geschrieben, um auf allen Systemen einen einzigen Quellcode zu haben.

## Ausführbare Dateien

| System | .NET-Kennung |
|---|---|
| Windows 64 Bit | `win-x64` |
| Windows 32 Bit | `win-x86` |
| Windows ARM (Snapdragon) | `win-arm64` |
| macOS Apple Silicon | `osx-arm64` |
| macOS Intel | `osx-x64` |
| Linux 64 Bit | `linux-x64` |
| Raspberry Pi 64 Bit | `linux-arm64` |
| Raspberry Pi 32 Bit | `linux-arm` |
| Alpine und andere Linux mit musl, 64 Bit | `linux-musl-x64` |
| Alpine und andere Linux mit musl, ARM64 | `linux-musl-arm64` |

Jede ausführbare Datei ist eigenständig (.NET muss nicht installiert werden) und besteht aus einer einzigen Datei.

## Komponenten je System

| Funktion | Lösung |
|---|---|
| Oberfläche | Avalonia |
| Audio | nativ, mit 8 kHz: MME (winmm) unter Windows, ALSA unter Linux/Raspberry (mit PipeWire/PulseAudio über das Gerät «default»), AudioQueue unter macOS |
| Gespeicherte Schlüssel | Windows: DPAPI · macOS: Schlüsselbund · Linux/Raspberry: verschlüsselte Datei, nur für den Benutzer lesbar |
| Gerätesteuerung | Hamlib `rigctld`: enthalten für Windows 64 und 32 Bit und ARM (für ARM die x64-Version, ausgeführt von der Emulation von Windows 11); unter Linux/Raspberry das Systempaket (`sudo apt install libhamlib-utils`); unter macOS Homebrew (`brew install hamlib`) |
| Seriell RTS/DTR, MQTT, UPnP, Verschlüsselung | bereits plattformübergreifende .NET-Bibliotheken |

## Bekannte Grenzen

- **Raspberry Pi**: nötig ist ein Pi 2, 3, 4 oder 5; .NET läuft nicht auf Pi Zero und Pi 1.
- **Linux mit Wayland**: die globalen Tasten Strg+Alt+F1…F8 erlaubt das System meist nicht; mit X11 funktionieren sie. Mit Wayland bleiben F1…F8 bei Fenster im Vordergrund.
- **macOS**: beim ersten Start fragt das System nach der Mikrofonberechtigung und, für die globalen Tasten, nach «Bedienungshilfen». Um das Programm ohne Warnungen zu verteilen, muss es signiert und notarisiert werden, wofür ein Mac nötig ist.

## Automatische Kompilierung

Jeder Push auf den Zweig `main` startet die Kompilierung auf GitHub Actions (Datei `.github/workflows/build.yml`) für alle elf Versionen. Die Pakete lassen sich im Reiter **Actions** des Repositorys (Abschnitt *Artifacts*) herunterladen und werden auf der Seite **Releases** mit der Versionsnummer aus der Datei `VERSION` veröffentlicht: für eine neue Version genügt es, diese Nummer zu ändern.

| System | Installer oder Paket | Portable Version |
|---|---|---|
| Windows 64 Bit | `IP-TRX-CTRL-<Version>-win-x64-setup.exe` | `…-win-x64-portatile.zip` |
| Windows 32 Bit | `IP-TRX-CTRL-<Version>-win-x86-setup.exe` | `…-win-x86-portatile.zip` |
| Windows ARM | `IP-TRX-CTRL-<Version>-win-arm64-setup.exe` | `…-win-arm64-portatile.zip` |
| Linux 64 Bit | `ip-trx-ctrl_<Version>_amd64.deb`, `ip-trx-ctrl-<Version>.x86_64.rpm`, `ip-trx-ctrl-<Version>-1-x86_64.pkg.tar.zst`, `ip-trx-ctrl-<Version>-x86_64-1_ic8tem.txz`, `IP-TRX-CTRL-<Version>-x86_64.AppImage` | `…-linux-x64-portatile.tar.gz` |
| Raspberry Pi / Linux ARM64 | `ip-trx-ctrl_<Version>_arm64.deb`, `….aarch64.rpm`, `…-aarch64.pkg.tar.zst`, `…-aarch64-1_ic8tem.txz`, `…-aarch64.AppImage` | `…-linux-arm64-portatile.tar.gz` |
| Raspberry Pi / Linux ARM 32 Bit | `ip-trx-ctrl_<Version>_armhf.deb`, `….armv7hl.rpm` | `…-linux-arm-portatile.tar.gz` |
| Alpine (musl) | — | `…-linux-musl-x64-portatile.tar.gz`, `…-linux-musl-arm64-portatile.tar.gz` |
| macOS Apple Silicon | `IP-TRX-CTRL-<Version>-osx-arm64.dmg` | `…-osx-arm64-app.zip` |
| macOS Intel | `IP-TRX-CTRL-<Version>-osx-x64.dmg` | `…-osx-x64-app.zip` |

Die Pakete `.rpm`, Arch und Slackware werden aus dem `.deb` abgeleitet (dieselben Dateien in `/opt/ip-trx-ctrl`); die Skripte liegen in `packaging/linux`. In den Systempaketen sagt die Datei `/opt/ip-trx-ctrl/PACCHETTO` dem Programm, sich nicht selbst zu aktualisieren. Unter Linux x86_64, ARM64 und Alpine prüft die Kompilierung auch den Start der ausführbaren Datei (`IP-TRX-CTRL --versione`).

Das `.deb` wird mit `sudo apt install ./ip-trx-ctrl_<Version>_<arch>.deb` installiert: es installiert auch Hamlib (`libhamlib-utils`) und legt den Eintrag im Anwendungsmenü an. Um die serielle Schnittstelle des Geräts zu nutzen, den eigenen Benutzer zur Gruppe `dialout` hinzufügen (`sudo usermod -aG dialout $USER`, dann ab- und wieder anmelden).

Unter macOS beim ersten Start: Rechtsklick auf die App › Öffnen (die App ist nicht notarisiert).

## Von Hand kompilieren

Nötig ist das .NET SDK 8:

```
dotnet publish src/IpTrxCtrl/IpTrxCtrl.csproj -c Release -r <Kennung> -o dist/<Kennung>
```

Um Hamlib in die Windows-Version aufzunehmen, vorher: `pwsh scripts/get_hamlib.ps1 -Arch w64` (oder `w32`).


---

[Inhalt](Deutsch) · Andere Sprachen: [Italiano](IT-Piattaforme-e-compilazione) · [English](EN-Platforms-and-building) · [Español](ES-Plataformas-y-compilación) · [Français](FR-Plateformes-et-compilation)
