# Plateformes et compilation

IP-TRX-CTRL est écrit en **C# (.NET 8)** avec l'interface **Avalonia**, pour avoir un seul code source sur tous les systèmes.

## Exécutables

| Système | Identifiant .NET |
|---|---|
| Windows 64 bits | `win-x64` |
| Windows 32 bits | `win-x86` |
| Windows ARM (Snapdragon) | `win-arm64` |
| macOS Apple Silicon | `osx-arm64` |
| macOS Intel | `osx-x64` |
| Linux 64 bits | `linux-x64` |
| Raspberry Pi 64 bits | `linux-arm64` |
| Raspberry Pi 32 bits | `linux-arm` |
| Alpine et autres Linux musl, 64 bits | `linux-musl-x64` |
| Alpine et autres Linux musl, ARM64 | `linux-musl-arm64` |

Chaque exécutable est autonome (pas besoin d'installer .NET) et tient en un seul fichier.

## Composants par système

| Fonction | Solution |
|---|---|
| Interface | Avalonia |
| Audio | natif, à 8 kHz : MME (winmm) sous Windows, ALSA sous Linux/Raspberry (avec PipeWire/PulseAudio via le périphérique «default»), AudioQueue sous macOS |
| Clés enregistrées | Windows : DPAPI · macOS : Trousseau · Linux/Raspberry : fichier chiffré lisible seulement par l'utilisateur |
| Commande de la radio | Hamlib `rigctld` : inclus pour Windows 64 et 32 bits et ARM (pour ARM la version x64, exécutée par l'émulation de Windows 11) ; sous Linux/Raspberry le paquet du système (`sudo apt install libhamlib-utils`) ; sous macOS Homebrew (`brew install hamlib`) |
| Série RTS/DTR, MQTT, UPnP, chiffrement | bibliothèques .NET déjà multiplateformes |

## Limites connues

- **Raspberry Pi** : il faut un Pi 2, 3, 4 ou 5 ; .NET ne fonctionne pas sur Pi Zero et Pi 1.
- **Linux avec Wayland** : les touches globales Ctrl+Alt+F1…F8 ne sont en général pas autorisées par le système ; elles marchent avec X11. Avec Wayland restent F1…F8 avec la fenêtre au premier plan.
- **macOS** : au premier lancement le système demande l'autorisation du micro et, pour les touches globales, celle d'«Accessibilité». Pour distribuer le programme sans avertissements, il faut le signer et le notariser, ce qui demande un Mac.

## Compilation automatique

Chaque push sur la branche `main` lance la compilation sur GitHub Actions (fichier `.github/workflows/build.yml`) pour les onze versions. Les paquets se téléchargent depuis l'onglet **Actions** du dépôt (section *Artifacts*) et sont publiés sur la page **Releases** avec le numéro de version écrit dans le fichier `VERSION` : pour une nouvelle version, il suffit de changer ce numéro.

| Système | Installateur ou paquet | Version portable |
|---|---|---|
| Windows 64 bits | `IP-TRX-CTRL-<version>-win-x64-setup.exe` | `…-win-x64-portatile.zip` |
| Windows 32 bits | `IP-TRX-CTRL-<version>-win-x86-setup.exe` | `…-win-x86-portatile.zip` |
| Windows ARM | `IP-TRX-CTRL-<version>-win-arm64-setup.exe` | `…-win-arm64-portatile.zip` |
| Linux 64 bits | `ip-trx-ctrl_<version>_amd64.deb`, `ip-trx-ctrl-<version>.x86_64.rpm`, `ip-trx-ctrl-<version>-1-x86_64.pkg.tar.zst`, `ip-trx-ctrl-<version>-x86_64-1_ic8tem.txz`, `IP-TRX-CTRL-<version>-x86_64.AppImage` | `…-linux-x64-portatile.tar.gz` |
| Raspberry Pi / Linux ARM64 | `ip-trx-ctrl_<version>_arm64.deb`, `….aarch64.rpm`, `…-aarch64.pkg.tar.zst`, `…-aarch64-1_ic8tem.txz`, `…-aarch64.AppImage` | `…-linux-arm64-portatile.tar.gz` |
| Raspberry Pi / Linux ARM 32 bits | `ip-trx-ctrl_<version>_armhf.deb`, `….armv7hl.rpm` | `…-linux-arm-portatile.tar.gz` |
| Alpine (musl) | — | `…-linux-musl-x64-portatile.tar.gz`, `…-linux-musl-arm64-portatile.tar.gz` |
| macOS Apple Silicon | `IP-TRX-CTRL-<version>-osx-arm64.dmg` | `…-osx-arm64-app.zip` |
| macOS Intel | `IP-TRX-CTRL-<version>-osx-x64.dmg` | `…-osx-x64-app.zip` |

Les paquets `.rpm`, Arch et Slackware sont dérivés du `.deb` (mêmes fichiers dans `/opt/ip-trx-ctrl`) ; les scripts sont dans `packaging/linux`. Dans les paquets système, le fichier `/opt/ip-trx-ctrl/PACCHETTO` indique au programme de ne pas se mettre à jour seul. Sous Linux x86_64, ARM64 et Alpine, la compilation vérifie aussi le démarrage de l'exécutable (`IP-TRX-CTRL --versione`).

Le `.deb` s'installe avec `sudo apt install ./ip-trx-ctrl_<version>_<arch>.deb` : il installe aussi Hamlib (`libhamlib-utils`) et crée l'entrée dans le menu des applications. Pour utiliser le port série de la radio, ajoutez votre utilisateur au groupe `dialout` (`sudo usermod -aG dialout $USER`, puis déconnectez-vous et reconnectez-vous).

Sous macOS, au premier lancement : clic droit sur l'app › Ouvrir (l'app n'est pas notarisée).

## Compiler à la main

Il faut le .NET SDK 8 :

```
dotnet publish src/IpTrxCtrl/IpTrxCtrl.csproj -c Release -r <identifiant> -o dist/<identifiant>
```

Pour inclure Hamlib dans la version Windows, d'abord : `pwsh scripts/get_hamlib.ps1 -Arch w64` (ou `w32`).


---

[Sommaire](Français) · Autres langues: [Italiano](IT-Piattaforme-e-compilazione) · [English](EN-Platforms-and-building) · [Español](ES-Plataformas-y-compilación) · [Deutsch](DE-Plattformen-und-Kompilierung)
