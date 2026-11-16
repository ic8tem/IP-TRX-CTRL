# Audio et cartes son du serveur

## Audio toujours à 8 kHz

Tout l'audio est traité à **8 kHz, 16 bits mono** :

- le système d'exploitation ouvre micro et haut-parleur en mode partagé à la fréquence de la carte ; le programme convertit à 8 kHz dès l'arrivée du signal et ne revient à la fréquence de la carte que pour la sortie ;
- filtres, compresseur, mélange micro/messages, réseau et décodeur CW travaillent à 8 kHz ;
- la bande utile atteint environ 4 kHz, suffisante pour la BLU et pour la cascade CW 0–2000 Hz.

## Choisir les cartes son du serveur depuis le client

Dans le cadre **Schede audio del server** (cartes son du serveur), les lignes *Ingresso* (entrée) et *Uscita* (sortie) sont deux listes des cartes présentes sur le PC serveur :

- **Ingresso** : la carte où le serveur prend l'audio de la radio (réception) ;
- **Uscita** : la carte par laquelle le serveur envoie l'audio à la radio (émission).

Quand on choisit une carte depuis le client, le serveur redémarre son audio sur la nouvelle carte et enregistre le choix. Si la carte ne s'ouvre pas (débranchée, occupée, permission refusée), le serveur revient aussitôt aux cartes précédentes et le client voit un avertissement ; une ligne `AUDIO` reste dans le `sessioni.log` du serveur. La liste des cartes du serveur se met à jour toute seule toutes les 30 secondes, par exemple après le branchement d'une interface USB.

La même liste fonctionne aussi sur le serveur : y changer la carte revient à la changer dans le cadre de l'audio local.

Avec le CAT via TCI (depuis la 0.2.5), les listes proposent aussi **«TCI · audio della radio»** (TCI · audio de la radio) : choisi en entrée et en sortie, l'audio passe par le TCI du programme de la radio, sans cartes ni câbles virtuels (voir *Connexion TCI*).

## Sourdine des cartes son du serveur

Le client voit si les deux cartes son du serveur reliées à la radio (réception et émission) sont mises en sourdine par le système d'exploitation, ainsi que leur volume système, et peut :

- enlever la sourdine à distance ;
- régler le volume système ;
- activer **Togli il mute da solo** (enlever la sourdine automatiquement) : le serveur l'enlève dès que quelqu'un l'active et le signale au client.

Comment c'est fait sur chaque système :

| Système | Interface |
|---|---|
| Windows | Core Audio |
| macOS | CoreAudio |
| Linux / Raspberry Pi | PipeWire ou PulseAudio ; ALSA à défaut |

Limites :

- le programme serveur doit tourner dans la session de l'utilisateur, pas comme service ;
- si le système refuse l'accès au micro pour la confidentialité, le programme le signale mais ne le change pas : il faut le débloquer à la main sur le serveur une fois ;
- une sourdine interne au pilote USB de la radio n'est pas visible par le système d'exploitation.


---

[Sommaire](Français) · Autres langues: [Italiano](IT-Audio-e-schede-audio-del-server) · [English](EN-Audio-and-server-sound-cards) · [Español](ES-Audio-y-tarjetas-de-sonido-del-servidor) · [Deutsch](DE-Audio-und-Soundkarten-des-Servers)
