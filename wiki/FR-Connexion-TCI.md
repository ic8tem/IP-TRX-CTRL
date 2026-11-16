# Connexion TCI

Depuis la 0.2.5, le serveur peut commander la radio avec le **TCI** (Transceiver Control Interface) au lieu de Hamlib. Le TCI est le protocole d'Expert Electronics (ExpertSDR2, ExpertSDR3), utilisé aussi par Thetis et d'autres programmes SDR : le programme de la radio fait office de serveur WebSocket, en général sur le même PC, port **40001**.

## Réglage

**Impostazioni › Radio (server) › Collegamento CAT** (réglages › radio › liaison CAT) → **TCI (ExpertSDR, Thetis e altri SDR)** :

- **Indirizzo** (adresse) du programme de la radio (par défaut `127.0.0.1`) et **porta TCI** (port TCI, par défaut `40001`) ;
- **Audio della radio via TCI** (audio de la radio via TCI) : l'audio de réception et d'émission passe par le TCI, sans cartes son ni câbles virtuels sur le serveur.

Dans le programme de la radio, activez le serveur TCI (ExpertSDR : Options › TCI ; Thetis : Setup › Network › TCI).

## Ce qui passe par le TCI

| Fonction | Commandes TCI |
|---|---|
| fréquence VFO A et VFO B | `vfo` |
| mode (USB, LSB, CW, CW-R, AM, FM, DIGU, DIGL…) | `modulation` |
| largeur du filtre (filtre large en CW) | `rx_filter_band` |
| split | `split_enable` |
| puissance TX en % | `drive` |
| PTT («CAT» ou «TCI») | `trx` (avec l'audio TCI : `trx:0,true,tci`) |
| S-mètre | `rx_smeter` / `rx_channel_sensors` |
| puissance de sortie et **ROS** (aussi pour la protection ROS) | `tx_sensors` |
| texte CW et vitesse | `cw_macros`, `cw_macros_stop`, `cw_macros_speed` |
| audio | flux binaires : réception, émission sur demande (`TX_CHRONO`), à 8 kHz mono quand le programme l'accepte, sinon converti |

Le serveur crée un petit «rigctld» interne qui traduit les commandes Hamlib en TCI : ainsi le reste du programme fonctionne comme avec Hamlib, y compris les **programmes numériques** du client (WSJT-X, JTDX, fldigi…). Pour les commandes qui ne concernent pas la radio (`\dump_state`, `\chk_vfo`), le serveur lance un `rigctld` de test (modèle 1, «Dummy») ; si Hamlib n'est pas installé, le TCI fonctionne quand même, mais les programmes numériques pourraient ne pas se connecter.

## Limites

- Le **pitch CW** ne passe pas par le TCI : réglez-le de la même façon dans le programme et dans la radio.
- La puissance se règle en **pourcentage** (le TCI ne donne pas la puissance maximale en watts).
- Le ROS et la puissance de sortie n'arrivent que si le programme de la radio envoie `tx_sensors` (ExpertSDR avec TCI 1.9 ou ultérieur) ; sans eux, la protection ROS ne peut pas agir.
- Avec l'audio TCI, les cartes du serveur sont toutes deux «TCI · audio della radio» : on ne peut pas avoir un sens sur le TCI et l'autre sur une carte.
- Dans le fichier `sessioni.log`, les lignes `TCI` indiquent quand la liaison s'ouvre, est prête ou tombe.

## Sources

Il n'existe pas de spécification publique unique consultable d'ici : le programme suit trois sources, comparées entre elles. Là où elles divergent, il lit format, canaux et fréquence dans l'en-tête de chaque bloc audio au lieu de les supposer.

| Source | Ce qu'elle confirme | Où elle concorde | Où elle diffère |
|---|---|---|---|
| [Hamlib TCI Audio Sidechannel Protocol](https://raw.githubusercontent.com/jfrancis42/hamlib-tci-sidecar/HEAD/PROTOCOL.md) | en-tête audio de 64 octets (16 entiers de 32 bits : récepteur, fréquence, format, codec, crc, longueur, type, canaux), formats 0–3, «longueur» en échantillons par canal, réponse à `TX_CHRONO` avec exactement ces échantillons | types de flux 1–3 identiques à JTDX | c'est le protocole d'un programme d'appoint pour Hamlib calqué sur le TCI, pas le TCI lui-même ; il ajoute un type 4 (état du PTT) que le TCI n'a pas |
| [JTDX, interface TCI](https://deepwiki.com/jtdx-project/jtdx/4.1-tci-transceiver-interface) | types de flux 0–3 (IQ, audio reçu, audio à émettre, `TX_CHRONO`), commandes `vfo`, `modulation`, `trx`, `split_enable`, `drive`, `audio_start`, `audio_samplerate` | types de flux identiques à la première source ; commandes identiques à la troisième | résumé généré à partir du code de JTDX, sans l'ordre des champs de l'en-tête ; indique un audio typique à 48 kHz stéréo float32 |
| [PE5JW, aperçu du protocole TCI](https://github.com/pe5jw/tci-protocol-overview) | syntaxe des commandes audio (`audio_stream_sample_type`, `audio_stream_channels`), `trx` avec la source `tci`, capteurs `rx_smeter`, `tx_sensors`, alias utilisés par Thetis et d'autres | commandes et modes identiques à JTDX ; ROS en 5e champ de `tx_sensors` | décrit un en-tête différent (il prévient qu'il varie selon les programmes) ; pour `tx_sensors` il indique puissance directe et réfléchie là où d'autres descriptions indiquent puissance efficace et de crête |

Conséquences dans le programme : de la ligne `tx_sensors` il n'utilise que le 3e champ (puissance de sortie) et le 5e (ROS), sur lesquels les sources concordent ; format, canaux et fréquence de l'audio sont lus dans chaque bloc.


---

[Sommaire](Français) · Autres langues: [Italiano](IT-Collegamento-TCI) · [English](EN-TCI-connection) · [Español](ES-Conexión-TCI) · [Deutsch](DE-TCI-Verbindung)
