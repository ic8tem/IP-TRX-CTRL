# Nouveautés de la 0.3.7

La 0.3.7 reprend les meilleures solutions d'autres programmes de commande à distance : remoteAudio (DH1TW), PiRO (VK3TR), RustyRig, RigControl (K6BP) et CAESAR (RA0SMS). Ce qu'IP-TRX-CTRL avait déjà (Opus, plusieurs auditeurs, STUN et UPnP, port rigctl, panadapter, macros CW, Icom en réseau) n'a pas été dupliqué.

## VOX sur le client (de remoteAudio)

Bouton **VOX** à côté du PTT du client : c'est le seul moyen de l'activer, et il est éteint à chaque démarrage. Le PTT passe seul en émission quand vous parlez et retombe après le temps de maintien. Seuil (dBFS) et maintien (ms) dans **Impostazioni › VOX, squelch vocale e registrazione**. Utilisez un casque : le programme reconnaît et ignore l'audio de la radio qui revient dans le micro, mais avec un haut-parleur le VOX reste peu fiable. Les sécurités du PTT s'appliquent (durée maximale, ROS, priorité à qui est à la radio).

## Débit Opus (de remoteAudio)

Les Réglages permettent de choisir la bande de l'audio compressé : 8, 12, **16** (par défaut), 24 ou 32 kbit/s.

## Notes entre opérateur et auditeurs (de PiRO et RustyRig)

Onglet **Note** : messages courts (« sur 40 m en FT8 jusqu'à 16 h 30 », « je peux la prendre après le net ? »), aussi depuis la page web. Le serveur les garde une heure et les envoie à qui se connecte ensuite.

## Les auditeurs entendent aussi l'opérateur (de PiRO)

Avec des auditeurs connectés, la voix de l'opérateur qui émet leur parvient aussi, comme dans un net. Pas en modes données ni en CW. À désactiver dans **Impostazioni › Sicurezza in trasmissione (server)**.

## Réduction de bruit neuronale (HamNoise, de PiRO)

**Audio › NR neurale** (client) : le réseau neuronal [HamNoise](https://github.com/e04/HamNoise) de e04, entraîné sur des signaux HF, le même que le bouton « HamNoise » de PiRO. Un modèle pour la phonie (SSB, AM, FM) et un pour la CW, choisis selon le mode de la radio ; il n'agit pas en modes données. La valeur à côté (25-100 %) règle la force du filtrage : sur les signaux faibles, une valeur plus basse laisse passer plus de signal. Il tourne sur le PC client, ajoute environ 60 ms de retard et très peu de charge.

## Squelch vocal (de PiRO)

**Audio › Squelch vocale** (client) : en SSB, AM et FM il coupe le souffle entre deux interventions. Il s'ouvre quand il reconnaît la périodicité d'une voix, pas sur les porteuses, les tonalités CW ou les parasites : aucun seuil à régler. Maintien réglable.

## Enregistrement de l'audio (de RustyRig)

**Audio › ● Registra** : un fichier WAV à 8 kHz avec réception et émission, dans le dossier `registrazioni` à côté des réglages (bouton « Cartella »). Avec **« Registra l'audio di ogni QSO »** l'enregistrement démarre seul quand vous tapez un indicatif dans le [journal](FR-Nouveautés-de-la-0.3.6) et s'arrête sur « Registra », avec l'indicatif dans le nom du fichier.

## Liaison IPv6 directe avec PCP (de RigControl)

En IPv6 il n'y a ni NAT ni CGNAT : si serveur et client ont une adresse IPv6 publique, le serveur écoute aussi dessus avec les mêmes ports et le client l'essaie en premier. Le serveur demande au routeur de laisser passer les deux ports avec **PCP** (RFC 6887). Sans PCP sur le routeur, autorisez les ports dans le pare-feu IPv6 du routeur pour le PC serveur ; si IPv6 ne répond pas, le client revient en IPv4 et ne réessaie pas pendant une demi-heure. À désactiver dans **Impostazioni › Porte aperte sul router**. Voir aussi [Ports et liaison](FR-Ports-et-liaison).

## Serveur sans fenêtre (de CAESAR)

Pour un Raspberry Pi ou un PC sans écran à côté de la radio : tout se règle une fois avec la fenêtre (rôle serveur, radio, audio, appairage ou code fixe), puis :

```
IP-TRX-CTRL --senza-finestra
```

Le paquet `.deb` installe aussi un service systemd qui démarre à l'allumage et redémarre tout seul :

```
sudo systemctl enable --now ip-trx-ctrl-server@UTILISATEUR
journalctl -u ip-trx-ctrl-server@UTILISATEUR -f
```

## WinKeyer pour les logiciels de log (de CAESAR)

**Impostazioni › WinKeyer per i programmi di log (client)**. N1MM, DXLog, TR4W, Win-Test, QLog et les autres voient un WinKeyer : le CW qu'ils envoient part de la radio à la vitesse demandée, et la touche Échap du logiciel l'arrête.

- **Windows** : il faut une paire de ports virtuels com0com (par exemple COM10 et COM11) : l'un dans IP-TRX-CTRL, l'autre dans le logiciel de log.
- **Linux et macOS** : avec le port **« virtuale »** le programme crée un port série virtuel ; dans le logiciel de log, indiquez le chemin `winkeyer` du dossier des réglages.

> Fonctions nouvelles, compilées mais pas encore essayées avec une vraie radio : en cas de problème, joignez le rapport de problème.


---

[Sommaire](Français) · Autres langues: [Italiano](IT-Funzioni-nuove-della-0.3.7) · [English](EN-New-in-0.3.7) · [Español](ES-Novedades-de-la-0.3.7) · [Deutsch](DE-Neu-in-0.3.7)
