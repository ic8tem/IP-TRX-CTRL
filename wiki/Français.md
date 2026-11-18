# IP-TRX-CTRL

IP-TRX-CTRL permet d'utiliser une radio HF à distance, via internet, comme si elle était sur votre bureau. Un seul programme tourne sur deux PC :

- **serveur** : le PC relié à la radio (audio, CAT via Hamlib/rigctld, PTT) ;
- **client** : le PC de l'opérateur, qui reçoit l'audio de la radio, émet avec son propre micro et commande la radio.

Les rôles se choisissent au premier lancement et peuvent être inversés à tout moment.

> **Version actuelle : 0.3.5.** Les images sont de vraies captures du programme (un client connecté à un serveur avec une radio simulée par Hamlib). L'interface du programme est en italien : ces pages donnent la signification de chaque libellé.

## En bref

- Audio bidirectionnel toujours à **8 kHz, 16 bits mono**, chiffré en AES-256.
- Ports, clés et reconnexions gérés automatiquement ; le serveur est retrouvé même quand son adresse IP change, grâce à un broker MQTT public.
- Commande CAT de la radio : fréquence, mode, VFO A/B avec split, puissance TX, lectures du S-mètre, de la puissance et du ROS, protection ROS avec blocage de l'émission.
- **Keyer** intégré au client (issu du programme WavPTT) : en BLU huit messages WAV sur F1…F8, en CW huit macros de texte émises par la radio.
- En mode CW : cascade 0–2000 Hz avec la fréquence réelle des signaux, décodeur sur le pitch et fenêtres de décodage supplémentaires pour les autres signaux.
- Programmes numériques sur le client (WSJT-X, JTDX, fldigi…) via un rigctld local et des câbles audio virtuels : voir [Modes numériques, pédale PTT et manipulateur CW](FR-Modes-numériques-et-pédale).
- Pédale PTT et manipulateur CW mécanique sur un port série du client.
- Cycle des ports entre serveur et client (UPnP vérifié de l'extérieur, connexion inverse, 20 tours) et audio en UDP si possible : voir [Ports et liaison](FR-Ports-et-liaison).
- Depuis la 0.3.0 : sécurité en émission (durée maximale, pas de changement de fréquence en émission, priorité à celui qui est à la radio, clients en écoute seule), commandes de la radio (NR, NB, Notch, ATT, préampli, AGC, compresseur, squelch, ALC), bandes, pas et mémoires, le [panadapter de la radio](FR-Panadapter), rapport de problème et avis de nouvelle version. Voir [Radio, split et puissance](FR-Radio-split-et-puissance) et [Fenêtre principale](FR-Fenêtre-principale).
- Depuis la 0.3.1 : [accès depuis le navigateur](FR-Accès-depuis-le-navigateur) de téléphones, tablettes et PC, avec la page «Trova il server» (trouver le serveur).
- Depuis la 0.3.2 : mises à jour automatiques (pas pour les paquets .deb, .rpm, Arch et Slackware) et paquets pour Windows ARM, Fedora/openSUSE, Arch, Slackware, AppImage et Alpine : voir [Journaux et résolution des problèmes](FR-Journaux-et-résolution-des-problèmes).
- Depuis la 0.3.5 : paquets pour Windows 7 SP1, 8 et 8.1 (`-win7-x64-`, `-win7-x86-`, avec .NET 6) ; en haut de chaque release, le tableau avec le lien vers chaque paquet.
- Depuis la 0.3.4 : ports choisis par vous dans les réglages (avec la liste des deux ports à ouvrir) ; double NAT et UPnP d'un appareil qui n'est pas le routeur reconnus.
- Depuis la 0.3.3 : sous Linux, Raspberry Pi et Mac, la première configuration, les réglages et les licences ne se bloquent plus ; libellés ROS et Gain visibles.
- Six thèmes de couleur : Chiaro, Notte, Nero, Blu, Viola, Verde (clair, nuit, noir, bleu, violet, vert) : voir [Thèmes de couleur](FR-Thèmes-de-couleur).
- Un seul code source pour **Windows 64 bits, 32 bits et ARM, macOS, Linux (Alpine aussi) et Raspberry Pi**.

Avant de mettre à jour l'un des deux PC, consultez les [Versions compatibles](FR-Versions-compatibles).

![Client connecté, thème Chiaro](https://raw.githubusercontent.com/ic8tem/IP-TRX-CTRL/video-corso/wiki-images/main.png)

## Pages

- [Cours vidéo](FR-Cours-vidéo)
- [Fenêtre principale](FR-Fenêtre-principale)
- [Appairage et MQTT](FR-Appairage-et-MQTT)
- [Ports et liaison](FR-Ports-et-liaison)
- [Radio, split et puissance](FR-Radio-split-et-puissance)
- [Audio et cartes son du serveur](FR-Audio-et-cartes-son-du-serveur)
- [Keyer, messages WAV et macros CW](FR-Keyer-messages-WAV-et-macros-CW)
- [Mode CW](FR-Mode-CW)
- [Panadapter](FR-Panadapter)
- [Modes numériques et pédale](FR-Modes-numériques-et-pédale)
- [Connexion TCI](FR-Connexion-TCI)
- [Accès depuis le navigateur](FR-Accès-depuis-le-navigateur)
- [Thèmes de couleur](FR-Thèmes-de-couleur)
- [Journaux et résolution des problèmes](FR-Journaux-et-résolution-des-problèmes)
- [Versions compatibles](FR-Versions-compatibles)
- [Plateformes et compilation](FR-Plateformes-et-compilation)
- [Licences](FR-Licences)


---

[Sommaire](Français) · Autres langues: [Italiano](Italiano) · [English](English) · [Español](Español) · [Deutsch](Deutsch)
