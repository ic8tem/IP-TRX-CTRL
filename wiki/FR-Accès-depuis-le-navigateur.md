# Accès depuis le navigateur

Depuis la 0.3.1, le serveur peut faire office de petit site **HTTPS** : depuis le navigateur d'un téléphone, d'une tablette ou d'un PC, on commande la radio **sans rien installer**. Le programme client reste la voie complète (keyer, CW, modes numériques, connexion inverse) ; la page web apporte l'essentiel.

## Activation (sur le serveur)

**Impostazioni › Accesso dal browser (server)** (réglages › accès depuis le navigateur) :

- **Accesso dal browser** (accès depuis le navigateur) : désactivé à l'installation.
- **Porta HTTPS** (port HTTPS) : 8443 par défaut.
- **Apri la porta anche sul router (UPnP)** (ouvrir aussi le port sur le routeur) : pour entrer depuis internet sans VPN. Désactivé par défaut.
- **Il browser parte in solo ascolto** (le navigateur démarre en écoute seule) : on l'autorise ensuite dans la liste des clients (entrée «Browser»).

On se connecte avec le **code fixe du serveur** (12 chiffres, le même que pour l'appairage) : s'il n'existe pas encore, le serveur le crée. Changer le code fixe change aussi celui du navigateur.

## La page «Trova il server» (trouver le serveur)

C'est un seul fichier, `trova-server.html`, à garder **sur le téléphone ou sur le PC** : il n'est sur aucun site et l'application de bureau n'est pas nécessaire.

- On l'enregistre depuis **Impostazioni › «Salva la pagina «Trova il server»…»** (sur le serveur ou sur le client) ou, sur le réseau de la maison, depuis `https://<adresse du serveur>:8443/trova-server.html`.
- On l'ouvre dans le navigateur, on saisit le code fixe et on appuie sur **Trova il server**.
- La page demande aux brokers MQTT publics (broker.hivemq.com, broker.emqx.io, test.mosquitto.org, via WebSocket sécurisé) le message que le serveur dépose toutes les 10 minutes. Le message est **chiffré** (AES-256-GCM) avec une clé dérivée du code (PBKDF2-SHA256, 600 000 itérations) : il contient indicatif, version, port, adresses (VPN, réseau domestique, IP publique si le port est ouvert) et l'**empreinte du certificat**. Le nom du canal sur le broker est dérivé du code et ne révèle rien.
- **Apri la stazione (HTTPS)** (ouvrir la station) essaie dans l'ordre le **VPN**, puis le **réseau domestique**, puis l'**IP publique** ; les autres adresses restent sous forme de liens.

Le serveur est derrière un **CGNAT** ? Hors de la maison, le navigateur ne l'atteint qu'avec un **VPN** (Tailscale, WireGuard, ZeroTier…) installé sur le serveur et sur le téléphone : la page le signale. Le navigateur ne sait pas faire la connexion inverse du programme client.

## La première fois : le certificat

Le serveur utilise un certificat HTTPS qu'il a créé lui-même (ECDSA P-256, 800 jours). Le navigateur avertit une fois par adresse :

1. «Votre connexion n'est pas privée» apparaît ;
2. dans les détails du certificat, comparez l'empreinte SHA-256 avec celle affichée par «Trova il server» (elle arrive chiffrée : sans le code, personne ne peut la falsifier) ;
3. si elle est identique : «Paramètres avancés» › «Continuer» ; si elle diffère, n'entrez pas.

**iPhone** : le certificat doit être installé comme profil : on le télécharge depuis `https://<adresse>:8443/certificato.cer`, puis Réglages › Profil téléchargé › Installer, et Réglages › Général › Informations › Réglages des certificats.

## La page de la station

- **Connexion** : code fixe et indicatif. Le code **ne circule pas sur le réseau** : le serveur envoie un défi et la page répond par une preuve (HMAC-SHA256) dérivée du code. Dix essais erronés depuis la même adresse bloquent l'accès pendant 10 minutes.
- **Téléphone** : fréquence (toucher pour la saisir en kHz), − / mode / +, bandes, S / Po / ROS / ALC, NR, NB, NOTCH, ATT, volume, un grand **PTT à maintenir appuyé**.
- **PC** : en plus VFO B, A/B, SPLIT, pas, COMP, PRE, AGC, puissance, choix du micro et du haut-parleur, gigue et paquets perdus, **panadapter** (si le serveur le fournit : clic = accord, molette = un pas). PTT aussi avec la barre d'espace.
- **Audio** : 8 kHz 16 bits comme le programme, dans le même WebSocket chiffré par HTTPS. Le micro du navigateur n'arrive à la radio **que** pendant qu'une émission depuis le navigateur est autorisée et en cours.
- Toutes les sécurités du serveur s'appliquent : durée maximale, pas de QSY en émission, priorité à celui qui est à la radio, écoute seule, protection ROS. Le PTT est relâché si la page passe en arrière-plan ou si la liaison tombe.
- **Un opérateur à la fois** : si un client est connecté, le navigateur trouve «server occupato» (serveur occupé), et inversement. Le navigateur apparaît dans la liste des clients du serveur sous le nom «Browser».

## Confidentialité et services externes

- Brokers MQTT publics : ils ne voient qu'un canal au nom quelconque et un message chiffré (comme pour le programme client).
- Polices de la page : Inter et JetBrains Mono depuis Google Fonts (Google voit l'adresse IP de celui qui ouvre la page) ; sans internet, la page utilise les polices du système.
- Aucun autre service.

## Sources

- MQTT 3.1.1 (OASIS) pour le dialogue minimal avec les brokers via WebSocket.
- Web Crypto API (W3C) : PBKDF2, HKDF, AES-GCM, HMAC dans le navigateur ; les mêmes calculs que le programme (.NET `System.Security.Cryptography`).
- Ports WebSocket sécurisés des brokers : liste des brokers publics du wiki de mqtt.org (EMQX 8084, Mosquitto 8081) et page du broker public HiveMQ (8884).


---

[Sommaire](Français) · Autres langues: [Italiano](IT-Accesso-dal-browser) · [English](EN-Browser-access) · [Español](ES-Acceso-desde-el-navegador) · [Deutsch](DE-Zugriff-über-den-Browser)
