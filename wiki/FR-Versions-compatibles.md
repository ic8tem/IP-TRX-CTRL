# Versions compatibles

Le serveur et le client doivent avoir des versions du même **protocole**. Le protocole ne change que lorsque les deux PC doivent être mis à jour ensemble ; au sein du même protocole, les versions se comprennent.

| Versions | Protocole | Compatibles avec |
|---|---|---|
| 0.1.0 – 0.1.3 | 1 | seulement les 0.1.x |
| 0.2.0 – 0.2.8 | 2 | toutes les 0.2.x |
| 0.3.0 – 0.3.7 | 3 | seulement les 0.3.x (numéro à 10 chiffres, code fixe du serveur, plusieurs clients sur le serveur, sécurité en émission, panadapter ; depuis la 0.3.1 accès depuis le navigateur ; depuis la 0.3.2 mises à jour automatiques) |

Une 0.1.x et une 0.2.x ne doivent **pas** être utilisées ensemble, ni une 0.2.x et une 0.3.x : mettez à jour les deux PC. La 0.3.0 change aussi le format de la télémétrie et ajoute de nouveaux messages, c'est pourquoi elle ne dialogue pas avec les versions précédentes. La 0.3.0 change l'appairage (numéro à 10 chiffres au lieu du long code) ; les clés déjà échangées restent valables et passent d'elles-mêmes dans la liste des serveurs du client et dans la liste des clients du serveur.

## Fonctions qui demandent une version minimale sur les deux PC

| Fonction | Serveur et client au moins |
|---|---|
| Programmes numériques, pédale/manipulateur CW, cycle des ports, audio UDP | 0.2.0 |
| Choix des cartes son du serveur depuis le client | 0.2.1 |
| Serveur sans UPnP, liaison sur le réseau local | 0.2.2 |
| Échange de la version et contrôle automatique de compatibilité | 0.2.3 |
| Le client indique au serveur les ports qui sortent de son réseau (réseaux filtrés) | 0.2.4 |
| Protection ROS : barre clignotante, blocage de l'émission et fenêtre sur le client | 0.2.4 |
| CAT et audio du serveur via TCI (ExpertSDR, Thetis et autres SDR) | 0.2.5 (sur le serveur suffit) |
| Cycle des ports aussi pendant l'appairage (20 tours, ports toujours nouveaux, connexion inverse) | 0.2.7 sur les deux PC |
| Numéro d'appairage à 10 chiffres, liste des serveurs sur le client | 0.3.0 sur les deux PC |
| Plusieurs clients sur le serveur (un connecté à la fois, «serveur occupé»), code fixe du serveur | 0.3.0 sur les deux PC |
| Sécurité en émission (durée maximale, pas de QSY en émission, priorité à celui qui est à la radio, écoute seule), commandes de la radio, mémoires du serveur, panadapter | 0.3.0 sur les deux PC |
| Accès depuis le navigateur (page web HTTPS, page «Trova il server») | 0.3.1 sur le serveur ; le client 0.3.0 continue de fonctionner |
| Mises à jour automatiques | 0.3.2 sur chaque PC : chacun se met à jour lui-même (pas avec les paquets .deb, .rpm, Arch et Slackware) |

## Mises à jour automatiques (depuis la 0.3.2)

Depuis la 0.3.2, chaque PC se met à jour tout seul vers la dernière version, au démarrage ou dans les 6 heures, et seulement quand aucune session n'est en cours : si un jour sort une version avec un nouveau protocole, serveur et client s'alignent d'eux-mêmes sans rester longtemps sur des protocoles différents. Font exception les paquets système (.deb, .rpm, Arch, Slackware), à mettre à jour à la main sur les deux PC. Détails dans le [README](https://github.com/ic8tem/IP-TRX-CTRL#aggiornamenti-automatici).

## Contrôle automatique (depuis la 0.2.3)

Les deux PC échangent leur version à trois moments :

- **appairage** : le serveur l'envoie, chiffrée, sur le broker MQTT avec adresse et ports ; le client la compare avant de se connecter. Si le protocole est différent, l'appairage s'arrête et le dit dans le journal de la fenêtre ;
- **adresse sur le broker MQTT** : le serveur la publie avec son adresse, à chaque mise à jour ;
- **début de chaque liaison** : dans le salut sur le canal chiffré.

Si l'autre PC a un protocole différent, la fenêtre **«Versioni non compatibili»** (versions incompatibles) apparaît ; s'il a une version plus ancienne du même protocole, un avis indique quelles fonctions manquent. Les versions jusqu'à la 0.2.2 n'envoient pas leur version : elles apparaissent comme «0.2.2 o precedente» (0.2.2 ou antérieure).

La version de l'autre PC se voit dans la ligne de son réseau dans la fenêtre principale ; chaque comparaison est écrite dans `sessioni.log` avec le préfixe `VERSIONE`. La version de ce PC est dans la fenêtre «Informazioni e licenze» (informations et licences).


---

[Sommaire](Français) · Autres langues: [Italiano](IT-Versioni-compatibili) · [English](EN-Compatible-versions) · [Español](ES-Versiones-compatibles) · [Deutsch](DE-Kompatible-Versionen)
