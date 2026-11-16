# Journaux et résolution des problèmes

## Où sont les fichiers du programme

| Système | Dossier des données |
|---|---|
| Windows | `%APPDATA%\IP-TRX-CTRL` (c'est-à-dire `C:\Users\<utilisateur>\AppData\Roaming\IP-TRX-CTRL`) |
| Linux et Raspberry Pi | `~/.config/IP-TRX-CTRL` |
| macOS | `~/.config/IP-TRX-CTRL` (dossier caché : dans le Finder Cmd+Maj+G et saisissez le chemin) |

Le dossier s'ouvre aussi depuis le programme : **Impostazioni › Apri cartella dati** (réglages › ouvrir le dossier des données).

Sous Windows : Win+R, saisissez `%APPDATA%\IP-TRX-CTRL` et appuyez sur Entrée.

## Ce qu'il contient

| Fichier | Contenu |
|---|---|
| `errori.log` | erreurs imprévues, avec date, heure et endroit du programme où elles se sont produites. Le fichier n'apparaît qu'après la première erreur |
| `sessioni.log` | journal des liaisons : chaque étape sur les ports (lignes `PORTE` : recherche du routeur UPnP, ports ouverts ou refusés, IP publique, fermeture), type de réseau (lignes `RETE` : IPv4 / IPv6), appairage avec le dialogue MQTT, connexions, changements de rôle, relâchements du PTT de sécurité. Heure en UTC |
| `settings.json` | réglages |
| `pairing.key` | clé de l'appairage, chiffrée (Windows : avec l'utilisateur Windows ; Linux/Raspberry : liée à la machine et à l'utilisateur). Sous macOS, la clé est dans le Trousseau, entrée «IP-TRX-CTRL» |
| `WAV/` | les fichiers des messages du keyer |

Sous Windows, Hamlib est extrait dans `%LOCALAPPDATA%\IP-TRX-CTRL\hamlib\<version>`.

## Comment signaler un problème

Depuis la 0.3.0 : **Impostazioni › Abbinamento e registri › «Rapporto per un problema…»** (rapport de problème).

1. Écrivez (si vous voulez) ce qui s'est passé.
2. **Salva il rapporto…** (enregistrer le rapport) : un fichier texte avec version, système, rôle, radio, méthodes PTT et CW, options de sécurité, panadapter, broker MQTT et les dernières parties de `errori.log` (200 ko) et `sessioni.log` (400 ko).
3. **Apri la pagina delle segnalazioni** (ouvrir la page des signalements) : la page **Issues** du dépôt s'ouvre ; joignez le fichier.

Dans le rapport, **les adresses IP publiques sont masquées** : chacune devient `[IP-1]`, `[IP-2]`… (la même adresse a toujours le même nom, pour que le journal reste lisible). Seules restent visibles les adresses de réseau local (192.168.x.x, 10.x.x.x, 172.16–31.x.x, 100.64–127.x.x du CGNAT, 127.x.x.x, 169.254.x.x), qui depuis internet ne mènent à personne ; les IPv6 sont toutes masquées. Le rapport ne contient ni clés ni codes d'appairage. Avant de le joindre, vous pouvez l'ouvrir et le lire.

Sans le rapport : ouvrez le dossier des données et joignez `errori.log` et `sessioni.log`, en indiquant système d'exploitation, version, rôle et radio. `sessioni.log` contient des adresses IP et des indicatifs. Ne joignez jamais les fichiers des clés (`pairing-….key`).

## Nouvelle version et mises à jour automatiques

Au démarrage puis toutes les 6 heures, le programme demande à GitHub quelle est la dernière version publiée (`api.github.com/repos/ic8tem/IP-TRX-CTRL/releases/latest`, sans compte).

Depuis la **0.3.2**, si elle est plus récente, le programme se met à jour tout seul : il télécharge le paquet adapté au PC, vérifie sa taille et son empreinte SHA-256 (celle que GitHub publie pour chaque fichier) et redémarre dans la nouvelle version, **seulement quand aucune session n'est en cours et qu'on n'émet pas**. Avec une session ouverte, la nouvelle version reste prête et s'installe dès la fin ; la ligne du haut l'indique et propose **Aggiorna adesso** (mettre à jour maintenant). Dans `sessioni.log`, chaque étape est écrite avec le préfixe `AGGIORNAMENTI`.

Se mettent à jour seuls : Setup et zip portable de Windows (depuis la 0.3.2 le Setup rend le dossier du programme accessible en écriture pour cela), tar.gz portables (Linux, Raspberry Pi, Alpine), AppImage, IP-TRX-CTRL.app sur Mac dans un dossier accessible en écriture. Les paquets système **.deb, .rpm, Arch et Slackware** ne se mettent **pas** à jour seuls : la ligne de nouvelle version apparaît avec **Apri la pagina** (ouvrir la page), et on installe le nouveau paquet avec la même commande que la première fois.

Les mises à jour automatiques se désactivent dans **Impostazioni › Finestra principale** : la ligne du haut reste, avec **Aggiorna adesso**, **Apri la pagina** et **Chiudi** (fermer ; elle ne réapparaît plus pour cette version). Si la mise à jour échoue (réseau, dossier non accessible en écriture), le programme continue avec la version en cours et réessaie au contrôle suivant.

## Problèmes fréquents

| Symptôme | Que vérifier |
|---|---|
| Fenêtre «Io mi spengo perché non riesco a funzionare» (je m'arrête car je ne peux pas fonctionner) avec le bouton Exit (serveur) | le routeur n'a pas ouvert les deux ports avec UPnP : activez UPnP sur le routeur et relancez. Les lignes `PORTE` de `sessioni.log` disent quelle étape a échoué |
| L'appairage s'arrête avec «la porta … non risponde» (le port … ne répond pas) | UPnP actif sur le routeur du serveur ; si l'opérateur utilise le CGNAT, le serveur n'est pas joignable depuis internet |
| «nessun broker disponibile» (aucun broker disponible) | connexion internet du PC, port 1883 non bloqué par le réseau |
| «Hamlib non trovato» (Hamlib introuvable) (Linux, Raspberry, macOS) | `sudo apt install libhamlib-utils` ou `brew install hamlib` |
| La radio ne répond pas au CAT | modèle, port série et vitesse dans les réglages ; sous Linux/Raspberry l'utilisateur doit être dans le groupe `dialout` |
| Pas d'audio du micro sous macOS | Réglages Système › Confidentialité et sécurité › Microphone : autorisez IP-TRX-CTRL |
| Audio du serveur en sourdine | cadre «Schede audio del server» : enlevez la sourdine ou activez «Togli il mute da solo» |


---

[Sommaire](Français) · Autres langues: [Italiano](IT-Registri-e-risoluzione-dei-problemi) · [English](EN-Logs-and-troubleshooting) · [Español](ES-Registros-y-solución-de-problemas) · [Deutsch](DE-Protokolle-und-Fehlersuche)
