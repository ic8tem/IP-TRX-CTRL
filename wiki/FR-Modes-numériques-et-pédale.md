# Modes numériques, pédale PTT et manipulateur CW

Ces fonctions concernent le **client** (le PC de l'opérateur).

## Programmes numériques (WSJT-X, JTDX, JS8Call, fldigi…)

Le client offre deux choses aux programmes numériques :

- **un rigctld local** sur `127.0.0.1:4532` : chaque commande CAT voyage chiffrée jusqu'au rigctld du serveur. Les commandes de PTT ne sont pas transmises : elles deviennent le PTT d'IP-TRX-CTRL, avec les mêmes sécurités que le bouton PTT ;
- **deux chemins audio** sur des câbles virtuels : la réception de la radio va vers le programme, et pendant l'émission demandée par le programme, l'audio du programme va à la radio à la place du micro.

On l'active dans Impostazioni › **Programmi digitali** (programmes numériques) › «Usa programmi digitali» (utiliser des programmes numériques).

### Réglages dans le programme numérique

| Élément | Valeur |
|---|---|
| Radio | Hamlib **NET rigctl** |
| Adresse | `127.0.0.1:4532` (le port se change dans les réglages d'IP-TRX-CTRL) |
| PTT | CAT |
| Entrée audio | le câble qu'IP-TRX-CTRL utilise comme «verso il programma» (vers le programme) |
| Sortie audio | le câble qu'IP-TRX-CTRL utilise comme «dal programma» (depuis le programme) |

### Câbles audio virtuels par système

| Système | Câble | Remarques |
|---|---|---|
| Windows 64 bits | **VB-CABLE** (VB-Audio, donationware) | L'installateur le propose en tâche facultative, le télécharge depuis le site du fabricant et le saute s'il est déjà installé. Un redémarrage est nécessaire après l'installation. |
| Windows 32 bits | — | Fonction non disponible. |
| macOS | **BlackHole 2ch** (MIT) | `brew install blackhole-2ch`, puis relancez IP-TRX-CTRL. |
| Linux / Raspberry Pi | créés par le programme | Deux «null sinks» `IPTRX_RX` et `IPTRX_TX` avec `pactl` (PipeWire ou PulseAudio, paquet `pulseaudio-utils`). Dans le programme numérique : entrée «Monitor of IP-TRX-CTRL_RX», sortie «IP-TRX-CTRL_TX». |

**Un seul câble** (VB-CABLE gratuit) : IP-TRX-CTRL écrit la réception sur «CABLE Input» et lit depuis «CABLE Output» ; le programme numérique fait de même. Cela fonctionne en **semi-duplex** : pendant que le programme émet, IP-TRX-CTRL cesse de lui envoyer la réception. **Deux câbles** (par exemple VB-CABLE A+B) : un par sens ; le bouton «Scegli i cavi in automatico» (choisir les câbles automatiquement) les attribue.

## Pédale PTT et manipulateur CW mécanique

Impostazioni › **Pedale PTT e tasto CW** (pédale PTT et manipulateur CW). Un port série du client (aussi un adaptateur USB-série) sert d'entrée :

| Contact | Câblage | Entrée à choisir |
|---|---|---|
| Pédale PTT | entre **RTS** (broche 7 du DB9) et **CTS** (broche 8) | CTS |
| Pioche | entre **DTR** (broche 4) et **DSR** (broche 6) | DSR |
| en alternative | entre DTR (broche 4) et **DCD** (broche 1) | DCD |

Le programme lève RTS et DTR et lit les entrées chaque milliseconde. La pédale agit comme le bouton PTT. Le manipulateur envoie au serveur chaque appui et relâchement avec son instant ; le serveur les répète sur le port CW de la radio avec un **retard fixe** (250 ms par défaut, réglable) qui absorbe les variations de retard d'internet, de sorte que la durée des points et des traits reste exacte. Le PTT de la radio s'active avant le premier élément et se relâche après 800 ms sans éléments ; un manipulateur resté appuyé plus de 10 secondes est relâché par sécurité.

**Port virtuel d'un autre programme.** À la place du matériel, on peut utiliser le port virtuel d'un programme qui commande PTT ou CW avec RTS et DTR. Sous Windows, il faut une paire de ports virtuels **com0com** : le programme utilise un port de la paire, IP-TRX-CTRL l'autre (RTS arrive sur CTS, DTR sur DSR). Sous Linux et macOS, les pseudo-ports (pty) n'ont pas de lignes de contrôle : il faut un vrai adaptateur.


---

[Sommaire](Français) · Autres langues: [Italiano](IT-Programmi-digitali-e-pedale) · [English](EN-Digital-modes-and-pedal) · [Español](ES-Modos-digitales-y-pedal) · [Deutsch](DE-Digitalbetrieb-und-Pedal)
