# Licences des composants tiers

IP-TRX-CTRL est distribué sous la **GNU Affero General Public License 3.0 ou ultérieure** (depuis la 0.3.6 ; jusqu'à la 0.3.5, GPL 3.0 ou ultérieure) : qui le modifie et fait utiliser sa version à d'autres par le réseau doit leur proposer aussi le code source.

IP-TRX-CTRL inclut ou utilise les composants listés ci-dessous. Le programme a une fenêtre **Informazioni e licenze** (informations et licences) avec les textes complets ; avant chaque distribution, titulaires et textes doivent être vérifiés sur les paquets réellement utilisés.

| Composant | Licence | Site |
|---|---|---|
| Hamlib (bibliothèque) | GNU LGPL 2.1 ou ultérieure | https://hamlib.github.io |
| Hamlib `rigctld`, `rigctl` | GNU GPL 2 ou ultérieure (inclus seulement dans la version Windows) | https://github.com/Hamlib/Hamlib |
| Avalonia | MIT | https://github.com/AvaloniaUI/Avalonia |
| Inter (police) | SIL Open Font License 1.1 | https://github.com/rsms/inter |
| MQTTnet | MIT | https://github.com/dotnet/MQTTnet |
| Open.NAT | MIT | https://github.com/lontivero/Open.NAT |
| Concentus (codec Opus, depuis la 0.3.6) | BSD-3-Clause | https://github.com/lostromb/concentus |
| HamNoise (réduction de bruit neuronale, depuis la 0.3.7) | MIT | https://github.com/e04/HamNoise |
| wfview (protocole Icom en réseau, réécrit en C#, depuis la 0.3.6) | GNU GPL 3.0 | https://gitlab.com/eliggett/wfview |
| .NET Runtime, System.IO.Ports | MIT | https://github.com/dotnet/runtime |

Remarques :

- `rigctld` et `rigctl` sont distribués comme programmes séparés, non modifiés, lancés en arrière-plan et utilisés via TCP. Les sources de la version incluse se trouvent sur la page Releases du dépôt Hamlib.
- L'audio et les touches globales utilisent directement les fonctions du système d'exploitation (winmm/Core Audio, ALSA/libX11, CoreAudio/Carbon) : aucune bibliothèque supplémentaire.
- Le code du keyer vient de WavPTT, du même auteur, et n'ajoute aucune contrainte.
- Le décodeur CW est écrit de zéro.
- Les noms des brokers MQTT publics ne servent que de point de rencontre ; aucune donnée n'est publiée en clair.


---

[Sommaire](Français) · Autres langues: [Italiano](IT-Licenze) · [English](EN-Licenses) · [Español](ES-Licencias) · [Deutsch](DE-Lizenzen)
