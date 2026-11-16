# Plataformas y compilación

IP-TRX-CTRL está escrito en **C# (.NET 8)** con interfaz **Avalonia**, para tener un único código fuente en todos los sistemas.

## Ejecutables

| Sistema | Identificador .NET |
|---|---|
| Windows 64 bits | `win-x64` |
| Windows 32 bits | `win-x86` |
| Windows ARM (Snapdragon) | `win-arm64` |
| macOS Apple Silicon | `osx-arm64` |
| macOS Intel | `osx-x64` |
| Linux 64 bits | `linux-x64` |
| Raspberry Pi 64 bits | `linux-arm64` |
| Raspberry Pi 32 bits | `linux-arm` |
| Alpine y otros Linux con musl, 64 bits | `linux-musl-x64` |
| Alpine y otros Linux con musl, ARM64 | `linux-musl-arm64` |

Cada ejecutable es autónomo (no hace falta instalar .NET) y en un solo archivo.

## Componentes por sistema

| Función | Solución |
|---|---|
| Interfaz | Avalonia |
| Audio | nativo, a 8 kHz: MME (winmm) en Windows, ALSA en Linux/Raspberry (con PipeWire/PulseAudio mediante el dispositivo «default»), AudioQueue en macOS |
| Claves guardadas | Windows: DPAPI · macOS: Llavero · Linux/Raspberry: archivo cifrado legible solo por el usuario |
| Control de la radio | Hamlib `rigctld`: incluido para Windows 64 y 32 bits y ARM (para ARM la versión x64, ejecutada por la emulación de Windows 11); en Linux/Raspberry el paquete del sistema (`sudo apt install libhamlib-utils`); en macOS Homebrew (`brew install hamlib`) |
| Serie RTS/DTR, MQTT, UPnP, cifrado | bibliotecas .NET ya multiplataforma |

## Límites conocidos

- **Raspberry Pi**: hace falta una Pi 2, 3, 4 o 5; .NET no funciona en Pi Zero ni Pi 1.
- **Linux con Wayland**: las teclas globales Ctrl+Alt+F1…F8 normalmente no las permite el sistema; funcionan con X11. Con Wayland quedan F1…F8 con la ventana en primer plano.
- **macOS**: al primer inicio el sistema pide permiso para el micrófono y, para las teclas globales, el de «Accesibilidad». Para distribuir el programa sin avisos hay que firmarlo y notarizarlo, operación que requiere un Mac.

## Compilación automática

Cada push a la rama `main` inicia la compilación en GitHub Actions (archivo `.github/workflows/build.yml`) para las once versiones. Los paquetes se descargan de la pestaña **Actions** del repositorio (sección *Artifacts*) y se publican en la página **Releases** con el número de versión escrito en el archivo `VERSION`: para una versión nueva basta cambiar ese número.

| Sistema | Instalador o paquete | Versión portátil |
|---|---|---|
| Windows 64 bits | `IP-TRX-CTRL-<versión>-win-x64-setup.exe` | `…-win-x64-portatile.zip` |
| Windows 32 bits | `IP-TRX-CTRL-<versión>-win-x86-setup.exe` | `…-win-x86-portatile.zip` |
| Windows ARM | `IP-TRX-CTRL-<versión>-win-arm64-setup.exe` | `…-win-arm64-portatile.zip` |
| Linux 64 bits | `ip-trx-ctrl_<versión>_amd64.deb`, `ip-trx-ctrl-<versión>.x86_64.rpm`, `ip-trx-ctrl-<versión>-1-x86_64.pkg.tar.zst`, `ip-trx-ctrl-<versión>-x86_64-1_ic8tem.txz`, `IP-TRX-CTRL-<versión>-x86_64.AppImage` | `…-linux-x64-portatile.tar.gz` |
| Raspberry Pi / Linux ARM64 | `ip-trx-ctrl_<versión>_arm64.deb`, `….aarch64.rpm`, `…-aarch64.pkg.tar.zst`, `…-aarch64-1_ic8tem.txz`, `…-aarch64.AppImage` | `…-linux-arm64-portatile.tar.gz` |
| Raspberry Pi / Linux ARM 32 bits | `ip-trx-ctrl_<versión>_armhf.deb`, `….armv7hl.rpm` | `…-linux-arm-portatile.tar.gz` |
| Alpine (musl) | — | `…-linux-musl-x64-portatile.tar.gz`, `…-linux-musl-arm64-portatile.tar.gz` |
| macOS Apple Silicon | `IP-TRX-CTRL-<versión>-osx-arm64.dmg` | `…-osx-arm64-app.zip` |
| macOS Intel | `IP-TRX-CTRL-<versión>-osx-x64.dmg` | `…-osx-x64-app.zip` |

Los paquetes `.rpm`, Arch y Slackware se obtienen del `.deb` (mismos archivos en `/opt/ip-trx-ctrl`); los scripts están en `packaging/linux`. En los paquetes de sistema el archivo `/opt/ip-trx-ctrl/PACCHETTO` le dice al programa que no se actualice solo. En Linux x86_64, ARM64 y Alpine la compilación prueba también el arranque del ejecutable (`IP-TRX-CTRL --versione`).

El `.deb` se instala con `sudo apt install ./ip-trx-ctrl_<versión>_<arch>.deb`: instala también Hamlib (`libhamlib-utils`) y crea la entrada en el menú de aplicaciones. Para usar el puerto serie de la radio añade tu usuario al grupo `dialout` (`sudo usermod -aG dialout $USER`, luego cierra la sesión y vuelve a entrar).

En macOS, al primer inicio: clic derecho en la app › Abrir (la app no está notarizada).

## Compilar a mano

Hace falta el .NET SDK 8:

```
dotnet publish src/IpTrxCtrl/IpTrxCtrl.csproj -c Release -r <identificador> -o dist/<identificador>
```

Para incluir Hamlib en la versión Windows, antes: `pwsh scripts/get_hamlib.ps1 -Arch w64` (o `w32`).


---

[Índice](Español) · Otros idiomas: [Italiano](IT-Piattaforme-e-compilazione) · [English](EN-Platforms-and-building) · [Français](FR-Plateformes-et-compilation) · [Deutsch](DE-Plattformen-und-Kompilierung)
