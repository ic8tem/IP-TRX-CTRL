# Nouveautés de la 0.3.6

## Audio Opus

Entre deux programmes 0.3.6 ou plus récents, l'audio est compressé avec **Opus** (16 kbit/s, voix à 8 kHz) au lieu du PCM : moins de bande passante, correction d'erreurs (FEC) et comblement des trous quand un paquet se perd. Avec les versions précédentes et avec la page web, l'audio reste en PCM à 8 kHz. Rien à régler.

## Plusieurs auditeurs sur le même serveur

**Impostazioni › Sicurezza in trasmissione (server) › « Altri client e pagine web possono collegarsi in solo ascolto »** (Réglages › Sécurité en émission (serveur) › « D'autres clients et pages web peuvent se connecter en écoute seule » ; actif par défaut). Pendant qu'un opérateur utilise la radio, jusqu'à **8** autres clients appairés ou pages web peuvent se connecter : ils entendent l'audio et voient fréquence et instruments, mais n'émettent pas et ne changent rien. Leur fenêtre affiche « tu sei in solo ascolto: trasmette X » (écoute seule : X émet) ; sur le serveur, le cadre de l'autre PC indique combien écoutent. Quand l'opérateur se déconnecte, les auditeurs sont déconnectés et le premier qui se reconnecte devient opérateur.

## Manipulateur CW USB et MIDI

**Impostazioni › Tasto CW USB e MIDI** (Réglages › Manipulateur CW USB et MIDI, client) :

- **Mode clavier** (VBand, Vail et similaires) : Ctrl gauche = point, Ctrl droit = trait.
- Manipulateur **iambique mode A ou B**, ou **pioche** ; vitesse 5–60 WPM ; inversion point/trait ; tonalité d'écoute sur le PC au pitch CW.
- **MIDI** (Vail en mode MIDI, TinyMIDI) : Do = pioche, Do# = point, Ré = trait. Windows et Linux (l'utilisateur doit être dans le groupe `audio`), pas sur Mac.

Le manipulateur tourne sur le client et envoie au serveur les temps de la clé, que le serveur répète avec le retard fixe de la clé CW (comme la clé mécanique sur port série, voir [Modes numériques et pédale](FR-Modes-numériques-et-pédale)).

La **page web** a aussi la clé : Réglages › Tasto CW, avec mode clavier (Ctrl ou `[` et `]`) et MIDI avec Chrome ou Edge.

## Journal des QSO

Bouton **« Registro »** en haut de la fenêtre principale : indicatif, RST envoyé et reçu, locator, nom, QTH et notes. Fréquence, bande, mode et heures viennent de la radio.

- **Registra** enregistre le QSO dans `registro.adi`, dans le dossier des réglages, et si c'est actif l'envoie aux logiciels de log avec le **protocole UDP de WSJT-X** (messages « QSO Logged » et « Logged ADIF », port 2237 par défaut) : GridTracker, JTAlert, Log4OM, N1MM, HRD et d'autres l'enregistrent comme un QSO de WSJT-X.
- **Esporta ADIF…** enregistre un fichier pour **LoTW** (TQSL), QRZ et Club Log.
- **Impostazioni › Registro dei QSO** : adresse, port et votre locator. Si WSJT-X est ouvert sur le même PC, ajoutez un second port dans le logiciel de log ou utilisez le relais multicast.

## Radios Icom en réseau

Pour IC-7610, IC-9700, IC-705, IC-7300MK2, IC-905 et les autres Icom avec réseau : le serveur parle à la radio comme RS-BA1 et wfview, **sans câbles USB et sans PC à côté de la radio**.

1. Sur la radio : Set › Network › **Network Control ON** et un utilisateur dans Network User1 (nom et mot de passe, 16 caractères au plus).
2. **Impostazioni › Radio Icom in rete (server)** : adresse de la radio, port 50001, utilisateur et mot de passe (gardé chiffré sur ce PC).
3. Dans **Radio (server)** : le modèle Hamlib de la radio et PTT « CAT ».
4. Enregistrez, rouvrez les Réglages et choisissez **« Icom LAN · audio della radio »** comme carte son du serveur (réception et émission à 8 kHz).

Le CAT passe par rigctld, relié à un pont interne vers le flux CI-V de la radio. Le protocole est réécrit en C# d'après [wfview](https://gitlab.com/eliggett/wfview) (GPL 3) et la documentation de [kappanhang](https://github.com/nonoo/kappanhang) ; voir [Licences](FR-Licences).

> Fonction nouvelle, testée jusqu'ici sans vraie radio : en cas de problème, joignez le rapport de problème (lignes ICOM de `sessioni.log`).


---

[Sommaire](Français) · Autres langues: [Italiano](IT-Funzioni-nuove-della-0.3.6) · [English](EN-New-in-0.3.6) · [Español](ES-Novedades-de-la-0.3.6) · [Deutsch](DE-Neu-in-0.3.6)
