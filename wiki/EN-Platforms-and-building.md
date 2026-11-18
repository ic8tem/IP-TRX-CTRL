# Platforms and building

IP-TRX-CTRL is written in **C# (.NET 8)** with the **Avalonia** interface, to have a single source code on every system.

## Executables

| System | .NET identifier |
|---|---|
| Windows 64 bit | `win-x64` |
| Windows 32 bit | `win-x86` |
| Windows ARM (Snapdragon) | `win-arm64` |
| Windows 7 SP1, 8, 8.1 (64 and 32 bit) | `win-x64` and `win-x86` with .NET 6, packages `win7-x64` and `win7-x86` |
| macOS Apple Silicon | `osx-arm64` |
| macOS Intel | `osx-x64` |
| Linux 64 bit | `linux-x64` |
| Raspberry Pi 64 bit | `linux-arm64` |
| Raspberry Pi 32 bit | `linux-arm` |
| Alpine and other musl Linux, 64 bit | `linux-musl-x64` |
| Alpine and other musl Linux, ARM64 | `linux-musl-arm64` |

Each executable is self-contained (no need to install .NET) and a single file.

## Components per system

| Function | Solution |
|---|---|
| Interface | Avalonia |
| Audio | native, at 8 kHz: MME (winmm) on Windows, ALSA on Linux/Raspberry (with PipeWire/PulseAudio through the «default» device), AudioQueue on macOS |
| Stored keys | Windows: DPAPI · macOS: Keychain · Linux/Raspberry: encrypted file readable only by the user |
| Radio control | Hamlib `rigctld`: included for Windows 64-bit, 32-bit and ARM (for ARM the x64 version, run by Windows 11's emulation); on Linux/Raspberry the system package (`sudo apt install libhamlib-utils`); on macOS Homebrew (`brew install hamlib`) |
| Serial RTS/DTR, MQTT, UPnP, encryption | .NET libraries that are already cross-platform |

## Known limits

- **Raspberry Pi**: Pi 2, 3, 4 or 5 is needed; .NET does not run on Pi Zero and Pi 1.
- **Linux with Wayland**: the global keys Ctrl+Alt+F1…F8 are usually not allowed by the system; they work with X11. With Wayland, F1…F8 still work with the window in the foreground.
- **macOS**: on the first start the system asks for microphone permission and, for the global keys, «Accessibility» permission. Distributing the program without warnings requires signing and notarisation, which needs a Mac.

## Automatic builds

Every push to the `main` branch starts the build on GitHub Actions (file `.github/workflows/build.yml`) for all eleven versions. The packages can be downloaded from the repository's **Actions** tab (*Artifacts* section) and are published on the **Releases** page with the version number written in the `VERSION` file: for a new version, changing that number is enough.

| System | Installer or package | Portable version |
|---|---|---|
| Windows 64 bit | `IP-TRX-CTRL-<version>-win-x64-setup.exe` | `…-win-x64-portatile.zip` |
| Windows 32 bit | `IP-TRX-CTRL-<version>-win-x86-setup.exe` | `…-win-x86-portatile.zip` |
| Windows ARM | `IP-TRX-CTRL-<version>-win-arm64-setup.exe` | `…-win-arm64-portatile.zip` |
| Windows 7 SP1, 8, 8.1 | `IP-TRX-CTRL-<version>-win7-x64-setup.exe`, `…-win7-x86-setup.exe` | `…-win7-x64-portatile.zip`, `…-win7-x86-portatile.zip` |
| Linux 64 bit | `ip-trx-ctrl_<version>_amd64.deb`, `ip-trx-ctrl-<version>.x86_64.rpm`, `ip-trx-ctrl-<version>-1-x86_64.pkg.tar.zst`, `ip-trx-ctrl-<version>-x86_64-1_ic8tem.txz`, `IP-TRX-CTRL-<version>-x86_64.AppImage` | `…-linux-x64-portatile.tar.gz` |
| Raspberry Pi / Linux ARM64 | `ip-trx-ctrl_<version>_arm64.deb`, `….aarch64.rpm`, `…-aarch64.pkg.tar.zst`, `…-aarch64-1_ic8tem.txz`, `…-aarch64.AppImage` | `…-linux-arm64-portatile.tar.gz` |
| Raspberry Pi / Linux ARM 32 bit | `ip-trx-ctrl_<version>_armhf.deb`, `….armv7hl.rpm` | `…-linux-arm-portatile.tar.gz` |
| Alpine (musl) | — | `…-linux-musl-x64-portatile.tar.gz`, `…-linux-musl-arm64-portatile.tar.gz` |
| macOS Apple Silicon | `IP-TRX-CTRL-<version>-osx-arm64.dmg` | `…-osx-arm64-app.zip` |
| macOS Intel | `IP-TRX-CTRL-<version>-osx-x64.dmg` | `…-osx-x64-app.zip` |

The `.rpm`, Arch and Slackware packages are derived from the `.deb` (same files in `/opt/ip-trx-ctrl`); the scripts are in `packaging/linux`. In the system packages, the file `/opt/ip-trx-ctrl/PACCHETTO` tells the program not to update itself. On Linux x86_64, ARM64 and Alpine the build also tests that the executable starts (`IP-TRX-CTRL --versione`).

The `.deb` is installed with `sudo apt install ./ip-trx-ctrl_<version>_<arch>.deb`: it also installs Hamlib (`libhamlib-utils`) and creates the entry in the applications menu. To use the radio's serial port, add your user to the `dialout` group (`sudo usermod -aG dialout $USER`, then log out and back in).

On macOS, on the first start: right-click the app › Open (the app is not notarised).

## Building by hand

You need the .NET 8 SDK:

```
dotnet publish src/IpTrxCtrl/IpTrxCtrl.csproj -c Release -r <identifier> -o dist/<identifier>
```

To include Hamlib in the Windows version, first: `pwsh scripts/get_hamlib.ps1 -Arch w64` (or `w32`).


---

[Contents](English) · Other languages: [Italiano](IT-Piattaforme-e-compilazione) · [Español](ES-Plataformas-y-compilación) · [Français](FR-Plateformes-et-compilation) · [Deutsch](DE-Plattformen-und-Kompilierung)
